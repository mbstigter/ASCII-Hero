using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

/// <summary>Keys are carried as gear: picked up into the Key slot, and only used on a gate while that slot is selected.</summary>
[TestClass]
public sealed class KeyGearTests
{
    private static async Task<(World2D World, Gate2D RedGate, Gear2D KeyGear)> LoadAsync()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "DoorsAndHatches");
        var redGate = world.Objects.OfType<Gate2D>().Single(gate => gate.LockName == "Red");
        var keyGear = world.Player.FindGear(Key2D.GearName) ?? throw new AssertFailedException("Settings.ini [Gear] should list Key.");
        return (world, redGate, keyGear);
    }

    private static Key2D TakeKey(World2D world, string lockName)
    {
        var key = world.Objects.OfType<Key2D>().Single(candidate => candidate.LockName == lockName);
        world.Objects.Remove(key);
        return key;
    }

    [TestMethod]
    public async Task PlayerTouchingKey_CarriesItInTheKeyGear()
    {
        var (world, _, keyGear) = await LoadAsync();
        var key = world.Objects.OfType<Key2D>().Single(candidate => candidate.LockName == "Red");
        world.Player.Position = key.Position;

        new CollisionSystem().Resolve(world, 0.0);
        world.ApplyPendingRemovals();

        Assert.AreEqual(1, keyGear.Count);
        Assert.DoesNotContain(key, world.Objects);
    }

    [TestMethod]
    public async Task TryOperateGate_NeedsTheKeyGearSelected()
    {
        var (world, redGate, keyGear) = await LoadAsync();
        keyGear.Add(TakeKey(world, "Red"));
        world.Player.Position = redGate.Position;

        world.Player.SelectedGear = world.Player.Gear.First(gear => gear != keyGear);
        Assert.IsFalse(world.TryOperateGate());
        Assert.IsFalse(redGate.IsOn);

        world.Player.SelectedGear = keyGear;
        Assert.IsTrue(world.TryOperateGate());
        Assert.IsTrue(redGate.IsOn);
        Assert.AreEqual(1, keyGear.Count);
    }

    [TestMethod]
    public async Task TryOperateGate_ClosesAnOpenGateWithTheSameKey()
    {
        var (world, redGate, keyGear) = await LoadAsync();
        keyGear.Add(TakeKey(world, "Red"));
        world.Player.SelectedGear = keyGear;
        world.Player.Position = redGate.Position;
        redGate.SetState(true);
        world.Player.Position = new Vector2D(redGate.Position.X - world.Player.Size.X - 0.5, redGate.Position.Y);

        Assert.IsTrue(world.TryOperateGate());
        Assert.IsFalse(redGate.IsOn);
        Assert.AreEqual(1, keyGear.Count);
    }

    [TestMethod]
    public async Task TryOperateGate_DoesNotCloseOnThePlayer()
    {
        var (world, redGate, keyGear) = await LoadAsync();
        keyGear.Add(TakeKey(world, "Red"));
        world.Player.SelectedGear = keyGear;
        redGate.SetState(true);
        world.Player.Position = redGate.Position;

        Assert.IsFalse(world.TryOperateGate());
        Assert.IsTrue(redGate.IsOn);
    }

    [TestMethod]
    public async Task TryOperateGate_WithTheWrongKey_KeepsTheGateClosedAndTheKey()
    {
        var (world, redGate, keyGear) = await LoadAsync();
        keyGear.Add(TakeKey(world, "Blue"));
        world.Player.SelectedGear = keyGear;
        world.Player.Position = redGate.Position;

        Assert.IsFalse(world.TryOperateGate());
        Assert.IsFalse(redGate.IsOn);
        Assert.AreEqual(1, keyGear.Count);
    }

    [TestMethod]
    public async Task TryOperateGate_PlaysKeyTurnThenTheGateSound()
    {
        var (world, redGate, keyGear) = await LoadAsync();
        keyGear.Add(TakeKey(world, "Red"));
        world.Player.SelectedGear = keyGear;
        world.Player.Position = redGate.Position;
        world.DrainPendingSounds();

        Assert.IsTrue(world.TryOperateGate());

        CollectionAssert.AreEqual(new[] { "KeyTurn", redGate.SoundName }, world.DrainPendingSounds().ToArray());
    }

    [TestMethod]
    public async Task TryOperateSwitch_PlaysTheSwitchSoundThenTheLinkedGateSound()
    {
        var (world, _, _) = await LoadAsync();
        var button = world.Objects.OfType<Switch2D>().Single(candidate => candidate.SoundName == "ButtonPress");
        var gate = button.Targets.OfType<Gate2D>().Single();
        world.Player.Position = button.Position;
        world.DrainPendingSounds();

        Assert.IsTrue(world.TryOperateSwitch());

        var sounds = world.DrainPendingSounds().ToArray();
        CollectionAssert.AreEqual(new[] { "ButtonPress", gate.SoundName }, sounds, string.Join(",", sounds));
    }

    [TestMethod]
    public async Task TryThrow_WithTheKeyGearSelected_ThrowsNothing()
    {
        var (world, _, keyGear) = await LoadAsync();
        keyGear.Add(TakeKey(world, "Red"));
        world.Player.SelectedGear = keyGear;

        Assert.IsFalse(world.TryThrow());
        Assert.AreEqual(1, keyGear.Count);
    }
}
