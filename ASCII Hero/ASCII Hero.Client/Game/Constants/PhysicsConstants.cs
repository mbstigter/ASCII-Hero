namespace ASCII_Hero.Client.Game.Constants;

/// <summary>
/// Centralized engine-level physics tuning values shared by <see cref="Physics.PhysicsSystem"/>
/// and <see cref="Physics.CollisionSystem"/> - the low-level simulation machinery itself, as
/// opposed to <see cref="GameDefaults"/>'s gameplay-facing movement/speed/force tuning.
/// </summary>
public static class PhysicsConstants
{
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
    /// Deliberately generous relative to a typical ~1/60s frame (this only needs to guard against
    /// rare outlier frames, not shrink the ordinary per-frame cost) yet small enough that even a
    /// body moving at a high multiple of ordinary walking speed can't clear a full cell in one
    /// step. Not overridable via any ini file.
    /// </summary>
    public const double FixedPhysicsStepSeconds = 1.0 / 60.0;

    /// <summary>
    /// Default gravity acceleration, in world cells per second squared - see
    /// <see cref="World.World2D.Gravity"/>. Overridable via the <c>[Physics] Gravity</c> key in
    /// <c>Global/Settings.ini</c>.
    /// </summary>
    public const double DefaultGravity = 40;

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

    /// <summary>
    /// World-space size (in cells) of one broad-phase spatial-grid bucket - see
    /// <see cref="Physics.SpatialGrid{T}"/>. Not overridable via any ini file.
    /// </summary>
    public const double GridCellSize = 4.0;

    /// <summary>
    /// Number of times the solids-and-movers narrow phase re-detects and re-resolves every
    /// candidate contact each frame, so simultaneous contacts (a corner formed by two solids, a
    /// body sandwiched between two others) converge instead of fighting - see the remarks in
    /// <see cref="Physics.CollisionSystem.Resolve"/>. Not overridable via any ini file.
    /// </summary>
    public const int SolverIterations = 4;

    /// <summary>
    /// Tuning constant for <see cref="Physics.PhysicsSystem.ResolveMediumForceScale"/>'s
    /// reciprocal falloff - larger values make a given <see cref="Assets.Material.Viscosity"/>
    /// dampen self-generated force more aggressively. Applied to raw <c>Viscosity</c> directly
    /// (see <see cref="Physics.PhysicsSystem.ResolveMediumForceScale"/>'s own doc comment for
    /// why), so even <c>Air</c>'s own small authored baseline (0.02, see
    /// <c>Global/MaterialLibrary.ini</c>) applies a slight land-side penalty; the jump/walk force
    /// constants in <see cref="GameDefaults"/> are tuned to compensate so on-land feel stays close
    /// to its pre-damping baseline. Not overridable via any ini file.
    /// </summary>
    public const double MediumForceScaleFalloff = 40.0;

    /// <summary>
    /// Floor for <see cref="Physics.PhysicsSystem.ResolveMediumForceScale"/>'s force scaling in
    /// viscous mediums, so even the thickest configured medium never reduces self-generated force
    /// all the way to zero. Not overridable via any ini file.
    /// </summary>
    public const double MinMediumForceScale = 0.15;

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
