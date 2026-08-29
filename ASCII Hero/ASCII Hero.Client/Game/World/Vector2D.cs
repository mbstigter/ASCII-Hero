namespace ASCII_Hero.Client.Game.World;

/// <summary>Simple floating-point 2D vector used for world-space positions and velocities.</summary>
public struct Vector2D(double x, double y)
{
    public double X = x;
    public double Y = y;

    public static Vector2D Zero => new(0, 0);

    public static Vector2D operator +(Vector2D a, Vector2D b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2D operator -(Vector2D a, Vector2D b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2D operator *(Vector2D a, double s) => new(a.X * s, a.Y * s);
}
