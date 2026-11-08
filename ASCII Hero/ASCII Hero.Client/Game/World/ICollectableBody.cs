namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// Marker for a body that can be picked up by an <see cref="ICollectorBody"/> (the player)
/// overlapping it, e.g. a diamond or ring. Detection is generic in
/// <see cref="Physics.CollisionSystem"/>; what a pickup does is decided by
/// <see cref="Physics.InteractionHandler.OnCollected"/>, which usually removes it via
/// <see cref="World2D.QueueRemoval(Body2D)"/> - except a key that only the player can carry and
/// that needs a lock name, and a collectable without a valid <c>Type</c>, which stay in place.
/// </summary>
public interface ICollectableBody;
