using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A passable doorway to another world of the same world folder (see <see cref="World2D.SubWorld"/>),
/// backed by a loaded sprite asset: a hatch, a door, a pipe mouth. Marked <see cref="IsStatic"/>
/// like a platform and, unlike a collectable, never removed - the player can use it again and again.
/// It fires once when the player starts overlapping it (see <see cref="Physics.CollisionSystem"/>).
/// </summary>
public class Portal2D : Body2D
{
    /// <summary>The world the portal leads to: 0 is the main world, 1 and up are its sub-worlds. From the placement's <c>TargetWorld</c> key.</summary>
    public int TargetWorld { get; set; }

    /// <summary>
    /// Name of the <c>Player</c> placement (its <c>Entry</c> key) in the target world where the player
    /// arrives. Null arrives at the target world's unnamed player placement. From the placement's <c>TargetEntry</c> key.
    /// </summary>
    public string? TargetEntry { get; set; }

    public Portal2D()
    {
        IsStatic = true;
    }

    /// <summary>Assigns the loaded sprite asset/clip/frame, world position and destination for this instance.</summary>
    public void Spawn(SpriteAsset sprite, string clipName, int frameIndex, Vector2D position, int targetWorld, string? targetEntry, int span = 0)
    {
        SetFrame(sprite, clipName, frameIndex, span);
        Position = position;
        TargetWorld = targetWorld;
        TargetEntry = targetEntry;
    }
}
