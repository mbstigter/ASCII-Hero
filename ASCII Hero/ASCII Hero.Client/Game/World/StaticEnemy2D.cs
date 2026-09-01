using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A non-moving hazard (e.g. spikes) backed by a loaded sprite asset. Like
/// <see cref="StaticObject2D"/> it is immovable terrain, but it also damages the player (or any
/// moving body) on contact.
/// </summary>
public class StaticEnemy2D : Body2D, IHazardBody, IEffectTrigger, IKillableBody
{
    /// <summary>
    /// Optional clip name (on this instance's own <see cref="Body2D.Sprite"/>) to play as a
    /// cosmetic effect on contact (e.g. a "crumble" clip when killed). Null (the default) means
    /// no effect.
    /// </summary>
    public string? EffectClipName { get; set; }

    /// <summary>
    /// Whether this instance can be "killed" (removed from the world) by a qualifying contact
    /// (landed on top of). Defaults to false, so existing levels that don't opt in are unaffected.
    /// </summary>
    public bool IsKillable { get; set; }

    /// <summary>Whether this instance's effect (if configured) persists as a permanent husk after a kill contact.</summary>
    public bool EffectPersists { get; set; }

    public StaticEnemy2D()
    {
        IsStatic = true;
    }

    /// <summary>Assigns the loaded sprite asset/clip/frame and world position for this instance.</summary>
    public void Spawn(SpriteAsset sprite, string clipName, int frameIndex, Vector2D position, int repeatCount = 1)
    {
        SetFrame(sprite, clipName, frameIndex, repeatCount);
        Position = position;
    }
}
