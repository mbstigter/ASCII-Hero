using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A non-player object that moves under its own velocity (and optionally gravity), such as the
/// bouncing ball. Unlike <see cref="Player2D"/> it is never driven by input; unlike
/// <see cref="StaticObject2D"/> it participates in motion integration and bounces off world
/// bounds and platforms instead of just sitting still.
/// </summary>
public class DynamicObject2D : Body2D, IPhysicsBody, IGravityAffected
{
    /// <summary>Current velocity, in world cells per second.</summary>
    public Vector2D Velocity { get; set; }

    /// <summary>Whether this object is currently resting on a platform or the world's floor.</summary>
    public bool IsGrounded { get; set; }

    /// <summary>Whether this object is subject to normal world gravity.</summary>
    public bool GravityAffected { get; set; } = true;

    public DynamicObject2D()
    {
        IsStatic = false;
    }

    /// <summary>Assigns the loaded sprite asset/clip/frame, initial position and velocity.</summary>
    public void Spawn(SpriteAsset sprite, string clipName, int frameIndex, Vector2D position, Vector2D velocity, bool gravityAffected, int repeatCount = 1)
    {
        SetFrame(sprite, clipName, frameIndex, repeatCount);
        Position = position;
        Velocity = velocity;
        GravityAffected = gravityAffected;
    }
}
