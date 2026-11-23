using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

/// <summary>
/// Hazard health (stomps and thrown objects), throwable pickup and throwing, run as real collision
/// passes against the shipped Enemies world (snakes and loose rocks).
/// </summary>
[TestClass]
public sealed class ThrowableInteractionTests
{
    private const double Penetration = 0.1;
    private static readonly string[] expectedArray0 = ["icon", "icon"];
    private static readonly string[] expectedArray = ["Stones/Small", "HealthPack"];
    private static readonly string[] expected = ["Rock", "HealthPack"];

    private static async Task<(World2D World, DynamicHazard2D Snake, ThrowableObject2D Rock)> LoadAsync()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Enemies");
        var snake = world.Objects.OfType<DynamicHazard2D>().First(hazard => hazard.IsKillable && hazard.Sprite.Name == "Snake");
        snake.Velocity = new Vector2D(0, 0);
        snake.Health = 1.0;
        var rock = world.Objects.OfType<ThrowableObject2D>().First();
        return (world, snake, rock);
    }

    /// <summary>Runs real physics steps (gravity, movement, collisions), e.g. to let loose objects settle on the floor.</summary>
    private static void Run(World2D world, double seconds)
    {
        var input = new InputState();
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        physics.Reset(input);
        var steps = (int)Math.Round(seconds / PhysicsConstants.FixedPhysicsStepSeconds);
        for (var i = 0; i < steps; i++)
        {
            physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
            world.ApplyPendingRemovals();
        }
    }

    private static void Resolve(World2D world)
    {
        new CollisionSystem().Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        world.ApplyPendingRemovals();
    }

    /// <summary>Throws <paramref name="rock"/> straight down onto the snake's head (its middle column).</summary>
    private static void DropOnto(ThrowableObject2D rock, DynamicHazard2D snake)
    {
        var snakeTop = snake.CollisionRects.Min(rect => rect.Top);
        rock.Launch(
            new Vector2D(x: snake.Position.X + 1, y: snakeTop - rock.Size.Y + Penetration),
            new Vector2D(x: 0, y: rock.HitSpeed * 4));
    }

    [TestMethod]
    public async Task ThrownRockHit_ReducesHazardHealthAndStartsRecovery()
    {
        var (world, snake, rock) = await LoadAsync();
        DropOnto(rock, snake);

        Resolve(world);

        Assert.Contains(snake, world.Objects);
        Assert.AreEqual(1.0 - rock.Damage, snake.Health, 1e-9);
        Assert.AreEqual(GameDefaults.DefaultHazardRecoverySeconds, snake.Recovery.RemainingSeconds, 1e-9);
    }

    [TestMethod]
    public async Task ThrownRockHitDuringRecovery_DoesNotHurt()
    {
        var (world, snake, rock) = await LoadAsync();
        snake.Recovery.Start();
        DropOnto(rock, snake);

        Resolve(world);

        Assert.AreEqual(1.0, snake.Health, 1e-9);
    }

    [TestMethod]
    public async Task ThrownRockHitEmptyingHealth_KillsHazard()
    {
        var (world, snake, rock) = await LoadAsync();
        snake.Health = rock.Damage;
        DropOnto(rock, snake);

        Resolve(world);

        Assert.DoesNotContain(snake, world.Objects);
    }

    [TestMethod]
    public async Task LooseRockTouchingHazard_DoesNotHurt()
    {
        var (world, snake, rock) = await LoadAsync();
        DropOnto(rock, snake);
        rock.Land();

        Resolve(world);

        Assert.AreEqual(1.0, snake.Health, 1e-9);
    }

    [TestMethod]
    public async Task LooseRockOverlappingHazard_IsNotPushedNorPushes()
    {
        var (world, snake, rock) = await LoadAsync();
        rock.Position = new Vector2D(x: snake.Position.X + 1, y: snake.Position.Y + 1);
        rock.Velocity = new Vector2D(0, 0);
        rock.Land();
        var rockPosition = rock.Position;
        var snakePosition = snake.Position;

        Resolve(world);

        Assert.AreEqual(rockPosition, rock.Position);
        Assert.AreEqual(snakePosition, snake.Position);
    }

    [TestMethod]
    public async Task SleepingLooseRockOverlappingHazard_StaysAsleep()
    {
        var (world, snake, rock) = await LoadAsync();
        rock.Position = new Vector2D(x: snake.Position.X + 1, y: snake.Position.Y + 1);
        rock.Land();
        rock.Sleep();

        Resolve(world);

        Assert.IsTrue(rock.IsSleeping);
    }

    [TestMethod]
    public async Task ThrownRockHittingHazard_StillCollidesWithIt()
    {
        var (world, snake, rock) = await LoadAsync();
        DropOnto(rock, snake);
        var speedBefore = rock.Velocity.Y;

        Resolve(world);

        Assert.IsLessThan(speedBefore, rock.Velocity.Y);
    }

    [TestMethod]
    public async Task SingleUseThrowableHit_IsUsedUp()
    {
        var (world, snake, rock) = await LoadAsync();
        rock.Reusable = false;
        DropOnto(rock, snake);

        Resolve(world);

        Assert.DoesNotContain(rock, world.Objects);
        Assert.AreEqual(1.0 - rock.Damage, snake.Health, 1e-9);
    }

    [TestMethod]
    [DataRow(true, 1, DisplayName = "Reusable: picked up")]
    [DataRow(false, 0, DisplayName = "Single-use: used up, not picked up")]
    public async Task ThrownRockComingToRestAtThePlayersFeet(bool reusable, int expectedGearCount)
    {
        var (world, _, rock) = await LoadAsync();
        var player = world.Player;
        var gear = player.Gear.Single(slot => slot.Name == rock.GearName);
        rock.Reusable = reusable;
        // Passable, so once settled on the floor it can lie under the player's left foot instead of being pushed apart.
        rock.IsPassable = true;
        Run(world, 1.0);
        player.Position = new Vector2D(x: rock.Position.X, y: rock.Bottom - player.Size.Y);
        player.Velocity = new Vector2D(x: 0, y: 0);
        rock.Launch(rock.Position, new Vector2D(x: 0, y: GameDefaults.ThrowableRestSpeed / 2));

        Resolve(world);

        Assert.DoesNotContain(rock, world.Objects);
        Assert.AreEqual(expectedGearCount, gear.Count);
    }

    [TestMethod]
    public async Task StompOnHazardWithHealthLeft_HurtsItAndBounces()
    {
        var (world, snake, _) = await LoadAsync();
        snake.Health = 2.0;
        var player = world.Player;
        player.Position = new Vector2D(snake.Position.X, snake.Position.Y - player.Size.Y + Penetration);
        player.Velocity = new Vector2D(0, snake.KillImpactSpeed * 2);
        var health = player.Health;

        Resolve(world);

        Assert.Contains(snake, world.Objects);
        Assert.AreEqual(2.0 - player.StompDamage, snake.Health, 1e-9);
        Assert.IsGreaterThan(0.0, snake.Recovery.RemainingSeconds);
        Assert.IsLessThanOrEqualTo(-GameDefaults.StompBounceSpeed, player.Velocity.Y);
        Assert.AreEqual(health, player.Health);
    }

    [TestMethod]
    public async Task PlayerTouchingLooseRock_PicksItUpIntoGear()
    {
        var (world, _, rock) = await LoadAsync();
        var player = world.Player;
        var gear = player.Gear.Single(slot => slot.Name == rock.GearName);
        player.Position = new Vector2D(rock.Position.X - player.Size.X + Penetration, rock.Bottom - player.Size.Y);
        player.Velocity = new Vector2D(5, 0);

        // The player's silhouette is not a solid rectangle, so walk it in until it meets the rock.
        for (var i = 0; i < 10 && gear.Count == 0; i++)
        {
            Resolve(world);
            player.Position = new Vector2D(player.Position.X + 0.25, player.Position.Y);
        }

        Assert.AreEqual(1, gear.Count);
        Assert.DoesNotContain(rock, world.Objects);
    }

    [TestMethod]
    public async Task TryThrow_LaunchesSelectedGearForward()
    {
        var (world, _, rock) = await LoadAsync();
        var player = world.Player;
        var gear = player.Gear.Single(slot => slot.Name == rock.GearName);
        world.Objects.Remove(rock);
        gear.Add(rock);
        player.SelectedGear = gear;
        player.Position = new Vector2D(10, 2);
        player.Velocity = new Vector2D(0, 0);

        var thrown = world.TryThrow();

        Assert.IsTrue(thrown);
        Assert.AreEqual(0, gear.Count);
        Assert.AreSame(gear, player.SelectedGear);
        Assert.Contains(rock, world.Objects);
        Assert.IsTrue(rock.IsThrown);
        Assert.IsGreaterThanOrEqualTo(player.Right, rock.Left);
        Assert.IsGreaterThan(0.0, rock.Velocity.X);
        Assert.IsLessThan(0.0, rock.Velocity.Y);
    }

    [TestMethod]
    public async Task TryThrow_WithoutGear_DoesNothing()
    {
        var (world, _, _) = await LoadAsync();
        var count = world.Objects.Count;

        Assert.IsFalse(world.TryThrow());
        Assert.HasCount(count, world.Objects);
    }

    [TestMethod]
    public async Task ThrownRockComingToRest_CanBePickedUpAgain()
    {
        var (world, _, rock) = await LoadAsync();
        rock.Launch(rock.Position, new Vector2D(0, 0));
        rock.Sleep();

        Resolve(world);

        Assert.IsFalse(rock.IsThrown);
        Assert.Contains(rock, world.Objects);
    }

    [TestMethod]
    public void GearSlots_AreReadInOrderWithTheirSpriteClips()
    {
        var settings = IniDocument.Parse("[Gear]\nRock = Stones/Small\nHealthPack = HealthPack ; compact icon\n");

        var slots = World2D.ParseGearSlots(settings);

        CollectionAssert.AreEqual(expected, slots.Select(slot => slot.Name).ToArray());
        CollectionAssert.AreEqual(expectedArray, slots.Select(slot => slot.IconAssetName).ToArray());
        CollectionAssert.AreEqual(expectedArray0, slots.Select(slot => slot.IconClipName).ToArray());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("HealthPack:monochrome")]
    public void GearSlots_RejectInvalidAssetNames(string assetName)
    {
        var settings = IniDocument.Parse($"[Gear]\nHealthPack = {assetName}\n");

        Assert.ThrowsExactly<FormatException>(() => World2D.ParseGearSlots(settings));
    }

    [TestMethod]
    public async Task TryThrow_TakesFromTheChosenGearNotTheFirst()
    {
        var (world, _, rock) = await LoadAsync();
        var player = world.Player;
        var first = player.Gear.Single(slot => slot.Name == rock.GearName);
        first.Add(new ThrowableObject2D { GearName = rock.GearName });
        var chosen = new Gear2D("Tomato", 'o');
        player.Gear.Add(chosen);
        world.Objects.Remove(rock);
        chosen.Add(rock);
        player.SelectedGear = chosen;
        player.Position = new Vector2D(10, 2);
        player.Velocity = new Vector2D(0, 0);

        Assert.IsTrue(world.TryThrow());
        Assert.AreEqual(0, chosen.Count);
        Assert.AreEqual(1, first.Count);
        Assert.Contains(rock, world.Objects);
    }

    [TestMethod]
    public void SelectedGear_IsTheFirstSlotUntilOneIsChosen()
    {
        var player = new Player2D();
        Assert.IsNull(player.SelectedGear);

        var rock = new Gear2D("Rock", '●');
        var tomato = new Gear2D("Tomato", 'o');
        player.Gear.AddRange([rock, tomato]);
        Assert.AreSame(rock, player.SelectedGear);

        player.SelectedGear = tomato;
        tomato.Add(new ThrowableObject2D { GearName = "Tomato" });
        Assert.IsTrue(tomato.TryTake(out _));
        Assert.AreSame(tomato, player.SelectedGear);
    }

    [TestMethod]
    public void SelectedGear_RejectsASlotThePlayerDoesNotHave()
    {
        var player = new Player2D();

        Assert.ThrowsExactly<ArgumentException>(() => player.SelectedGear = new Gear2D("Rock", '●'));
    }

    [TestMethod]
    public void CarryOver_KeepsGearContents()
    {
        var item = new ThrowableObject2D { GearName = "Rock" };
        var previous = new Player2D();
        previous.Gear.Add(new Gear2D("Rock", '●'));
        previous.Gear[0].Add(item);
        var next = new Player2D();
        next.Gear.Add(new Gear2D("Rock", '●'));

        next.CarryOverFrom(previous);

        Assert.AreEqual(1, next.Gear[0].Count);
        Assert.IsTrue(next.Gear[0].TryTake(out var carried));
        Assert.AreSame(item, carried);
    }

    [TestMethod]
    public void CarryOver_KeepsTheChosenGear()
    {
        var previous = new Player2D();
        previous.Gear.AddRange([new Gear2D("Rock", '●'), new Gear2D("Tomato", 'o')]);
        previous.SelectedGear = previous.Gear[1];
        var next = new Player2D();
        next.Gear.AddRange([new Gear2D("Rock", '●'), new Gear2D("Tomato", 'o')]);

        next.CarryOverFrom(previous);

        Assert.AreSame(next.Gear[1], next.SelectedGear);
    }
}
