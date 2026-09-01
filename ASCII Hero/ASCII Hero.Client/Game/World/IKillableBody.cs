namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// Capability for an enemy body that can be "killed" (removed from the world) by a qualifying
/// contact, e.g. the player landing on top of it. Unlike the bare marker interfaces
/// (<see cref="IHazardBody"/>, <see cref="ICollectableBody"/>), this carries real per-instance
/// state: implementing it unconditionally with <see cref="IsKillable"/> always true would make
/// every instance of the implementing class killable in every level with no way to opt out, so
/// <see cref="Physics.CollisionSystem"/> must check <c>body is IKillableBody { IsKillable: true }</c>,
/// not just interface presence.
/// </summary>
public interface IKillableBody
{
    bool IsKillable { get; }

    /// <summary>
    /// Whether this instance's <see cref="IEffectTrigger.EffectClipName"/> effect (if configured)
    /// should persist as a permanent decorative body after a kill contact (e.g. a "dead plant"
    /// husk), instead of self-removing once its clip finishes playing like an ordinary effect.
    /// </summary>
    bool EffectPersists { get; }
}
