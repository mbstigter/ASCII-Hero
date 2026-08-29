using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>Resolves simple axis-aligned bounding box collisions between the player and platforms.</summary>
public class CollisionSystem
{
    public void Resolve(World2D world)
    {
        var player = world.Player;
        player.IsGrounded = false;

        foreach (var platform in world.Platforms)
        {
            if (!Overlaps(player, platform))
            {
                continue;
            }

            // Determine overlap depth on each axis and resolve the smaller one,
            // which keeps corner collisions from causing incorrect pushes.
            var overlapLeft = player.Right - platform.Left;
            var overlapRight = platform.Right - player.Left;
            var overlapTop = player.Bottom - platform.Top;
            var overlapBottom = platform.Bottom - player.Top;

            var minHorizontal = Math.Min(overlapLeft, overlapRight);
            var minVertical = Math.Min(overlapTop, overlapBottom);

            if (minVertical < minHorizontal)
            {
                if (overlapTop < overlapBottom)
                {
                    // Landing on top of the platform.
                    player.Position = new Vector2D(player.Position.X, platform.Top - player.Size.Y);
                    player.Velocity = new Vector2D(player.Velocity.X, 0);
                    player.IsGrounded = true;
                }
                else
                {
                    // Hitting the underside of the platform.
                    player.Position = new Vector2D(player.Position.X, platform.Bottom);
                    player.Velocity = new Vector2D(player.Velocity.X, 0);
                }
            }
            else
            {
                if (overlapLeft < overlapRight)
                {
                    // Colliding with the platform's left edge.
                    player.Position = new Vector2D(platform.Left - player.Size.X, player.Position.Y);
                }
                else
                {
                    // Colliding with the platform's right edge.
                    player.Position = new Vector2D(platform.Right, player.Position.Y);
                }
                player.Velocity = new Vector2D(0, player.Velocity.Y);
            }
        }
    }

    private static bool Overlaps(Player2D player, StaticObject2D platform) =>
        player.Left < platform.Right &&
        player.Right > platform.Left &&
        player.Top < platform.Bottom &&
        player.Bottom > platform.Top;
}
