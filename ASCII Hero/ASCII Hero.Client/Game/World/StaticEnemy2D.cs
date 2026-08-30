using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A non-moving hazard (e.g. spikes) backed by a loaded sprite asset. Like
/// <see cref="StaticObject2D"/> it is immovable terrain, but it also damages the player (or any
/// moving body) on contact.
/// </summary>
public class StaticEnemy2D : Body2D, IHazardBody
{
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
