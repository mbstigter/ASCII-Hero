using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>The player-controlled character, backed by the loaded "Player" sprite asset.</summary>
public class Player2D : GameObject2D, IMovingBody
{
    /// <summary>Current velocity, in world cells per second.</summary>
    public Vector2D Velocity { get; set; }

    /// <summary>Whether the player is currently standing on a platform or the world's floor.</summary>
    public bool IsGrounded { get; set; }

    public Player2D()
    {
        IsStatic = false;
    }

    /// <summary>Assigns the loaded Player sprite asset and activates its "idle" clip.</summary>
    public void Spawn(SpriteAsset sprite) => SetFrame(sprite, "idle");
}
