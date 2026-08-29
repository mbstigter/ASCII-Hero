using ASCII_Hero.Client.Game.Input;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>Applies horizontal movement input, gravity and jumping to the player each frame.</summary>
public class PhysicsSystem
{
    private const double MoveSpeed = 12.0;
    private const double JumpSpeed = 18.0;

    public void Step(World2D world, InputState input, double deltaSeconds)
    {
        var player = world.Player;
        var velocity = player.Velocity;

        // Horizontal movement is directly driven by input (no acceleration/friction for milestone 1).
        velocity.X = 0;
        if (input.IsLeftPressed)
        {
            velocity.X -= MoveSpeed;
        }
        if (input.IsRightPressed)
        {
            velocity.X += MoveSpeed;
        }

        // Gravity.
        velocity.Y += world.Gravity * deltaSeconds;

        if (input.IsJumpPressed && player.IsGrounded)
        {
            velocity.Y = -JumpSpeed;
            player.IsGrounded = false;
        }

        player.Velocity = velocity;

        // Integrate position.
        player.Position = new Vector2D(
            player.Position.X + velocity.X * deltaSeconds,
            player.Position.Y + velocity.Y * deltaSeconds);
    }
}
