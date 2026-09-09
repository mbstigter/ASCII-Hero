namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A physics body whose ground-level horizontal locomotion (walking/crawling) is driven by its
/// own mass-scaled force rather than direct velocity assignment - mirrors <see cref="IPatrolBody"/>
/// for an AI-patrolling enemy, but for player-style input-driven walk/crawl movement instead of a
/// scripted patrol. <see cref="Physics.PhysicsSystem.StepMovingBodyWithForces"/> sums
/// <see cref="WalkForce"/> into its per-frame net force alongside gravity, exactly like
/// <see cref="IPatrolBody.PatrolForce"/> already does, instead of the body needing its own bespoke
/// velocity-mutation code path.
/// </summary>
public interface IWalkForceBody : IPhysicsBody
{
    /// <summary>The mass-scaled force this body's current sustained locomotion (walk/crawl/climb/hang) currently contributes.</summary>
    Vector2D WalkForce { get; }
}
