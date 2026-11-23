namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A <see cref="CollectableType.Key"/> collectable. On pickup it goes into the player's
/// <see cref="GearName"/> gear slot; with that slot selected, the action key opens and closes a
/// <see cref="Gate2D"/> with the same lock name (see <see cref="World2D.TryOperateGate"/>). The key is
/// never used up.
/// </summary>
public class Key2D : Collectable2D
{
    /// <summary>The <c>[Gear]</c> slot keys are carried in; without it listed, keys cannot be picked up.</summary>
    public const string GearName = "Key";

    /// <summary>
    /// Name of the lock this key opens, from the placement's <c>Lock</c> key (matched
    /// case-insensitively). A key without one is inert.
    /// </summary>
    public string? LockName { get; set; }

    /// <summary>
    /// Optional <c>UseSound</c> inherited from <see cref="Collectable2D"/>, played when the key is
    /// turned in its gate's lock; the gate's <c>OperateSound</c> follows. Its <c>CollectSound</c>
    /// plays on pickup.
    /// </summary>
}
