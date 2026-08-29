using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Camera;

/// <summary>
/// A camera that follows a target's bounding box using a "dead zone": it only scrolls once the
/// target gets within <see cref="EdgeMarginCells"/> of the current view's edge, and never
/// scrolls past the world's own edges. As a result, a target moving well within the dead zone
/// doesn't move the camera at all, and a target near a world edge (where there's no more world
/// left to reveal) can walk right up to the edge of the screen instead of the camera trying
/// (and failing) to keep it centered.
/// </summary>
public class Camera2D
{
    /// <summary>Top-left world position visible on screen, in world cells.</summary>
    public Vector2D Position { get; private set; }

    /// <summary>How quickly the camera catches up once it starts scrolling (higher = snappier).</summary>
    public double FollowSpeed { get; set; } = 8.0;

    /// <summary>
    /// How close (in world cells) the target's bounding box may get to the edge of the current
    /// view before the camera starts scrolling to keep up with it.
    /// </summary>
    public double EdgeMarginCells { get; set; } = 6.0;

    /// <summary>
    /// Immediately centers the camera on a target's bounding box (no smoothing), clamped to the
    /// world's edges. Used once at level load so the game doesn't open mid-pan toward whatever
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
        var maxX = Math.Max(0.0, worldWidthCells - viewportWidthCells);
        var maxY = Math.Max(0.0, worldHeightCells - viewportHeightCells);
        var targetCenterX = targetPosition.X + targetSize.X / 2;
        var targetCenterY = targetPosition.Y + targetSize.Y / 2;

        Position = new Vector2D(
            Math.Clamp(targetCenterX - viewportWidthCells / 2, 0, maxX),
            Math.Clamp(targetCenterY - viewportHeightCells / 2, 0, maxY));
    }

    public void Follow(
        Vector2D targetPosition,
        Vector2D targetSize,
        double worldWidthCells,
        double worldHeightCells,
        double viewportWidthCells,
        double viewportHeightCells,
        double deltaSeconds)
    {
        // The furthest the camera is ever allowed to scroll on each axis - clamped at zero so a
        // world narrower/shorter than the viewport just keeps the camera pinned at the origin
        // rather than trying to scroll into negative territory.
        var maxX = Math.Max(0.0, worldWidthCells - viewportWidthCells);
        var maxY = Math.Max(0.0, worldHeightCells - viewportHeightCells);

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

        desired = new Vector2D(Math.Clamp(desired.X, 0, maxX), Math.Clamp(desired.Y, 0, maxY));

        var t = Math.Clamp(FollowSpeed * deltaSeconds, 0, 1);
        Position = new Vector2D(
            Position.X + (desired.X - Position.X) * t,
            Position.Y + (desired.Y - Position.Y) * t);
    }
}
