namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A physics body that patrols back and forth along the X axis between two world-space bounds
/// under its own force, rather than moving via direct velocity assignment - mirrors how
/// <see cref="IGravityAffected"/> lets <see cref="Physics.PhysicsSystem"/> apply gravity
/// generically to any opted-in body. <see cref="Physics.PhysicsSystem.StepMovingBodyWithForces"/>
/// sums <see cref="PatrolForce"/> into its per-frame net force alongside gravity, exactly as its
/// own "future force source" extension point anticipated, instead of adding a second, bespoke
/// velocity-mutation code path the way the player's movement works.
/// </summary>
public interface IPatrolBody : IPhysicsBody
{
    /// <summary>Whether this body is currently patrolling at all. False means no patrol force is ever applied.</summary>
    bool IsPatrolling { get; }

    /// <summary>Left bound of the patrol range, in world cells (this body's left edge never goes below this).</summary>
    double PatrolMinX { get; }

    /// <summary>Right bound of the patrol range, in world cells (this body's left edge never goes above this).</summary>
    double PatrolMaxX { get; }

    /// <summary>
    /// Recomputes which direction this body should currently be heading (called once per frame by
    /// <see cref="Physics.PhysicsSystem"/> before <see cref="PatrolForce"/> is read), flipping at
    /// either end of the <see cref="PatrolMinX"/>/<see cref="PatrolMaxX"/> range.
    /// </summary>
    void UpdatePatrolDirection();

    /// <summary>The horizontal-only force this body's patrol currently contributes, mass-scaled like gravity.</summary>
    Vector2D PatrolForce { get; }
}
