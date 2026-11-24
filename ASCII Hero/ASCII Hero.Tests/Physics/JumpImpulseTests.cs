using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.Rendering;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

[TestClass]
public sealed class JumpImpulseTests
{
    private static InputState JumpInput()
    {
        var input = new InputState();
        input.KeyDown("Space");
        return input;
    }

    [TestMethod]
    public async Task GroundJump_MovableSupport_ConservesMomentumAndReachesRelativeSpeed()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Crates");
        var player = world.Player;
        var support = world.Objects.OfType<DynamicObject2D>().First();
        player.AddContact(ContactType.SurfaceBottom, support);
        player.Velocity = new Vector2D(3, 4);
        support.Velocity = new Vector2D(0, -2);
        var momentumBefore = player.Mass * player.Velocity.Y + support.Mass * support.Velocity.Y;

        new PhysicsSystem().Step(world, JumpInput(), 0);

        Assert.AreEqual(momentumBefore, player.Mass * player.Velocity.Y + support.Mass * support.Velocity.Y, 1e-8);
        Assert.AreEqual(-GameDefaults.GroundJumpSpeed, player.Velocity.Y - support.Velocity.Y, 1e-8);
        Assert.AreEqual(3, player.Velocity.X, 1e-8);
        Assert.IsFalse(player.IsGrounded);
    }

    [TestMethod]
    public async Task GroundJump_IsEffortLimitedButDoesNotRequireFloorFriction()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var floor = world.Objects.First(body => body.IsStatic && !body.IsPassable && body.CollisionRects.Count > 0);
        floor.Friction = 0;
        player.AddContact(ContactType.SurfaceBottom, floor);
        player.Velocity = new Vector2D(0, 100);

        new PhysicsSystem().Step(world, JumpInput(), 0);

        Assert.AreEqual(100 - GameDefaults.GroundJumpSpeed * GameDefaults.JumpEffortMultiplier, player.Velocity.Y, 1e-8);
    }

    [TestMethod]
    public async Task ClimbJump_UsesCurrentVelocityAndMovingLadderReference()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var ladder = world.Objects.OfType<KinematicObject2D>().First(body => body.IsClimbable);
        ladder.Velocity = new Vector2D(0, -2);
        player.IsTouchingClimbable = true;
        player.IsClimbing = true;
        player.AddContact(ContactType.Climbable, ladder);
        player.Velocity = new Vector2D(3, -12);

        new PhysicsSystem().Step(world, JumpInput(), 0);

        Assert.IsFalse(player.IsClimbing);
        Assert.AreEqual(-2 - GameDefaults.ClimbJumpSpeed, player.Velocity.Y, 1e-8);
        Assert.AreEqual(3, player.Velocity.X, 1e-8);
    }

    [TestMethod]
    public async Task ClimbJump_InsufficientFrictionLimitsImpulse()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var ladder = world.Objects.OfType<KinematicObject2D>().First(body => body.IsClimbable);
        ladder.Velocity = Vector2D.Zero;
        player.Friction = 1;
        ladder.Friction = 0.01;
        player.IsTouchingClimbable = true;
        player.IsClimbing = true;
        player.AddContact(ContactType.Climbable, ladder);
        player.Velocity = Vector2D.Zero;

        new PhysicsSystem().Step(world, JumpInput(), 0);

        var expectedSpeed = Math.Sqrt(player.Friction * ladder.Friction) * PhysicsConstants.GripStrengthPerMass * GameDefaults.GripJumpDurationSeconds;
        Assert.AreEqual(-expectedSpeed, player.Velocity.Y, 1e-8);
    }

    [TestMethod]
    public async Task HangJump_RetainsHorizontalVelocityAndPipeCarry()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var pipe = world.Objects.OfType<KinematicObject2D>().First(body => body.IsHangable);
        pipe.Velocity = new Vector2D(4, -2);
        player.IsTouchingHangable = true;
        player.IsHanging = true;
        player.AddContact(ContactType.Hangable, pipe);
        player.Velocity = new Vector2D(7, -2);

        new PhysicsSystem().Step(world, JumpInput(), 0);

        Assert.IsFalse(player.IsHanging);
        Assert.AreEqual(7, player.Velocity.X, 1e-8);
        Assert.AreEqual(-2 - GameDefaults.HangJumpSpeed, player.Velocity.Y, 1e-8);
    }

    [TestMethod]
    [DataRow(true, 0.0, 0.0)]
    [DataRow(true, 0.25, 6.0)]
    [DataRow(false, 0.25, 6.0)]
    public async Task WallJump_UsesContactSideAndFriction(bool wallOnRight, double friction, double expectedUpSpeed)
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var wall = world.Objects.First(body => body.IsStatic && !body.IsPassable && body.CollisionRects.Count > 0);
        player.Friction = 1;
        wall.Friction = friction;
        var playerRect = player.CollisionRects[0];
        var wallRect = wall.CollisionRects[0];
        player.Position += new Vector2D(
            wallOnRight ? wallRect.Left - playerRect.Right + 0.01 : wallRect.Right - playerRect.Left - 0.01,
            wallRect.Top - playerRect.Bottom + 0.5);
        Assert.IsTrue(player.CollisionRects.Any(a => wall.CollisionRects.Any(a.Overlaps)), "Test setup must overlap the wall.");
        player.AddContact(wallOnRight ? ContactType.SurfaceRight : ContactType.SurfaceLeft, wall);
        player.Velocity = Vector2D.Zero;

        new PhysicsSystem().Step(world, JumpInput(), 0);

        Assert.AreEqual(wallOnRight ? -GameDefaults.WallJumpHorizontalSpeed : GameDefaults.WallJumpHorizontalSpeed, player.Velocity.X, 1e-8);
        Assert.AreEqual(-expectedUpSpeed, player.Velocity.Y, 1e-8);
    }

    [TestMethod]
    public async Task ClimbJump_AlreadyMovingFasterIsNotBraked()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var ladder = world.Objects.OfType<KinematicObject2D>().First(body => body.IsClimbable);
        ladder.Velocity = Vector2D.Zero;
        player.IsTouchingClimbable = true;
        player.IsClimbing = true;
        player.AddContact(ContactType.Climbable, ladder);
        player.Velocity = new Vector2D(0, -20);

        new PhysicsSystem().Step(world, JumpInput(), 0);

        Assert.AreEqual(-20, player.Velocity.Y, 1e-8);
    }

    [TestMethod]
    public async Task HangJump_FrictionlessPipeCannotProvideUpwardGrip()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var pipe = world.Objects.OfType<KinematicObject2D>().First(body => body.IsHangable);
        pipe.Friction = 0;
        pipe.Velocity = Vector2D.Zero;
        player.IsTouchingHangable = true;
        player.IsHanging = true;
        player.AddContact(ContactType.Hangable, pipe);
        player.Velocity = new Vector2D(4, 0);

        new PhysicsSystem().Step(world, JumpInput(), 0);

        Assert.AreEqual(4, player.Velocity.X, 1e-8);
        Assert.AreEqual(0, player.Velocity.Y, 1e-8);
        Assert.IsFalse(player.IsHanging);
    }

    [TestMethod]
    public async Task WallJump_MovableWallReceivesOppositeImpulseOnBothAxes()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Crates");
        var player = world.Player;
        var wall = world.Objects.OfType<DynamicObject2D>().First();
        player.Friction = 1;
        wall.Friction = 1;
        var playerRect = player.CollisionRects[0];
        var wallRect = wall.CollisionRects[0];
        player.Position += new Vector2D(wallRect.Left - playerRect.Right + 0.01, wallRect.Top - playerRect.Bottom + 0.5);
        Assert.IsTrue(player.CollisionRects.Any(a => wall.CollisionRects.Any(a.Overlaps)));
        player.AddContact(ContactType.SurfaceRight, wall);
        player.Velocity = Vector2D.Zero;
        wall.Velocity = Vector2D.Zero;

        new PhysicsSystem().Step(world, JumpInput(), 0);

        Assert.IsLessThan(0, player.Velocity.X);
        Assert.IsLessThan(0, player.Velocity.Y);
        Assert.AreEqual(0, player.Mass * player.Velocity.X + wall.Mass * wall.Velocity.X, 1e-8);
        Assert.AreEqual(0, player.Mass * player.Velocity.Y + wall.Mass * wall.Velocity.Y, 1e-8);
        Assert.AreEqual(-GameDefaults.WallJumpHorizontalSpeed, player.Velocity.X - wall.Velocity.X, 1e-8);
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(1.0)]
    public async Task WallJump_NearMovableWall_UsesFrictionAndConservesMomentum(double friction)
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Crates");
        var player = world.Player;
        var wall = world.Objects.OfType<DynamicObject2D>().First();
        world.Objects.RemoveAll(body => body != player && body != wall);
        player.Friction = 1;
        wall.Friction = friction;
        var playerRect = player.CollisionRects[0];
        var wallRect = wall.CollisionRects[0];
        player.Position += new Vector2D(
            x: wallRect.Left - player.CollisionRects.Max(rect => rect.Right) - 0.1,
            y: wallRect.Top - playerRect.Bottom + 0.5);
        player.Velocity = Vector2D.Zero;
        wall.Velocity = Vector2D.Zero;

        new PhysicsSystem().Step(world, JumpInput(), 0);

        Assert.IsLessThan(0, player.Velocity.X);
        Assert.AreEqual(-GameDefaults.WallJumpHorizontalSpeed, player.Velocity.X - wall.Velocity.X, 1e-8);
        Assert.AreEqual(0, player.Mass * player.Velocity.X + wall.Mass * wall.Velocity.X, 1e-8);
        Assert.AreEqual(0, player.Mass * player.Velocity.Y + wall.Mass * wall.Velocity.Y, 1e-8);
        if (friction == 0)
        {
            Assert.AreEqual(0, player.Velocity.Y, 1e-8);
        }
        else
        {
            Assert.IsLessThan(0, player.Velocity.Y);
        }
    }

    [TestMethod]
    [DataRow(1.0)]
    [DataRow(-1.0)]
    public async Task WallJump_NearMovingWall_UsesRelativeVelocity(double relativeX)
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var wall = world.Objects.OfType<KinematicObject2D>().First(body => body.IsClimbable);
        world.Objects.RemoveAll(body => body != player && body != wall);
        wall.IsPassable = false;
        wall.Velocity = new Vector2D(x: 3, y: 0);
        var playerRect = player.CollisionRects[0];
        var wallRect = wall.CollisionRects[0];
        player.Position += new Vector2D(
            x: wallRect.Left - player.CollisionRects.Max(rect => rect.Right) - 0.1,
            y: wallRect.Top - playerRect.Bottom + 0.5);
        player.Velocity = new Vector2D(x: wall.Velocity.X + relativeX, y: 0);

        new PhysicsSystem().Step(world, JumpInput(), 0);

        Assert.AreEqual(wall.Velocity.X - GameDefaults.WallJumpHorizontalSpeed, player.Velocity.X, 1e-8);
        Assert.IsLessThan(0, player.Velocity.Y);
    }

    [TestMethod]
    [DataRow(-1.0, -12.0, true)]
    [DataRow(-12.0, -12.0, false)]
    [DataRow(-15.0, -15.0, false)]
    public async Task WallJump_SeparatingOverlappingContact_DoesNotBrake(double initialX, double expectedX, bool hasUpwardImpulse)
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var wall = world.Objects.First(body => body.IsStatic && !body.IsPassable && body.CollisionRects.Count > 0);
        world.Objects.RemoveAll(body => body != player && body != wall);
        player.Friction = 1;
        wall.Friction = 1;
        var playerRect = player.CollisionRects[0];
        var wallRect = wall.CollisionRects[0];
        player.Position += new Vector2D(x: wallRect.Left - playerRect.Right + 0.01, y: wallRect.Top - playerRect.Bottom + 0.5);
        Assert.IsTrue(player.CollisionRects.Any(a => wall.CollisionRects.Any(a.Overlaps)));
        player.AddContact(ContactType.SurfaceRight, wall);
        player.Velocity = new Vector2D(x: initialX, y: 0);

        new PhysicsSystem().Step(world, JumpInput(), 0);

        Assert.AreEqual(expectedX, player.Velocity.X, 1e-8);
        if (hasUpwardImpulse)
        {
            Assert.IsLessThan(0, player.Velocity.Y);
        }
        else
        {
            Assert.AreEqual(0, player.Velocity.Y, 1e-8);
        }
    }

    [TestMethod]
    public async Task WallJump_RealContactRequiresFreshPress()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var wall = world.Objects.First(body => body.IsStatic && !body.IsPassable && body.CollisionRects.Count > 0);
        var playerRect = player.CollisionRects[0];
        var wallRect = wall.CollisionRects[0];
        player.Position += new Vector2D(wallRect.Left - playerRect.Right + 0.01, wallRect.Top - playerRect.Bottom + 0.5);
        player.Velocity = new Vector2D(1, 0);
        var collision = new CollisionSystem();
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        Assert.IsTrue(player.GetContactingBodies(ContactType.SurfaceRight).Contains(wall));
        var input = JumpInput();
        var physics = new PhysicsSystem();

        physics.Step(world, input, 0);
        Assert.IsLessThan(0, player.Velocity.X);
        player.Velocity = Vector2D.Zero;
        physics.Step(world, input, 0);
        Assert.AreEqual(0, player.Velocity.X, 1e-8, "Holding Jump must not trigger a second wall push.");
    }

    [TestMethod]
    public async Task ClimbJump_OffMovingLadderRunsThroughPhysicsAndCollision()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var ladder = world.Objects.OfType<KinematicObject2D>().First(body => body.IsClimbable);
        player.Position = new Vector2D(
            x: ladder.Position.X + (ladder.Size.X - player.Size.X) / 2,
            y: ladder.Position.Y + (ladder.Size.Y - player.Size.Y) / 2);
        player.Velocity = Vector2D.Zero;
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        var input = new InputState();
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        Assert.IsTrue(player.IsTouchingClimbable);
        input.KeyDown("ArrowUp");
        physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        Assert.IsTrue(player.IsClimbing);
        var beforeJump = player.Position.Y;
        input.KeyDown("Space");

        physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        Assert.IsFalse(player.IsClimbing);
        Assert.IsLessThan(beforeJump, player.Position.Y);
        Assert.IsLessThan(0, player.Velocity.Y);
    }

    [TestMethod]
    public async Task HangJump_OffMovingPipeRetainsShimmyMomentumDuringRealStep()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var pipe = world.Objects.OfType<KinematicObject2D>().First(body => body.IsHangable);
        var pipeRect = pipe.CollisionRects.OrderBy(rect => rect.Top).First();
        var head = player.CollisionRects.OrderBy(rect => rect.Top).First();
        player.Position = new Vector2D(
            x: (pipeRect.Left + pipeRect.Right) / 2 - ((head.Left + head.Right) / 2 - player.Position.X),
            y: pipeRect.Top + (pipeRect.Bottom - pipeRect.Top) / 2 - (head.Top - player.Position.Y));
        player.Velocity = Vector2D.Zero;
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        var input = new InputState();
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        Assert.IsTrue(player.IsTouchingHangable);
        input.KeyDown("ArrowUp");
        input.KeyDown("ArrowRight");
        for (var i = 0; i < 10; i++)
        {
            physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        }
        Assert.IsTrue(player.IsHanging);
        var horizontalBeforeJump = player.Velocity.X;
        Assert.IsGreaterThan(0, horizontalBeforeJump);
        input.KeyUp("ArrowRight");
        input.KeyDown("Space");

        physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        Assert.IsFalse(player.IsHanging);
        Assert.AreEqual(horizontalBeforeJump, player.Velocity.X, 0.3);
        Assert.IsLessThan(0, player.Velocity.Y);
    }

    [TestMethod]
    public async Task WallJump_FromResolvedContactMovesAwayDuringRealStep()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var wall = world.Objects.First(body => body.IsStatic && !body.IsPassable && body.CollisionRects.Count > 0);
        var playerRect = player.CollisionRects[0];
        var wallRect = wall.CollisionRects[0];
        player.Position += new Vector2D(wallRect.Left - playerRect.Right + 0.01, wallRect.Top - playerRect.Bottom + 0.5);
        player.Velocity = new Vector2D(1, 0);
        var collision = new CollisionSystem();
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        Assert.IsTrue(player.GetContactingBodies(ContactType.SurfaceRight).Contains(wall));
        var startX = player.Position.X;

        new PhysicsSystem().Step(world, JumpInput(), PhysicsConstants.FixedPhysicsStepSeconds);
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        Assert.IsLessThan(startX, player.Position.X);
        Assert.IsLessThan(0, player.Velocity.X);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WallJump_PlaysJumpSoundOnceWithOrWithoutAirborneTransition(bool initiallyGrounded)
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var wall = world.Objects.First(body => body.IsStatic && !body.IsPassable && body.CollisionRects.Count > 0);
        var playerRect = player.CollisionRects[0];
        var wallRect = wall.CollisionRects[0];
        player.Position += new Vector2D(wallRect.Left - playerRect.Right + 0.01, wallRect.Top - playerRect.Bottom + 0.5);
        player.AddContact(ContactType.SurfaceRight, wall);
        player.Velocity = Vector2D.Zero;
        if (initiallyGrounded)
        {
            player.AddContact(ContactType.SurfaceBottom, wall);
        }
        var animation = new AnimationSystem();
        animation.Update(world, 0);
        world.DrainPendingSounds();
        player.RemoveContact(ContactType.SurfaceBottom);

        new PhysicsSystem().Step(world, JumpInput(), 0);
        animation.Update(world, 0);
        animation.Update(world, 0);

        var jumpSound = player.Sprite.PoseSounds["JumpStart"];
        Assert.AreEqual(1, world.DrainPendingSounds().Count(sound => sound == jumpSound));
    }

    [TestMethod]
    public async Task GroundJump_TakesPriorityOverWallContact()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var wall = world.Objects.First(body => body.IsStatic && !body.IsPassable && body.CollisionRects.Count > 0);
        var playerRect = player.CollisionRects[0];
        var wallRect = wall.CollisionRects[0];
        player.Position += new Vector2D(wallRect.Left - playerRect.Right + 0.01, wallRect.Top - playerRect.Bottom + 0.5);
        player.AddContact(ContactType.SurfaceRight, wall);
        player.AddContact(ContactType.SurfaceBottom, wall);
        player.Velocity = Vector2D.Zero;

        new PhysicsSystem().Step(world, JumpInput(), 0);

        Assert.AreEqual(0, player.Velocity.X, 1e-8);
        Assert.AreEqual(-GameDefaults.GroundJumpSpeed, player.Velocity.Y, 1e-8);
    }

    [TestMethod]
    public async Task WallJump_RejectsStaleContact()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var player = world.Player;
        var wall = world.Objects.First(body => body.IsStatic && !body.IsPassable && body.CollisionRects.Count > 0);
        player.Position = new Vector2D(wall.Position.X - 20, wall.Position.Y - 20);
        player.AddContact(ContactType.SurfaceRight, wall);
        player.Velocity = Vector2D.Zero;

        new PhysicsSystem().Step(world, JumpInput(), 0);

        Assert.AreEqual(0, player.Velocity.X, 1e-8);
        Assert.AreEqual(0, player.Velocity.Y, 1e-8);
    }
}
