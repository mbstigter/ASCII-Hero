using ASCII_Hero.Client.Game.Physics;

namespace ASCII_Hero.Client.Game.World;

/// <summary>Base class for anything that lives in the game world at a floating-point position.</summary>
public abstract class Body2D
{
    /// <summary>Position of the body's top-left corner, in world cells (not pixels).</summary>
    public Vector2D Position { get; set; }

    /// <summary>Size of the body's bounding box, in world cells.</summary>
    public Vector2D Size { get; set; } = new(1, 1);

    /// <summary>Whether this body is immovable terrain (true) or subject to physics/movement (false).</summary>
    public bool IsStatic { get; protected init; }

    /// <summary>
    /// Collision shape, as one or more rectangles in local cell coordinates (relative to
    /// <see cref="Position"/>, not world space). Defaults to a single rectangle covering the
    /// full <see cref="Size"/>, but derived types may override this with rectangles derived
    /// from actual sprite data (see <see cref="CollisionShapeBuilder"/>) to exclude blank/empty
    /// cells from physics.
    /// </summary>
    public virtual IReadOnlyList<Rect2D> LocalCollisionRects => [new Rect2D(0, 0, Size.X, Size.Y)];

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
}
