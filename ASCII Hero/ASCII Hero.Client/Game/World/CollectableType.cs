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
    /// <summary>Removed on pickup, adds 1 to the collecting <see cref="Player2D"/>'s <see cref="Player2D.Score"/> and nothing else.</summary>
    Diamond,

    /// <summary>
    /// Removed on pickup, adds one to the collecting <see cref="Player2D"/>'s <see cref="Player2D.Rings"/>
    /// (and one point to its score). Every <see cref="World2D.RingsPerLife"/> rings reset the count
    /// to 0 and add a <see cref="Player2D.Lives"/>. Does not restore health.
    /// </summary>
    Ring,

    /// <summary>
    /// Removed on pickup like <see cref="Diamond"/>/<see cref="Ring"/>, but its own
    /// <see cref="Collectable2D.EffectPersists"/> effect remains in its place permanently as a
    /// used marker (mirrors a killed <see cref="StaticHazard2D"/>'s persistent husk). Also records
    /// its position as <see cref="World2D.RespawnPoint"/>, consumed by <see cref="World2D.Respawn"/>.
    /// </summary>
    Checkpoint,

    /// <summary>
    /// Removed on pickup like <see cref="Diamond"/>/<see cref="Ring"/>, and sets
    /// <see cref="World2D.PendingTransition"/> to <see cref="PendingTransition.LevelEnd"/> so
    /// <see cref="GameLoop"/> can transition out of play.
    /// </summary>
    LevelEnd,

    /// <summary>
    /// Removed on pickup like <see cref="Diamond"/>/<see cref="Ring"/>, and adds its required
    /// <see cref="Key2D.LockName"/> to <see cref="Player2D.Keys"/>. A key without a <c>Lock</c>
    /// name is inert. Opens a gate with the same lock name, see <see cref="World2D.TryOpenGate"/>.
    /// </summary>
    Key,
}
