using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>The player-controlled character, backed by the loaded "Player" sprite asset.</summary>
public class Player2D : Body2D, IPhysicsBody, IGravityAffected, ICollectorBody
{
    /// <summary>Current velocity, in world cells per second.</summary>
    public Vector2D Velocity { get; set; }

    /// <summary>Whether the player is currently standing on a platform or the world's floor.</summary>
    public bool IsGrounded { get; set; }

    /// <summary>
    /// The player is always subject to normal world gravity - unlike <see cref="DynamicObject2D"/>/
    /// <see cref="MovingEnemy2D"/> there is no per-instance toggle, so this is a fixed <c>true</c>
    /// rather than a settable property.
    /// </summary>
    public bool GravityAffected => true;

    public Player2D()
    {
        IsStatic = false;
    }

    /// <summary>Assigns the loaded Player sprite asset and activates its "idle" clip.</summary>
    public void Spawn(SpriteAsset sprite) => SetFrame(sprite, "idle", sprite.DefaultFrame ?? 0);
}
