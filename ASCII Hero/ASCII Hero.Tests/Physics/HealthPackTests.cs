using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

/// <summary>Health packs are stored on pickup and consumed whole only when explicitly used while hurt.</summary>
[TestClass]
public sealed class HealthPackTests
{
    private static async Task<(World2D World, Collectable2D Pack, Gear2D Gear)> LoadWithHealthPackAsync()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "TheMountains");
        var pack = world.Objects.OfType<Collectable2D>().Single(item => item.Type == CollectableType.HealthPack);
        world.DrainPendingSounds();
        var gear = world.Player.Gear.Single(slot => slot.Name == nameof(CollectableType.HealthPack));
        return (world, pack, gear);
    }

    [TestMethod]
    public async Task Pickup_StoresPackWithoutUsingItEvenAtFullHealth()
    {
        var (world, pack, gear) = await LoadWithHealthPackAsync();

        InteractionHandler.OnCollected(world, world.Player, pack);
        world.ApplyPendingRemovals();

        Assert.AreEqual("HealthUse", pack.UseSoundName);
        Assert.AreEqual(GameDefaults.PlayerFullHealth, world.Player.Health);
        Assert.AreEqual(1, gear.Count);
        Assert.IsTrue(gear.TryPeek(out var storedPack));
        Assert.AreSame(pack, storedPack);
        Assert.DoesNotContain(pack, world.Objects);
        Assert.Contains("HealthPickup", world.DrainPendingSounds());
    }

    [TestMethod]
    public async Task UseAtFullHealth_DoesNotConsumePack()
    {
        var (world, pack, gear) = await LoadWithHealthPackAsync();
        gear.Add(pack);
        world.Player.SelectedGear = gear;

        Assert.IsFalse(InteractionHandler.TryUseHealthPack(world));

        Assert.AreEqual(1, gear.Count);
        Assert.AreEqual(GameDefaults.PlayerFullHealth, world.Player.Health);
    }

    private static readonly string[] ExpectedUseSounds = ["HealthUse"];

    [TestMethod]
    public async Task EachUse_ConsumesOneWholePackAndHealsFully()
    {
        var (world, pack, gear) = await LoadWithHealthPackAsync();
        gear.Add(pack);
        gear.Add(new Collectable2D { Type = CollectableType.HealthPack, UseSoundName = "HealthUse" });
        gear.Add(new Collectable2D { Type = CollectableType.HealthPack, UseSoundName = "HealthUse" });
        world.Player.SelectedGear = gear;
        world.Player.Health = 0.9;

        Assert.IsTrue(InteractionHandler.TryUseHealthPack(world));

        Assert.AreEqual(GameDefaults.PlayerFullHealth, world.Player.Health);
        Assert.AreEqual(2, gear.Count);
        CollectionAssert.AreEqual(ExpectedUseSounds, world.DrainPendingSounds().ToArray());
        Assert.IsFalse(InteractionHandler.TryUseHealthPack(world));
        Assert.AreEqual(2, gear.Count);
        Assert.IsEmpty(world.DrainPendingSounds());

        world.Player.Health = 0.2;
        Assert.IsTrue(InteractionHandler.TryUseHealthPack(world));

        Assert.AreEqual(GameDefaults.PlayerFullHealth, world.Player.Health);
        Assert.AreEqual(1, gear.Count);
        CollectionAssert.AreEqual(ExpectedUseSounds, world.DrainPendingSounds().ToArray());
    }
}
