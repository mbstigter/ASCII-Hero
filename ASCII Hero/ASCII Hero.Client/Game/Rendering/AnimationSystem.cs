using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// Updates animation state for every body in the world that has multi-frame clips with animation
/// timing configured, and queues the player's animation-driven sounds (movement, jump, land).
/// Called once per frame from the game loop, after physics/collision but before rendering, so
/// animated frame changes are immediately visible.
/// </summary>
public class AnimationSystem
{
    private bool _wasAirborne;
    private bool _hasPreviousAirborneState;

    /// <summary>
    /// Plays the sprite's "JumpStart" sound when the player leaves the ground moving upward and its
    /// "Land" sound when it touches down after being airborne (not after climbing/hanging/swimming),
    /// using the same [Sounds] section as the per-pose movement sounds.
    /// </summary>
    private void UpdateJumpAndLandSounds(World2D world, Player2D player)
    {
        var airborne = !player.IsGrounded && !player.IsClimbing && !player.IsHanging && !player.IsSwimming;
        if (_hasPreviousAirborneState)
        {
            if (airborne && !_wasAirborne && player.Velocity.Y < 0
                && player.Sprite.PoseSounds.TryGetValue("JumpStart", out var jumpSound))
            {
                world.PlaySound(jumpSound);
            }
            else if (!airborne && _wasAirborne && player.IsGrounded
                && player.Sprite.PoseSounds.TryGetValue("Land", out var landSound))
            {
                world.PlaySound(landSound);
            }
        }

        _wasAirborne = airborne;
        _hasPreviousAirborneState = true;
    }

    /// <summary>
    /// Advances animation timers for all bodies in the world. Bodies without animation settings
    /// or with single-frame clips no-op internally (see <see cref="Body2D.AdvanceAnimation"/>).
    /// Also ticks the lifetime of any <see cref="EffectInstance2D"/>, queuing it for removal once
    /// its effect has finished playing (unless it's configured to persist).
    /// </summary>
    public void Update(World2D world, double deltaSeconds)
    {
        foreach (var body in world.Objects)
        {
            var advanced = body.AdvanceAnimation(deltaSeconds);

            if (body is Player2D jumpPlayer)
            {
                UpdateJumpAndLandSounds(world, jumpPlayer);
            }

            // Movement sounds: each time the player's moving clip steps to its next frame, the
            // displayed pose's own sound (if the sprite's [Sounds] section names one) plays, so
            // the rhythm automatically follows the pose's animation speed. Idle clips stay silent.
            if (advanced
                && body is Player2D player
                && !player.Clip.Name.EndsWith("_idle", StringComparison.OrdinalIgnoreCase)
                && player.Sprite.PoseSounds.TryGetValue(player.ResolvedPose, out var poseSound))
            {
                world.PlaySound(poseSound);
            }

            if (body is EffectInstance2D effect)
            {
                effect.Tick(deltaSeconds);
                if (effect.IsExpiredAndShouldBeRemoved)
                {
                    world.QueueRemoval(effect);
                }
            }
        }
    }
}
