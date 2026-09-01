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

    /// <summary>
    /// Current stance (e.g. "Walk", "Crawl"). Plain string rather than an enum so this
    /// mechanism (and <see cref="Body2D.SetPose(Assets.SpriteAsset, string, Assets.Facing)"/>) stays generic across any body's own stance
    /// vocabulary, not just the player's. Settable directly (e.g. by <see cref="Physics.PhysicsSystem"/>
    /// toggling Walk/Crawl) without immediately re-resolving a clip - <see cref="Body2D.SetPose(Assets.SpriteAsset, string, Assets.Facing)"/>
    /// is the separate call that actually applies a stance+facing pair's clip. See docs/AssetFormat.md §2.6.
    /// </summary>
    public string Stance { get; set; } = "Walk";

    public Player2D()
    {
        IsStatic = false;
    }

    /// <summary>Assigns the loaded Player sprite asset and activates its default stance/idle facing.</summary>
    public void Spawn(SpriteAsset sprite)
    {
        if (sprite.Stances is not null)
        {
            Stance = sprite.DefaultStance ?? "Walk";
            SetPose(sprite, Stance, Facing.Idle);
        }
        else
        {
            SetFrame(sprite, "walk_idle", sprite.GetClip("walk_idle").DefaultFrame);
        }
    }
}
