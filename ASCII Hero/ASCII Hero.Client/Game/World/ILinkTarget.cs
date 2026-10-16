namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// Capability for a body that a <see cref="Switch2D"/> can drive through a shared name: a
/// <see cref="Gate2D"/> (on = open) or a <see cref="KinematicObject2D"/> (on = moving). Linking
/// is one-to-one for now: exactly one switch and one target share a given <see cref="LinkName"/>.
/// </summary>
public interface ILinkTarget
{
    /// <summary>Name shared with the controlling switch's own <c>Link</c> key (matched case-insensitively), or null if not linked.</summary>
    string? LinkName { get; }

    /// <summary>Applies the controlling switch's state to this target.</summary>
    void SetState(bool on);
}
