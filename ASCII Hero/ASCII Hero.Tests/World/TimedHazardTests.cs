using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.World;

/// <summary>A timed hazard switches itself on and off, and only hurts while on.</summary>
[TestClass]
public sealed class TimedHazardTests
{
    private const string Folder = "Assets/Global/Sprites/Thing";

    private sealed class MemoryAssetFileProvider(Dictionary<string, string> files) : IAssetFileProvider
    {
        public Task<string?> TryReadTextAsync(string relativePath) =>
            Task.FromResult(files.TryGetValue(relativePath, out var content) ? content : null);
    }

    private static async Task<TimedHazard2D> CreateAsync(string? offClip, bool initialOn, double? onSeconds, double? offSeconds = null)
    {
        var sprite = await new SpriteLoader(new MemoryAssetFileProvider(new Dictionary<string, string>
        {
            [$"{Folder}/Thing_settings.ini"] = "[Layout]\nEmptyChar = ' '",
            [$"{Folder}/Thing_off_characters.txt"] = "_",
            [$"{Folder}/Thing_on_characters.txt"] = "^",
        })).LoadAsync("Thing", ["off", "on"], worldName: null);
        var hazard = new TimedHazard2D { OnSeconds = onSeconds, OffSeconds = offSeconds };
        hazard.SpawnTwoState(sprite, offClip, "on", initialOn, new Vector2D(0, 0), span: 0);
        return hazard;
    }

    [TestMethod]
    public async Task OnSecondsOnly_AlternatesWithTheSameOffTime()
    {
        var hazard = await CreateAsync("off", initialOn: true, onSeconds: 2);

        hazard.Tick(1.9);
        Assert.IsTrue(hazard.IsOn);
        hazard.Tick(0.2);
        Assert.IsFalse(hazard.IsOn);
        hazard.Tick(1.7);
        Assert.IsFalse(hazard.IsOn);
        hazard.Tick(0.5);
        Assert.IsTrue(hazard.IsOn);
    }

    [TestMethod]
    public async Task OffSeconds_SetsTheOffTimeSeparately()
    {
        var hazard = await CreateAsync("off", initialOn: true, onSeconds: 1, offSeconds: 3);

        hazard.Tick(1.1);
        Assert.IsFalse(hazard.IsOn);
        hazard.Tick(2.4);
        Assert.IsFalse(hazard.IsOn);
        hazard.Tick(0.6);
        Assert.IsTrue(hazard.IsOn);
    }

    [TestMethod]
    public async Task WithoutOnSeconds_NeverChangesByItself()
    {
        var hazard = await CreateAsync("off", initialOn: false, onSeconds: null);

        hazard.Tick(100);

        Assert.IsFalse(hazard.IsOn);
    }

    [TestMethod]
    public async Task OnlyHarmfulWhileOn()
    {
        var hazard = await CreateAsync("off", initialOn: false, onSeconds: 1);

        Assert.IsFalse(((IHazardBody)hazard).IsHarmful);
        hazard.SetState(true);
        Assert.IsTrue(((IHazardBody)hazard).IsHarmful);
    }

    [TestMethod]
    public async Task WithoutOffClip_IsHiddenWhileOff()
    {
        var hazard = await CreateAsync(offClip: null, initialOn: false, onSeconds: 1);

        Assert.IsTrue(hazard.IsHidden);
        hazard.SetState(true);
        Assert.IsFalse(hazard.IsHidden);
    }

    [TestMethod]
    public async Task WithOffClip_IsNeverHidden()
    {
        var hazard = await CreateAsync("off", initialOn: false, onSeconds: 1);

        Assert.IsFalse(hazard.IsHidden);
    }

    [TestMethod]
    public async Task ShippedThrowablesWorld_HasTimedSpikesThatStartHiddenOrOut()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Throwables");
        var spikes = world.Objects.OfType<TimedHazard2D>().ToList();

        Assert.IsNotEmpty(spikes);
        Assert.IsTrue(spikes.All(spike => spike.OnSeconds is > 0));
        Assert.IsTrue(spikes.Any(spike => spike.IsOn));
        Assert.IsTrue(spikes.Any(spike => spike.IsHidden));
    }
}
