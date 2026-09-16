namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A physics body that patrols back and forth independently along the X and/or Y axis between
/// world-space bounds under its own force, rather than moving via direct velocity assignment -
/// mirrors how <see cref="IGravityAffected"/> lets <see cref="Physics.PhysicsSystem"/> apply
/// gravity generically to any opted-in body. <see cref="Physics.PhysicsSystem.StepMovingBodyWithForces"/>
/// sums <see cref="PatrolForce"/> into its per-frame net force alongside gravity, exactly as its
/// own "future force source" extension point anticipated, instead of adding a second, bespoke
/// velocity-mutation code path the way the player's movement works. A body with both X and Y
/// bounds configured patrols diagonally - each axis bounces between its own bounds on its own
/// schedule, with no separate "diagonal mode" needed.
/// </summary>
public interface IPatrolBody : IPhysicsBody
{
    /// <summary>Whether this body is currently patrolling on either axis. False means no patrol force is ever applied.</summary>
    bool IsPatrolling { get; }

    /// <summary>Left bound of the horizontal patrol range, in world cells, or null if this body doesn't patrol on the X axis.</summary>
    double? PatrolMinX { get; }

    /// <summary>Right bound of the horizontal patrol range, in world cells, or null if this body doesn't patrol on the X axis.</summary>
    double? PatrolMaxX { get; }

    /// <summary>
    /// Recomputes which direction this body should currently be heading on each patrolled axis
    /// (called once per frame by <see cref="Physics.PhysicsSystem"/> before <see cref="PatrolForce"/>
    /// is read), flipping at either end of that axis's own bounds. <paramref name="gravity"/> is the
    /// world's own gravity acceleration, needed so a vertically-patrolling body can cancel it while
    /// climbing (see <see cref="MovingEnemy2D.UpdatePatrolDirection"/>).
    /// </summary>
    void UpdatePatrolDirection(double gravity);

    /// <summary>The force this body's patrol currently contributes, mass-scaled like gravity.</summary>
    Vector2D PatrolForce { get; }
}
