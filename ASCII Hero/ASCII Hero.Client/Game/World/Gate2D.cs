namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A door, hatch or similar passage that is solid while closed and passable while open. It is
/// opened in one of two ways: with a key (<see cref="LockName"/> set - the player presses the
/// action key next to it while holding the matching <see cref="Key2D"/>, see
/// <see cref="World2D.TryOpenGate"/>; it then stays open), or by a <see cref="Switch2D"/>
/// (<see cref="LinkName"/> set - it follows that switch's state and can close again). A gate has
/// at most one of the two.
/// </summary>
public class Gate2D : TwoStateObject2D, ILinkTarget
{
    /// <summary>Name of the key that opens this gate, from the placement's <c>Lock</c> key. Null if it is not key-operated.</summary>
    public string? LockName { get; set; }

    /// <summary>Name of the switch that drives this gate, from the placement's <c>Link</c> key. Null if it is not switch-operated.</summary>
    public string? LinkName { get; set; }

    protected override void OnStateChanged() => IsPassable = IsOn;
}
