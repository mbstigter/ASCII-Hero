namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// Marker for a body that damages the player (or any moving body) on contact, e.g. a static
/// spike trap or a moving/patrolling enemy. Detection is generic (any <see cref="IPhysicsBody"/>
/// overlapping any <see cref="IHazardBody"/>) in <see cref="Physics.CollisionSystem"/>; the actual
/// damage effect is not yet implemented since no health/damage system exists in the game yet.
/// </summary>
public interface IHazardBody;
