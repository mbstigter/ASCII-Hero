using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.World;

/// <summary>
/// Loads every shipped world exactly as the game does, so any change that makes a real world
/// fail to load (e.g. stricter asset validation) is caught here.
/// </summary>
[TestClass]
public sealed class ShippedWorldsTests
{
    public static IEnumerable<object[]> WorldNames()
    {
        var lines = File.ReadAllLines(Path.Combine(DiskAssetFileProvider.WwwRoot, "Assets", "Global", "Worlds.ini"));
        var order = lines
            .Select(line => line.Split(';')[0].Trim())
            .First(line => line.StartsWith("Order", StringComparison.OrdinalIgnoreCase));
        return order[(order.IndexOf('=') + 1)..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => new object[] { name });
    }

    private static readonly string[] expected = ["Key", "Rock", "HealthPack"];
    private static readonly string[] expectedArray = ["icon", "icon", "icon"];
    private static readonly char[] expectedArray0 = ['^', ' ', '^'];
    private static readonly char[] expectedArray1 = [' ', '^', ' '];

    [TestMethod]
    [DynamicData(nameof(WorldNames))]
    public async Task ShippedWorld_Loads(string worldName)
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), worldName);

        Assert.IsNotNull(world);
        CollectionAssert.AreEqual(expected, world.Player.Gear.Select(gear => gear.Name).ToArray());
        CollectionAssert.AreEqual(expectedArray, world.Player.Gear.Select(gear => gear.IconClipName).ToArray());
        Assert.IsTrue(world.Player.Gear.All(gear => gear.IconFrame is not null));
        var healthPackIcon = world.Player.Gear.Single(gear => gear.Name == "HealthPack").IconFrame!;
        CollectionAssert.AreEqual(expectedArray0, Enumerable.Range(0, healthPackIcon.Width).Select(col => healthPackIcon.Fore[0, col]).ToArray());
        CollectionAssert.AreEqual(expectedArray1, Enumerable.Range(0, healthPackIcon.Width).Select(col => healthPackIcon.Back[0, col]).ToArray());
        Assert.AreEqual("PlayerHurt", world.Player.HurtSoundName);
        Assert.AreEqual("PlayerKill", world.Player.KillSoundName);
        var hurt = world.Sounds.Definitions.Single(sound => sound.Name == "PlayerHurt");
        var kill = world.Sounds.Definitions.Single(sound => sound.Name == "PlayerKill");
        Assert.AreNotEqual(hurt.DurationSeconds, kill.DurationSeconds);
    }
}
