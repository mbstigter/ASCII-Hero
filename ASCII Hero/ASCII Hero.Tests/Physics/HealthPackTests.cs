using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

/// <summary>
/// A health pack restores the player's health fully, but is left in place while health is already full.
/// </summary>
[TestClass]
public sealed class HealthPackTests
{
    private static async Task<(World2D World, Collectable2D Pack)> LoadWithHealthPackAsync()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "TheMountains");
        var pack = new Collectable2D { SoundName = "HealthPickup" };
        pack.Spawn(world.Player.Sprite, "walk_idle", 0, world.Player.Position, CollectableType.HealthPack);
        world.Objects.Add(pack);
        world.DrainPendingSounds();
        return (world, pack);
    }

    [TestMethod]
    public async Task HurtPlayer_IsHealedFullyAndPackRemoved()
    {
        var (world, pack) = await LoadWithHealthPackAsync();
        world.Player.Health = 0.3;

        InteractionHandler.OnCollected(world, world.Player, pack);
        world.ApplyPendingRemovals();

        Assert.AreEqual(GameDefaults.PlayerFullHealth, world.Player.Health);
        Assert.DoesNotContain(pack, world.Objects);
        Assert.Contains("HealthPickup", world.DrainPendingSounds());
    }

    [TestMethod]
    public async Task FullHealthPlayer_LeavesPackInPlace()
    {
        var (world, pack) = await LoadWithHealthPackAsync();
        world.Player.Health = GameDefaults.PlayerFullHealth;

        InteractionHandler.OnCollected(world, world.Player, pack);
        world.ApplyPendingRemovals();

        Assert.Contains(pack, world.Objects);
        Assert.IsEmpty(world.DrainPendingSounds());
    }
}
