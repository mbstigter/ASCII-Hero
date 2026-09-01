namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// Capability for a body that can optionally trigger a cosmetic <see cref="EffectInstance2D"/> on
/// contact (e.g. a collectable's pickup fade, a hazard's spark, a killed enemy's crumble). A null
/// <see cref="EffectClipName"/> (the default) means no effect is configured for this instance; a
/// non-null value names a clip that must already exist on this same body's own
/// <see cref="Body2D.Sprite"/>, so the effect reuses a clip authored on the existing asset instead
/// of a separate effect-specific asset.
/// </summary>
public interface IEffectTrigger
{
    string? EffectClipName { get; }
}
