using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

[TestClass]
public sealed class RopesWallJumpBaselineTests
{
    private const double StepSeconds = PhysicsConstants.FixedPhysicsStepSeconds;

    private static async Task<(World2D World, Body2D Wall)> LoadWallAsync()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "RopesAndVines");
        var wall = world.Objects.Single(body => body.IsStatic && !body.IsPassable && body.Size.X == 1 && body.Size.Y == 28);
        return (world, wall);
    }

    private static void PlaceRightOfWall(Player2D player, Body2D wall, double gap)
    {
        var left = player.CollisionRects.Min(rect => rect.Left);
        player.Position += new Vector2D(
            x: wall.Right + gap - left,
            y: wall.Top + 5 - player.Top);
    }

    private static void Step(World2D world, PhysicsSystem physics, CollisionSystem collision, InputState input)
    {
        physics.Step(world, input, StepSeconds);
        collision.Resolve(world, StepSeconds);
        world.ApplyPendingRemovals();
    }

    [TestMethod]
    public async Task ResolvedContactOnRightOfTallWall_FreshJumpPushesRightAndUp()
    {
        var (world, wall) = await LoadWallAsync();
        var player = world.Player;
        PlaceRightOfWall(player, wall, gap: -0.01);
        player.Velocity = new Vector2D(x: -1, y: 0);
        var collision = new CollisionSystem();
        collision.Resolve(world, StepSeconds);
        Assert.IsTrue(player.GetContactingBodies(ContactType.SurfaceLeft).Contains(wall));
        Assert.IsFalse(player.IsGrounded);
        player.Velocity = Vector2D.Zero;
        var startX = player.Position.X;
        var input = new InputState();
        input.KeyDown("Space");

        new PhysicsSystem().Step(world, input, StepSeconds);
        Assert.IsGreaterThan(5, player.Velocity.X);
        Assert.IsLessThan(0, player.Velocity.Y);
        collision.Resolve(world, StepSeconds);

        Assert.IsGreaterThan(startX, player.Position.X);
        Assert.IsGreaterThan(5, player.Velocity.X);
        Assert.IsLessThan(0, player.Velocity.Y, "The upward push should survive collision resolution.");
    }

    [TestMethod]
    public async Task WallImpactRebound_AllowsFreshJumpDespiteSeparation()
    {
        var (world, wall) = await LoadWallAsync();
        var player = world.Player;
        PlaceRightOfWall(player, wall, gap: -0.01);
        player.Velocity = new Vector2D(x: -1, y: 0);
        var collision = new CollisionSystem();
        collision.Resolve(world, StepSeconds);
        Assert.IsTrue(player.GetContactingBodies(ContactType.SurfaceLeft).Contains(wall));
        Assert.IsGreaterThan(0, player.Velocity.X, "The real wall collision rebounds the player away.");
        var input = new InputState();
        input.KeyDown("Space");

        new PhysicsSystem().Step(world, input, StepSeconds);

        Assert.IsGreaterThan(5, player.Velocity.X, "Rebounding must not block a wall push.");
        Assert.IsLessThan(0, player.Velocity.Y);
    }

    [TestMethod]
    [DataRow(0.001, true)]
    [DataRow(0.1, true)]
    [DataRow(0.2, true)]
    [DataRow(0.49999, true)]
    [DataRow(0.5, true)]
    [DataRow(0.50001, false)]
    public async Task GapWithoutContact_UsesConfiguredReach(double gap, bool canJump)
    {
        var (world, wall) = await LoadWallAsync();
        var player = world.Player;
        PlaceRightOfWall(player, wall, gap);
        player.Velocity = Vector2D.Zero;
        var collision = new CollisionSystem();
        collision.Resolve(world, StepSeconds);
        Assert.IsFalse(player.HasContact(ContactType.SurfaceLeft));
        Assert.IsFalse(player.IsGrounded);
        var input = new InputState();
        input.KeyDown("Space");

        Step(world, new PhysicsSystem(), collision, input);

        if (canJump)
        {
            Assert.IsGreaterThan(5, player.Velocity.X);
            Assert.IsLessThan(0, player.Velocity.Y);
        }
        else
        {
            Assert.AreEqual(0, player.Velocity.X, 1e-8);
            Assert.IsGreaterThan(0, player.Velocity.Y);
        }
    }

    [TestMethod]
    [DataRow(-2.0)]
    [DataRow(0.0)]
    [DataRow(2.0)]
    public async Task SmallGap_MovingTowardOrAway_AllowsJump(double initialVelocityX)
    {
        var (world, wall) = await LoadWallAsync();
        PlaceRightOfWall(world.Player, wall, gap: 0.1);
        world.Player.Velocity = new Vector2D(x: initialVelocityX, y: 0);
        var collision = new CollisionSystem();
        collision.Resolve(world, StepSeconds);
        Assert.IsFalse(world.Player.HasContact(ContactType.SurfaceLeft));
        var input = new InputState();
        input.KeyDown("Space");

        Step(world, new PhysicsSystem(), collision, input);

        Assert.IsGreaterThan(5, world.Player.Velocity.X);
        Assert.IsLessThan(0, world.Player.Velocity.Y);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Reach_WorksOnBothWallSides(bool wallOnRight)
    {
        var (world, wall) = await LoadWallAsync();
        world.Objects.RemoveAll(body => body != world.Player && body != wall);
        var player = world.Player;
        PlaceRightOfWall(player, wall, gap: 0.1);
        if (wallOnRight)
        {
            var right = player.CollisionRects.Max(rect => rect.Right);
            player.Position += new Vector2D(x: wall.Left - 0.1 - right, y: 0);
        }
        player.Velocity = Vector2D.Zero;
        var input = new InputState();
        input.KeyDown("Space");

        new PhysicsSystem().Step(world, input, 0);

        Assert.AreEqual(wallOnRight ? -GameDefaults.WallJumpHorizontalSpeed : GameDefaults.WallJumpHorizontalSpeed, player.Velocity.X, 1e-8);
        Assert.IsLessThan(0, player.Velocity.Y);
    }

    [TestMethod]
    public async Task Reach_ChoosesNearestSideInsteadOfFirstWorldObject()
    {
        var (world, wall) = await LoadWallAsync();
        var other = world.Objects.Single(body => body.IsStatic && !body.IsPassable && body.Size.X == 100 && body.Size.Y == 1);
        var player = world.Player;
        world.Objects.RemoveAll(body => body != player && body != wall && body != other);
        PlaceRightOfWall(player, wall, gap: 0.1);
        var otherRect = other.CollisionRects[0];
        var playerRect = player.CollisionRects[0];
        other.Position += new Vector2D(
            x: player.CollisionRects.Max(rect => rect.Right) + 0.2 - otherRect.Left,
            y: playerRect.Top + 0.1 - otherRect.Top);
        world.Objects.Remove(other);
        world.Objects.Insert(0, other);
        player.Velocity = Vector2D.Zero;
        var input = new InputState();
        input.KeyDown("Space");

        new PhysicsSystem().Step(world, input, 0);

        Assert.AreEqual(GameDefaults.WallJumpHorizontalSpeed, player.Velocity.X, 1e-8);
        Assert.IsLessThan(0, player.Velocity.Y);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Reach_DoesNotUsePassableWallOrCorner(bool passable)
    {
        var (world, wall) = await LoadWallAsync();
        world.Objects.RemoveAll(body => body != world.Player && body != wall);
        var player = world.Player;
        PlaceRightOfWall(player, wall, gap: 0.1);
        wall.IsPassable = passable;
        if (!passable)
        {
            var bottom = player.CollisionRects.Max(rect => rect.Bottom);
            player.Position += new Vector2D(x: 0, y: wall.Top - bottom);
        }
        player.Velocity = Vector2D.Zero;
        var input = new InputState();
        input.KeyDown("Space");

        new PhysicsSystem().Step(world, input, 0);

        Assert.AreEqual(0, player.Velocity.X, 1e-8);
        Assert.AreEqual(0, player.Velocity.Y, 1e-8);
    }

    [TestMethod]
    public async Task JumpPressedBeforeWallContact_IsNotBufferedWhenContactIsReached()
    {
        var (world, wall) = await LoadWallAsync();
        var (controlWorld, controlWall) = await LoadWallAsync();
        var player = world.Player;
        PlaceRightOfWall(player, wall, gap: GameDefaults.WallJumpReachCells + 0.15);
        PlaceRightOfWall(controlWorld.Player, controlWall, gap: GameDefaults.WallJumpReachCells + 0.15);
        player.Velocity = new Vector2D(x: -2, y: 0);
        controlWorld.Player.Velocity = player.Velocity;
        var collision = new CollisionSystem();
        var controlCollision = new CollisionSystem();
        collision.Resolve(world, StepSeconds);
        controlCollision.Resolve(controlWorld, StepSeconds);
        Assert.IsFalse(player.HasContact(ContactType.SurfaceLeft));
        var input = new InputState();
        input.KeyDown("ArrowLeft");
        input.KeyDown("Space");
        var controlInput = new InputState();
        controlInput.KeyDown("ArrowLeft");
        var physics = new PhysicsSystem();
        var controlPhysics = new PhysicsSystem();

        for (var i = 0; i < 15; i++)
        {
            Step(world, physics, collision, input);
            Step(controlWorld, controlPhysics, controlCollision, controlInput);
            Assert.AreEqual(controlWorld.Player.Velocity.X, player.Velocity.X, 1e-8);
            Assert.AreEqual(controlWorld.Player.Velocity.Y, player.Velocity.Y, 1e-8);
            Assert.AreEqual(controlWorld.Player.Position.X, player.Position.X, 1e-8);
            Assert.AreEqual(controlWorld.Player.Position.Y, player.Position.Y, 1e-8);
            if (player.GetContactingBodies(ContactType.SurfaceLeft).Contains(wall))
            {
                return;
            }
        }

        Assert.Fail("The player must reach the wall while Jump is held.");
    }

    [TestMethod]
    public async Task WalkIntoWall_ReleaseGroundJumpAndPressAgain_PushesOff()
    {
        var (world, wall) = await LoadWallAsync();
        var player = world.Player;
        var floor = world.Objects.Single(body => body.IsStatic && !body.IsPassable && body.Size.X == 100 && body.Size.Y == 1);
        var leftOffset = player.CollisionRects.Min(rect => rect.Left) - player.Position.X;
        var bottomOffset = player.CollisionRects.Max(rect => rect.Bottom) - player.Position.Y;
        player.Position = new Vector2D(x: wall.Right - leftOffset + 0.4, y: floor.Top - bottomOffset - 0.01);
        player.Velocity = Vector2D.Zero;
        var input = new InputState();
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        for (var i = 0; i < 10 && !player.IsGrounded; i++)
        {
            Step(world, physics, collision, input);
        }
        Assert.IsTrue(player.IsGrounded);
        input.KeyDown("ArrowLeft");
        for (var i = 0; i < 60 && !player.GetContactingBodies(ContactType.SurfaceLeft).Contains(wall); i++)
        {
            Step(world, physics, collision, input);
        }
        Assert.IsTrue(player.GetContactingBodies(ContactType.SurfaceLeft).Contains(wall), "The player must walk into the wall.");
        input.KeyUp("ArrowLeft");
        Step(world, physics, collision, input);
        input.KeyDown("Space");
        Step(world, physics, collision, input);
        Assert.IsFalse(player.IsGrounded);
        input.KeyUp("Space");
        Step(world, physics, collision, input);
        Assert.IsLessThan(-GameDefaults.WallJumpVerticalSpeed, player.Velocity.Y);
        input.KeyDown("Space");

        Step(world, physics, collision, input);

        Assert.IsGreaterThan(5, player.Velocity.X);
        Assert.IsLessThan(0, player.Velocity.Y);
    }

    [TestMethod]
    public async Task GroundJumpNextToTallWall_ReboundDoesNotBlockFreshWallJump()
    {
        var (world, wall) = await LoadWallAsync();
        var player = world.Player;
        var floor = world.Objects.Single(body => body.IsStatic && !body.IsPassable && body.Size.X == 100 && body.Size.Y == 1);
        var leftOffset = player.CollisionRects.Min(rect => rect.Left) - player.Position.X;
        var bottomOffset = player.CollisionRects.Max(rect => rect.Bottom) - player.Position.Y;
        player.Position = new Vector2D(x: wall.Right - leftOffset + 0.01, y: floor.Top - bottomOffset - 0.01);
        player.Velocity = Vector2D.Zero;
        var input = new InputState();
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        for (var i = 0; i < 10 && !player.IsGrounded; i++)
        {
            Step(world, physics, collision, input);
        }
        Assert.IsTrue(player.IsGrounded, "The player must begin on the floor.");
        input.KeyDown("Space");
        Step(world, physics, collision, input);
        Assert.IsFalse(player.IsGrounded);
        Assert.IsLessThan(0, player.Velocity.Y);
        input.KeyUp("Space");
        input.KeyDown("ArrowLeft");
        for (var i = 0; i < 20 && !player.GetContactingBodies(ContactType.SurfaceLeft).Contains(wall); i++)
        {
            Step(world, physics, collision, input);
        }
        Assert.IsTrue(player.GetContactingBodies(ContactType.SurfaceLeft).Contains(wall), "The airborne player must reach the wall.");
        Assert.IsGreaterThan(0, player.Velocity.X, "The impact has already made the player rebound away from the wall.");
        Assert.IsLessThan(-GameDefaults.WallJumpVerticalSpeed, player.Velocity.Y);
        input.KeyUp("ArrowLeft");
        input.KeyDown("Space");

        Step(world, physics, collision, input);

        Assert.IsGreaterThan(5, player.Velocity.X, "Rebound must not block a fresh wall jump.");
        Assert.IsLessThan(0, player.Velocity.Y, "The player continues to rise.");
    }
}
