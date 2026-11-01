using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.World;

/// <summary>Only objects that stay in place or follow a fixed path can have a moving (conveyor) surface.</summary>
[TestClass]
public sealed class ConveyorValidationTests
{
    /// <summary>Serves the shipped Crates world, with the given lines added to its light crate placement.</summary>
    private sealed class CrateOverrideProvider(string extraLines) : IAssetFileProvider
    {
        private readonly DiskAssetFileProvider _disk = new();

        public async Task<string?> TryReadTextAsync(string relativePath)
        {
            var text = await _disk.TryReadTextAsync(relativePath);
            return relativePath.EndsWith("Crates_objects.ini", StringComparison.Ordinal) && text is not null
                ? text.Replace("[Crate]", $"[Crate]\n{extraLines}", StringComparison.Ordinal)
                : text;
        }
    }

    [TestMethod]
    [DataRow("ConveyorDirection = Left")]
    [DataRow("ConveyorSpeed = 3\nConveyorDirection = Right")]
    public async Task ConveyorKeysOnMovableObject_AreRejected(string extraLines)
    {
        await Assert.ThrowsExactlyAsync<FormatException>(() => World2D.LoadAsync(new CrateOverrideProvider(extraLines), "Crates"));
    }
}
