using System;
using ASCII_Hero.Client.Game.Assets;

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
public class MovingEnemy2D : Body2D, IPhysicsBody, IHazardBody, IGravityAffected, IPatrolBody, IPosedBody, IEffectTrigger, IKillableBody
{
    /// <summary>
    /// Default magnitude of the mass-scaled horizontal force applied while patrolling, tuned to
    /// produce a gentle, readable patrol speed rather than an instant snap to some target
    /// velocity - mirrors how <see cref="Physics.PhysicsSystem.StepMovingBodyWithForces"/> already
    /// scales gravity by mass rather than assigning velocity directly. Used by <see cref="SetPatrol"/>
    /// when a placement doesn't set its own <c>PatrolForce</c> (see docs/AssetFormat.md).
    /// </summary>
    public const double DefaultPatrolForceMultiplier = 60.0;

    /// <summary>
    /// How close (in world cells) this body's left edge must get to a patrol bound before turning
    /// around, so it reverses just shy of the bound rather than oscillating exactly on it.
    /// </summary>
    private const double PatrolTurnThreshold = 0.25;

    private bool _patrolMovingRight = true;

    /// <summary>Current velocity, in world cells per second.</summary>
    public Vector2D Velocity { get; set; }

    /// <summary>Whether this enemy is subject to normal world gravity.</summary>
    public bool GravityAffected { get; set; } = true;

    /// <summary>Whether this enemy is currently patrolling (see <see cref="IPatrolBody"/>).</summary>
    public bool IsPatrolling { get; set; }

    /// <summary>Left bound of the patrol range, in world cells.</summary>
    public double PatrolMinX { get; set; }

    /// <summary>Right bound of the patrol range, in world cells.</summary>
    public double PatrolMaxX { get; set; }

    /// <summary>The horizontal-only force this body's patrol currently contributes.</summary>
    public Vector2D PatrolForce { get; private set; }

    /// <summary>
    /// Magnitude of the mass-scaled horizontal force this body applies while patrolling (see
    /// <see cref="UpdatePatrolDirection"/>) - configured per-placement via <see cref="SetPatrol"/>'s
    /// <c>forceMultiplier</c> parameter (the ini <c>PatrolForce</c> key), defaulting to
    /// <see cref="DefaultPatrolForceMultiplier"/> so existing placements that don't set it are
    /// unaffected. Higher values produce a faster (but still mass-scaled, not instant) patrol
    /// speed, exactly like a stronger gravity would fall faster.
    /// </summary>
    public double PatrolForceMultiplier { get; set; } = DefaultPatrolForceMultiplier;

    /// <summary>
    /// Current stance (see <see cref="Player2D.Stance"/> for the equivalent player-side member).
    /// Only one stance exists today ("Move", with idle/left/right facing clips - see
    /// <see cref="UpdatePose"/>), but kept as a settable member rather than a hardcoded literal so
    /// a future MovingEnemy asset with multiple stances (e.g. a distinct "Attack" pose) doesn't
    /// need a new mechanism.
    /// </summary>
    public string Stance { get; set; } = "Move";

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
    /// Configures linear patrol between the given world-space X bounds (see
    /// <see cref="IPatrolBody"/>), optionally overriding the patrol force's strength and/or
    /// initial direction.
    /// </summary>
    /// <param name="patrolMinX">Left bound of the patrol range, in world cells.</param>
    /// <param name="patrolMaxX">Right bound of the patrol range, in world cells.</param>
    /// <param name="forceMultiplier">
    /// Patrol force magnitude (see <see cref="PatrolForceMultiplier"/>); defaults to
    /// <see cref="DefaultPatrolForceMultiplier"/> if not given.
    /// </param>
    /// <param name="initialDirectionRight">
    /// Which direction to start heading in. If null (the default), starts heading toward
    /// whichever bound is farther from the spawn position, so a body spawned near one end still
    /// immediately patrols across the full range instead of instantly hitting the near bound and
    /// turning back within the first frame or two. An explicit value (true = right, false = left)
    /// overrides that inference - e.g. to make a placement's enemy visibly start off moving toward
    /// the player instead.
    /// </param>
    public void SetPatrol(double patrolMinX, double patrolMaxX, double forceMultiplier = DefaultPatrolForceMultiplier, bool? initialDirectionRight = null)
    {
        IsPatrolling = true;
        PatrolMinX = patrolMinX;
        PatrolMaxX = patrolMaxX;
        PatrolForceMultiplier = forceMultiplier;
        // Start heading toward whichever bound is farther, so a body spawned near one end
        // immediately patrols across the full range instead of instantly hitting the near bound
        // and turning back within the first frame or two - unless the caller explicitly requested
        // a starting direction instead.
        _patrolMovingRight = initialDirectionRight ?? (Position.X - patrolMinX <= patrolMaxX - Position.X);
    }

    /// <summary>
    /// Flips <see cref="_patrolMovingRight"/> once within <see cref="PatrolTurnThreshold"/> of the
    /// current target bound, then recomputes <see cref="PatrolForce"/> toward the (possibly new)
    /// target - ported from the old ConsoleGame2D reference project's linear-patrol direction
    /// logic, adapted to contribute a force rather than assign velocity directly. Facing/animation
    /// is deliberately not decided here - see <see cref="UpdatePose"/>, which runs after this
    /// frame's force is actually integrated into <see cref="Velocity"/>, so the sprite reflects
    /// this frame's resolved motion rather than the direction this (pre-integration) force is
    /// merely heading toward.
    /// </summary>
    public void UpdatePatrolDirection()
    {
        if (!IsPatrolling)
        {
            PatrolForce = new Vector2D(0, 0);
            return;
        }

        var targetX = _patrolMovingRight ? PatrolMaxX : PatrolMinX;
        if (Math.Abs(Position.X - targetX) <= PatrolTurnThreshold)
        {
            _patrolMovingRight = !_patrolMovingRight;
        }

        var mass = Mass > 0 ? Mass : 1.0;
        var forceX = (_patrolMovingRight ? 1.0 : -1.0) * mass * PatrolForceMultiplier;
        PatrolForce = new Vector2D(forceX, 0);
    }

    /// <summary>
    /// Resolves and applies this enemy's pose (see <see cref="IPosedBody"/>) from its own
    /// now-integrated <see cref="Velocity"/>.X, via the same shared
    /// <see cref="Body2D.ResolveHorizontalFacing"/> rule <see cref="Player2D"/> uses - a body with
    /// no matching stance (<c>Sprite.Stances</c> null, e.g. a MovingEnemy asset that hasn't
    /// authored left/right clips) simply no-ops here (see <see cref="Body2D.SetPose"/>).
    /// </summary>
    public void UpdatePose()
    {
        SetPose(Sprite, Stance, ResolveHorizontalFacing(Velocity.X));
    }
}
