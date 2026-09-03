namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A body that participates in physics integration (position/velocity) and collision, as opposed
/// to static terrain. Lets movement-related systems (world bounds, platform collision, and
/// collision between moving bodies themselves) operate generically without caring which concrete
/// type they're given. Named for the capability (participates in physics), not for whether the
/// body is currently in motion - a body at rest still implements this.
/// </summary>
public interface IPhysicsBody
{
    Vector2D Position { get; set; }
    Vector2D Velocity { get; set; }
    Vector2D Size { get; }

    /// <summary>
    /// Whether the body currently rests on something solid, be it a platform's top surface, the
    /// world's own floor, or the top of another moving body. Bodies never set this on
    /// themselves; it is assigned each frame by whichever collision resolution finds them
    /// resting on a supporting surface.
    /// </summary>
    bool IsGrounded { get; set; }

    /// <summary>The body's collision shape, as one or more rectangles in world space.</summary>
    IReadOnlyList<Physics.Rect2D> CollisionRects { get; }

    /// <summary>
    /// This body's mass (see <see cref="Body2D.Mass"/>), used by
    /// <see cref="Physics.CollisionSystem"/>'s mass-weighted impulse/position-correction math.
    /// Every concrete <see cref="IPhysicsBody"/> is a <see cref="Body2D"/>, so this simply
    /// exposes that computed property through the capability interface instead of requiring
    /// collision code to downcast.
    /// </summary>
    double Mass { get; }

    /// <summary>This body's material-derived friction (see <see cref="Body2D.Friction"/>).</summary>
    double Friction { get; }

    /// <summary>This body's material-derived (or ini-overridden) restitution (see <see cref="Body2D.Restitution"/>).</summary>
    double Restitution { get; }
}
