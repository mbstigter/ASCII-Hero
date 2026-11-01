using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

/// <summary>
/// Grabbing a hangable is deliberate (Up held while touching it), like a climbable: merely
/// touching one lets the player pass straight through.
/// </summary>
[TestClass]
public sealed class HangGrabTests
{
    private const int MaxSettleSteps = 600;

    private static void StepPhysics(World2D world, PhysicsSystem physics, InputState input) =>
        physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);

    [TestMethod]
    public async Task TouchingHangable_WithoutUp_DoesNotHang()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Ropes");
        var player = world.Player;
        player.IsTouchingHangable = true;

        StepPhysics(world, new PhysicsSystem(), new InputState());

        Assert.IsFalse(player.IsHanging);
    }

    [TestMethod]
    public async Task TouchingHangable_WithUpHeld_Hangs()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Ropes");
        var player = world.Player;
        player.IsTouchingHangable = true;
        var input = new InputState();
        input.KeyDown("ArrowUp");

        StepPhysics(world, new PhysicsSystem(), input);

        Assert.IsTrue(player.IsHanging);
        Assert.IsFalse(player.IsClambering);
    }

    [TestMethod]
    public async Task CrawlingUnderHangable_UpPress_GrabsStraightIntoClamber()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Ropes");
        var player = world.Player;
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        var input = new InputState();
        for (var i = 0; i < MaxSettleSteps && !player.IsGrounded; i++)
        {
            StepPhysics(world, physics, input);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        }

        Assert.IsTrue(player.IsGrounded, "Player never landed.");
        player.Stance = "Crawl";
        player.IsTouchingHangable = true;
        input.KeyDown("ArrowUp");

        StepPhysics(world, physics, input);

        Assert.IsTrue(player.IsHanging);
        Assert.IsTrue(player.IsClambering);
    }

    [TestMethod]
    public async Task LetGo_BlocksRegrabUntilLanding_EvenWhileStillTouchingHangable()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Ropes");
        var player = world.Player;
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        var input = new InputState();
        for (var i = 0; i < MaxSettleSteps && !player.IsGrounded; i++)
        {
            StepPhysics(world, physics, input);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        }

        Assert.IsTrue(player.IsGrounded, "Player never landed.");
        player.IsTouchingHangable = true;
        player.IsHanging = true;
        input.KeyDown("ArrowDown");
        StepPhysics(world, physics, input);
        Assert.IsFalse(player.IsHanging, "Down lets go.");

        input.KeyUp("ArrowDown");
        input.KeyDown("ArrowUp");
        StepPhysics(world, physics, input);
        Assert.IsFalse(player.IsHanging, "Debounce blocks an instant re-grab.");

        StepPhysics(world, physics, input);
        Assert.IsTrue(player.IsHanging, "Debounce released on landing, though the hangable is still touched.");
    }

    [TestMethod]
    public async Task PassingThroughHangable_IsOnlySnappedWhileHanging()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Ropes");
        var player = world.Player;
        var hangable = world.Objects.First(body => body.IsStatic && body.IsHangable && body.GetCarryVelocity().X == 0);
        var hangableRect = hangable.CollisionRects.OrderBy(rect => rect.Top).First();
        var playerRects = player.CollisionRects;
        var topOffset = playerRects.Min(rect => rect.Top) - player.Position.Y;
        var head = playerRects.OrderBy(rect => rect.Top).First();
        var headCenterOffset = (head.Left + head.Right) / 2 - player.Position.X;
        var start = new Vector2D(
            x: (hangableRect.Left + hangableRect.Right) / 2 - headCenterOffset,
            y: hangableRect.Top + (hangableRect.Bottom - hangableRect.Top) / 2 - topOffset);
        var fallingVelocity = new Vector2D(0, PhysicsConstants.MaxGripSpeed / 2);

        player.Position = start;
        player.Velocity = fallingVelocity;
        new CollisionSystem().Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        Assert.IsTrue(player.IsTouchingHangable);
        Assert.AreEqual(fallingVelocity.Y, player.Velocity.Y, "Not hanging: passes through.");

        player.Position = start;
        player.Velocity = fallingVelocity;
        player.IsHanging = true;
        new CollisionSystem().Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        Assert.AreEqual(0.0, player.Velocity.Y, "Hanging: caught and held.");
    }
}
