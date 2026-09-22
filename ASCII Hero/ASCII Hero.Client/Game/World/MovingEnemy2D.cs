using System;
using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Constants;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// An AI-controlled enemy that moves (patrols/chases) and damages the player on contact. Reuses
/// the same physics/collision handling as any other <see cref="IPhysicsBody"/> - it participates
/// in gravity/force integration and platform/world-bounds collision exactly like a
/// <see cref="DynamicObject2D"/>. Optional linear patrol (see <see cref="IPatrolBody"/>) is its
/// first movement behavior - contributed as a mass-scaled horizontal force, summed by
/// <see cref="Physics.PhysicsSystem.StepMovingBodyWithForces"/> alongside gravity, consistent with
/// every other non-player body's force-based movement rather than direct velocity assignment.
/// Chase behavior does not exist yet.
/// </summary>
public class MovingEnemy2D : Body2D, IPhysicsBody, IHazardBody, IGravityAffected, IMediumAffected, IPatrolBody, IPosedBody, IEffectTrigger, IKillableBody
{
    private bool _patrolMovingRight = true;
    private bool _patrolMovingDown = true;

    /// <summary>Current velocity, in world cells per second.</summary>
    public Vector2D Velocity { get; set; }

    /// <summary>Whether this enemy is subject to normal world gravity.</summary>
    public bool GravityAffected { get; set; } = true;

    /// <summary>Whether this enemy is subject to ambient-medium buoyancy/drag.</summary>
    public bool MediumAffected { get; set; } = true;

    /// <summary>Whether this enemy is currently patrolling on either axis (see <see cref="IPatrolBody"/>).</summary>
    public bool IsPatrolling { get; set; }

    /// <summary>Left bound of the horizontal patrol range, in world cells, or null if this body doesn't patrol on the X axis.</summary>
    public double? PatrolMinX { get; set; }

    /// <summary>Right bound of the horizontal patrol range, in world cells, or null if this body doesn't patrol on the X axis.</summary>
    public double? PatrolMaxX { get; set; }

    /// <summary>Top bound of the vertical patrol range, in world cells, or null if this body doesn't patrol on the Y axis.</summary>
    public double? PatrolMinY { get; set; }

    /// <summary>Bottom bound of the vertical patrol range, in world cells, or null if this body doesn't patrol on the Y axis.</summary>
    public double? PatrolMaxY { get; set; }

    /// <summary>
    /// The force this body's patrol currently contributes - independently computed per axis (see
    /// <see cref="UpdatePatrolDirection"/>), so a body configured with both X and Y bounds
    /// patrols diagonally (each axis bounces between its own bounds on its own schedule) with no
    /// separate "diagonal mode" needed.
    /// </summary>
    public Vector2D PatrolForce { get; private set; }

    /// <summary>
    /// "Muscle power" - the mass-scaled force gain this body applies to converge its velocity
    /// toward <see cref="PatrolCruiseSpeed"/> while patrolling (see <see cref="UpdatePatrolDirection"/>)
    /// - configured per-placement via <see cref="SetPatrol"/>'s <c>forceMultiplier</c> parameter
    /// (the ini <c>PatrolForceMultiplier</c> key), defaulting to <see cref="GameDefaults.PatrolForceMultiplier"/> so
    /// existing placements that don't set it are unaffected. Higher values reach the cruising
    /// speed sooner (a stronger "motor"), but no longer change the cruising speed itself - see
    /// <see cref="PatrolCruiseSpeedX"/> for that.
    /// </summary>
    public double PatrolForceMultiplier { get; set; } = GameDefaults.PatrolForceMultiplier;

    /// <summary>
    /// The target horizontal speed (in world cells/second) this body's X-axis patrol force
    /// converges toward and holds - configured per-placement via <see cref="SetPatrol"/>'s
    /// <c>cruiseSpeedX</c> parameter (the ini <c>PatrolCruiseSpeedX</c> key), defaulting to
    /// <see cref="GameDefaults.PatrolCruiseSpeed"/>. Mirrors the player's own fixed walk/crawl speeds -
    /// unlike <see cref="PatrolForceMultiplier"/> (how strongly/quickly it gets there), this is
    /// what actually caps the steady-state patrol speed, preventing the unbounded acceleration a
    /// constant-thrust-only force would otherwise produce.
    /// </summary>
    public double PatrolCruiseSpeedX { get; set; } = GameDefaults.PatrolCruiseSpeed;

    /// <summary>
    /// The target vertical speed (in world cells/second) this body's Y-axis patrol force converges
    /// toward and holds - configured per-placement via <see cref="SetPatrol"/>'s
    /// <c>cruiseSpeedY</c> parameter (the ini <c>PatrolCruiseSpeedY</c> key), defaulting to
    /// <see cref="GameDefaults.PatrolCruiseSpeed"/>. Independent of <see cref="PatrolCruiseSpeedX"/> so a
    /// diagonally-patrolling body can travel at a different pace on each axis.
    /// </summary>
    public double PatrolCruiseSpeedY { get; set; } = GameDefaults.PatrolCruiseSpeed;

    /// <summary>
    /// Current pose (see <see cref="Player2D.Pose"/> for the equivalent player-side member).
    /// Only one pose exists today ("Move", with idle/left/right facing clips - see
    /// <see cref="UpdatePose"/>), but kept as a settable member rather than a hardcoded literal so
    /// a future MovingEnemy asset with multiple poses (e.g. a distinct "Attack" pose) doesn't
    /// need a new mechanism.
    /// </summary>
    public string Pose { get; set; } = "Move";

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

    /// <summary>
    /// Configures linear patrol independently on the X and/or Y axis (see <see cref="IPatrolBody"/>) -
    /// either axis pair left null (both min and max) means that axis simply isn't patrolled, so a
    /// body configured with both X and Y bounds patrols diagonally, each axis bouncing between its
    /// own bounds on its own schedule. Optionally overrides the patrol force's strength and/or each
    /// axis's initial direction.
    /// </summary>
    /// <param name="patrolMinX">Left bound of the horizontal patrol range, or null to not patrol on X.</param>
    /// <param name="patrolMaxX">Right bound of the horizontal patrol range, or null to not patrol on X.</param>
    /// <param name="cruiseSpeedX">
    /// Target horizontal patrol cruising speed (see <see cref="PatrolCruiseSpeedX"/>); defaults to
    /// <see cref="GameDefaults.PatrolCruiseSpeed"/> if not given.
    /// </param>
    /// <param name="patrolMinY">Top bound of the vertical patrol range, or null to not patrol on Y.</param>
    /// <param name="patrolMaxY">Bottom bound of the vertical patrol range, or null to not patrol on Y.</param>
    /// <param name="cruiseSpeedY">
    /// Target vertical patrol cruising speed (see <see cref="PatrolCruiseSpeedY"/>); defaults to
    /// <see cref="GameDefaults.PatrolCruiseSpeed"/> if not given.
    /// </param>
    /// <param name="forceMultiplier">
    /// Patrol force gain / "muscle power" (see <see cref="PatrolForceMultiplier"/>), shared by
    /// both axes; defaults to <see cref="GameDefaults.PatrolForceMultiplier"/> if not given.
    /// </param>
    /// <param name="initialDirectionRight">
    /// Which way to start heading on the X axis. If null (the default), starts heading toward
    /// whichever bound is farther from the spawn position, so a body spawned near one end still
    /// immediately patrols across the full range instead of instantly hitting the near bound and
    /// turning back within the first frame or two. An explicit value (true = right, false = left)
    /// overrides that inference - e.g. to make a placement's enemy visibly start off moving toward
    /// the player instead.
    /// </param>
    /// <param name="initialDirectionDown">
    /// Which way to start heading on the Y axis. Same semantics as
    /// <paramref name="initialDirectionRight"/>, but for <see cref="PatrolMinY"/>/<see cref="PatrolMaxY"/>
    /// (true = toward <see cref="PatrolMaxY"/>/down, false = toward <see cref="PatrolMinY"/>/up).
    /// </param>
    public void SetPatrol(
        double? patrolMinX, double? patrolMaxX, double cruiseSpeedX,
        double? patrolMinY, double? patrolMaxY, double cruiseSpeedY,
        double forceMultiplier = GameDefaults.PatrolForceMultiplier,
        bool? initialDirectionRight = null, bool? initialDirectionDown = null)
    {
        PatrolMinX = patrolMinX;
        PatrolMaxX = patrolMaxX;
        PatrolCruiseSpeedX = cruiseSpeedX;
        PatrolMinY = patrolMinY;
        PatrolMaxY = patrolMaxY;
        PatrolCruiseSpeedY = cruiseSpeedY;
        PatrolForceMultiplier = forceMultiplier;
        IsPatrolling = (patrolMinX.HasValue && patrolMaxX.HasValue) || (patrolMinY.HasValue && patrolMaxY.HasValue);

        // Start heading toward whichever bound is farther, so a body spawned near one end
        // immediately patrols across the full range instead of instantly hitting the near bound
        // and turning back within the first frame or two - unless the caller explicitly requested
        // a starting direction instead. Only meaningful (and only computed) when that axis is
        // actually patrolled.
        if (patrolMinX.HasValue && patrolMaxX.HasValue)
        {
            _patrolMovingRight = initialDirectionRight ?? (Position.X - patrolMinX.Value <= patrolMaxX.Value - Position.X);
        }
        if (patrolMinY.HasValue && patrolMaxY.HasValue)
        {
            _patrolMovingDown = initialDirectionDown ?? (Position.Y - patrolMinY.Value <= patrolMaxY.Value - Position.Y);
        }
    }

    /// <summary>
    /// Recomputes <see cref="PatrolForce"/> independently per axis, flipping each axis's own
    /// heading once within <see cref="GameDefaults.PatrolTurnThreshold"/> of its current target bound - mirrors
    /// <see cref="Physics.PhysicsSystem"/>'s own player-side <c>UpdateWalkForce</c>, so patrol
    /// force behaves the same way the player's walk force does: strong while far from the target
    /// speed, tapering to zero once reached, rather than a constant thrust that would otherwise
    /// accelerate this body indefinitely. Also sets <see cref="Body2D.MoveIntentX"/> from the
    /// (possibly just-flipped) X-axis heading - <see cref="UpdatePose"/> reads it from there rather
    /// than from <see cref="Velocity"/>, since velocity reflects this body's actual,
    /// physically-resolved motion (still converging toward the target speed, and subject to
    /// platform carry) rather than its plain directional intent.
    ///
    /// The Y axis applies one extra rule beyond the plain proportional "motor" force: while
    /// <see cref="GravityAffected"/> and heading up, gravity's own contribution (added separately
    /// by <see cref="Physics.PhysicsSystem.StepMovingBodyWithForces"/>) is cancelled out here so the
    /// same proportional gain that already works horizontally can actually reach its target speed
    /// against a constant opposing pull, instead of asymptoting below it ("fighting gravity to
    /// climb"). Heading down needs no such adjustment - the plain proportional force already
    /// naturally lets gravity's own pull carry most of the descent, only correcting once actual
    /// velocity overshoots the target ("gliding down").
    /// </summary>
    public void UpdatePatrolDirection(double gravity)
    {
        if (!IsPatrolling)
        {
            PatrolForce = new Vector2D(0, 0);
            MoveIntentX = 0.0;
            return;
        }

        var mass = Mass > 0 ? Mass : 1.0;
        double forceX = 0.0;
        double forceY = 0.0;

        if (PatrolMinX.HasValue && PatrolMaxX.HasValue)
        {
            var targetX = _patrolMovingRight ? PatrolMaxX.Value : PatrolMinX.Value;
            if (Math.Abs(Position.X - targetX) <= GameDefaults.PatrolTurnThreshold)
            {
                _patrolMovingRight = !_patrolMovingRight;
            }

            // Target speed (and the force converging toward it) is expressed relative to whatever
            // solid this body currently rests on (see Body2D.GetSurfaceVelocityX) - mirroring
            // PhysicsSystem.Step's own player-side groundVelocityX fix - rather than an absolute
            // world-frame speed. Without this, a fast horizontal platform's own carry is baked into
            // this body's absolute Velocity.X, so the patrol force (aimed at an absolute target
            // speed) would spend part of its "muscle power" fighting the platform's own motion every
            // frame instead of just walking across it, letting the body slide relative to the
            // platform's surface instead of patrolling it at a steady cruise speed.
            var surfaceVelocityX = GetSurfaceVelocityX();
            var targetVelocityX = surfaceVelocityX + (_patrolMovingRight ? 1.0 : -1.0) * PatrolCruiseSpeedX;
            forceX = (targetVelocityX - Velocity.X) * mass * PatrolForceMultiplier;

            // Facing intent (see Body2D.MoveIntentX) is simply this patrol's current heading -
            // an AI has no separate "input" to read, so its patrol direction stands in for intent,
            // same as Player2D.MoveIntentX stands in for the player's own key state. Set here
            // (rather than in UpdatePose) since this is where the heading itself is actually
            // decided/flipped.
            MoveIntentX = _patrolMovingRight ? 1.0 : -1.0;
        }
        else
        {
            MoveIntentX = 0.0;
        }

        if (PatrolMinY.HasValue && PatrolMaxY.HasValue)
        {
            var targetY = _patrolMovingDown ? PatrolMaxY.Value : PatrolMinY.Value;
            if (Math.Abs(Position.Y - targetY) <= GameDefaults.PatrolTurnThreshold)
            {
                _patrolMovingDown = !_patrolMovingDown;
            }

            var targetVelocityY = (_patrolMovingDown ? 1.0 : -1.0) * PatrolCruiseSpeedY;
            forceY = (targetVelocityY - Velocity.Y) * mass * PatrolForceMultiplier;

            if (GravityAffected && !_patrolMovingDown)
            {
                // Heading up: cancel gravity's own contribution so this proportional force can
                // actually reach its target climb speed instead of asymptoting below it.
                forceY -= mass * gravity;
            }
        }

        PatrolForce = new Vector2D(forceX, forceY);
    }

    /// <summary>
    /// Resolves and applies this enemy's pose (see <see cref="IPosedBody"/>) from its current
    /// patrol-direction intent (see <see cref="Body2D.MoveIntentX"/>, set by
    /// <see cref="UpdatePatrolDirection"/>) via the shared <see cref="Body2D.ResolveHorizontalFacing()"/>
    /// rule <see cref="Player2D"/> also uses - a body with no matching pose (<c>Sprite.Poses</c>
    /// null, e.g. a MovingEnemy asset that hasn't authored left/right clips) simply no-ops here
    /// (see <see cref="Body2D.SetPose"/>). Resolving against <see cref="Velocity"/> directly would
    /// flip facing to match whichever direction a fast-moving platform happens to be carrying this
    /// body, even while its own patrol intent hasn't changed.
    /// </summary>
    public void UpdatePose()
    {
        SetPose(Sprite, Pose, ResolveHorizontalFacing());
    }
}

