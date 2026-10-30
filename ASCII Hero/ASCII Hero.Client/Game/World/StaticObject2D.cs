using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>A solid, static object (platform, wall, decoration) backed by a loaded sprite asset.</summary>
public class StaticObject2D : Body2D
{
    public StaticObject2D()
    {
        IsStatic = true;
    }

    /// <summary>Assigns the loaded sprite asset/clip/frame and world position for this instance.</summary>
    public void Spawn(SpriteAsset sprite, string clipName, int frameIndex, Vector2D position, int span = 0)
    {
        SetFrame(sprite, clipName, frameIndex, span);
        Position = position;
    }
}
