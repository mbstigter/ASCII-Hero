namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A physics body that can swim - directional swim thrust (left/right horizontal, up/down
/// vertical depth control) while submerged in a sufficiently dense/viscous ambient medium (see
/// <see cref="Body2D.CurrentMedium"/> and <see cref="Physics.PhysicsSystem.IsSwimmableMedium"/>).
/// A small, focused capability interface (mirroring <see cref="IClimberBody"/>/<see cref="IHangerBody"/>)
/// rather than one combined "mover" interface. Only <see cref="Player2D"/> implements this today,
/// but nothing here is player-specific.
///
/// Unlike <see cref="IClimberBody"/>/<see cref="IHangerBody"/>, this has no separate
/// "IsTouching*" flag set by <see cref="Physics.CollisionSystem"/> from proximity to a specific
/// climbable/hangable surface: detection here is medium-based, not reach-based, and
/// <see cref="Body2D.CurrentMedium"/> is already resolved fresh every frame for every body by
/// <see cref="Physics.PhysicsSystem"/> regardless, so it alone is the "touching" signal swim
/// needs. Engaging still requires a deliberate directional key press (mirroring how climbing
/// requires a deliberate Up/Down press, unlike hanging's automatic grab) - see
/// <see cref="Physics.PhysicsSystem.Step"/>. Grounded/climbing/hanging all take priority over
/// swim: a solid floor stays solid underwater (walking/crawling keeps working exactly as on
/// land), and a ladder/pipe's own capability check is independent of medium, so swim only
/// engages once none of those apply.
/// </summary>
public interface ISwimmerBody : IPhysicsBody
{
    /// <summary>
    /// Whether the body is actually swimming right now - engaged by <see cref="Physics.PhysicsSystem"/>
    /// from a directional key press while <see cref="Body2D.CurrentMedium"/> is swimmable and the
    /// body is not grounded, climbing, or hanging (see <see cref="Physics.PhysicsSystem.Step"/>).
    /// Held until the medium is no longer swimmable, or the body becomes grounded/climbing/
    /// hanging. Unlike climbing/hanging, this deliberately does NOT suspend gravity (see
    /// <see cref="Player2D.GravityAffected"/>) - swimming is buoyant motion in a fluid, not
    /// gripping a solid surface, so gravity keeps acting and is offset by the existing ambient
    /// buoyancy force (<see cref="IMediumAffected"/>, always-on, independent of this flag) the
    /// same way it would for any other body immersed in a medium. This flag only adds the
    /// player-directed swim motor force (see <see cref="World.IWalkForceBody"/>) on top of that,
    /// the same way walking adds motor force on top of gravity for an ordinary grounded body.
    /// </summary>
    bool IsSwimming { get; set; }
}
