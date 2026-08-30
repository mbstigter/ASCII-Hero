namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// Marker for a body that is removed from the world when a moving body (the player) contacts
/// it, e.g. a coin or power-up. Detection and removal are generic (any <see cref="IPhysicsBody"/>
/// overlapping any <see cref="ICollectableBody"/> queues it for removal via
/// <see cref="World2D.QueueRemoval(Body2D)"/>) in <see cref="Physics.CollisionSystem"/>.
/// </summary>
public interface ICollectableBody;
