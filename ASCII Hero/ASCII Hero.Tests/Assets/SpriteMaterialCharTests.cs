using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Tests.Assets;

[TestClass]
public sealed class SpriteMaterialCharTests
{
    private const string Folder = "Assets/Global/Sprites/Thing";

    private sealed class MemoryAssetFileProvider(Dictionary<string, string> files) : IAssetFileProvider
    {
        public Task<string?> TryReadTextAsync(string relativePath) =>
            Task.FromResult(files.TryGetValue(relativePath, out var content) ? content : null);
    }

    private static Task<SpriteAsset> LoadAsync(string settings)
    {
        var files = new Dictionary<string, string>
        {
            [$"{Folder}/Thing_settings.ini"] = settings,
            [$"{Folder}/Thing_idle_characters.txt"] = "^^",
        };

        return new SpriteLoader(new MemoryAssetFileProvider(files)).LoadAsync("Thing", ["idle"], worldName: null);
    }

    [TestMethod]
    public async Task MaterialChar_IsReadFromLayout()
    {
        var sprite = await LoadAsync("[Layout]\nMaterialChar = '^'");

        Assert.AreEqual('^', sprite.MaterialChar);
    }

    [TestMethod]
    public async Task MaterialChar_IsNullWhenAbsent()
    {
        var sprite = await LoadAsync("[Layout]\nEmptyChar = ' '");

        Assert.IsNull(sprite.MaterialChar);
    }
}
