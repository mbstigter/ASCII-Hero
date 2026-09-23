using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Constants;

namespace ASCII_Hero.Client.Game.World;

/// <summary>The player-controlled character, backed by the loaded "Player" sprite asset.</summary>
public class Player2D : Body2D, IPhysicsBody, IGravityAffected, IMediumAffected, ICollectorBody, IKillerBody, IEffectTrigger, IClimberBody, IHangerBody, ISwimmerBody, IPosedBody, IWalkForceBody
{
    /// <summary>Current velocity, in world cells per second.</summary>
    public Vector2D Velocity { get; set; }

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

    /// <inheritdoc/>
    public bool IsSwimming { get; set; }

    /// <summary>
    /// The player is subject to normal world gravity except while <see cref="IsClimbing"/> or
    /// <see cref="IsHanging"/>, during which it is suspended so <see cref="Physics.PhysicsSystem"/>
    /// can drive vertical/lateral movement directly instead of fighting gravity's pull.
    /// <see cref="IsSwimming"/> deliberately does NOT suspend gravity - unlike climbing/hanging
    /// (gripping a solid surface), swimming is buoyant motion in a fluid, so gravity must keep
    /// acting and is offset by the existing ambient buoyancy force (<see cref="IMediumAffected"/>,
    /// always-on, separate from this flag) exactly like any other body immersed in a medium.
    /// The swim motor force (see <see cref="Physics.PhysicsSystem.Step"/>) only adds
    /// player-directed thrust on top of that, the same way <see cref="IWalkForceBody"/> adds
    /// motor force on top of gravity for an ordinary walking body - it does not replace gravity.
    /// </summary>
    public bool GravityAffected => !(IsClimbing || IsHanging);

    /// <summary>The player is always subject to ambient-medium buoyancy/drag.</summary>
    public bool MediumAffected => true;

    /// <summary>
    /// Current pose (e.g. "Walk", "Crawl").
    /// mechanism (and <see cref="Body2D.SetPose(Assets.SpriteAsset, string, Assets.Facing)"/>) stays generic across any body's own pose
    /// vocabulary, not just the player's. Settable directly (e.g. by <see cref="Physics.PhysicsSystem"/>
    /// toggling Walk/Crawl) without immediately re-resolving a clip - <see cref="Body2D.SetPose(Assets.SpriteAsset, string, Assets.Facing)"/>
    /// is the separate call that actually applies a pose+facing pair's clip. See docs/AssetFormat.md §2.6.
    /// </summary>
    public string Pose { get; set; } = "Walk";

    /// <summary>
    /// Optional clip name (on this instance's own <see cref="Body2D.Sprite"/>) to play as a
    /// cosmetic effect on contact (e.g. a hazard-hit spark). Null (the default) means no effect.
    /// </summary>
    public string? EffectClipName { get; set; }

    /// <summary>
    /// The mass-scaled force this frame's sustained locomotion input contributes - computed each
    /// frame by <see cref="Physics.PhysicsSystem.Step"/> as a simple proportional "motor" force
    /// converging <see cref="Velocity"/> toward a target walk/crawl/climb/hang velocity, summed
    /// into the net force alongside gravity by <see cref="Physics.PhysicsSystem.StepMovingBodyWithForces"/> -
    /// mirrors how <see cref="IPatrolBody.PatrolForce"/> contributes for a patrolling enemy.
    /// Horizontal-only while on the ground (Walk/Crawl); also carries a vertical component while
    /// <see cref="IsClimbing"/> (driving up/down movement) or <see cref="IsHanging"/> (holding
    /// position against nothing, since gravity is suspended) - both are sustained, ongoing
    /// locomotion for as long as they're engaged, exactly like walking, so they share this same
    /// continuous force model rather than a direct velocity assignment. Only the discrete
    /// jump-off/let-go moments (see <see cref="Physics.PhysicsSystem.Step"/>) are true
    /// instantaneous impulses instead.
    /// </summary>
    public Vector2D WalkForce { get; set; }

    /// <summary>
    /// "Muscle power" - the mass-scaled force gain applied to converge <see cref="Velocity"/>
    /// toward the current target walk/crawl/climb/hang velocity (see
    /// <see cref="Physics.PhysicsSystem.UpdateWalkForce"/>). Defaults to
    /// <see cref="GameDefaults.WalkForceMultiplier"/>, but a placement may
    /// override it via the <c>WalkForceMultiplier</c> ini key (see <see cref="World2D.LoadAsync"/>) -
    /// same name/role as <see cref="MovingEnemy2D.PatrolForceMultiplier"/> for a patrolling enemy,
    /// since both represent the exact same "muscle power toward a target speed" concept. One
    /// single gain covers every sustained pose (Walk, Crawl, Climb, Hang) - they are all the same
    /// kind of ongoing, motor-driven locomotion, just converging toward a different target
    /// velocity depending on the current pose/state (see <see cref="Physics.PhysicsSystem.Step"/>).
    /// </summary>
    public double WalkForceMultiplier { get; set; } = GameDefaults.WalkForceMultiplier;


    /// <summary>
    /// Target ground speed (in world cells/second) while standing/walking (<see cref="Pose"/> ==
    /// "Walk"). Defaults to <see cref="GameDefaults.WalkSpeed"/>, but a placement
    /// may override it via the <c>WalkSpeed</c> ini key (see <see cref="World2D.LoadAsync"/>).
    /// </summary>
    public double WalkSpeed { get; set; } = GameDefaults.WalkSpeed;

    /// <summary>
    /// Target ground speed (in world cells/second) while crouched/crawling (<see cref="Pose"/> ==
    /// "Crawl"). Defaults to <see cref="GameDefaults.CrawlSpeed"/>, but a placement
    /// may override it via the <c>CrawlSpeed</c> ini key (see <see cref="World2D.LoadAsync"/>).
    /// </summary>
    public double CrawlSpeed { get; set; } = GameDefaults.CrawlSpeed;

    public Player2D()
    {
        IsStatic = false;
    }

    /// <summary>Assigns the loaded Player sprite asset and activates its default pose/idle facing.</summary>
    public void Spawn(SpriteAsset sprite)
    {
        if (sprite.Poses is not null)
        {
            Pose = sprite.DefaultPose ?? "Walk";
            SetPose(sprite, Pose, Facing.Idle);
        }
        else
        {
            SetFrame(sprite, "walk_idle", sprite.GetClip("walk_idle").DefaultFrame);
        }
    }

    /// <summary>
    /// Resolves and applies the player's pose (see <see cref="IPosedBody"/>) from its current
    /// pose/climbing/hanging/swimming state - vertical facing (via
    /// <see cref="Body2D.ResolveVerticalFacing()"/>, reading <see cref="Body2D.MoveIntentY"/>)
    /// while climbing, since climbing has no horizontal facing at all; horizontal facing (via the
    /// shared <see cref="Body2D.ResolveHorizontalFacing()"/>) from <see cref="Body2D.MoveIntentX"/>
    /// for every other intent-driven pose - the same intent-based rule <see cref="MovingEnemy2D"/>
    /// uses (set from its own patrol-direction decision instead of raw key input), now that both
    /// bodies share one "facing follows intent, not velocity" mechanism. Swimming is the one
    /// deliberate exception - see the facing resolution below.
    /// </summary>
    public void UpdatePose()
    {
        // "Jump" is a visual-only pose, not a distinct named pose the player can be toggled
        // into/out of like Crawl - it's simply what's shown while airborne, regardless of which
        // pose (Walk or Crawl) the player was in when they left the ground (e.g. crawling off a
        // ledge still assumes the jump pose mid-air). Pose itself stays "Walk"/"Crawl" throughout;
        // only the resolved pose swaps to the Jump pose's clips while not grounded. Climbing/
        // hanging/swimming take priority over both: they're their own dedicated poses ("Climb",
        // "Hang"/"Clamber" depending on IsClambering, and "Swim"), shown regardless of IsGrounded.
        var resolvedPose = IsClimbing ? "Climb"
            : IsHanging ? (IsClambering ? "Clamber" : "Hang")
            : IsSwimming ? "Swim"
            : !IsGrounded ? "Jump"
            : Pose;
        // Facing is resolved from the player's own raw move input intent (see MoveIntentX),
        // never from Velocity.X, for every pose except Swim - velocity is influenced by whatever
        // the player is standing/riding on (a moving platform's carry, or leftover momentum for a
        // frame or two after leaving one), none of which reflects the player's own facing intent.
        // Swimming has no such platform-carry ambiguity and, unlike every other pose, is
        // deliberately requested by the user to read as "swimming left/right" for as long as
        // there's meaningful horizontal drift/momentum, even while only Up/Down is currently held
        // (e.g. still coasting sideways from an earlier stroke while now thrusting to surface) -
        // so it intentionally uses the actual horizontal Velocity.X (via the existing
        // velocity-based ResolveHorizontalFacing(double) overload, otherwise only used for
        // climbing's vertical axis) rather than raw intent, falling back to vertical facing only
        // once horizontal speed decays below GameDefaults.SwimHorizontalFacingDeadzone - i.e.
        // genuinely moving (near-)straight up/down with no material horizontal component at all.
        var facing = IsClimbing ? ResolveVerticalFacing()
            : IsSwimming ? (Math.Abs(Velocity.X) > GameDefaults.SwimHorizontalFacingDeadzone
                ? ResolveHorizontalFacing(Velocity.X)
                : ResolveVerticalFacing())
            : ResolveHorizontalFacing();
        SetPose(Sprite, resolvedPose, facing);
    }

}
