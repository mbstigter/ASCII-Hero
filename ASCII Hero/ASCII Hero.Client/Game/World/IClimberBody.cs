namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A physics body that can climb an <see cref="Body2D.IsClimbable"/> surface (e.g. a ladder) -
/// straight up/down movement, gravity suspended while actually climbing. A small, focused
/// capability interface (mirroring <see cref="IGravityAffected"/>/<see cref="ICollectorBody"/>/
/// <see cref="IKillerBody"/>) rather than one combined "mover" interface, so a body can implement
/// this without also being able to hang (see <see cref="IHangerBody"/>) - the two mechanics are
/// conceptually independent even though <see cref="Player2D"/> currently implements both.
/// Only <see cref="Player2D"/> implements this today, but nothing here is player-specific: any
/// future climbing-capable body (an enemy that patrols up/down a ladder) can opt in the same way.
/// </summary>
public interface IClimberBody : IPhysicsBody
{
    /// <summary>
    /// Whether the body is currently overlapping an <see cref="Body2D.IsClimbable"/> surface,
    /// regardless of whether it is actually climbing right now. Set each frame by
    /// <see cref="Physics.CollisionSystem"/> from the body's current overlap (the same
    /// "recomputed fresh every frame, never persisted" pattern used for
    /// <see cref="IPhysicsBody.IsGrounded"/>), consumed the same frame by
    /// <see cref="Physics.PhysicsSystem"/> - actually, consumed the *following* frame, since
    /// physics runs before collision each tick - to decide whether an up/down press should
    /// engage <see cref="IsClimbing"/>. Kept separate from <see cref="IsClimbing"/> so merely
    /// walking past/through a passable ladder (no climb key held) never locks movement.
    /// </summary>
    bool IsTouchingClimbable { get; set; }

    /// <summary>
    /// Whether the body is actually climbing straight up/down right now - engaged by
    /// <see cref="Physics.PhysicsSystem"/> from an up/down key press while
    /// <see cref="IsTouchingClimbable"/> (and not moving too fast to grab on, see
    /// <see cref="Physics.CollisionSystem"/>'s snap-speed gate), held until the body leaves the
    /// climbable surface or steps off sideways onto solid ground.
    /// </summary>
    bool IsClimbing { get; set; }
}
