using ASCII_Hero.Client.Game.Input;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>
/// Applies horizontal movement input to the player, gravity to any <see cref="IGravityAffected"/>
/// body, and integrates position from velocity for every <see cref="IPhysicsBody"/> in
/// <see cref="World2D.Objects"/> each frame. Kinematic bodies move at a constant, predefined
/// velocity and never receive gravity or input.
/// </summary>
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

        if (input.IsJumpPressed && player.IsGrounded)
        {
            velocity.Y = -JumpSpeed;
            player.IsGrounded = false;
        }

        player.Velocity = velocity;

        foreach (var body in world.Objects)
        {
            switch (body)
            {
                case KinematicObject2D kinematicObject:
                    // Predefined constant motion, no gravity/force integration.
                    kinematicObject.Position = new Vector2D(
                        kinematicObject.Position.X + kinematicObject.Velocity.X * deltaSeconds,
                        kinematicObject.Position.Y + kinematicObject.Velocity.Y * deltaSeconds);
                    break;

                case IGravityAffected gravityAffected:
                    StepMovingBody(world, gravityAffected, gravityAffected.UseGravity, deltaSeconds);
                    break;

                case IPhysicsBody physicsBody:
                    StepMovingBody(world, physicsBody, useGravity: false, deltaSeconds);
                    break;
            }
        }
    }

    private static void StepMovingBody(World2D world, IPhysicsBody body, bool useGravity, double deltaSeconds)
    {
        var velocity = body.Velocity;

        if (useGravity)
        {
            velocity.Y += world.Gravity * deltaSeconds;
        }

        body.Velocity = velocity;

        body.Position = new Vector2D(
            body.Position.X + velocity.X * deltaSeconds,
            body.Position.Y + velocity.Y * deltaSeconds);
    }
}
