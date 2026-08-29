using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>An axis-aligned rectangle in world cells, used to describe a piece of a body's collision shape.</summary>
public readonly struct Rect2D(double x, double y, double width, double height)
{
    public double X { get; } = x;
    public double Y { get; } = y;
    public double Width { get; } = width;
    public double Height { get; } = height;

    public double Left => X;
    public double Right => X + Width;
    public double Top => Y;
    public double Bottom => Y + Height;

    /// <summary>Returns this rectangle translated by the given world offset (e.g. a body's Position).</summary>
    public Rect2D Translate(Vector2D offset) => new(X + offset.X, Y + offset.Y, Width, Height);

    public bool Overlaps(Rect2D other) =>
        Left < other.Right &&
        Right > other.Left &&
        Top < other.Bottom &&
        Bottom > other.Top;
}
