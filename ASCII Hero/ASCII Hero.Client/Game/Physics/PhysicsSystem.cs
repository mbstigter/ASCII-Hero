using ASCII_Hero.Client.Game.Assets;
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
    private const double MoveSpeedStanding = 12.0;
    private const double MoveSpeedCrawling = 6.0;
    private const double JumpSpeed = 18.0;

    private bool _wasCrawlKeyDown;

    public void Step(World2D world, InputState input, double deltaSeconds)
    {
        var player = world.Player;
        var velocity = player.Velocity;

        // Edge-triggered stance toggle: Down crouches the player into Crawl (only while
        // Walking); Up or Space stands them back up from Crawl (matching the existing jump
        // keys, so "up" always means "up" regardless of stance). Standing back up must not
        // also trigger a jump on this same frame, so `stoodUpThisFrame` explicitly suppresses
        // the jump check below even though Stance is now "Walk" again - jumping only fires on a
        // later, separate press once already standing.
        var crawlKeyDown = input.IsCrawlPressed;
        var stoodUpThisFrame = false;
        if (player.Stance == "Walk" && crawlKeyDown && !_wasCrawlKeyDown)
        {
            player.Stance = "Crawl";
        }
        else if (player.Stance == "Crawl" && input.IsJumpPressed)
        {
            player.Stance = "Walk";
            stoodUpThisFrame = true;
        }
        _wasCrawlKeyDown = crawlKeyDown;

        var moveSpeed = player.Stance == "Walk" ? MoveSpeedStanding : MoveSpeedCrawling;

        // TODO: Player movement is currently driven directly by velocity assignment from input,
        // unlike every other body (DynamicObject2D/MovingEnemy2D), which move via force/velocity
        // integration. Revisit this to apply movement as a force causing acceleration instead,
        // consistent with the rest of the physics model, as part of the physics engine refinement.

        // Horizontal movement is directly driven by input (no acceleration/friction for milestone 1).
        velocity.X = 0;
        if (input.IsLeftPressed)
        {
            velocity.X -= moveSpeed;
        }
        if (input.IsRightPressed)
        {
            velocity.X += moveSpeed;
        }

        if (input.IsJumpPressed && player.IsGrounded && player.Stance == "Walk" && !stoodUpThisFrame)
        {
            velocity.Y = -JumpSpeed;
            player.IsGrounded = false;
        }

        player.Velocity = velocity;

        // "Jump" is a visual-only pose, not a distinct stance the player can be toggled into/out
        // of like Crawl - it's simply what's shown while airborne, regardless of which stance
        // (Walk or Crawl) the player was in when they left the ground (e.g. crawling off a ledge
        // still assumes the jump pose mid-air). Stance itself stays "Walk"/"Crawl" throughout;
        // only the resolved pose swaps to the Jump stance's clips while not grounded.
        var poseStance = !player.IsGrounded ? "Jump" : player.Stance;
        var facing = velocity.X < 0 ? Facing.Left : velocity.X > 0 ? Facing.Right : Facing.Idle;
        player.SetPose(player.Sprite, poseStance, facing);

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
                    StepMovingBody(world, gravityAffected, gravityAffected.GravityAffected, deltaSeconds);
                    break;

                case IPhysicsBody physicsBody:
                    StepMovingBody(world, physicsBody, gravityAffected: false, deltaSeconds);
                    break;
            }
        }
    }

    private static void StepMovingBody(World2D world, IPhysicsBody body, bool gravityAffected, double deltaSeconds)
    {
        var velocity = body.Velocity;

        if (gravityAffected)
        {
            velocity.Y += world.Gravity * deltaSeconds;
        }

        body.Velocity = velocity;

        body.Position = new Vector2D(
            body.Position.X + velocity.X * deltaSeconds,
            body.Position.Y + velocity.Y * deltaSeconds);
    }
}
