namespace ASCII_Hero.Client.Game.Constants;

/// <summary>
/// Centralized engine-level physics tuning values shared by <see cref="Physics.PhysicsSystem"/>
/// and <see cref="Physics.CollisionSystem"/> - the low-level simulation machinery itself, as
/// opposed to <see cref="GameDefaults"/>'s gameplay-facing movement/speed/force tuning.
/// </summary>
public static class PhysicsConstants
{
    // --- Time stepping ---

    /// <summary>
    /// The fixed duration of every single Physics/Collision update (<see cref="Physics.PhysicsSystem.Step"/>
    /// plus the caller's own <see cref="Physics.CollisionSystem.Resolve"/> call) - see
    /// <see cref="GameLoop.OnPlayingFrameAsync"/>, which accumulates each real animation frame's
    /// reported <c>deltaSeconds</c> and drains that accumulator in however many whole steps of
    /// exactly this size are currently available, carrying any leftover remainder forward to next
    /// frame's accumulator rather than folding it into an odd-sized partial step (the standard
    /// "fix your timestep" pattern). Force integration (see
    /// <see cref="Physics.PhysicsSystem.StepMovingBodyWithForces"/>) scales velocity and position
    /// directly by <c>deltaSeconds</c>, and collision detection (see
    /// <see cref="Physics.CollisionSystem"/>) is purely discrete (no continuous/swept detection) -
    /// both an unusually large step (e.g. after a real frame hitch - GC pause, a slow JS interop
    /// round-trip, a dropped/re-entrant frame, see <c>GameLoop._isProcessingFrame</c> - which
    /// could move a body far enough in one step to jitter erratically or tunnel straight through
    /// a wall) and an inconsistent step size varying frame to frame with ordinary frame-rate
    /// jitter (which, even well short of any tunneling risk, was enough on its own to visibly
    /// flicker a body's pose right at a grounded/airborne boundary, since the exact instant
    /// contact is gained/lost - and how far ahead of that instant the query lands - shifts based
    /// on the immediately preceding step's size) are avoided by this constant always being the
    /// step size, deterministically, regardless of how the frame rate happens to fluctuate.
    /// Fast bodies are additionally substepped (see <see cref="MaxStepTravelCells"/>) so they
    /// cannot tunnel through thin walls. Not overridable via any ini file.
    /// </summary>
    public const double FixedPhysicsStepSeconds = 1.0 / 60.0;

    /// <summary>
    /// Furthest (in cells) any moving body may travel in a single physics (sub)step. Collision
    /// detection is discrete and resolves along the shallowest overlap, so a body that moves more
    /// than about half of a one-cell wall in one step can be pushed out the far side (tunnel).
    /// When the fastest body would exceed this, the fixed step is split into equal substeps - see
    /// <c>GameLoop.CountSubsteps</c>. Not overridable via any ini file.
    /// </summary>
    public const double MaxStepTravelCells = 0.4;

    /// <summary>
    /// Upper bound on substeps per fixed step (see <see cref="MaxStepTravelCells"/>), guarding
    /// against runaway cost. Not overridable via any ini file.
    /// </summary>
    public const int MaxSubsteps = 8;

    // --- World ---

    /// <summary>
    /// Default gravity acceleration, in world cells per second squared - see
    /// <see cref="World.World2D.Gravity"/>. Overridable via the <c>[Physics] Gravity</c> key in
    /// <c>Global/Settings.ini</c>.
    /// </summary>
    public const double DefaultGravity = 40;

    // --- Collision solver ---

    /// <summary>
    /// World-space size (in cells) of one broad-phase spatial-grid bucket - see
    /// <see cref="Physics.SpatialGrid{T}"/>. Not overridable via any ini file.
    /// </summary>
    public const double GridCellSize = 4.0;

    /// <summary>
    /// Number of times the solids-and-movers narrow phase re-detects and re-resolves every
    /// candidate contact each step, so simultaneous contacts (a corner formed by two solids, a
    /// body sandwiched between two others, a stack of crates with the player on top) converge
    /// instead of fighting - see the remarks in <see cref="Physics.CollisionSystem.Resolve"/>.
    /// Not overridable via any ini file.
    /// </summary>
    public const int SolverIterations = 8;

    /// <summary>
    /// Impact speed (cells/second, along the contact normal) below which a collision is treated
    /// as fully inelastic (no bounce) regardless of restitution - see
    /// <see cref="Physics.CollisionSystem"/>'s <c>EffectiveRestitution</c>. Set just above the
    /// speed default gravity adds in one fixed step (40 * 1/60 = ~0.67), so a resting body settles
    /// instead of endlessly micro-bouncing on that per-step gain. Not overridable via any ini file.
    /// </summary>
    public const double RestingBounceSpeedThreshold = 1.0;

    // --- Friction / traction ---

    /// <summary>
    /// Calibration factor on the grounded traction limit (combined friction x weight) for the
    /// player's walk/crawl force - see <see cref="Physics.PhysicsSystem.UpdateWalkForce"/>. The
    /// walk motor's initial acceleration is <see cref="GameDefaults.WalkForceMultiplier"/> x
    /// <see cref="GameDefaults.WalkSpeed"/> = 264 cells/s^2, while plain friction x gravity is only
    /// ~29 cells/s^2 on concrete (0.71 x 40). Game gravity and character proportions are
    /// stylised, so the grip is scaled to match: at 10 the limit (~286 on concrete) does not bind
    /// on ordinary floors, while genuinely slippery pairings still cap acceleration and braking.
    /// Not overridable via any ini file.
    /// </summary>
    public const double GroundTractionMultiplier = 10.0;

    // --- Climbing / hanging ---

    /// <summary>
    /// Speed (in cells/second) above which a body is moving too fast to snap onto a
    /// climbable/hangable surface on first touch; it keeps falling/moving through instead. Not
    /// overridable via any ini file.
    /// </summary>
    public const double MaxSnapSpeed = 24.0;

    /// <summary>
    /// Amount <see cref="Physics.CollisionSystem.SnapOntoHangable"/> pulls the hanger's top edge
    /// above the hangable surface's bottom edge, keeping the two rectangles genuinely overlapping
    /// (per <see cref="Physics.Rect.Overlaps"/>'s strict inequalities) rather than merely
    /// touching, so <see cref="World.IHangerBody.IsTouchingHangable"/> stays true on the frame
    /// right after snapping. Not overridable via any ini file.
    /// </summary>
    public const double HangOverlapEpsilon = 0.01;

    // --- Kinematic platforms ---

    /// <summary>
    /// Maximum acceleration (cells/second squared) of a patrolling kinematic platform - see
    /// <see cref="World.KinematicObject2D.Move"/>. A platform brakes smoothly to a stop at each
    /// end of its route and speeds up again from rest, instead of reversing velocity instantly,
    /// so riders feel a finite acceleration they can hold on to by ordinary friction. Not
    /// overridable via any ini file.
    /// </summary>
    public const double KinematicPlatformAcceleration = 20.0;

    // --- Swimming ---
    /// <summary>
    /// Minimum <see cref="Assets.Material.Density"/> for a body's <see cref="World.Body2D.CurrentMedium"/>
    /// to count as "swimmable" (see <see cref="Physics.PhysicsSystem.IsSwimmableMedium"/>) - set
    /// well above <c>Air</c>'s authored 0.0012 but at/below <c>Water</c>'s 1.0 (see
    /// <c>Global/MaterialLibrary.ini</c>), so ordinary air never engages swim while any
    /// water-like medium does. A medium clearing either this or
    /// <see cref="SwimMediumMinViscosity"/> qualifies - see that constant's own doc comment for
    /// why an "either" rule was chosen over requiring both. Not overridable via any ini file.
    /// </summary>
    public const double SwimMediumMinDensity = 0.5;

    /// <summary>
    /// Minimum <see cref="Assets.Material.Viscosity"/> for a body's <see cref="World.Body2D.CurrentMedium"/>
    /// to count as "swimmable" (see <see cref="Physics.PhysicsSystem.IsSwimmableMedium"/>) - set
    /// well above <c>Air</c>'s authored 0.02 but at/below <c>Water</c>'s 0.15 (see
    /// <c>Global/MaterialLibrary.ini</c>). Checked independently of <see cref="SwimMediumMinDensity"/>
    /// (a medium qualifies if it clears either threshold) so a hypothetical dense-but-thin fluid,
    /// or a thin-but-viscous one, still reads as swimmable rather than requiring an unrealistic
    /// combination of both properties at once. Not overridable via any ini file.
    /// </summary>
    public const double SwimMediumMinViscosity = 0.1;
}
