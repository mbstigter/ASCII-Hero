using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// An AI-controlled enemy that moves (patrols/chases) and damages the player on contact. Reuses
/// the same physics/collision handling as any other <see cref="IPhysicsBody"/> - it participates
/// in gravity/force integration and platform/world-bounds collision exactly like a
/// <see cref="DynamicObject2D"/>. Patrol/chase behavior itself is not yet implemented; this class
/// currently exists so it can be placed and collided against generically as a hazard.
/// </summary>
public class MovingEnemy2D : Body2D, IPhysicsBody, IHazardBody, IGravityAffected, IEffectTrigger, IKillableBody
{
    /// <summary>Current velocity, in world cells per second.</summary>
    public Vector2D Velocity { get; set; }

    /// <summary>Whether this enemy is currently resting on a platform or the world's floor.</summary>
    public bool IsGrounded { get; set; }

    /// <summary>Whether this enemy is subject to normal world gravity.</summary>
    public bool GravityAffected { get; set; } = true;

    /// <summary>
    /// Optional clip name (on this instance's own <see cref="Body2D.Sprite"/>) to play as a
    /// cosmetic effect on contact (e.g. a "crumble" clip when killed). Null (the default) means
    /// no effect.
    /// </summary>
    public string? EffectClipName { get; set; }

    /// <summary>
    /// Whether this instance can be "killed" (removed from the world) by a qualifying contact
    /// (landed on top of). Defaults to false, so existing levels that don't opt in are unaffected.
    /// </summary>
    public bool IsKillable { get; set; }

    /// <summary>Whether this instance's effect (if configured) persists as a permanent husk after a kill contact.</summary>
    public bool EffectPersists { get; set; }

    public MovingEnemy2D()
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
