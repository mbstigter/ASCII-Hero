namespace ASCII_Hero.Client.Game.World;

/// <summary>The player-controlled character, rendered as '@'.</summary>
public class Player2D : Body2D
{
    public const char Glyph = '@';

    /// <summary>Current velocity, in world cells per second.</summary>
    public Vector2D Velocity { get; set; }

    /// <summary>Whether the player is currently standing on a platform.</summary>
    public bool IsGrounded { get; set; }

    public Player2D()
    {
        IsStatic = false;
        Size = new Vector2D(1, 1);
    }
}
