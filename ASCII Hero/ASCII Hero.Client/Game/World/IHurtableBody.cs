namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A body that has health, loses it when hurt and then gets a short safe window (see
/// <see cref="HitRecovery"/>) before it can be hurt again: the player and killable hazards.
/// </summary>
public interface IHurtableBody
{
    /// <summary>
    /// Remaining health, from 0 to full (<see cref="Constants.GameDefaults.PlayerFullHealth"/> for the
    /// player, the placement's <c>Health</c> key for a hazard). Reaching 0 kills the body.
    /// </summary>
    double Health { get; set; }

    /// <summary>
    /// The safe window after surviving a hit. Its duration is the placement's <c>RecoverySeconds</c>
    /// key, or <see cref="Constants.GameDefaults.DefaultPlayerRecoverySeconds"/> /
    /// <see cref="Constants.GameDefaults.DefaultHazardRecoverySeconds"/> when absent.
    /// </summary>
    HitRecovery Recovery { get; }

    /// <summary>
    /// Optional clip (on this body's own sprite) played on it when it is hurt but survives - from the
    /// placement's <c>HurtEffectClip</c> key. Null means no effect.
    /// </summary>
    string? HurtEffectClipName { get; }
}
