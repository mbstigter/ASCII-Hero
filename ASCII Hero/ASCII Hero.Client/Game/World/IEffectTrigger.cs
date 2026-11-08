namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// Capability for a body that can optionally trigger a cosmetic <see cref="EffectInstance2D"/> on
/// contact (e.g. a collectable's pickup fade, a killed hazard's crumble). A null
/// <see cref="EffectClipName"/> (the default) means no effect is configured for this instance; a
/// non-null value names a clip that must already exist on this same body's own
/// <see cref="Body2D.Sprite"/>, so the effect reuses a clip authored on the existing asset instead
/// of a separate effect-specific asset. The player's own reaction effects (e.g. its hazard-hit
/// spark) are separate named slots on <see cref="Player2D"/>, not this interface.
/// </summary>
public interface IEffectTrigger
{
    /// <summary>Clip on this body's own sprite to play as its effect, or null for none.</summary>
    string? EffectClipName { get; }
}
