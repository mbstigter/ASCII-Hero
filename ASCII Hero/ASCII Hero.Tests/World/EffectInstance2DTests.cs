using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Rendering;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.World;

[TestClass]
public sealed class EffectInstance2DTests
{
    private const string Folder = "Assets/Global/Sprites/Thing";

    private sealed class MemoryAssetFileProvider(Dictionary<string, string> files) : IAssetFileProvider
    {
        public Task<string?> TryReadTextAsync(string relativePath) =>
            Task.FromResult(files.TryGetValue(relativePath, out var content) ? content : null);
    }

    private static Task<SpriteAsset> LoadAsync(string settings) =>
        new SpriteLoader(new MemoryAssetFileProvider(new Dictionary<string, string>
        {
            [$"{Folder}/Thing_settings.ini"] = settings,
            [$"{Folder}/Thing_idle_characters.txt"] = "#",
            [$"{Folder}/Thing_fx_characters.txt"] = "*",
        })).LoadAsync("Thing", ["idle", "fx"], worldName: null);

    [TestMethod]
    public async Task FollowingEffect_TracksTargetAtClipOffset()
    {
        var sprite = await LoadAsync("[Animation.fx]\nEffectDurationSeconds = 1.0\nOffsetX = 1\nOffsetY = -2");
        var target = new EffectInstance2D();
        target.Spawn(sprite, "idle", new Vector2D(5, 5), persistsAfterPlayback: true);
        var effect = new EffectInstance2D();

        effect.Spawn(sprite, "fx", target.Position, followTarget: target);
        Assert.AreEqual(new Vector2D(6, 3), effect.Position);

        target.Position = new Vector2D(10, 8);
        effect.Follow();
        Assert.AreEqual(new Vector2D(11, 6), effect.Position);
    }

    [TestMethod]
    public async Task Tint_BlinksBetweenTintAndAlternateColor()
    {
        var sprite = await LoadAsync("[Animation.fx]\nEffectDurationSeconds = 1.0\nTintColor = E\nTintAlternateColor = 3\nTintBlinkSeconds = 0.1");
        var effect = new EffectInstance2D();
        effect.Spawn(sprite, "fx", new Vector2D(0, 0));

        Assert.AreEqual('E', effect.CurrentTintColor);
        effect.Tick(0.15);
        Assert.AreEqual('3', effect.CurrentTintColor);
        effect.Tick(0.1);
        Assert.AreEqual('E', effect.CurrentTintColor);
    }

    [TestMethod]
    public async Task NoTint_IsNull()
    {
        var sprite = await LoadAsync("[Animation.fx]\nEffectDurationSeconds = 1.0");
        var effect = new EffectInstance2D();
        effect.Spawn(sprite, "fx", new Vector2D(0, 0));

        Assert.IsNull(effect.CurrentTintColor);
    }
}
