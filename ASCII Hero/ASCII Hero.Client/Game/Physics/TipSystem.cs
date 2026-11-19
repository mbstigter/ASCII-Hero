using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>
/// Decides, for every moving body standing on something, whether it is balanced or tips off a
/// ledge. Bodies never rotate, so tipping is modelled as a sideways pull: a grounded body whose
/// center of mass lies outside the span it actually stands on (from the leftmost to the rightmost
/// edge of everything under its feet, so a body bridging two supports is balanced) gets a
/// horizontal <see cref="Body2D.TipAccelerationX"/> toward the open side, growing with how far its
/// center is past the edge - like the growing torque of a real body tipping over an edge. While
/// tipping, its ground contact applies no friction (see <see cref="CollisionSystem"/>) and it never
/// sleeps (see <see cref="SleepSystem"/>), so it slides off and falls.
/// </summary>
/// <remarks>
/// Run once per physics step at the end of <see cref="CollisionSystem.Resolve"/>, from that step's
/// final contacts; the result is applied by <see cref="PhysicsSystem"/> on the next step.
/// <see cref="Body2D.LedgeToleranceCells"/> lets a body (the player) stand a little further past
/// an edge than strict balance allows.
/// </remarks>
public static class TipSystem
{
    /// <summary>How close (cells) a support's top must be to a body's bottom to count as standing on it.</summary>
    private const double SupportToleranceCells = 0.1;

    /// <summary>Updates <see cref="Body2D.TipAccelerationX"/> for every moving body.</summary>
    public static void Update(IReadOnlyList<IPhysicsBody> movingBodies, double gravity)
    {
        foreach (var movingBody in movingBodies)
        {
            var body = (Body2D)movingBody;
            body.TipAccelerationX = GetTipAccelerationX(body, Math.Abs(gravity));
        }
    }

    private static double GetTipAccelerationX(Body2D body, double gravity)
    {
        if (gravity <= 0.0
            || body is IClimberBody { IsClimbing: true }
            || body is IHangerBody { IsHanging: true }
            || !TryGetSupportSpan(body, out var supportLeft, out var supportRight)
            || !TryGetCenterOfMassX(body, out var centerX))
        {
            return 0.0;
        }

        var tolerance = Math.Max(body.LedgeToleranceCells, 0.0);
        var overhang = centerX < supportLeft - tolerance
            ? centerX - (supportLeft - tolerance)
            : centerX > supportRight + tolerance
                ? centerX - (supportRight + tolerance)
                : 0.0;
        if (overhang == 0.0)
        {
            return 0.0;
        }

        var halfWidth = Math.Max(body.Size.X / 2.0, 0.5);
        var lever = Math.Clamp(Math.Abs(overhang) / halfWidth, GameDefaults.MinTipFraction, 1.0);
        return Math.Sign(overhang) * gravity * lever;
    }

    /// <summary>
    /// The horizontal span <paramref name="body"/> stands on: the union of every horizontal overlap
    /// between its own collision rectangles and the top of a body recorded below it.
    /// </summary>
    private static bool TryGetSupportSpan(Body2D body, out double left, out double right)
    {
        left = double.MaxValue;
        right = double.MinValue;
        var bodyRects = body.CollisionRects;
        foreach (var support in body.GetContactingBodies(ContactType.SurfaceBottom))
        {
            foreach (var supportRect in support.CollisionRects)
            {
                foreach (var bodyRect in bodyRects)
                {
                    var overlapLeft = Math.Max(bodyRect.Left, supportRect.Left);
                    var overlapRight = Math.Min(bodyRect.Right, supportRect.Right);
                    if (overlapRight - overlapLeft <= 1e-9
                        || Math.Abs(supportRect.Top - bodyRect.Bottom) > SupportToleranceCells)
                    {
                        continue;
                    }

                    left = Math.Min(left, overlapLeft);
                    right = Math.Max(right, overlapRight);
                }
            }
        }

        return left <= right;
    }

    /// <summary>The horizontal center of mass of <paramref name="body"/>'s visible (non-empty) cells, in world space.</summary>
    private static bool TryGetCenterOfMassX(Body2D body, out double centerX)
    {
        var chars = body.Frame.Chars;
        var empty = body.Sprite.EmptyChar;
        var sum = 0.0;
        var count = 0;
        for (var row = 0; row < chars.GetLength(0); row++)
        {
            for (var column = 0; column < chars.GetLength(1); column++)
            {
                if (chars[row, column] != empty)
                {
                    sum += column + 0.5;
                    count++;
                }
            }
        }

        centerX = count > 0 ? body.Position.X + sum / count : 0.0;
        return count > 0;
    }
}
