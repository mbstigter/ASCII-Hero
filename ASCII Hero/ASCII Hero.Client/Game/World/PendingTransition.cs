namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A change of game state that has been triggered but is still waiting for its effect and sound
/// to play out - see <see cref="World2D.PendingTransition"/>. While one is pending,
/// <see cref="GameLoop"/> freezes physics (cosmetic effects keep animating) and, after a short
/// delay, carries out the transition.
/// </summary>
public enum PendingTransition
{
    /// <summary>Nothing pending; normal play.</summary>
    None,

    /// <summary>The player reached a <see cref="CollectableType.LevelEnd"/>; the level-complete screen follows.</summary>
    LevelEnd,

    /// <summary>The player lost a life; a respawn follows, or the game-over screen when no lives are left.</summary>
    Death,
}
