using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

[TestClass]
public sealed class PlacementSoundTests
{
    [TestMethod]
    [DataRow(0.5, "PlayerHurt")]
    [DataRow(1.0, "PlayerDeath")]
    public async Task HazardDamage_PlaysDamagedPlayersSound(double damage, string expected)
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Enemies");
        var hazard = world.Objects.OfType<DynamicHazard2D>().First(body => body.IsKillable);
        var player = world.Player;
        player.HurtSoundName = "PlayerHurt";
        player.KillSoundName = "PlayerDeath";
        hazard.Damage = damage;
        world.DrainPendingSounds();

        InteractionHandler.OnHazardHit(world, player, hazard);

        CollectionAssert.AreEqual(new[] { expected }, world.DrainPendingSounds().ToArray());
        if (damage == 1.0)
        {
            InteractionHandler.OnHazardHit(world, player, hazard);
            Assert.IsEmpty(world.DrainPendingSounds());
        }
    }

    [TestMethod]
    public async Task HazardDamage_WithNoPlayerSounds_IsSilent()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Enemies");
        var hazard = world.Objects.OfType<DynamicHazard2D>().First(body => body.IsKillable);
        world.Player.HurtSoundName = null;
        world.Player.KillSoundName = null;
        hazard.Damage = 0.1;
        world.DrainPendingSounds();

        InteractionHandler.OnHazardHit(world, world.Player, hazard);

        Assert.IsEmpty(world.DrainPendingSounds());
    }

    private static readonly string[] expected = ["HazardHurt"];
    private static readonly string[] expectedArray = ["HazardKill"];

    [TestMethod]
    public async Task HazardDamage_PlaysHazardsOwnHurtAndKillSounds()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Enemies");
        var hazard = world.Objects.OfType<DynamicHazard2D>().First(body => body.IsKillable);
        hazard.Health = 2;
        world.DrainPendingSounds();

        Assert.IsFalse(InteractionHandler.DamageHazard(world, hazard, hazard, 1));
        CollectionAssert.AreEqual(expected, world.DrainPendingSounds().ToArray());
        hazard.Recovery.Clear();
        Assert.IsTrue(InteractionHandler.DamageHazard(world, hazard, hazard, 1));
        CollectionAssert.AreEqual(expectedArray, world.DrainPendingSounds().ToArray());
    }

    private static readonly string[] expectedArray0 = ["ThrowablePickup"];
    private static readonly string[] expectedArray1 = ["ThrowableUse"];
    private static readonly string[] expectedArray2 = ["HazardHurt"];

    [TestMethod]
    public async Task Throwable_UsesConfiguredCollectUseAndHitSounds()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Enemies");
        var rock = world.Objects.OfType<ThrowableObject2D>().First();
        var hazard = world.Objects.OfType<DynamicHazard2D>().First(body => body.IsKillable);
        var player = world.Player;
        player.SelectedGear = player.FindGear(rock.GearName);
        player.Position = new Vector2D(10, 2);
        player.Velocity = new Vector2D(0, 0);
        world.DrainPendingSounds();

        InteractionHandler.OnThrowableTouched(world, player, rock);
        world.ApplyPendingRemovals();
        CollectionAssert.AreEqual(expectedArray0, world.DrainPendingSounds().ToArray());

        Assert.IsTrue(world.TryThrow());
        CollectionAssert.AreEqual(expectedArray1, world.DrainPendingSounds().ToArray());
        Assert.IsTrue(world.Sounds.Definitions.Any(sound => sound.Name == "ThrowableUse"));

        hazard.Health = 2;
        InteractionHandler.OnHazardStruck(world, rock, hazard, hazard);
        CollectionAssert.AreEqual(expectedArray2, world.DrainPendingSounds().ToArray());
    }

    [TestMethod]
    public async Task Throwable_WithNoUseSound_ThrowsSilently()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Enemies");
        var rock = world.Objects.OfType<ThrowableObject2D>().First();
        rock.UseSoundName = null;
        world.Objects.Remove(rock);
        var gear = world.Player.FindGear(rock.GearName)!;
        gear.Add(rock);
        world.Player.SelectedGear = gear;
        world.Player.Position = new Vector2D(10, 2);
        world.DrainPendingSounds();

        Assert.IsTrue(world.TryThrow());
        Assert.IsEmpty(world.DrainPendingSounds());
    }

    private static readonly string[] expectedArray3 = ["Switch", "Device"];

    [TestMethod]
    public async Task Switch_AlsoPlaysLinkedDevicesOperateSound()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "FansAndFlows");
        var control = world.Objects.OfType<Switch2D>()
            .First(candidate => candidate.Targets.OfType<Device2D>().Any());
        var device = control.Targets.OfType<Device2D>().First();
        control.OperateSoundName = "Switch";
        device.OperateSoundName = "Device";
        world.Player.Position = control.Position;
        world.DrainPendingSounds();

        Assert.IsTrue(world.TryOperateSwitch());

        CollectionAssert.AreEqual(expectedArray3, world.DrainPendingSounds().ToArray());
    }

    private static readonly string[] expectedArray4 = ["ButtonPress", "Elevator"];

    [TestMethod]
    public async Task Switch_AlsoPlaysLinkedKinematicObjectsOperateSound()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "MovingClimbables");
        var control = world.Objects.OfType<Switch2D>().Single();
        var target = control.Targets.OfType<KinematicObject2D>().Single();
        target.OperateSoundName = "Elevator";
        world.Player.Position = control.Position;
        world.DrainPendingSounds();

        Assert.IsTrue(world.TryOperateSwitch());
        CollectionAssert.AreEqual(expectedArray4, world.DrainPendingSounds().ToArray());
    }
}
