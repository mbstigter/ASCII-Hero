namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A <see cref="CollectableType.Key"/> collectable. On pickup its <see cref="LockName"/> is added
/// to <see cref="Player2D.Keys"/>, letting the player open a <see cref="Gate2D"/> with the same
/// lock name (see <see cref="World2D.TryOpenGate"/>).
/// </summary>
public class Key2D : Collectable2D
{
    /// <summary>
    /// Name of the lock this key opens, from the placement's <c>Lock</c> key (matched
    /// case-insensitively). A key without one is inert.
    /// </summary>
    public string? LockName { get; set; }
}
