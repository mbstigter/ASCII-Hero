using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Camera;

/// <summary>A camera that smoothly follows a target position using exponential smoothing (lerp).</summary>
public class Camera2D
{
    /// <summary>Top-left world position visible on screen, in world cells.</summary>
    public Vector2D Position { get; private set; }

    /// <summary>How quickly the camera catches up to the target (higher = snappier).</summary>
    public double FollowSpeed { get; set; } = 4.0;

    public void Follow(Vector2D targetCenter, double viewportWidthCells, double viewportHeightCells, double deltaSeconds)
    {
        var desired = new Vector2D(
            targetCenter.X - viewportWidthCells / 2,
            targetCenter.Y - viewportHeightCells / 2);

        var t = Math.Clamp(FollowSpeed * deltaSeconds, 0, 1);
        Position = new Vector2D(
            Position.X + (desired.X - Position.X) * t,
            Position.Y + (desired.Y - Position.Y) * t);
    }
}
