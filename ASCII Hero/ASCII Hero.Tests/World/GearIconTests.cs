using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.World;

[TestClass]
public sealed class GearIconTests
{
    private static SpriteFrame Frame(int width = 1, int height = 1, char fore = '^', char back = ' ')
    {
        var chars = new char[height, width];
        var foreground = new char[height, width];
        var background = new char[height, width];
        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                chars[row, col] = 'X';
                foreground[row, col] = fore;
                background[row, col] = back;
            }
        }

        return new SpriteFrame { Chars = chars, Fore = foreground, Back = background };
    }

    private static SpriteAsset Asset(IReadOnlyList<SpriteFrame> frames, int defaultFrame = 0, char emptyChar = ' ') => new()
    {
        Name = "Item",
        EmptyChar = emptyChar,
        Clips = new Dictionary<string, SpriteClip>
        {
            ["icon"] = new() { Name = "icon", Frames = frames, DefaultFrame = defaultFrame },
        },
    };

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void LoadIcon_AcceptsOneToThreeCells(int width)
    {
        var frame = Frame(width);
        var gear = new Gear2D("Item", "Item", "icon");

        gear.LoadIcon(Asset([frame]));

        Assert.AreSame(frame, gear.IconFrame);
        Assert.AreEqual(' ', gear.IconEmptyChar);
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(4, 1)]
    [DataRow(1, 0)]
    [DataRow(1, 2)]
    public void LoadIcon_RejectsInvalidDimensions(int width, int height)
    {
        var gear = new Gear2D("Item", "Item", "icon");

        var error = Assert.ThrowsExactly<FormatException>(() => gear.LoadIcon(Asset([Frame(width, height)])));

        Assert.Contains("Item/icon", error.Message);
        Assert.Contains("one row and one to three cells wide", error.Message);
        Assert.IsNull(gear.IconFrame);
    }

    [TestMethod]
    [DataRow('H', ' ', "foreground")]
    [DataRow('^', 'H', "background")]
    public void LoadIcon_RejectsPaletteColors(char fore, char back, string layer)
    {
        var gear = new Gear2D("Item", "Item", "icon");

        var error = Assert.ThrowsExactly<FormatException>(() => gear.LoadIcon(Asset([Frame(fore: fore, back: back)])));

        Assert.Contains(layer, error.Message);
        Assert.IsNull(gear.IconFrame);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    public void LoadIcon_RejectsInvalidDefaultFrame(int defaultFrame)
    {
        var gear = new Gear2D("Item", "Item", "icon");

        var error = Assert.ThrowsExactly<FormatException>(() => gear.LoadIcon(Asset([Frame()], defaultFrame)));

        Assert.Contains("DefaultFrame", error.Message);
        Assert.IsNull(gear.IconFrame);
    }

    [TestMethod]
    public void LoadIcon_HoldsTheConfiguredDefaultFrame()
    {
        var first = Frame();
        var second = Frame(width: 3);
        var gear = new Gear2D("Item", "Item", "icon");

        gear.LoadIcon(Asset([first, second], defaultFrame: 1));

        Assert.AreSame(second, gear.IconFrame);
    }

    [TestMethod]
    public void LoadIcon_AcceptsCustomEmptyChar()
    {
        var frame = Frame(fore: '.', back: '^');
        var gear = new Gear2D("Item", "Item", "icon");

        gear.LoadIcon(Asset([frame], emptyChar: '.'));

        Assert.AreSame(frame, gear.IconFrame);
        Assert.AreEqual('.', gear.IconEmptyChar);
    }

    [TestMethod]
    public void LoadIcon_RejectsEmptyCharConflictingWithColorMarker()
    {
        var gear = new Gear2D("Item", "Item", "icon");

        var error = Assert.ThrowsExactly<FormatException>(() => gear.LoadIcon(Asset([Frame()], emptyChar: '^')));

        Assert.Contains("EmptyChar", error.Message);
        Assert.IsNull(gear.IconFrame);
    }
}
