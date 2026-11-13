using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.World;

/// <summary>A switch-operated device with a moving surface (e.g. a rope) only moves while it is on.</summary>
[TestClass]
public sealed class DeviceConveyorTests
{
    private const string Folder = "Assets/Global/Sprites/Thing";

    private sealed class MemoryAssetFileProvider(Dictionary<string, string> files) : IAssetFileProvider
    {
        public Task<string?> TryReadTextAsync(string relativePath) =>
            Task.FromResult(files.TryGetValue(relativePath, out var content) ? content : null);
    }

    [TestMethod]
    public async Task Device_AppliesItsRunningVelocityOnlyWhileOn()
    {
        var sprite = await new SpriteLoader(new MemoryAssetFileProvider(new Dictionary<string, string>
        {
            [$"{Folder}/Thing_settings.ini"] = "[Layout]\nEmptyChar = ' '",
            [$"{Folder}/Thing_off_characters.txt"] = "a",
            [$"{Folder}/Thing_on_characters.txt"] = "b",
        })).LoadAsync("Thing", ["off", "on"], worldName: null);
        var running = new Vector2D(0, -3);
        var device = new Device2D { RunningConveyorVelocity = running };
        device.SpawnTwoState(sprite, "off", "on", initialOn: false, new Vector2D(0, 0), span: 0);

        Assert.AreEqual(default, device.ConveyorVelocity);
        device.SetState(true);
        Assert.AreEqual(running, device.ConveyorVelocity);
        device.SetState(false);
        Assert.AreEqual(default, device.ConveyorVelocity);
    }

    [TestMethod]
    public async Task ShippedWorld_LinkedRopeRunsOnlyWhileItsLeverIsOn()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "AncientTestWorld");
        var rope = world.Objects.OfType<Device2D>().Single(device => device.LinkName == "AncientRope");
        var lever = world.Objects.OfType<Switch2D>().Single(switchObject => switchObject.LinkName == "AncientRope");

        Assert.AreEqual(default, rope.ConveyorVelocity);
        Assert.IsLessThan(0, rope.RunningConveyorVelocity.Y, "rope should pull upward");

        world.Player.Position = lever.Position;
        Assert.IsTrue(world.TryOperateSwitch());
        Assert.AreEqual(rope.RunningConveyorVelocity, rope.ConveyorVelocity);

        Assert.IsTrue(world.TryOperateSwitch());
        Assert.AreEqual(default, rope.ConveyorVelocity);
    }
}
