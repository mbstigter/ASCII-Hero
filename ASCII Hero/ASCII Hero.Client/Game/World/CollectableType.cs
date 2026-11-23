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
    /// (and <see cref="Constants.GameDefaults.RingScore"/> points to its score). Every <see cref="World2D.RingsPerLife"/> rings reset the count
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
    /// <see cref="World2D.PendingTransition"/> to <see cref="PendingTransition.WorldEnd"/> so
    /// <see cref="GameLoop"/> can transition out of play.
    /// </summary>
    WorldEnd,

    /// <summary>
    /// Removed on pickup like <see cref="Diamond"/>/<see cref="Ring"/>, and carried in the player's
    /// <see cref="Key2D.GearName"/> gear slot. A key without a <c>Lock</c> name (or without that slot
    /// listed) is inert. Never used up: it opens and closes a gate with the same lock name, see
    /// <see cref="World2D.TryOperateGate"/>.
    /// </summary>
    Key,

    /// <summary>
    /// Picked up into the player's <c>HealthPack</c> gear slot, even at full health. The action key
    /// consumes one whole pack to restore full health only when the player's health is below full.
    /// </summary>
    HealthPack,
}
