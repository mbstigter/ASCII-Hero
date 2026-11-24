using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Constants;

namespace ASCII_Hero.Client.Game.World;

/// <summary>The player-controlled character, backed by the loaded "Player" sprite asset.</summary>
public class Player2D : Body2D, IPhysicsBody, IGravityAffected, IMediumAffected, ICollectorBody, IKillerBody, IClimberBody, IHangerBody, ISwimmerBody, IPosedBody, IMotorBody, IHurtableBody
{
    /// <summary>Current velocity, in world cells per second.</summary>
    public Vector2D Velocity { get; set; }

    /// <summary>Wall push-off awaiting the next animation/sound update.</summary>
    internal bool WallJumpPending { get; set; }

    /// <inheritdoc/>
    public bool IsTouchingClimbable { get; set; }

    /// <inheritdoc/>
    public bool IsTouchingHangable { get; set; }

    /// <inheritdoc/>
    public bool IsClimbing { get; set; }

    /// <inheritdoc/>
    public bool IsHanging { get; set; }

    /// <summary>Derived, not stored: hanging while in the compact <see cref="Stance"/> ("Crawl").</summary>
    public bool IsClambering => IsHanging && Stance == "Crawl";

    /// <inheritdoc/>
    public bool IsSwimming { get; set; }

    /// <summary>
    /// The player is always subject to normal world gravity - including while
    /// <see cref="IsClimbing"/>/<see cref="IsHanging"/>: the grip on the climbable/hangable carries the
    /// weight instead (see <see cref="Physics.PhysicsSystem.UpdateMotorForce"/>), limited by the
    /// combined friction of hands and surface, with an equal and opposite reaction on a finite-mass
    /// gripped body. <see cref="IsSwimming"/> likewise keeps gravity, offset by the ambient
    /// buoyancy force (<see cref="IMediumAffected"/>, always-on, separate from this flag); the
    /// swim motor force only adds player-directed thrust on top of that.
    /// </summary>
    public bool GravityAffected => true;

    /// <summary>The player is always subject to ambient-medium buoyancy/drag.</summary>
    public bool MediumAffected => true;

    /// <summary>
    /// The player's deliberately chosen stance: "Walk" (upright / stretched) or "Crawl" (compact -
    /// shown as Clamber while hanging). Stored state toggled by Up/Down in <see cref="Physics.PhysicsSystem"/>
    /// and forced back to "Walk" whenever airborne (crawling only exists on the ground, or as
    /// Clamber while hanging). Distinct from the pose, which is the displayed result: see
    /// <see cref="ResolvedPose"/> and <see cref="UpdatePose"/>, which combine the stance with the
    /// current situation (climbing, hanging, swimming, airborne). See docs/AssetFormat.md §2.6.
    /// </summary>
    public string Stance { get; set; } = "Walk";

    /// <summary>
    /// Optional clip name (on this instance's own <see cref="Body2D.Sprite"/>) to play as a
    /// cosmetic effect on an ordinary (non-fatal) hazard hit. Null (the default) means no effect.
    /// </summary>
    public string? HurtEffectClipName { get; set; }

    /// <summary>Optional sound played when the player is hurt but survives.</summary>
    public string? HurtSoundName { get; set; }

    /// <summary>Optional sound played when the player loses a life.</summary>
    public string? KillSoundName { get; set; }

    /// <summary>
    /// Optional clip name (on this instance's own <see cref="Body2D.Sprite"/>) to play as a
    /// cosmetic effect when a <see cref="CollectableType.Checkpoint"/> is reached. Null (the
    /// default) means no effect. Kept as its own separate slot rather than reusing
    /// <see cref="HurtEffectClipName"/> so the player can show different effects for
    /// different situations - see
    /// <see cref="Physics.CollisionSystem.ResolveHazardsAndCollectables"/>.
    /// </summary>
    public string? CheckpointReachedEffectClipName { get; set; }

    /// <summary>
    /// Optional clip name to play as a cosmetic effect when a <see cref="CollectableType.WorldEnd"/>
    /// is reached. Null (the default) means no effect.
    /// </summary>
    public string? WorldEndReachedEffectClipName { get; set; }

    /// <summary>
    /// Optional clip name to play as a cosmetic effect when the player loses a life (see
    /// <see cref="PendingTransition.Death"/>). Null (the default) means no effect.
    /// </summary>
    public string? DiedEffectClipName { get; set; }

    /// <summary>
    /// Optional clip name to play as a cosmetic effect when a <see cref="CollectableType.HealthPack"/>
    /// restores health. Null (the default) means no effect.
    /// </summary>
    public string? HealedEffectClipName { get; set; }

    /// <summary>
    /// Optional clip name to play as a cosmetic effect when the player steps into a <see cref="Portal2D"/>,
    /// before the move to the target world. Null (the default) means no effect and no delay.
    /// </summary>
    public string? PortalEnteredEffectClipName { get; set; }

    /// <summary>Optional sound name played when the player steps into a portal.</summary>
    public string? PortalEnteredSoundName { get; set; }

    /// <summary>
    /// Optional clip name to play as a cosmetic effect when the player arrives in a world through a
    /// portal. Null (the default) means no effect and no wait.
    /// </summary>
    public string? PortalArrivedEffectClipName { get; set; }

    /// <summary>Optional sound name played when the player arrives in a world through a portal.</summary>
    public string? PortalArrivedSoundName { get; set; }

    /// <summary>
    /// The mass-scaled force this frame's sustained locomotion input contributes - computed each
    /// frame by <see cref="Physics.PhysicsSystem.Step"/> as a simple proportional "motor" force
    /// converging <see cref="Velocity"/> toward a target walk/crawl/climb/hang/swim velocity, summed
    /// into the net force alongside gravity by <see cref="Physics.PhysicsSystem.StepMovingBodyWithForces"/> -
    /// mirrors how <see cref="IPatrolBody.PatrolForce"/> contributes for a patrolling body.
    /// Horizontal-only while on the ground (Walk/Crawl); also carries a vertical component while
    /// <see cref="IsClimbing"/> (driving up/down movement), <see cref="IsHanging"/> (holding
    /// position against gravity, carried by the grip) or <see cref="IsSwimming"/> with Up/Down held
    /// (depth control) - all sustained, ongoing
    /// locomotion for as long as they're engaged, exactly like walking, so they share this same
    /// continuous force model rather than a direct velocity assignment. Jump-offs instead use
    /// instantaneous impulses; letting go without a jump applies no impulse.
    /// </summary>
    public Vector2D MotorForce { get; set; }

    /// <summary>
    /// True while <see cref="MotorForce"/> pushes horizontally against the ground (walking or
    /// crawling): that force is then the foot friction itself - traction-limited, with a reaction
    /// on the support - so contact friction must not be applied to the same ground contact again.
    /// </summary>
    public bool IsDrivingOnGround => IsGrounded && UsesFeetLocomotion;

    /// <summary>The player keeps its footing a little further past an edge than strict balance allows.</summary>
    public override double LedgeToleranceCells => GameDefaults.PlayerLedgeToleranceCells;

    /// <summary>
    /// True when the player moves by pushing with the feet (walking/crawling), i.e. not climbing,
    /// hanging or swimming. Unlike <see cref="IsDrivingOnGround"/> this does not depend on this
    /// frame's contacts, so the collision pass can use it while those contacts are still being
    /// rebuilt.
    /// </summary>
    public bool UsesFeetLocomotion => !IsClimbing && !IsHanging && !IsSwimming;

    /// <summary>
    /// "Muscle power" - the mass-scaled force gain applied to converge <see cref="Velocity"/>
    /// toward the current target walk/crawl/climb/hang/swim velocity (see
    /// <see cref="Physics.PhysicsSystem.UpdateMotorForce"/>). Defaults to
    /// <see cref="GameDefaults.MotorForceMultiplier"/>, but a placement may
    /// override it via the <c>MotorForceMultiplier</c> ini key (see <see cref="World2D.LoadAsync"/>) -
    /// same name/role as <see cref="DynamicObject2D.PatrolForceMultiplierX"/>/<see cref="DynamicObject2D.PatrolForceMultiplierY"/> for a patrolling object,
    /// since both represent the exact same "muscle power toward a target speed" concept. One
    /// single gain covers every sustained pose (Walk, Crawl, Climb, Hang, Swim) - they are all the same
    /// kind of ongoing, motor-driven locomotion, just converging toward a different target
    /// velocity depending on the current pose/state (see <see cref="Physics.PhysicsSystem.Step"/>).
    /// </summary>
    public double MotorForceMultiplier { get; set; } = GameDefaults.MotorForceMultiplier;


    /// <summary>
    /// Target ground speed (in world cells/second) while standing/walking (<see cref="Stance"/> ==
    /// "Walk"). Defaults to <see cref="GameDefaults.WalkSpeed"/>, but a placement
    /// may override it via the <c>WalkSpeed</c> ini key (see <see cref="World2D.LoadAsync"/>).
    /// </summary>
    public double WalkSpeed { get; set; } = GameDefaults.WalkSpeed;

    /// <summary>
    /// Target ground speed (in world cells/second) while crouched/crawling (<see cref="Stance"/> ==
    /// "Crawl"). Defaults to <see cref="GameDefaults.CrawlSpeed"/>, but a placement
    /// may override it via the <c>CrawlSpeed</c> ini key (see <see cref="World2D.LoadAsync"/>).
    /// </summary>
    public double CrawlSpeed { get; set; } = GameDefaults.CrawlSpeed;

    /// <summary>
    /// Current score: +1 per <see cref="CollectableType.Diamond"/>, +5 per <see cref="CollectableType.Ring"/>
    /// (see <see cref="Physics.CollisionSystem.ResolveHazardsAndCollectables"/>).
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// The gear slots the player carries, in <c>Global/Settings.ini</c> <c>[Gear]</c> order - always
    /// all of them, empty until something is picked up (see <see cref="Gear2D"/>); keys are carried
    /// here too, in the <see cref="Key2D.GearName"/> slot. Kept across respawns.
    /// </summary>
    public List<Gear2D> Gear { get; } = [];

    /// <summary>The <see cref="Gear"/> slot named <paramref name="name"/> (case-insensitive), or null when it isn't listed.</summary>
    public Gear2D? FindGear(string name) =>
        Gear.Find(gear => string.Equals(gear.Name, name, StringComparison.OrdinalIgnoreCase));

    private Gear2D? _selectedGear;

    /// <summary>
    /// The gear slot the action key uses (throwing an item, operating a gate with a key, or using a
    /// health pack), shown in the HUD's gear box: the one picked in the gear ring or, until one is
    /// picked, the first slot; null only without any <see cref="Gear"/>. It stays selected when it
    /// runs empty. Kept across respawns.
    /// </summary>
    public Gear2D? SelectedGear
    {
        get => _selectedGear ?? Gear.FirstOrDefault();
        set
        {
            if (value is not null && !Gear.Contains(value))
            {
                throw new ArgumentException("The selected gear must be one of the player's own gear slots.", nameof(value));
            }

            _selectedGear = value;
        }
    }

    /// <summary>
    /// The horizontal direction the player last moved or faced: -1 (left) or +1 (right). Throws go this
    /// way, also while standing still facing the screen.
    /// </summary>
    public double LastFacingX { get; private set; } = 1.0;

    /// <inheritdoc />
    public double StompDamage { get; set; } = GameDefaults.DefaultStompDamage;

    /// <summary>
    /// Health of the current life, from 0 to <see cref="GameDefaults.PlayerFullHealth"/> (shown as
    /// the HUD bar). Each hazard hit removes that hazard's <see cref="IHazardBody.Damage"/> (see
    /// <see cref="Physics.CollisionSystem.ResolveHazardsAndCollectables"/>). Reaching 0 costs one
    /// <see cref="Lives"/> and starts a <see cref="PendingTransition.Death"/>; the respawn that follows
    /// (see <see cref="World2D.Respawn"/>) refills the bar.
    /// </summary>
    public double Health { get; set; } = GameDefaults.PlayerFullHealth;

    /// <summary>
    /// Health removed by a hard landing - one whose closing speed with the surface landed on
    /// reaches <see cref="FallDamageImpactSpeed"/> (see <see cref="Physics.CollisionSystem"/>).
    /// Defaults to <see cref="GameDefaults.DefaultFallDamage"/>; the <c>FallDamage</c> ini key overrides it.
    /// </summary>
    public double FallDamage { get; set; } = GameDefaults.DefaultFallDamage;

    /// <summary>
    /// Minimum landing speed (cells/second) that causes <see cref="FallDamage"/>. Defaults to
    /// <see cref="GameDefaults.DefaultFallDamageImpactSpeed"/> (a 10-cell drop at default gravity);
    /// the <c>FallDamageImpactSpeed</c> ini key overrides it.
    /// </summary>
    public double FallDamageImpactSpeed { get; set; } = GameDefaults.DefaultFallDamageImpactSpeed;

    /// <summary>
    /// The safe window after surviving a hazard hit, during which hazards cannot hurt the player
    /// (hard landings are not affected). Its length is <see cref="GameDefaults.DefaultPlayerRecoverySeconds"/>
    /// unless the player's <c>RecoverySeconds</c> ini key overrides it.
    /// </summary>
    public HitRecovery Recovery { get; } = new(GameDefaults.DefaultPlayerRecoverySeconds);

    /// <summary>Remaining lives (shown in the HUD). Lost when <see cref="Health"/> runs out, gained every <see cref="World2D.RingsPerLife"/> rings.</summary>
    public int Lives { get; set; } = GameDefaults.PlayerStartingLives;

    /// <summary>Rings collected towards the next extra life; resets to 0 when it reaches <see cref="World2D.RingsPerLife"/>. Kept across respawns.</summary>
    public int Rings { get; set; }

    /// <summary>
    /// Takes over the progress a player carries from one world to another through a portal: score,
    /// lives, rings, health, gear (keys included) and the selected gear. Position and everything about the body stay as they are.
    /// </summary>
    public void CarryOverFrom(Player2D other)
    {
        Score = other.Score;
        Lives = other.Lives;
        Rings = other.Rings;
        Health = other.Health;
        foreach (var gear in Gear)
        {
            if (other.FindGear(gear.Name) is { } otherGear)
            {
                gear.TakeOverFrom(otherGear);
            }
        }

        _selectedGear = other._selectedGear is { } otherSelected ? FindGear(otherSelected.Name) : null;
    }

    public Player2D()
    {
        IsStatic = false;
    }

    /// <summary>Every pose <see cref="UpdatePose"/> can resolve; a posed player sprite must declare all of them.</summary>
    public static readonly IReadOnlyList<string> RequiredPoses = ["Walk", "Crawl", "Jump", "Climb", "Hang", "Clamber", "Swim"];

    /// <summary>Assigns the loaded Player sprite asset and activates its default pose/idle facing.</summary>
    public void Spawn(SpriteAsset sprite)
    {
        if (sprite.Poses is not null)
        {
            var missingPoses = RequiredPoses.Where(pose => !sprite.Poses.ContainsKey(pose)).ToList();
            if (missingPoses.Count > 0)
            {
                throw new InvalidDataException($"Player sprite '{sprite.Name}' is missing required pose(s): {string.Join(", ", missingPoses)}.");
            }

            Stance = sprite.DefaultPose ?? "Walk";
            SetPose(sprite, Stance, Facing.Idle);
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
    /// for every other intent-driven pose - the same intent-based rule <see cref="DynamicHazard2D"/>
    /// uses (set from its own patrol-direction decision instead of raw key input), now that both
    /// bodies share one "facing follows intent, not velocity" mechanism. Swimming is the one
    /// deliberate exception - see the facing resolution below.
    /// </summary>
    public void UpdatePose()
    {
        // "Jump" is a visual-only pose, not a distinct named pose the player can be toggled
        // into/out of like Crawl - it's simply what's shown while airborne. Mid-air the stance is
        // always Walk anyway (crawling off a ledge stands the player up the moment they leave the
        // ground - see PhysicsSystem.ReleaseAirborneCrawl), so only the resolved pose swaps to the
        // Jump pose's clips while not grounded. Climbing/
        // hanging/swimming take priority over both: they're their own dedicated poses ("Climb",
        // "Hang"/"Clamber" depending on IsClambering, and "Swim"), shown regardless of IsGrounded.
        if (!IsClimbing)
        {
            _gripClimbStyle = null;
        }

        var resolvedPose = IsClimbing ? ResolveClimbPose()
            : IsHanging ? (IsClambering ? "Clamber" : "Hang")
            : IsSwimming ? "Swim"
            : !IsGrounded ? "Jump"
            : Stance;
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
        if (MoveIntentX != 0)
        {
            LastFacingX = Math.Sign(MoveIntentX);
        }

        SetPose(Sprite, resolvedPose, facing);
        ResolvedPose = resolvedPose;
    }

    /// <summary>Style of the climbable grabbed when the current climb began; kept so crossing another style mid-climb doesn't switch pose.</summary>
    private ClimbStyle? _gripClimbStyle;

    /// <summary>"Climb" or, for a <see cref="ClimbStyle.Shimmy"/> surface, "Shimmy" (falling back to "Climb" if the sprite declares no Shimmy pose).</summary>
    private string ResolveClimbPose()
    {
        _gripClimbStyle ??= GetFirstContactingBody(Physics.ContactType.Climbable)?.ClimbStyle;
        return _gripClimbStyle == ClimbStyle.Shimmy && Sprite.Poses?.ContainsKey("Shimmy") == true ? "Shimmy" : "Climb";
    }

    /// <summary>
    /// The pose actually being shown after <see cref="UpdatePose"/> resolved it ("Walk", "Crawl",
    /// "Jump", "Climb", "Shimmy", "Hang", "Clamber" or "Swim"), as opposed to <see cref="Stance"/>, which only ever
    /// holds "Walk"/"Crawl". Used to pick the pose's own movement sound.
    /// </summary>
    public string ResolvedPose { get; private set; } = "Walk";

    /// <summary>
    /// What the player's limbs push against: the climbable/hangable it grips (including that
    /// surface's own running speed, see <see cref="Body2D.GetCarryVelocity"/>) while climbing or
    /// hanging, the water's flow while swimming, otherwise the ground or medium as for any body.
    /// </summary>
    protected override Vector2D GetLocomotionReferenceVelocity()
    {
        var grip = IsClimbing ? GetFirstContactingBody(Physics.ContactType.Climbable)
            : IsHanging ? GetFirstContactingBody(Physics.ContactType.Hangable)
            : null;
        return grip?.GetCarryVelocity()
            ?? (IsSwimming ? CurrentFlow : base.GetLocomotionReferenceVelocity());
    }

}
