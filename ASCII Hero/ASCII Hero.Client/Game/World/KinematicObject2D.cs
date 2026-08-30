using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A non-player object that moves under a predefined, constant velocity rather than force
/// integration - unlike <see cref="DynamicObject2D"/> it never accelerates under gravity or
/// bounces off surfaces via restitution. <see cref="Physics.PhysicsSystem"/> integrates its
/// position from <see cref="Velocity"/> directly instead of applying gravity to it. Intended as
/// the base for future predefined-path motion (e.g. a platform patrolling back and forth); for
/// now it simply moves at a constant configured velocity.
/// </summary>
public class KinematicObject2D : Body2D, IPhysicsBody
{
    /// <summary>Current velocity, in world cells per second. Constant unless changed externally.</summary>
    public Vector2D Velocity { get; set; }

    /// <summary>Whether this object is currently resting on a platform or the world's floor.</summary>
    public bool IsGrounded { get; set; }

    public KinematicObject2D()
    {
        IsStatic = false;
    }

    /// <summary>Assigns the loaded sprite asset/clip/frame, initial position and constant velocity.</summary>
    public void Spawn(SpriteAsset sprite, string clipName, int frameIndex, Vector2D position, Vector2D velocity, int repeatCount = 1)
    {
        SetFrame(sprite, clipName, frameIndex, repeatCount);
        Position = position;
        Velocity = velocity;
    }
}
