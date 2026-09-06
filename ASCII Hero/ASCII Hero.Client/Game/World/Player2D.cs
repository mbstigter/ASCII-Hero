using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>The player-controlled character, backed by the loaded "Player" sprite asset.</summary>
public class Player2D : Body2D, IPhysicsBody, IGravityAffected, ICollectorBody, IKillerBody, IEffectTrigger, IClimberBody, IHangerBody, IPosedBody
{
    /// <summary>Current velocity, in world cells per second.</summary>
    public Vector2D Velocity { get; set; }

    /// <summary>Whether the player is currently standing on a platform or the world's floor.</summary>
    public bool IsGrounded { get; set; }

    /// <inheritdoc/>
    public bool IsTouchingClimbable { get; set; }

    /// <inheritdoc/>
    public bool IsTouchingHangable { get; set; }

    /// <inheritdoc/>
    public bool IsClimbing { get; set; }

    /// <inheritdoc/>
    public bool IsHanging { get; set; }

    /// <inheritdoc/>
    public bool IsClambering { get; set; }

    /// <inheritdoc/>
    public bool SuppressHangUntilClear { get; set; }

    /// <summary>
    /// The player is subject to normal world gravity except while <see cref="IsClimbing"/> or
    /// <see cref="IsHanging"/>, during which it is suspended so <see cref="Physics.PhysicsSystem"/>
    /// can drive vertical/lateral movement directly instead of fighting gravity's pull.
    /// </summary>
    public bool GravityAffected => !(IsClimbing || IsHanging);

    /// <summary>
    /// Current stance (e.g. "Walk", "Crawl"). Plain string rather than an enum so this
    /// mechanism (and <see cref="Body2D.SetPose(Assets.SpriteAsset, string, Assets.Facing)"/>) stays generic across any body's own stance
    /// vocabulary, not just the player's. Settable directly (e.g. by <see cref="Physics.PhysicsSystem"/>
    /// toggling Walk/Crawl) without immediately re-resolving a clip - <see cref="Body2D.SetPose(Assets.SpriteAsset, string, Assets.Facing)"/>
    /// is the separate call that actually applies a stance+facing pair's clip. See docs/AssetFormat.md §2.6.
    /// </summary>
    public string Stance { get; set; } = "Walk";

    /// <summary>
    /// Optional clip name (on this instance's own <see cref="Body2D.Sprite"/>) to play as a
    /// cosmetic effect on contact (e.g. a hazard-hit spark). Null (the default) means no effect.
    /// </summary>
    public string? EffectClipName { get; set; }

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

    /// <summary>
    /// Resolves and applies the player's pose (see <see cref="IPosedBody"/>) from its current
    /// stance/climbing/hanging state and its own now-integrated <see cref="Velocity"/> - vertical
    /// (via <see cref="Body2D.ResolveVerticalFacing"/>) while climbing, since climbing sets
    /// <see cref="Velocity"/>.Y directly from up/down input and has no horizontal facing at all;
    /// horizontal (via <see cref="Body2D.ResolveHorizontalFacing"/>) for every other stance, the
    /// same rule <see cref="MovingEnemy2D"/> uses. Not yet called by
    /// <see cref="Physics.PhysicsSystem"/>'s generic force-based path (the player still moves via
    /// direct velocity assignment - see the TODO in <see cref="Physics.PhysicsSystem.Step"/>), but
    /// implemented now so the eventual conversion to that path is a drop-in rather than a redesign.
    /// </summary>
    public void UpdatePose()
    {
        // "Jump" is a visual-only pose, not a distinct stance the player can be toggled into/out
        // of like Crawl - it's simply what's shown while airborne, regardless of which stance
        // (Walk or Crawl) the player was in when they left the ground (e.g. crawling off a ledge
        // still assumes the jump pose mid-air). Stance itself stays "Walk"/"Crawl" throughout;
        // only the resolved pose swaps to the Jump stance's clips while not grounded. Climbing/
        // hanging take priority over both: they're their own dedicated stances ("Climb" and
        // "Hang"/"Clamber" depending on IsClambering), shown regardless of IsGrounded.
        var poseStance = IsClimbing ? "Climb"
            : IsHanging ? (IsClambering ? "Clamber" : "Hang")
            : !IsGrounded ? "Jump"
            : Stance;
        var facing = IsClimbing ? ResolveVerticalFacing(Velocity.Y) : ResolveHorizontalFacing(Velocity.X);
        SetPose(Sprite, poseStance, facing);
    }
}
