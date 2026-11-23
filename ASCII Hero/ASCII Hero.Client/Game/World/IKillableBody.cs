namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// Capability for a hazard body that can be "killed" (removed from the world) by a qualifying
/// contact by an <see cref="IKillerBody"/>, e.g. the player landing on top of it. Unlike a pure
/// capability marker such as <see cref="ICollectableBody"/>, whether an instance is killable is
/// per-instance state: implementing it unconditionally with <see cref="IsKillable"/> always true would
/// make every instance of the implementing class killable in every world with no way to opt out, so
/// <see cref="Physics.CollisionSystem"/> must check <c>body is IKillableBody { IsKillable: true }</c>,
/// not just interface presence. Its health (stomps and thrown objects remove it; 0 kills the hazard),
/// safe window and hurt effect come from <see cref="IHurtableBody"/>, shared with the player.
/// </summary>
public interface IKillableBody : IHurtableBody
{
    /// <summary>Whether this instance can be killed at all, from the placement's <c>Killable</c> key.</summary>
    bool IsKillable { get; }

    /// <summary>
    /// Whether this instance's <see cref="IEffectTrigger.EffectClipName"/> effect (if configured)
    /// should persist as a permanent decorative body after a kill contact (e.g. a "dead plant"
    /// husk), instead of self-removing once its clip finishes playing like an ordinary effect.
    /// </summary>
    bool EffectPersists { get; }

    /// <summary>
    /// Optional <c>SoundLibrary.ini</c> name, from the placement's <c>KillSound</c> key: played
    /// when this hazard is defeated. Null means silent.
    /// </summary>
    string? KillSoundName { get; }

    /// <summary>
    /// Minimum downward closing speed (cells/second) at which landing on this hazard kills it,
    /// from the placement's <c>KillImpactSpeed</c> key (default
    /// <see cref="Constants.GameDefaults.DefaultKillImpactSpeed"/>). A gentler touch from above is
    /// an ordinary hazard hit.
    /// </summary>
    double KillImpactSpeed { get; }

    /// <summary>Optional sound played when this hazard is hurt but survives - from the placement's <c>HurtSound</c> key.</summary>
    string? HurtSoundName { get; }
}
