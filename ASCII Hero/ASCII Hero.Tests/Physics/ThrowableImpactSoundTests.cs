using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

[TestClass]
public sealed class ThrowableImpactSoundTests
{
    private static async Task<(World2D World, ThrowableObject2D Rock)> CreateWorldAsync()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Enemies");
        var rock = world.Objects.OfType<ThrowableObject2D>().First();
        world.Objects.Clear();
        world.Objects.Add(rock);
        rock.Restitution = 1;
        world.DrainPendingSounds();
        return (world, rock);
    }

    private static StaticObject2D AddSurface(World2D world, ThrowableObject2D rock, bool passable = false)
    {
        var surface = new StaticObject2D { IsPassable = passable, Restitution = 1 };
        surface.Spawn(rock.Sprite, rock.Clip.Name, 0, new Vector2D(20, 12));
        world.Objects.Add(surface);
        return surface;
    }

    private static void Drop(ThrowableObject2D rock, double speed) =>
        rock.Launch(new Vector2D(20, 11.1), new Vector2D(0, speed));

    private static readonly string[] expected = ["ThrowableHit"];

    [TestMethod]
    [DataRow(0.5, false)]
    [DataRow(1.0, true)]
    [DataRow(10.0, true)]
    public async Task SurfaceImpact_UsesClosingSpeedThreshold(double speed, bool audible)
    {
        var (world, rock) = await CreateWorldAsync();
        AddSurface(world, rock);
        Drop(rock, speed);

        new CollisionSystem().Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        CollectionAssert.AreEqual(audible ? expected : [], world.DrainPendingSounds().ToArray());
    }

    [TestMethod]
    public async Task PassableNonHazardOverlap_IsSilent()
    {
        var (world, rock) = await CreateWorldAsync();
        AddSurface(world, rock, passable: true);
        Drop(rock, 10);

        new CollisionSystem().Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        Assert.IsEmpty(world.DrainPendingSounds());
    }

    [TestMethod]
    [DataRow(false, "ThrowableHit", "HazardHurt")]
    [DataRow(true, "HazardHurt", null)]
    public async Task HazardImpact_OnlyPhysicalContactPlaysHitSound(bool passable, string firstSound, string? secondSound)
    {
        var (world, rock) = await CreateWorldAsync();
        var hazard = new StaticHazard2D
        {
            IsPassable = passable,
            IsKillable = true,
            Health = 2,
            HurtSoundName = "HazardHurt",
            Restitution = 1,
        };
        hazard.Spawn(rock.Sprite, rock.Clip.Name, 0, new Vector2D(20, 12));
        world.Objects.Add(hazard);
        Drop(rock, 10);

        new CollisionSystem().Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        Assert.AreEqual(1.5, hazard.Health, 1e-9);
        var expected = secondSound is null ? new[] { firstSound } : [firstSound, secondSound];
        CollectionAssert.AreEqual(expected, world.DrainPendingSounds().ToArray());
    }

    [TestMethod]
    [DataRow(2.0, false, 2.0, "ThrowableHit")]
    [DataRow(10.0, true, 2.0, "ThrowableHit")]
    [DataRow(10.0, false, 0.5, "ThrowableHit,HazardKill")]
    public async Task SolidHazardImpact_IsIndependentOfDamage(double speed, bool recovering, double health, string expected)
    {
        var (world, rock) = await CreateWorldAsync();
        var hazard = new StaticHazard2D
        {
            IsKillable = true,
            Health = health,
            HurtSoundName = "HazardHurt",
            KillSoundName = "HazardKill",
            Restitution = 1,
        };
        hazard.Spawn(rock.Sprite, rock.Clip.Name, 0, new Vector2D(20, 12));
        if (recovering)
        {
            hazard.Recovery.Start();
        }
        world.Objects.Add(hazard);
        Drop(rock, speed);

        new CollisionSystem().Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        CollectionAssert.AreEqual(expected.Split(','), world.DrainPendingSounds().ToArray());
        Assert.AreEqual(health == 0.5 ? 0 : health, hazard.Health, 1e-9);
    }

    private static readonly string[] expectedArray = ["ThrowableHit"];
    private static readonly string[] expectedArray0 = ["ThrowableHit"];

    [TestMethod]
    public async Task LooseThrowable_ImpactAndSubsequentRest()
    {
        var (world, rock) = await CreateWorldAsync();
        AddSurface(world, rock);
        Drop(rock, 10);
        rock.Land();
        var collision = new CollisionSystem();

        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        CollectionAssert.AreEqual(expectedArray, world.DrainPendingSounds().ToArray());

        rock.Position = new Vector2D(20, 11.1);
        rock.Velocity = new Vector2D(0, 0.5);
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        Assert.IsEmpty(world.DrainPendingSounds());

        rock.Position = new Vector2D(20, 11.1);
        rock.Velocity = new Vector2D(0, 10);
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        CollectionAssert.AreEqual(expectedArray0, world.DrainPendingSounds().ToArray());
    }

    private static readonly string[] expectedArray1 = ["ThrowableHit"];

    [TestMethod]
    public async Task MovingObjectImpact_PlaysSound()
    {
        var (world, rock) = await CreateWorldAsync();
        var target = new DynamicObject2D { Restitution = 1 };
        target.Spawn(rock.Sprite, rock.Clip.Name, 0, new Vector2D(20, 12), Vector2D.Zero, gravityAffected: false);
        world.Objects.Add(target);
        Drop(rock, 10);

        new CollisionSystem().Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        CollectionAssert.AreEqual(expectedArray1, world.DrainPendingSounds().ToArray());
    }

    private static readonly string[] expectedArray2 = ["ThrowableHit"];

    [TestMethod]
    public async Task WorldEdgeImpact_PlaysSound()
    {
        var (world, rock) = await CreateWorldAsync();
        rock.Launch(new Vector2D(-0.1, 10), new Vector2D(-10, 0));

        new CollisionSystem().Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        CollectionAssert.AreEqual(expectedArray2, world.DrainPendingSounds().ToArray());
    }

    private static readonly string[] expectedArray3 = ["ThrowableHit"];

    [TestMethod]
    public async Task SingleUseThrowableLanding_PlaysImpactBeforeRemoval()
    {
        var (world, rock) = await CreateWorldAsync();
        var surface = AddSurface(world, rock);
        surface.Restitution = 0;
        rock.Restitution = 0;
        rock.Reusable = false;
        Drop(rock, 10);

        new CollisionSystem().Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        CollectionAssert.AreEqual(expectedArray3, world.DrainPendingSounds().ToArray());
        Assert.IsTrue(world.IsQueuedForRemoval(rock));
    }
}
