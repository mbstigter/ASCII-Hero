namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// Marker for a body that can pick up <see cref="ICollectableBody"/> instances on contact, e.g.
/// the player. Implemented by <see cref="Player2D"/>; any future additional player-controlled body
/// (e.g. a second player in multiplayer) implements this too, so pickup logic in
/// <see cref="Physics.CollisionSystem"/> never needs to special-case a specific player type or
/// count - it just checks the capability generically, the same way <see cref="IHazardBody"/> and
/// <see cref="ICollectableBody"/> overlap detection does.
/// </summary>
public interface ICollectorBody : IPhysicsBody;
