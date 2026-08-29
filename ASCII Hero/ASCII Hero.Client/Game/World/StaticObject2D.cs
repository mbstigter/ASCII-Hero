namespace ASCII_Hero.Client.Game.World;

/// <summary>A solid, static platform made of ASCII characters that the player can stand on.</summary>
public class StaticObject2D : Body2D
{
    public const char Glyph = '#';

    public StaticObject2D(double x, double y, double width, double height)
    {
        IsStatic = true;
        Position = new Vector2D(x, y);
        Size = new Vector2D(width, height);
    }
}
