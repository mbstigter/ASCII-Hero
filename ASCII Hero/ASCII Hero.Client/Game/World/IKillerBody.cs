namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// Marker for a body that can "kill" (stomp) an <see cref="IKillableBody"/> hazard by landing on
/// top of it, e.g. the player. Kept entirely separate from <see cref="ICollectorBody"/> - picking
/// up collectables and stomping enemies are independent capabilities that happen to both be true
/// for the player today, but a future body could plausibly have one without the other (e.g. a
/// non-collecting ally that can still stomp enemies, or a collector that can't). Implemented by
/// <see cref="Player2D"/>; any future additional player-controlled body (e.g. a second player in
/// multiplayer) implements this too, so kill logic in <see cref="Physics.CollisionSystem"/> never
/// needs to special-case a specific player type or count - it just checks the capability
/// generically, the same way <see cref="IHazardBody"/>/<see cref="ICollectableBody"/> overlap
/// detection does.
/// </summary>
public interface IKillerBody : IPhysicsBody;
