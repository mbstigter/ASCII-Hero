using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.World;

/// <summary>Gravity is a finite, non-negative magnitude; anything else is rejected at load.</summary>
[TestClass]
public sealed class GravityValidationTests
{
    /// <summary>Serves the shipped Enemies world, with its settings file replaced by a given [Physics] Gravity.</summary>
    private sealed class GravityOverrideProvider(string gravity) : IAssetFileProvider
    {
        private readonly DiskAssetFileProvider _disk = new();

        public Task<string?> TryReadTextAsync(string relativePath) =>
            relativePath.EndsWith("Enemies_settings.ini", StringComparison.Ordinal)
                ? Task.FromResult<string?>($"[Physics]\nGravity = {gravity}\n")
                : _disk.TryReadTextAsync(relativePath);
    }

    [TestMethod]
    [DataRow("-1")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("heavy")]
    public async Task InvalidGravity_IsRejected(string gravity)
    {
        await Assert.ThrowsExactlyAsync<FormatException>(() => World2D.LoadAsync(new GravityOverrideProvider(gravity), "Enemies"));
    }

    [TestMethod]
    [DataRow("0", 0.0)]
    [DataRow("25.5", 25.5)]
    public async Task ValidGravity_IsApplied(string gravity, double expected)
    {
        var world = await World2D.LoadAsync(new GravityOverrideProvider(gravity), "Enemies");

        Assert.AreEqual(expected, world.Gravity);
    }
}
