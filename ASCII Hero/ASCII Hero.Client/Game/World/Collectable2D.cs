using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// An item the player can gather, backed by a loaded sprite asset. Marked <see cref="IsStatic"/>
/// like a platform (it doesn't move), but unlike a platform it is not solid: it is excluded from
/// platform-collision blocking in <see cref="Physics.CollisionSystem"/> and instead only
/// participates in the overlap pass that removes it from the world on contact.
/// </summary>
public class Collectable2D : Body2D, ICollectableBody
{
    public Collectable2D()
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
