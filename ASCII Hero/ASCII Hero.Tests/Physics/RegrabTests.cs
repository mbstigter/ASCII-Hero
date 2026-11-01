using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

/// <summary>
/// After jumping off or letting go, a still-held grab key can't re-grab straight away, but a fresh
/// press re-grabs mid-air - even on the same climbable/hangable, without first landing.
/// </summary>
[TestClass]
public sealed class RegrabTests
{
    private const int HeldSteps = 5;

    [TestMethod]
    public async Task ClimbJumpOff_HeldKeyBlocks_FreshPressRegrabsMidAir()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "TheMountains");
        var climbable = world.Objects
            .Where(body => body.IsStatic && body.IsClimbable && body.Size.Y > body.Size.X)
            .OrderByDescending(body => body.Size.Y)
            .First();
        var player = world.Player;
        player.Position = new Vector2D(
            x: climbable.Position.X + climbable.Size.X / 2 - player.Size.X / 2,
            y: climbable.Position.Y + climbable.Size.Y / 2 - player.Size.Y / 2);
        player.Velocity = new Vector2D(0, 0);
        player.IsTouchingClimbable = true;
        player.IsClimbing = true;
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        var input = new InputState();
        void Step()
        {
            physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        }

        input.KeyDown("ArrowUp");
        input.KeyDown("Space");
        Step();
        Assert.IsFalse(player.IsClimbing, "Jump lets go.");

        input.KeyUp("Space");
        for (var i = 0; i < HeldSteps; i++)
        {
            Step();
            Assert.IsTrue(player.IsTouchingClimbable, "Test setup must stay on the climbable.");
            Assert.IsFalse(player.IsClimbing, "Still-held Up must not cancel the jump.");
        }

        input.KeyUp("ArrowUp");
        Step();
        input.KeyDown("ArrowUp");
        Step();
        Step();

        Assert.IsFalse(player.IsGrounded);
        Assert.IsTrue(player.IsClimbing, "A fresh Up press re-grabs mid-air.");
    }

    [TestMethod]
    public async Task HangLetGo_HeldKeyBlocks_FreshPressRegrabsMidAir()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Ropes");
        var player = world.Player;
        player.IsTouchingHangable = true;
        player.IsHanging = true;
        var physics = new PhysicsSystem();
        var input = new InputState();
        void Step() => physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);

        input.KeyDown("ArrowDown");
        Step();
        Assert.IsFalse(player.IsHanging, "Down lets go.");

        input.KeyUp("ArrowDown");
        input.KeyDown("ArrowUp");
        for (var i = 0; i < HeldSteps; i++)
        {
            Step();
            Assert.IsFalse(player.IsHanging, "Up held straight after letting go must not re-grab.");
        }

        input.KeyUp("ArrowUp");
        Step();
        input.KeyDown("ArrowUp");
        Step();

        Assert.IsFalse(player.IsGrounded);
        Assert.IsTrue(player.IsHanging, "A fresh Up press re-grabs mid-air.");
    }
}
