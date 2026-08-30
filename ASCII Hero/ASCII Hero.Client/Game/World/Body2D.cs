using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Physics;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// Base class for anything that lives in the game world at a floating-point position and is
/// backed by a loaded sprite frame (characters/foregroundcolors/backgroundcolors/materials
/// grid). Size and collision shape are both derived once from the frame's actual grid data via
/// <see cref="CollisionShapeBuilder"/>, so every sprite-backed object - the player, static
/// platforms, enemies, collectables - shares one loading/shape-derivation path instead of each
/// subclass repeating it.
/// </summary>
public abstract class Body2D
{
    private IReadOnlyList<Rect2D> _localCollisionRects = [];

    /// <summary>Position of the body's top-left corner, in world cells (not pixels).</summary>
    public Vector2D Position { get; set; }

    /// <summary>Size of the body's bounding box, in world cells.</summary>
    public Vector2D Size { get; set; } = new(1, 1);

    /// <summary>Whether this body is immovable terrain (true) or subject to physics/movement (false).</summary>
    public bool IsStatic { get; protected init; }

    /// <summary>The sprite asset this object was spawned from.</summary>
    public SpriteAsset Sprite { get; private set; } = null!;

    /// <summary>The clip currently being displayed/collided against (e.g. "idle").</summary>
    public SpriteClip Clip { get; private set; } = null!;

    /// <summary>The specific frame within <see cref="Clip"/> currently active.</summary>
    public SpriteFrame Frame { get; private set; } = null!;

    /// <summary>
    /// Collision shape, as one or more rectangles in local cell coordinates (relative to
    /// <see cref="Position"/>, not world space). Derived from the active frame's actual grid
    /// data via <see cref="CollisionShapeBuilder"/>, excluding blank/empty cells from physics.
    /// </summary>
    public IReadOnlyList<Rect2D> LocalCollisionRects => _localCollisionRects;

    /// <summary>The body's collision shape translated into world-space rectangles.</summary>
    public IReadOnlyList<Rect2D> CollisionRects
    {
        get
        {
            var position = Position;
            return LocalCollisionRects.Select(rect => rect.Translate(position)).ToList();
        }
    }

    /// <summary>Overall bounding box of the body in world space, spanning all collision rectangles.</summary>
    public double Left => Position.X;
    public double Right => Position.X + Size.X;
    public double Top => Position.Y;
    public double Bottom => Position.Y + Size.Y;

    /// <summary>
    /// Assigns the sprite/clip/frame this object renders and collides as, deriving Size and
    /// collision rectangles from the frame's char grid. Called once at spawn time; for a
    /// non-animating object (a static shape variant) this is the only call needed. Animated
    /// objects can call this again later to switch frames/clips. When the sprite declares a
    /// <see cref="Assets.TileAxis"/> and <paramref name="repeatCount"/> is greater than 1, the
    /// frame's authored unit is repeated along that axis first (see
    /// <see cref="SpriteFrameTiler"/>), letting one small tileable unit build up an
    /// arbitrary-length platform/wall.
    /// </summary>
    protected void SetFrame(SpriteAsset sprite, string clipName, int frameIndex = 0, int repeatCount = 1)
    {
        Sprite = sprite;
        Clip = sprite.GetClip(clipName);
        Frame = SpriteFrameTiler.Tile(Clip.Frames[frameIndex], sprite.TileAxis, repeatCount);

        Size = new Vector2D(Frame.Width, Frame.Height);
        _localCollisionRects = CollisionShapeBuilder.DeriveRectangles(Frame.Chars, sprite.EmptyChar);
    }
}
