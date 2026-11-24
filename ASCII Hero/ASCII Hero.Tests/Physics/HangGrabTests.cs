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
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "RopesAndVines");
        var player = world.Player;
        player.IsTouchingHangable = true;

        StepPhysics(world, new PhysicsSystem(), new InputState());

        Assert.IsFalse(player.IsHanging);
    }

    [TestMethod]
    public async Task TouchingHangable_WithUpHeld_Hangs()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "RopesAndVines");
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
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "RopesAndVines");
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
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "RopesAndVines");
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
    [DataRow(0.0)]
    [DataRow(1.0)]
    public async Task PassingThroughHangable_CollisionDoesNotCancelVelocity(double friction)
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "RopesAndVines");
        var player = world.Player;
        var hangable = world.Objects.First(body => body.IsStatic && body.IsHangable && body.GetCarryVelocity().X == 0);
        hangable.Friction = friction;
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

        Assert.AreEqual(fallingVelocity.Y, player.Velocity.Y, "Hanging collision must not cancel velocity without force.");
        Assert.AreEqual(start.Y, player.Position.Y, 1e-8);
    }

    [TestMethod]
    public async Task ReachBelowPipe_OnlyMaintainsAnExistingHang()
    {
        var (world, pipe) = await PrepareHangAsync(friction: 1);
        var player = world.Player;
        player.Position += new Vector2D(x: 0, y: 0.3);
        var collision = new CollisionSystem();

        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        Assert.IsFalse(player.IsTouchingHangable);
        player.IsHanging = true;
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        Assert.IsFalse(player.IsTouchingHangable, "Reach alone cannot acquire a surface without a prior grip.");
        player.AddContact(ContactType.Hangable, pipe);
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        Assert.IsTrue(player.GetContactingBodies(ContactType.Hangable).Contains(pipe));

        player.Position += new Vector2D(x: 0, y: PhysicsConstants.HangGripReachCells);
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        Assert.IsFalse(player.IsTouchingHangable);
    }

    [TestMethod]
    public async Task ReachBelowPipe_DoesNotTransferGripWithoutTouchingOtherPipe()
    {
        var (world, pipe) = await PrepareHangAsync(friction: 1);
        var other = new KinematicObject2D { IsHangable = true, IsPassable = true };
        other.Spawn(pipe.Sprite, pipe.Clip.Name, 0, pipe.Position, Vector2D.Zero);
        world.Objects.Insert(0, other);
        var player = world.Player;
        var head = player.CollisionRects.OrderBy(rect => rect.Top).First();
        var otherRect = other.CollisionRects[0];
        other.Position += new Vector2D(x: (head.Left + head.Right - otherRect.Left - otherRect.Right) / 2, y: 0);
        player.Position += new Vector2D(x: 0, y: 0.3);
        player.IsHanging = true;
        player.AddContact(ContactType.Hangable, pipe);
        var collision = new CollisionSystem();

        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        Assert.AreSame(pipe, player.GetContactingBodies(ContactType.Hangable).Single());

        player.Position += new Vector2D(x: 0, y: -0.3);
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        Assert.AreSame(other, player.GetContactingBodies(ContactType.Hangable).Single(), "Actual contact can transfer the grip.");
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(0.0025)]
    public async Task SlipperyPipe_CannotKeepPlayerSuspended(double friction)
    {
        var (world, _) = await PrepareHangAsync(friction);
        var player = world.Player;
        var startY = player.Position.Y;
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        var input = new InputState();
        input.KeyDown("ArrowUp");
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        Assert.IsTrue(player.IsTouchingHangable);

        for (var i = 0; i < 60; i++)
        {
            StepPhysics(world, physics, input);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
            if (!player.IsHanging)
            {
                Assert.IsGreaterThan(startY + 1, player.Position.Y);
                Assert.IsGreaterThan(0, player.Velocity.Y);
                return;
            }
        }

        Assert.Fail("A grip too weak to hold the player's weight must release.");
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(4.0)]
    [DataRow(12.0)]
    public async Task GrippyPipe_CatchesAndSettlesWithoutJitter(double fallingSpeed)
    {
        var (world, pipe) = await PrepareHangAsync(friction: 1);
        var player = world.Player;
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        var input = new InputState();
        input.KeyDown("ArrowUp");
        player.Velocity = new Vector2D(x: 0, y: fallingSpeed);
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        Assert.IsTrue(player.IsTouchingHangable);

        for (var i = 0; i < 180; i++)
        {
            StepPhysics(world, physics, input);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
            Assert.IsTrue(player.IsHanging, $"Grip lost at step {i}.");
            if (i >= 120)
            {
                Assert.AreEqual(pipe.CollisionRects.Max(rect => rect.Bottom), player.CollisionRects.Min(rect => rect.Top), 0.05);
                Assert.AreEqual(pipe.GetCarryVelocity().Y, player.Velocity.Y, 0.05);
            }
        }

        var handTop = player.CollisionRects.Min(rect => rect.Top);
        var pipeBottom = pipe.CollisionRects.Max(rect => rect.Bottom);
        Assert.AreEqual(pipeBottom, handTop, 0.05);
        Assert.AreEqual(pipe.GetCarryVelocity().Y, player.Velocity.Y, 0.05);
    }

    [TestMethod]
    [DataRow(-2.0)]
    [DataRow(2.0)]
    public async Task GrippyMovingPipe_CarriesHangerVertically(double speed)
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var pipe = world.Objects.OfType<KinematicObject2D>().First(body => body.IsHangable);
        world.Objects.RemoveAll(body => body != player && body != pipe);
        pipe.SetPatrol(null, null, 0, null, null, 0);
        pipe.Position = new Vector2D(x: 25, y: 25);
        pipe.Velocity = new Vector2D(x: 0, y: speed);
        pipe.ConveyorVelocity = Vector2D.Zero;
        pipe.Friction = 1;
        player.Friction = 1;
        var pipeRect = pipe.CollisionRects[0];
        var head = player.CollisionRects.OrderBy(rect => rect.Top).First();
        player.Position = new Vector2D(
            x: (pipeRect.Left + pipeRect.Right) / 2 - ((head.Left + head.Right) / 2 - player.Position.X),
            y: pipeRect.Bottom - 0.1 - (head.Top - player.Position.Y));
        player.Velocity = new Vector2D(x: 0, y: speed);
        var input = new InputState();
        input.KeyDown("ArrowUp");
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        Assert.IsTrue(player.IsTouchingHangable);

        for (var i = 0; i < 120; i++)
        {
            StepPhysics(world, physics, input);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
            Assert.IsTrue(player.IsHanging, $"Grip lost at step {i}.");
        }

        Assert.AreEqual(pipe.CollisionRects[0].Bottom, player.CollisionRects.Min(rect => rect.Top), 0.05);
        Assert.AreEqual(speed, player.Velocity.Y, 0.05);
    }

    private static async Task<(World2D World, Body2D Pipe)> PrepareHangAsync(double friction)
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "RopesAndVines");
        var player = world.Player;
        var pipe = world.Objects.First(body => body.IsStatic && body.IsHangable && body.GetCarryVelocity().X == 0);
        world.Objects.RemoveAll(body => body != player && body != pipe);
        player.Friction = 1;
        pipe.Friction = friction;
        var pipeRect = pipe.CollisionRects.OrderBy(rect => rect.Top).First();
        var head = player.CollisionRects.OrderBy(rect => rect.Top).First();
        player.Position = new Vector2D(
            x: (pipeRect.Left + pipeRect.Right) / 2 - ((head.Left + head.Right) / 2 - player.Position.X),
            y: pipeRect.Bottom - 0.1 - (head.Top - player.Position.Y));
        player.Velocity = Vector2D.Zero;
        return (world, pipe);
    }
}
