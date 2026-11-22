using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

/// <summary>
/// Runs a real collision pass against a shipped world, so the whole path from solver contact to
/// gameplay consequence (kill or hit) is covered, not just the contact bookkeeping.
/// </summary>
[TestClass]
public sealed class HazardInteractionTests
{
    private const double Penetration = 0.1;

    private static async Task<(World2D World, DynamicHazard2D Snake)> LoadSnakeAsync()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Enemies");
        var snake = world.Objects.OfType<DynamicHazard2D>().First(hazard => hazard.IsKillable && hazard.Sprite.Name == "Snake");
        snake.Velocity = new Vector2D(0, 0);
        return (world, snake);
    }

    private static void Resolve(World2D world)
    {
        new CollisionSystem().Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        world.ApplyPendingRemovals();
    }

    [TestMethod]
    public async Task HardLandingOnSolidHazard_KillsIt()
    {
        var (world, snake) = await LoadSnakeAsync();
        snake.Health = 1;
        var player = world.Player;
        player.Position = new Vector2D(snake.Position.X, snake.Position.Y - player.Size.Y + Penetration);
        player.Velocity = new Vector2D(0, snake.KillImpactSpeed * 2);
        var health = player.Health;

        Resolve(world);

        Assert.DoesNotContain(snake, world.Objects);
        Assert.AreEqual(health, player.Health);
        Assert.IsTrue(world.Objects.OfType<CorpseObject2D>().Any(), "A defeated snake should leave its body behind.");
    }

    [TestMethod]
    public async Task Corpse_IsPassableToThePlayerAndKeepsFallingUnderGravity()
    {
        var (world, snake) = await LoadSnakeAsync();
        snake.Health = 1;
        var player = world.Player;
        player.Position = new Vector2D(snake.Position.X, snake.Position.Y - player.Size.Y + Penetration);
        player.Velocity = new Vector2D(0, snake.KillImpactSpeed * 2);
        Resolve(world);
        var corpse = world.Objects.OfType<CorpseObject2D>().Single();
        var corpseHeight = corpse.Position.Y;

        player.Position = corpse.Position;
        player.Velocity = new Vector2D(0, 0);
        var health = player.Health;
        Resolve(world);

        Assert.AreEqual(health, player.Health, "A corpse is not a hazard.");
        Assert.IsTrue(corpse.GravityAffected);
        Assert.IsFalse(corpse is IHazardBody);
        Assert.AreEqual(corpseHeight, corpse.Position.Y, 1e-9);
    }

    [TestMethod]
    public async Task CorpseDroppedInMidAir_FallsToTheFloorAndStaysThere()
    {
        var (world, snake) = await LoadSnakeAsync();
        snake.Health = 1;
        var player = world.Player;
        player.Position = new Vector2D(snake.Position.X, snake.Position.Y - player.Size.Y + Penetration);
        player.Velocity = new Vector2D(0, snake.KillImpactSpeed * 2);
        Resolve(world);
        var corpse = world.Objects.OfType<CorpseObject2D>().Single();
        var floorY = corpse.Position.Y;
        corpse.Position = new Vector2D(corpse.Position.X, floorY - 3);
        corpse.Velocity = new Vector2D(0, 0);

        var input = new InputState();
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        physics.Reset(input);
        for (var i = 0; i < 300; i++)
        {
            physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
            world.ApplyPendingRemovals();
        }

        var restingY = corpse.Position.Y;
        Assert.IsGreaterThan(floorY - 3 + 1, restingY, "The corpse should have fallen.");

        for (var i = 0; i < 60; i++)
        {
            physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        }

        Assert.AreEqual(restingY, corpse.Position.Y, 0.05, "The corpse should rest on the floor.");
        Assert.Contains(corpse, world.Objects);
    }

    [TestMethod]
    public async Task FallDamageSpeedLandingOnSolidHazard_KillsWithoutFallDamage()
    {
        var (world, snake) = await LoadSnakeAsync();
        snake.Health = 1;
        var player = world.Player;
        player.Position = new Vector2D(snake.Position.X, snake.Position.Y - player.Size.Y + Penetration);
        player.Velocity = new Vector2D(0, player.FallDamageImpactSpeed * 1.5);
        var health = player.Health;

        Resolve(world);

        Assert.DoesNotContain(snake, world.Objects);
        Assert.AreEqual(health, player.Health);
    }

    [TestMethod]
    public async Task GentleLandingOnSolidHazard_HurtsInsteadOfKilling()
    {
        var (world, snake) = await LoadSnakeAsync();
        var player = world.Player;
        player.Position = new Vector2D(snake.Position.X, snake.Position.Y - player.Size.Y + Penetration);
        player.Velocity = new Vector2D(0, snake.KillImpactSpeed / 2);
        var health = player.Health;

        Resolve(world);

        Assert.Contains(snake, world.Objects);
        Assert.IsLessThan(health, player.Health);
    }

    [TestMethod]
    public async Task HardSideHitOnSolidHazard_HurtsInsteadOfKilling()
    {
        var (world, snake) = await LoadSnakeAsync();
        var player = world.Player;
        var playerRects = player.CollisionRects;
        var snakeRects = snake.CollisionRects;
        var playerRightOffset = playerRects.Max(rect => rect.Right) - player.Position.X;
        var snakePosition = snake.Position;
        var health = player.Health;
        var snakeTop = snakeRects.Min(rect => rect.Top);
        var snakeBottom = snakeRects.Max(rect => rect.Bottom);
        var playerHeight = playerRects.Max(rect => rect.Bottom) - player.Position.Y;

        // The sprites are not solid rectangles, so scan heights until the two silhouettes really
        // meet side-on; each attempt starts from the same state.
        var sideHit = false;
        for (var y = snakeTop - playerHeight + 0.5; y < snakeBottom && !sideHit; y += 0.25)
        {
            snake.Position = snakePosition;
            snake.Velocity = new Vector2D(0, 0);
            player.Health = health;
            player.Recovery.Clear();
            player.Position = new Vector2D(x: snakeRects.Min(rect => rect.Left) - playerRightOffset + Penetration, y: y);
            player.Velocity = new Vector2D(snake.KillImpactSpeed * 4, 0);

            Resolve(world);

            sideHit = player.GetContactImpactSpeed(ContactType.SurfaceRight, snake) > 0
                && !player.GetContactingBodies(ContactType.SurfaceBottom).Contains(snake);
        }

        Assert.IsTrue(sideHit, "No height produced a side-on contact with the snake.");
        Assert.AreEqual(0.0, player.GetContactImpactSpeed(ContactType.SurfaceBottom, snake));
        Assert.Contains(snake, world.Objects);
        Assert.IsLessThan(health, player.Health);
    }

    [TestMethod]
    public async Task SurvivedHazardHit_StartsRecovery()
    {
        var (world, snake) = await LoadSnakeAsync();
        snake.Damage = 0.1;
        var player = world.Player;
        player.Position = new Vector2D(snake.Position.X, snake.Position.Y - player.Size.Y + Penetration);
        player.Velocity = new Vector2D(0, snake.KillImpactSpeed / 2);

        Resolve(world);

        Assert.AreEqual(GameDefaults.DefaultPlayerRecoverySeconds, player.Recovery.RemainingSeconds, 1e-9);
    }

    [TestMethod]
    public async Task HazardHitDuringRecovery_DoesNotHurt()
    {
        var (world, snake) = await LoadSnakeAsync();
        snake.Damage = 0.1;
        var player = world.Player;
        player.Recovery.Start();
        player.Position = new Vector2D(snake.Position.X, snake.Position.Y - player.Size.Y + Penetration);
        player.Velocity = new Vector2D(0, snake.KillImpactSpeed / 2);
        var health = player.Health;

        Resolve(world);

        Assert.AreEqual(health, player.Health);
    }

    [TestMethod]
    public async Task NonPlayerTouchingHazard_IsUnaffected()
    {
        var (world, snake) = await LoadSnakeAsync();
        var spike = new StaticHazard2D();
        var objectCount = world.Objects.Count;
        var health = snake.Health;

        InteractionHandler.OnHazardHit(world, snake, spike);

        Assert.HasCount(objectCount, world.Objects, "A non-player body plays no effect on touching a hazard.");
        Assert.AreEqual(health, snake.Health);
        Assert.Contains(snake, world.Objects);
    }

    [TestMethod]
    public void PhysicsReset_SeedsKeyEdgesFromHeldKeys()
    {
        var input = new InputState();
        input.KeyDown("Space");
        input.KeyDown("ArrowUp");
        var physics = new PhysicsSystem();

        physics.Reset(input);

        Assert.AreEqual((true, false, true), physics.KeyEdgeBaseline);
    }
}
