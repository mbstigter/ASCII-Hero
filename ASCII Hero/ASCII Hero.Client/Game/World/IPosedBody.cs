namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A physics body that resolves and applies its own sprite pose (stance + facing, see
/// <see cref="Body2D.SetPose(Assets.SpriteAsset, string, Assets.Facing)"/>) once per frame, rather
/// than that decision being made externally - mirrors how <see cref="IPatrolBody"/> lets a body
/// own its own patrol-direction decision while <see cref="Physics.PhysicsSystem"/> merely calls it
/// at the right moment. <see cref="UpdatePose"/> is called by
/// <see cref="Physics.PhysicsSystem.StepMovingBodyWithForces"/> (and, once the player is converted
/// to force-based movement, its player-specific counterpart) after velocity has been integrated
/// for the frame, so a pose based on velocity/direction reflects this frame's actual resolved
/// motion rather than a pre-integration force-direction estimate that force-based acceleration/
/// deceleration could still momentarily disagree with.
/// </summary>
public interface IPosedBody : IPhysicsBody
{
    /// <summary>
    /// Recomputes and applies this body's current pose (stance + facing) from its own
    /// now-integrated state (typically <see cref="IPhysicsBody.Velocity"/>). Called once per frame
    /// after position/velocity integration.
    /// </summary>
    void UpdatePose();
}
