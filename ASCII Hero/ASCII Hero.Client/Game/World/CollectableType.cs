namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// Distinguishes what happens when a <see cref="Collectable2D"/> is picked up - see the branching
/// in <see cref="Physics.CollisionSystem.ResolveHazardsAndCollectables"/>. Set via the required
/// <c>Type</c> key on a collectable's placement section in <c>{World}_objects.ini</c> (see
/// docs/AssetFormat.md §3); <see cref="Collectable2D.Type"/> is nullable and an
/// absent/unrecognized <c>Type</c> is deliberately left <see langword="null"/> rather than
/// falling back to some default variant, so a placement that forgets to set <c>Type</c> is
/// inert (does nothing on pickup) instead of silently behaving like some other variant.
/// </summary>
public enum CollectableType
{
    /// <summary>Removed on pickup, increments the collecting <see cref="Player2D"/>'s <see cref="Player2D.Score"/>.</summary>
    Points,

    /// <summary>Removed on pickup, increments the collecting <see cref="Player2D"/>'s <see cref="Player2D.Health"/>.</summary>
    Health,

    /// <summary>
    /// Removed on pickup like <see cref="Health"/>/<see cref="Points"/>, but its own
    /// <see cref="Collectable2D.EffectPersists"/> effect remains in its place permanently as a
    /// used marker (mirrors a killed <see cref="StaticEnemy2D"/>'s persistent husk). Also records
    /// its position as <see cref="World2D.RespawnPoint"/>, consumed by <see cref="World2D.Respawn"/>.
    /// </summary>
    Checkpoint,

    /// <summary>
    /// Removed on pickup like <see cref="Health"/>/<see cref="Points"/>, and sets
    /// <see cref="World2D.LevelCompleted"/> so <see cref="GameLoop"/> can transition out of play.
    /// </summary>
    LevelEnd,

    /// <summary>
    /// Removed on pickup like <see cref="Health"/>/<see cref="Points"/>. Placeholder for a future
    /// door/gate-unlock system - currently has no unlock target wiring.
    /// </summary>
    Key,
}
