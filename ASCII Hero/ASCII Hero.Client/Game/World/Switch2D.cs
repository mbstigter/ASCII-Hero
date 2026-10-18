namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A button or lever: a passable, non-collected object with an off/on state that drives every
/// <see cref="ILinkTarget"/> sharing its <see cref="LinkName"/> (resolved once after load by
/// <see cref="World2D"/>). The player operates it with the action key when next to it, see
/// <see cref="World2D.TryOperateSwitch"/>; <see cref="Operate"/> is the single entry point, so
/// other bodies (e.g. a future enemy that flips levers) can call it the same way.
/// </summary>
public class Switch2D : TwoStateObject2D
{
    /// <summary>If true, operating the switch turns it on and it stays on; if false (default) each operation toggles it.</summary>
    public bool Latching { get; set; }

    /// <summary>Name shared with the target's own <c>Link</c> key (matched case-insensitively). A switch without one is inert.</summary>
    public string? LinkName { get; set; }

    /// <summary>The targets this switch drives, resolved by <see cref="World2D"/> after load. Empty if unlinked.</summary>
    public List<ILinkTarget> Targets { get; } = [];

    /// <summary>
    /// Operates the switch: toggles it, or only turns it on if <see cref="Latching"/>. Pushes the
    /// new state to every one of <see cref="Targets"/>.
    /// </summary>
    public bool Operate()
    {
        var newState = Latching || !IsOn;
        if (newState == IsOn)
        {
            return false;
        }

        SetState(newState);
        foreach (var target in Targets)
        {
            target.SetState(newState);
        }
        return true;
    }
}
