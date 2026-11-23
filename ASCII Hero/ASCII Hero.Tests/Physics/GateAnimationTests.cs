using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

[TestClass]
public sealed class GateAnimationTests
{
    private sealed class InitiallyOnSwitchProvider : IAssetFileProvider
    {
        private readonly DiskAssetFileProvider _disk = new();

        public async Task<string?> TryReadTextAsync(string relativePath)
        {
            var text = await _disk.TryReadTextAsync(relativePath);
            return text is not null && relativePath.EndsWith("DoorsAndHatches_objects.ini", StringComparison.Ordinal)
                ? text.Replace("[ButtonSwitch]", "[ButtonSwitch]\nInitialState = On", StringComparison.Ordinal)
                : text;
        }
    }

    [TestMethod]
    [DataRow("Door", "default")]
    [DataRow("Door", "ancient")]
    [DataRow("Hatch", "default")]
    [DataRow("Hatch", "ancient")]
    public async Task Gate_AnimatesBothWaysWhileCollisionChangesImmediately(string asset, string open)
    {
        var sprite = await new SpriteLoader(new DiskAssetFileProvider()).LoadAsync(asset, [open], null);
        var gate = new Gate2D();
        gate.SpawnTwoState(sprite, open, null, initialOn: false, position: new Vector2D(0, 0), span: 8);
        var openClip = sprite.GetClip(open);
        var frames = openClip.Frames.Select(frame => SpriteFrameTiler.Tile(frame, openClip.TileAxis, 8)).ToArray();

        Assert.AreEqual(0.25, openClip.FrameDurationSeconds);
        Assert.IsFalse(gate.IsPassable);
        gate.SetState(true);
        Assert.IsTrue(gate.IsPassable);
        Assert.AreEqual(frames[0].Width, gate.Frame.Width);
        gate.AdvanceAnimation(0.25);
        Assert.AreEqual(frames[1].Width, gate.Frame.Width);
        Assert.AreEqual(frames[1].Height, gate.Frame.Height);
        gate.AdvanceAnimation(0.25);
        Assert.AreEqual(frames[2].Width, gate.Frame.Width);
        Assert.AreEqual(frames[2].Height, gate.Frame.Height);

        gate.SetState(false);
        Assert.IsFalse(gate.IsPassable);
        Assert.AreEqual(open, gate.Clip.Name);
        Assert.AreEqual(frames[2].Height, gate.Frame.Height);
        CollectionAssert.AreEqual(gate.ClosedCollisionRects.ToArray(), gate.CollisionRects.ToArray());
        gate.AdvanceAnimation(0.25);
        Assert.AreEqual(frames[1].Width, gate.Frame.Width);
        Assert.AreEqual(frames[1].Height, gate.Frame.Height);
        CollectionAssert.AreEqual(gate.ClosedCollisionRects.ToArray(), gate.CollisionRects.ToArray());
        gate.AdvanceAnimation(0.25);
        Assert.AreEqual(open, gate.Clip.Name);
        Assert.AreEqual(frames[0].Width, gate.Frame.Width);
        Assert.AreEqual(frames[0].Height, gate.Frame.Height);
        Assert.IsFalse(gate.IsPassable);
        gate.AdvanceAnimation(1);
        Assert.AreEqual(frames[0].Width, gate.Frame.Width);
        Assert.AreEqual(frames[0].Height, gate.Frame.Height);
    }

    [TestMethod]
    [DataRow("Door", "DoorMove")]
    [DataRow("Hatch", "HatchMove")]
    public async Task GateSound_MatchesFullMovementDuration(string asset, string soundName)
    {
        var provider = new DiskAssetFileProvider();
        var sprite = await new SpriteLoader(provider).LoadAsync(asset, ["default"], null);
        var sounds = await SoundLibrary.LoadAsync(provider, null);
        var clip = sprite.GetClip("default");
        var sound = sounds.Definitions.Single(item => item.Name == soundName);

        Assert.AreEqual((clip.Frames.Count - 1) * clip.FrameDurationSeconds!.Value, sound.DurationSeconds, 1e-9);
    }

    [TestMethod]
    [DataRow(false, 2)]
    [DataRow(true, 6)]
    public async Task SingleClipGate_InitialStateHoldsItsEndpoint(bool initialOn, int expectedWidth)
    {
        var sprite = await new SpriteLoader(new DiskAssetFileProvider()).LoadAsync("Door", ["default"], null);
        var gate = new Gate2D();
        gate.SpawnTwoState(sprite, "default", null, initialOn, new Vector2D(0, 0));
        gate.AdvanceAnimation(2);
        Assert.AreEqual(expectedWidth, gate.Frame.Width);
        Assert.AreEqual(initialOn, gate.IsPassable);
    }

    [TestMethod]
    public async Task LinkedGate_InitiallyOnStartsAtOpenEndpoint()
    {
        var world = await World2D.LoadAsync(new InitiallyOnSwitchProvider(), "DoorsAndHatches");
        var gate = world.Objects.OfType<Gate2D>().Single(item => item.LinkName == "GreyDoor");
        Assert.IsTrue(gate.IsOn);
        Assert.IsTrue(gate.IsPassable);
        Assert.AreEqual(6, gate.Frame.Width);
        gate.AdvanceAnimation(1);
        Assert.AreEqual(6, gate.Frame.Width);
    }

    [TestMethod]
    public async Task TwoClipGate_StillReturnsToClosedClip()
    {
        var original = await new SpriteLoader(new DiskAssetFileProvider()).LoadAsync("Door", ["default"], null);
        var open = original.GetClip("default");
        var closed = new SpriteClip { Name = "closed", Frames = [open.Frames[0]] };
        var sprite = new SpriteAsset
        {
            Name = "TestGate",
            EmptyChar = original.EmptyChar,
            Clips = new Dictionary<string, SpriteClip> { ["closed"] = closed, ["default"] = open },
        };
        var gate = new Gate2D();
        gate.SpawnTwoState(sprite, "closed", "default", initialOn: false, position: new Vector2D(0, 0));
        gate.SetState(true);
        gate.AdvanceAnimation(1);
        gate.SetState(false);
        Assert.AreEqual("default", gate.Clip.Name);
        gate.AdvanceAnimation(1);
        Assert.AreEqual("closed", gate.Clip.Name);
        Assert.AreEqual(2, gate.Frame.Width);
    }

    [TestMethod]
    public async Task Gate_ReversesDirectionDuringMovement()
    {
        var sprite = await new SpriteLoader(new DiskAssetFileProvider()).LoadAsync("Door", ["default"], null);
        var gate = new Gate2D();
        gate.SpawnTwoState(sprite, "default", null, initialOn: false, position: new Vector2D(0, 0));
        gate.SetState(true);
        gate.AdvanceAnimation(0.25);
        gate.SetState(false);
        Assert.AreEqual(4, gate.Frame.Width);
        gate.SetState(true);
        Assert.AreEqual(4, gate.Frame.Width);
        gate.AdvanceAnimation(0.25);
        Assert.AreEqual(6, gate.Frame.Width);
        Assert.IsTrue(gate.IsPassable);
    }

    private static readonly string[] expected = ["LeverPull", "HatchMove", "DoorMove"];

    [TestMethod]
    public async Task Switch_RejectsEntireOperationWhenAnyLinkedGateIsBlocked()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "DoorsAndHatches");
        var lever = world.Objects.OfType<Switch2D>().Single(item => item.LinkName == "GreyHatch");
        var hatch = lever.Targets.OfType<Gate2D>().Single();
        var otherGate = world.Objects.OfType<Gate2D>().Single(item => item.LockName == "Blue");
        var crate = world.Objects.OfType<DynamicObject2D>().First();
        lever.Targets.Add(otherGate);
        world.Player.Position = lever.Position;
        Assert.IsTrue(world.TryOperateSwitch());
        hatch.AdvanceAnimation(1);
        otherGate.AdvanceAnimation(1);
        crate.Position = otherGate.Position;
        world.DrainPendingSounds();

        Assert.IsFalse(lever.Operate(world));
        Assert.IsFalse(world.TryOperateSwitch());
        Assert.IsTrue(lever.IsOn);
        Assert.IsTrue(hatch.IsOn);
        Assert.IsTrue(otherGate.IsOn);
        Assert.IsEmpty(world.DrainPendingSounds());

        crate.Position = new Vector2D(0, 0);
        Assert.IsTrue(world.TryOperateSwitch());
        Assert.IsFalse(lever.IsOn);
        Assert.IsFalse(hatch.IsOn);
        Assert.IsFalse(otherGate.IsOn);
        CollectionAssert.AreEqual(expected, world.DrainPendingSounds().ToArray());
    }

    [TestMethod]
    public async Task Switch_RefusesToCloseOnPlayer()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "DoorsAndHatches");
        var button = world.Objects.OfType<Switch2D>().Single(item => item.LinkName == "GreyDoor");
        var gate = button.Targets.OfType<Gate2D>().Single();
        world.Player.Position = button.Position;
        Assert.IsTrue(world.TryOperateSwitch());
        gate.AdvanceAnimation(1);
        button.Position = gate.Position;
        world.Player.Position = gate.Position;
        world.DrainPendingSounds();

        Assert.IsFalse(world.TryOperateSwitch());
        Assert.IsTrue(button.IsOn);
        Assert.IsTrue(gate.IsOn);
        Assert.IsEmpty(world.DrainPendingSounds());
    }

    [TestMethod]
    public async Task KeyGate_RefusesToCloseOnCrate()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "DoorsAndHatches");
        var gate = world.Objects.OfType<Gate2D>().Single(item => item.LockName == "Blue");
        var key = world.Objects.OfType<Key2D>().Single(item => item.LockName == "Blue");
        var gear = world.Player.FindGear(Key2D.GearName)!;
        gear.Add(key);
        world.Objects.Remove(key);
        world.Player.SelectedGear = gear;
        gate.SetState(true);
        gate.AdvanceAnimation(1);
        world.Player.Position = new Vector2D(gate.Position.X - world.Player.Size.X - 0.5, gate.Position.Y);
        world.Objects.OfType<DynamicObject2D>().First().Position = gate.Position;
        world.DrainPendingSounds();

        Assert.IsFalse(world.TryOperateGate());
        Assert.IsTrue(gate.IsOn);
        Assert.IsEmpty(world.DrainPendingSounds());
    }

    [TestMethod]
    public async Task ClosingGate_UsesClosedCollisionCharactersDuringAnimation()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "DoorsAndHatches");
        var gate = world.Objects.OfType<Gate2D>().Single(item => item.LockName == "Blue");
        var closed = gate.Sprite.GetClip("default");
        var empty = new SpriteFrame { Chars = new char[,] { { '.', '.' } }, Fore = new char[1, 2], Back = new char[1, 2] };
        var open = new SpriteClip
        {
            Name = "open",
            Frames = [closed.Frames[0], empty],
            TileAxis = closed.TileAxis,
            AnimationMode = AnimationMode.Once,
            FrameDurationSeconds = 0.25,
        };
        var sprite = new SpriteAsset
        {
            Name = "TestGate",
            EmptyChar = '.',
            Clips = new Dictionary<string, SpriteClip> { ["closed"] = closed, ["open"] = open },
        };
        gate.SpawnTwoState(sprite, "open", null, initialOn: false, position: new Vector2D(10, 10), span: 8);
        world.Objects.RemoveAll(body => body != gate && body != world.Player);
        gate.SetState(true);
        gate.AdvanceAnimation(0.25);
        gate.SetState(false);
        Assert.AreEqual('.', gate.Frame.Chars[0, 0]);
        Assert.AreNotEqual('.', gate.CollisionFrame.Chars[0, 0]);
        world.Player.Position = gate.Position;
        var before = world.Player.Position;

        new CollisionSystem().Resolve(world, 0);

        Assert.AreNotEqual(before, world.Player.Position);
        Assert.IsFalse(gate.IsPassable);
    }
}
