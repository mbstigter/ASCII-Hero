namespace ASCII_Hero.Client.Game.Constants;

/// <summary>
/// Centralized gameplay-facing movement/speed/force tuning defaults - as opposed to
/// <see cref="PhysicsConstants"/>'s low-level engine tuning. Grouped by locomotion
/// category (ground/climb/hang/clamber/swim speeds, jump speeds, force multipliers, patrol,
/// camera, misc) regardless of whether a given value is ini-overridable - see each constant's own
/// doc comment for its override path, if any.
/// </summary>
public static class GameDefaults
{
    // --- Locomotion speeds (ground/climb/hang/clamber/swim), in world cells/second ---

    /// <summary>
    /// Default target ground speed while standing/walking - see <see cref="World.Player2D.WalkSpeed"/>.
    /// Overridable per-placement via the <c>WalkSpeed</c> ini key in a world's <c>objects.ini</c>.
    /// </summary>
    public const double WalkSpeed = 12.0;

    /// <summary>
    /// Default target ground speed while crouched/crawling - see <see cref="World.Player2D.CrawlSpeed"/>.
    /// Overridable per-placement via the <c>CrawlSpeed</c> ini key in a world's <c>objects.ini</c>.
    /// </summary>
    public const double CrawlSpeed = 6.0;

    /// <summary>
    /// Target horizontal speed while climbing a ladder - lets the player step off sideways onto an
    /// adjacent floor/ladder while climbing rather than only ever being able to leave via a jump
    /// (see <see cref="Physics.PhysicsSystem.Step"/>). Not overridable via any ini file.
    /// </summary>
    public const double ClimbHorizontalSpeed = 8.0;

    /// <summary>
    /// Target vertical climb speed while climbing a ladder. Not overridable via any ini file.
    /// </summary>
    public const double ClimbVerticalSpeed = 10.0;

    /// <summary>
    /// Target movement speed while hanging from a pipe/bar - its own dedicated (slower) speed
    /// rather than reusing the ground Walk/Crawl speeds, since swinging/shimmying along a
    /// hangable surface is its own distinct kind of locomotion. Not overridable via any ini file.
    /// </summary>
    public const double HangSpeed = 8.0;

    /// <summary>
    /// Target movement speed while clambering - the crouched, arms-and-legs-gripping counterpart
    /// to <see cref="HangSpeed"/>'s fully-stretched hang (the "Crawl" to hang's "Walk", if you
    /// will) - slower still than <see cref="HangSpeed"/>. Not overridable via any ini file.
    /// </summary>
    public const double ClamberSpeed = 5.0;

    /// <summary>
    /// Target movement speed while swimming. Reserved for the planned Swim capability (see
    /// docs/Design.md's "Swim stance" item) - not yet consumed anywhere, since no swim pose exists
    /// yet. Not overridable via any ini file.
    /// </summary>
    public const double SwimSpeed = 8.0;

    // --- Jump-off speeds (instantaneous velocity kicks, not sustained speeds), in world cells/second ---

    // Jump-off impulses: an instantaneous velocity change (Delta-v), not a target speed to
    // converge toward like the continuous locomotion speeds above - a real jump/push-off is over
    // in a single instant (the leg/arm extends and releases contact), not something sustained
    // across multiple frames, so it is modeled as one direct vertical velocity kick (added to
    // whatever vertical velocity already exists - see the jump-off sites in
    // Physics.PhysicsSystem.Step) rather than a force integrated over time. Graded by how much of
    // the body's own momentum/leverage backs the push-off: solid ground under both feet gives the
    // strongest launch, a ladder rung under just hands/feet is weaker, and swinging free from a
    // single-handed pipe/rope grip is weakest.

    /// <summary>Instantaneous vertical velocity kick for jumping off solid ground. Not overridable via any ini file.</summary>
    public const double WalkJumpSpeed = 40.0;

    /// <summary>Instantaneous vertical velocity kick for jumping off a ladder. Not overridable via any ini file.</summary>
    public const double ClimbJumpSpeed = 18.0;

    /// <summary>Instantaneous vertical velocity kick for jumping off while hanging. Not overridable via any ini file.</summary>
    public const double HangJumpSpeed = 18.0;

    // --- Force multipliers ---

    /// <summary>
    /// Default magnitude of the mass-scaled horizontal "motor" force applied to converge the
    /// player's <see cref="World.Player2D.Velocity"/>.X toward the current target walk/crawl/
    /// climb/hang speed (see <see cref="Physics.PhysicsSystem.UpdateWalkForce"/>) - same name/role
    /// as <see cref="PatrolForceMultiplier"/> for a patrolling enemy, proportional to the
    /// remaining speed gap so the player accelerates promptly yet still settles at exactly the
    /// target speed rather than overshooting it every frame. Overridable per-placement via the
    /// <c>WalkForceMultiplier</c> ini key in a world's <c>objects.ini</c> - see
    /// <see cref="World.Player2D.WalkForceMultiplier"/>.
    /// </summary>
    public const double WalkForceMultiplier = 40.0;

    /// <summary>
    /// Fraction of the ordinary (grounded) horizontal walk-force strength applied while airborne -
    /// deliberately much weaker than the full ground motor so that horizontal velocity at the
    /// moment of jump-off (which may include a moving platform's own carried speed, per
    /// <see cref="World.Body2D.GetSurfaceVelocityX"/>) decays/steers gradually over the jump's arc
    /// instead of snapping to the absolute walk-speed target within a single frame. The full-
    /// strength ground motor exists to make grounded input feel immediately responsive; in the air
    /// there is no such "instantly responsive" expectation, and a real jumping body's horizontal
    /// momentum is governed far more by whatever speed it left the ground with than by mid-air
    /// steering input, so a soft, gradual air-control force is the more physically honest (and more
    /// forgiving-feeling) choice. Not overridable via any ini file.
    /// </summary>
    public const double AirControlMultiplier = 0.25;

    // --- Patrol (enemies / kinematic platforms) ---

    /// <summary>
    /// Default "muscle power" - the mass-scaled force gain applied to converge a patrolling
    /// body's horizontal velocity toward <see cref="PatrolCruiseSpeed"/> (see
    /// <see cref="World.MovingEnemy2D.UpdatePatrolDirection"/>) - same name/role as
    /// <see cref="WalkForceMultiplier"/> for the player. Overridable per-placement via the
    /// <c>PatrolForceMultiplier</c> ini key in a world's <c>objects.ini</c> (see
    /// <see cref="World.MovingEnemy2D.PatrolForceMultiplier"/>).
    /// </summary>
    public const double PatrolForceMultiplier = 60.0;

    /// <summary>
    /// Default patrol cruising speed (in world cells/second) - chosen to feel comparable to
    /// <see cref="CrawlSpeed"/>, a readable, deliberate pace rather than a full walking sprint.
    /// Overridable per-placement via the <c>PatrolCruiseSpeedX</c>/<c>PatrolCruiseSpeedY</c> ini
    /// keys in a world's <c>objects.ini</c> (see <see cref="World.MovingEnemy2D.PatrolCruiseSpeedX"/>).
    /// </summary>
    public const double PatrolCruiseSpeed = 6.0;

    /// <summary>
    /// How close (in world cells) a patrolling body's leading edge must get to a patrol bound
    /// before turning around, so it reverses just shy of the bound rather than oscillating exactly
    /// on it - shared by <see cref="World.MovingEnemy2D"/> and <see cref="World.KinematicObject2D"/>.
    /// Not overridable via any ini file.
    /// </summary>
    public const double PatrolTurnThreshold = 0.25;

    // --- Camera ---

    /// <summary>
    /// How quickly the camera catches up once it starts scrolling (higher = snappier) - see
    /// <see cref="Rendering.Camera.FollowSpeed"/>. Not overridable via any ini file.
    /// </summary>
    public const double CameraFollowSpeed = 8.0;

    /// <summary>
    /// How close (in world cells) the camera's follow target's bounding box may get to the edge of
    /// the current view before the camera starts scrolling to keep up with it - see
    /// <see cref="Rendering.Camera.EdgeMarginCells"/>. Not overridable via any ini file.
    /// </summary>
    public const double CameraEdgeMarginCells = 6.0;

    // --- Misc ---

    /// <summary>
    /// Fallback lifetime, in seconds, used by <see cref="World.EffectInstance2D"/> when the
    /// spawned clip has no configured frame duration (i.e. isn't animated). Not overridable via
    /// any ini file.
    /// </summary>
    public const double EffectLifetimeSeconds = 0.5;
}
