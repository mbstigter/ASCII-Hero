namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A physics body that patrols back and forth independently along the X and/or Y axis between
/// world-space bounds under its own force, rather than moving via direct velocity assignment -
/// mirrors how <see cref="IGravityAffected"/> lets <see cref="Physics.PhysicsSystem"/> apply
/// gravity generically to any opted-in body. <see cref="Physics.PhysicsSystem.StepMovingBodyWithForces"/>
/// sums <see cref="PatrolForce"/> into its per-frame net force alongside gravity, instead of a
/// bespoke velocity-mutation code path. A body with both X and Y
/// bounds configured patrols diagonally - each axis bounces between its own bounds on its own
/// schedule, with no separate "diagonal mode" needed.
/// </summary>
public interface IPatrolBody : IPhysicsBody
{
    /// <summary>
    /// Recomputes which direction this body should currently be heading on each patrolled axis
    /// (called once per frame by <see cref="Physics.PhysicsSystem"/> before <see cref="PatrolForce"/>
    /// is read), flipping at either end of that axis's own bounds, or earlier once blocked for a
    /// while. <paramref name="gravity"/> is the
    /// world's own gravity acceleration, which the per-axis force caps (in multiples of g) are
    /// expressed against; <paramref name="deltaSeconds"/> is this step's length, which the
    /// blocked-turn timer counts in.
    /// </summary>
    void UpdatePatrolDirection(double gravity, double deltaSeconds);

    /// <summary>The force this body's patrol currently contributes, mass-scaled like gravity.</summary>
    Vector2D PatrolForce { get; }
}
