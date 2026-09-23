using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>An axis-aligned rectangle in world cells, used to describe a piece of a body's collision shape.</summary>
public readonly struct Rect(double x, double y, double width, double height)
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
    public Rect Translate(Vector2D offset) => new(X + offset.X, Y + offset.Y, Width, Height);

    public bool Overlaps(Rect other) =>
        Left < other.Right &&
        Right > other.Left &&
        Top < other.Bottom &&
        Bottom > other.Top;

    /// <summary>
    /// The overlapped vertical extent (in world cells) between this rectangle and
    /// <paramref name="other"/>, or 0 if they don't overlap vertically at all (regardless of
    /// horizontal overlap) - used by <see cref="Physics.PhysicsSystem"/>'s submerged-fraction
    /// calculation (see its own doc comment) to determine how much of a body is actually below a
    /// medium volume's surface, rather than treating any overlap at all as "fully immersed."
    /// </summary>
    public double VerticalOverlap(Rect other)
    {
        var overlapTop = Math.Max(Top, other.Top);
        var overlapBottom = Math.Min(Bottom, other.Bottom);
        return Math.Max(0, overlapBottom - overlapTop);
    }
}
