using System;
using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Constants;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A non-player object that moves under its own velocity, gravity and ambient-medium forces
/// (e.g. a bouncing ball, a drifting leaf, a patrolling critter). Unlike <see cref="Player2D"/> it is
/// never driven by input; unlike <see cref="StaticObject2D"/> it participates in motion integration
/// and bounces off world bounds and platforms. Optional linear patrol (see <see cref="IPatrolBody"/>)
/// is contributed as a mass-scaled force, summed by
/// <see cref="Physics.PhysicsSystem.StepMovingBodyWithForces"/> alongside gravity, consistent with
/// every other non-player body's force-based movement rather than direct velocity assignment.
/// Chase behavior does not exist yet. <see cref="DynamicHazard2D"/> extends this with hazard
/// capabilities.
/// </summary>
public class DynamicObject2D : Body2D, IPhysicsBody, IGravityAffected, IMediumAffected, IPatrolBody, IPosedBody
{
    private bool _patrolMovingRight = true;
    private bool _patrolMovingDown = true;

    /// <summary>Current velocity, in world cells per second.</summary>
    public Vector2D Velocity { get; set; }

    /// <summary>Whether this object is subject to normal world gravity.</summary>
    public bool GravityAffected { get; set; } = true;

    /// <summary>Whether this object is subject to ambient-medium buoyancy/drag.</summary>
    public bool MediumAffected { get; set; } = true;

    /// <summary>Whether this object is currently patrolling (see <see cref="SetPatrol"/>).</summary>
    public bool IsPatrolling { get; set; }

    /// <summary>Left bound of the horizontal patrol range, in world cells. Equal to <see cref="PatrolMaxX"/> when the body holds its spawn column instead of patrolling.</summary>
    public double? PatrolMinX { get; set; }

    /// <summary>Right bound of the horizontal patrol range, in world cells (see <see cref="PatrolMinX"/>).</summary>
    public double? PatrolMaxX { get; set; }

    /// <summary>Top bound of the vertical patrol range, in world cells. Equal to <see cref="PatrolMaxY"/> when the body holds its spawn height instead of patrolling.</summary>
    public double? PatrolMinY { get; set; }

    /// <summary>Bottom bound of the vertical patrol range, in world cells (see <see cref="PatrolMinY"/>).</summary>
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
    /// toward the cruise speed (<see cref="PatrolCruiseSpeedX"/>) while patrolling (see <see cref="UpdatePatrolDirection"/>)
    /// - configured per-placement via <see cref="SetPatrol"/>'s <c>forceMultiplierX</c> parameter
    /// (the ini <c>PatrolForceMultiplierX</c> key),
    /// defaulting to <see cref="GameDefaults.PatrolForceMultiplier"/> so existing placements that
    /// don't set it are unaffected. Higher values reach the cruising speed sooner (a stronger
    /// "motor"), but no longer change the cruising speed itself - see
    /// <see cref="PatrolCruiseSpeedX"/> for that. This is the X-axis gain; see
    /// <see cref="PatrolForceMultiplierY"/> for the Y axis.
    /// </summary>
    public double PatrolForceMultiplierX { get; set; } = GameDefaults.PatrolForceMultiplier;

    /// <summary>
    /// The Y-axis counterpart of <see cref="PatrolForceMultiplierX"/> (ini <c>PatrolForceMultiplierY</c> key,
    /// independent of X).
    /// </summary>
    public double PatrolForceMultiplierY { get; set; } = GameDefaults.PatrolForceMultiplier;

    /// <summary>
    /// The target horizontal speed (in world cells/second) this body's X-axis patrol force
    /// converges toward and holds - configured per-placement via <see cref="SetPatrol"/>'s
    /// <c>cruiseSpeedX</c> parameter (the ini <c>PatrolCruiseSpeedX</c> key), defaulting to
    /// <see cref="GameDefaults.PatrolCruiseSpeed"/>. Mirrors the player's own fixed walk/crawl speeds -
    /// unlike <see cref="PatrolForceMultiplierX"/> (how strongly/quickly it gets there), this is
    /// what actually caps the speed the force converges to, which a constant-thrust-only force
    /// would otherwise never settle at.
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
    /// The maximum horizontal force this body's patrol can apply, in multiples of its own weight
    /// (units of g). Configured via the ini <c>PatrolMaxForceX</c> key, defaulting to
    /// <see cref="GameDefaults.DefaultPatrolMaxForceX"/> (unlimited). Unlike
    /// <see cref="PatrolForceMultiplierX"/> (a gain: how responsive), this is a cap (how strong), so a
    /// weak body can be pushed around by flow or drag instead of always reaching its cruise speed.
    /// </summary>
    public double PatrolMaxForceX { get; set; } = GameDefaults.DefaultPatrolMaxForceX;

    /// <summary>
    /// The maximum vertical force this body's patrol can apply, in multiples of its own weight
    /// (units of g). Configured via the ini <c>PatrolMaxForceY</c> key, defaulting to
    /// <see cref="GameDefaults.DefaultPatrolMaxForceY"/> (0: no vertical thrust, so a walker just
    /// falls). For a flier this is its "wing power": 1.0 can just hover, above 1.0 it can also climb.
    /// </summary>
    public double PatrolMaxForceY { get; set; } = GameDefaults.DefaultPatrolMaxForceY;

    /// <summary>
    /// Current pose (see <see cref="Player2D.Stance"/> for the closest player-side member).
    /// Only one pose exists today ("Move", with idle/left/right facing clips - see
    /// <see cref="UpdatePose"/>), but kept as a settable member rather than a hardcoded literal so
    /// a future dynamic asset with multiple poses (e.g. a distinct "Attack" pose) doesn't
    /// need a new mechanism.
    /// </summary>
    public string Pose { get; set; } = "Move";

    public DynamicObject2D()
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
    /// either axis pair left null (both min and max) means that axis has no range and holds the
    /// spawn position instead (a zero-width range, returning to it after a bump), so a body
    /// configured with both X and Y ranges patrols diagonally, each axis bouncing between its
    /// own bounds on its own schedule. Whether the body can actually push on an axis is decided
    /// only by that axis's force cap (see <see cref="PatrolMaxForceX"/>/<see cref="PatrolMaxForceY"/>).
    /// Optionally overrides the patrol force's strength and/or each axis's initial direction.
    /// </summary>
    /// <param name="patrolMinX">Left bound of the horizontal patrol range, or null to hold the spawn column.</param>
    /// <param name="patrolMaxX">Right bound of the horizontal patrol range, or null to hold the spawn column.</param>
    /// <param name="cruiseSpeedX">
    /// Target horizontal patrol cruising speed (see <see cref="PatrolCruiseSpeedX"/>); defaults to
    /// <see cref="GameDefaults.PatrolCruiseSpeed"/> if not given.
    /// </param>
    /// <param name="patrolMinY">Top bound of the vertical patrol range, or null to hold the spawn height.</param>
    /// <param name="patrolMaxY">Bottom bound of the vertical patrol range, or null to hold the spawn height.</param>
    /// <param name="cruiseSpeedY">
    /// Target vertical patrol cruising speed (see <see cref="PatrolCruiseSpeedY"/>); defaults to
    /// <see cref="GameDefaults.PatrolCruiseSpeed"/> if not given.
    /// </param>
    /// <param name="forceMultiplierX">
    /// X-axis patrol force gain (see <see cref="PatrolForceMultiplierX"/>).
    /// </param>
    /// <param name="forceMultiplierY">
    /// Y-axis patrol force gain (see <see cref="PatrolForceMultiplierY"/>).
    /// </param>
    /// <param name="maxForceX">
    /// Horizontal force cap in multiples of g (see <see cref="PatrolMaxForceX"/>).
    /// </param>
    /// <param name="maxForceY">
    /// Vertical force cap in multiples of g (see <see cref="PatrolMaxForceY"/>); 0 means no vertical thrust.
    /// </param>
    /// <param name="initialDirectionRight">
    /// Which way to start heading on the X axis. When null, it starts heading toward
    /// whichever bound is farther from the spawn position, so a body spawned near one end still
    /// immediately patrols across the full range instead of instantly hitting the near bound and
    /// turning back within the first frame or two.
    /// overrides that inference - e.g. to make a placement's object visibly start off moving toward
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
        double forceMultiplierX = GameDefaults.PatrolForceMultiplier,
        double forceMultiplierY = GameDefaults.PatrolForceMultiplier,
        bool? initialDirectionRight = null, bool? initialDirectionDown = null,
        double maxForceX = GameDefaults.DefaultPatrolMaxForceX, double maxForceY = GameDefaults.DefaultPatrolMaxForceY)
    {
        PatrolMaxForceX = maxForceX;
        PatrolMaxForceY = maxForceY;

        // An axis with no range holds its spawn position (a zero-width range), so every patrolling
        // body runs the same controller on both axes. Whether the body can actually push on that
        // axis is decided by its force cap, not by the range.
        if (!(patrolMinX.HasValue && patrolMaxX.HasValue))
        {
            patrolMinX = Position.X;
            patrolMaxX = Position.X;
        }
        if (!(patrolMinY.HasValue && patrolMaxY.HasValue))
        {
            patrolMinY = Position.Y;
            patrolMaxY = Position.Y;
        }

        PatrolMinX = patrolMinX;
        PatrolMaxX = patrolMaxX;
        PatrolCruiseSpeedX = cruiseSpeedX;
        PatrolMinY = patrolMinY;
        PatrolMaxY = patrolMaxY;
        PatrolCruiseSpeedY = cruiseSpeedY;
        PatrolForceMultiplierX = forceMultiplierX;
        PatrolForceMultiplierY = forceMultiplierY;
        IsPatrolling = true;

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
    /// Both axes turn around once they reach or pass a bound (so a body bumped past a bound heads
    /// back). The Y axis has no gravity special case: gravity is an ordinary force in the net sum,
    /// so climbing saturates the vertical force cap while descending
    /// needs little force. An axis with no range holds its spawn position.
    /// </summary>
    public void UpdatePatrolDirection(double gravity, double forceScale)
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
            var axisTargetX = ResolveAxisTargetVelocity(Position.X, PatrolMinX.Value, PatrolMaxX.Value, PatrolCruiseSpeedX, ref _patrolMovingRight);

            // Target speed (and the force converging toward it) is expressed relative to whatever
            // solid this body currently rests on (see Body2D.GetSurfaceVelocityX) - mirroring
            // PhysicsSystem.Step's own player-side groundVelocityX fix - rather than an absolute
            // world-frame speed. Without this, a fast horizontal platform's own carry is baked into
            // this body's absolute Velocity.X, so the patrol force (aimed at an absolute target
            // speed) would spend part of its "muscle power" fighting the platform's own motion every
            // frame instead of just walking across it, letting the body slide relative to the
            // platform's surface instead of patrolling it at a steady cruise speed.
            var surfaceVelocityX = GetSurfaceVelocityX();
            var targetVelocityX = surfaceVelocityX + axisTargetX;
            forceX = (targetVelocityX - Velocity.X) * mass * PatrolForceMultiplierX * forceScale;
            var maxForceX = PatrolMaxForceX * mass * gravity;
            forceX = Math.Clamp(forceX, -maxForceX, maxForceX);

            // Facing intent (see Body2D.MoveIntentX) is simply this patrol's current heading -
            // an AI has no separate "input" to read, so its patrol direction stands in for intent,
            // same as Player2D.MoveIntentX stands in for the player's own key state. Set here
            // (rather than in UpdatePose) since this is where the heading itself is actually
            // decided/flipped. The intent is the direction of the target velocity (dead-zoned), so
            // a body returning to its hold position faces that way and settles back to idle.
            MoveIntentX = Math.Abs(axisTargetX) > GameDefaults.PatrolTurnThreshold ? Math.Sign(axisTargetX) : 0.0;
        }
        else
        {
            MoveIntentX = 0.0;
        }

        if (PatrolMinY.HasValue && PatrolMaxY.HasValue)
        {
            var targetVelocityY = ResolveAxisTargetVelocity(Position.Y, PatrolMinY.Value, PatrolMaxY.Value, PatrolCruiseSpeedY, ref _patrolMovingDown);
            forceY = (targetVelocityY - Velocity.Y) * mass * PatrolForceMultiplierY * forceScale;

            // Force cap ("wing power"). Gravity is just another force in the net sum, so
            // climbing needs the force to beat it (saturating the cap) while descending
            // needs little or none. The default cap of 0 means no thrust at all (a walker).
            var maxForceY = PatrolMaxForceY * mass * gravity;
            forceY = Math.Clamp(forceY, -maxForceY, maxForceY);
        }

        PatrolForce = new Vector2D(forceX, forceY);
    }

    /// <summary>
    /// The one per-axis patrol rule, shared by X and Y: given a position, a range and a cruise
    /// speed, returns the target velocity on that axis. Heads toward the current bound at the
    /// cruise speed and flips heading on reaching or passing it (so a body bumped outside its range
    /// heads back in). A range no wider than the turn thresholds (e.g. min == max) has no
    /// bounds to bounce between, so the target is instead a spring toward its centre, capped at
    /// the cruise speed - holding that position and returning to it after a bump.
    /// </summary>
    private static double ResolveAxisTargetVelocity(double position, double min, double max, double cruiseSpeed, ref bool movingPositive)
    {
        if (max - min <= 2 * GameDefaults.PatrolTurnThreshold)
        {
            return Math.Clamp(((min + max) / 2 - position) * GameDefaults.PatrolLevelSpringGain, -cruiseSpeed, cruiseSpeed);
        }

        if (movingPositive
            ? position >= max - GameDefaults.PatrolTurnThreshold
            : position <= min + GameDefaults.PatrolTurnThreshold)
        {
            movingPositive = !movingPositive;
        }

        return (movingPositive ? 1.0 : -1.0) * cruiseSpeed;
    }

    /// <summary>
    /// Resolves and applies this object's pose from its
    /// patrol-direction intent
    /// <see cref="UpdatePatrolDirection"/>) via the shared <see cref="Body2D.ResolveHorizontalFacing()"/>
    /// rule <see cref="Player2D"/> also uses - a body with no matching pose (<c>Sprite.Poses</c>
    /// null, e.g. a dynamic asset that hasn't authored left/right clips) simply no-ops here
    /// (see <see cref="Body2D.SetPose"/>). Resolving against <see cref="Velocity"/> directly would
    /// flip facing to match whichever direction a fast-moving platform happens to be carrying this
    /// body, even while its own patrol intent hasn't changed.
    /// </summary>
    public void UpdatePose()
    {
        SetPose(Sprite, Pose, ResolveHorizontalFacing());
    }
}

