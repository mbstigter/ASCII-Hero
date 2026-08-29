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

    public double Left => Position.X;
    public double Right => Position.X + Size.X;
    public double Top => Position.Y;
    public double Bottom => Position.Y + Size.Y;
}
