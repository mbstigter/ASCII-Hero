using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Animation;

/// <summary>
/// Updates animation state for every body in the world that has multi-frame clips with animation
/// timing configured. Called once per frame from the game loop, after physics/collision but
/// before rendering, so animated frame changes are immediately visible.
/// </summary>
public class AnimationSystem
{
    /// <summary>
    /// Advances animation timers for all bodies in the world. Bodies without animation settings
    /// or with single-frame clips no-op internally (see <see cref="Body2D.AdvanceAnimation"/>).
    /// </summary>
    public void Update(World2D world, double deltaSeconds)
    {
        foreach (var body in world.Objects)
        {
            body.AdvanceAnimation(deltaSeconds);
        }
    }
}
