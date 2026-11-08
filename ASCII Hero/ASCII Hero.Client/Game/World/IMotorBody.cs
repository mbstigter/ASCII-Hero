namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A physics body whose sustained, input-driven locomotion (walking, crawling, climbing, hanging
/// and swimming) is driven by its own mass-scaled motor force on both axes rather than direct
/// velocity assignment - mirrors <see cref="IPatrolBody"/>, but for player-style input instead of a
/// scripted patrol. <see cref="Physics.PhysicsSystem.StepMovingBodyWithForces"/> sums
/// <see cref="MotorForce"/> into its per-frame net force alongside gravity, exactly like
/// <see cref="IPatrolBody.PatrolForce"/>, instead of the body needing its own bespoke
/// velocity-mutation code path.
/// </summary>
public interface IMotorBody : IPhysicsBody
{
    /// <summary>The mass-scaled force this body's current sustained locomotion (walk/crawl/climb/hang/swim) currently contributes.</summary>
    Vector2D MotorForce { get; }
}
