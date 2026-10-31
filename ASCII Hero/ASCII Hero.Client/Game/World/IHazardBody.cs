namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// Marker for a body that damages the player (or any moving body) on contact, e.g. a static
/// spike trap or a moving/patrolling hazard. Detection is generic (any <see cref="IPhysicsBody"/>
/// touching any <see cref="IHazardBody"/>) in <see cref="Physics.CollisionSystem"/>; the damage
/// itself is applied by <see cref="Physics.InteractionHandler"/>, using the hazard's <see cref="Damage"/>.
/// </summary>
public interface IHazardBody
{
    /// <summary>
    /// Health removed from the player per hit, from the placement's <c>Damage</c> key; 1.0 is a
    /// whole health bar.
    /// </summary>
    double Damage { get; }
}
