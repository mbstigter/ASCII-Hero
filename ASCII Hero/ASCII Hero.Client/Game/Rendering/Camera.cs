using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// A camera that follows a target's bounding box using a "dead zone": it only scrolls once the
/// target gets within <see cref="EdgeMarginCells"/> of the current view's edge, and never
/// scrolls past the world's own edges. As a result, a target moving well within the dead zone
/// doesn't move the camera at all, and a target near a world edge (where there's no more world
/// left to reveal) can walk right up to the edge of the screen instead of the camera trying
/// (and failing) to keep it centered.
/// </summary>
public class Camera
{
    /// <summary>Top-left world position visible on screen, in world cells.</summary>
    public Vector2D Position { get; private set; }

    /// <summary>How quickly the camera catches up once it starts scrolling (higher = snappier).</summary>
    public double FollowSpeed { get; set; } = GameDefaults.CameraFollowSpeed;

    /// <summary>
    /// How close (in world cells) the target's bounding box may get to the edge of the current
    /// view before the camera starts scrolling to keep up with it.
    /// </summary>
    public double EdgeMarginCells { get; set; } = GameDefaults.CameraEdgeMarginCells;

    /// <summary>
    /// Immediately centers the camera on a target's bounding box (no smoothing), clamped to the
    /// world's edges. Used once at world load so the game doesn't open mid-pan toward whatever
    /// the camera happens to be following.
    /// </summary>
    public void SnapTo(
        Vector2D targetPosition,
        Vector2D targetSize,
        double worldWidthCells,
        double worldHeightCells,
        double viewportWidthCells,
        double viewportHeightCells)
    {
        var targetCenterX = targetPosition.X + targetSize.X / 2;
        var targetCenterY = targetPosition.Y + targetSize.Y / 2;

        Position = new Vector2D(
            ClampAxis(targetCenterX - viewportWidthCells / 2, worldWidthCells, viewportWidthCells),
            ClampAxis(targetCenterY - viewportHeightCells / 2, worldHeightCells, viewportHeightCells));
    }

    /// <summary>
    /// Keeps the view inside the world on one axis. A world smaller than the viewport on that axis
    /// cannot scroll at all, so it is centered instead (a negative view origin, with empty space
    /// on both sides).
    /// </summary>
    private static double ClampAxis(double value, double worldCells, double viewportCells) =>
        worldCells <= viewportCells ? (worldCells - viewportCells) / 2 : Math.Clamp(value, 0, worldCells - viewportCells);

    public void Follow(
        Vector2D targetPosition,
        Vector2D targetSize,
        double worldWidthCells,
        double worldHeightCells,
        double viewportWidthCells,
        double viewportHeightCells,
        double deltaSeconds)
    {
        var targetLeft = targetPosition.X;
        var targetRight = targetPosition.X + targetSize.X;
        var targetTop = targetPosition.Y;
        var targetBottom = targetPosition.Y + targetSize.Y;

        var desired = Position;

        var leftDeadZone = Position.X + EdgeMarginCells;
        var rightDeadZone = Position.X + viewportWidthCells - EdgeMarginCells;
        if (targetLeft < leftDeadZone)
        {
            desired = new Vector2D(targetLeft - EdgeMarginCells, desired.Y);
        }
        else if (targetRight > rightDeadZone)
        {
            desired = new Vector2D(targetRight - viewportWidthCells + EdgeMarginCells, desired.Y);
        }

        var topDeadZone = Position.Y + EdgeMarginCells;
        var bottomDeadZone = Position.Y + viewportHeightCells - EdgeMarginCells;
        if (targetTop < topDeadZone)
        {
            desired = new Vector2D(desired.X, targetTop - EdgeMarginCells);
        }
        else if (targetBottom > bottomDeadZone)
        {
            desired = new Vector2D(desired.X, targetBottom - viewportHeightCells + EdgeMarginCells);
        }

        desired = new Vector2D(
            ClampAxis(desired.X, worldWidthCells, viewportWidthCells),
            ClampAxis(desired.Y, worldHeightCells, viewportHeightCells));

        var t = Math.Clamp(FollowSpeed * deltaSeconds, 0, 1);
        Position = new Vector2D(
            Position.X + (desired.X - Position.X) * t,
            Position.Y + (desired.Y - Position.Y) * t);
    }
}
