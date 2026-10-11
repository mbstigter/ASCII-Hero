namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// Marker for a body that damages the player (or any moving body) on contact, e.g. a static
/// spike trap or a moving/patrolling hazard. Detection is generic (any <see cref="IPhysicsBody"/>
/// overlapping any <see cref="IHazardBody"/>) in <see cref="Physics.CollisionSystem"/>; the damage
/// itself is applied there too, using the hazard's <see cref="Body2D.Damage"/>.
/// </summary>
public interface IHazardBody;
