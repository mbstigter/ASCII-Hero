using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.World;

/// <summary>The world's [Colors] WorldBackdrop / InterfaceBackdrop keys.</summary>
[TestClass]
public sealed class BackdropSettingsTests
{
    /// <summary>Serves the shipped Enemies world, with its settings file replaced by a given [Colors] section.</summary>
    private sealed class ColorsOverrideProvider(string colors) : IAssetFileProvider
    {
        private readonly DiskAssetFileProvider _disk = new();

        public Task<string?> TryReadTextAsync(string relativePath) =>
            relativePath.EndsWith("Enemies_settings.ini", StringComparison.Ordinal)
                ? Task.FromResult<string?>($"[Colors]\n{colors}\n")
                : _disk.TryReadTextAsync(relativePath);
    }

    [TestMethod]
    public async Task InterfaceBackdrop_Unset_IsNull()
    {
        var world = await World2D.LoadAsync(new ColorsOverrideProvider("WorldBackdrop = S"), "Enemies");

        Assert.AreEqual('S', world.WorldBackdrop);
        Assert.IsNull(world.InterfaceBackdrop);
    }

    [TestMethod]
    public async Task InterfaceBackdrop_Code_IsRead()
    {
        var world = await World2D.LoadAsync(new ColorsOverrideProvider("InterfaceBackdrop = 1"), "Enemies");

        Assert.AreEqual('1', world.InterfaceBackdrop);
    }

    [TestMethod]
    public async Task InterfaceBackdrop_WorldBackdropKeyword_UsesWorldBackdrop()
    {
        var world = await World2D.LoadAsync(new ColorsOverrideProvider("WorldBackdrop = S\nInterfaceBackdrop = WorldBackdrop"), "Enemies");

        Assert.AreEqual('S', world.InterfaceBackdrop);
    }

    [TestMethod]
    public async Task InterfaceBackdrop_WorldBackdropKeyword_WithoutWorldBackdrop_IsRejected()
    {
        await Assert.ThrowsExactlyAsync<FormatException>(() => World2D.LoadAsync(new ColorsOverrideProvider("InterfaceBackdrop = WorldBackdrop"), "Enemies"));
    }
}
