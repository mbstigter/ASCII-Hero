using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.World;

/// <summary>
/// Loads every shipped world exactly as the game does, so any change that makes a real level
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

    [TestMethod]
    [DynamicData(nameof(WorldNames))]
    public async Task ShippedWorld_Loads(string worldName)
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), worldName);

        Assert.IsNotNull(world);
    }
}
