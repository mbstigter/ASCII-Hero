using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Rendering;

namespace ASCII_Hero.Tests.Rendering;

[TestClass]
public sealed class GearIconRenderingTests
{
    [TestMethod]
    public void AddMonochromeIcon_UsesUiForegroundAndBackgroundMarkers()
    {
        var frame = new SpriteFrame
        {
            Chars = new[,] { { '▐', '+', '▌' } },
            Fore = new[,] { { '^', ' ', '^' } },
            Back = new[,] { { ' ', '^', ' ' } },
        };
        var glyphs = new List<Glyph>();

        UIRenderer.AddMonochromeIcon(
            glyphs, frame, emptyChar: ' ',
            col: 0, row: 0, width: 3, height: 1,
            interfaceForegroundColor: "#00ff00", interfaceBackgroundColor: "#000000", backColor: null,
            cellWidthPixels: 8, cellHeightPixels: 16);

        Assert.HasCount(3, glyphs);
        Assert.AreEqual("▐+▌", string.Concat(glyphs.Select(glyph => glyph.Character)));
        Assert.AreEqual("#00ff00", glyphs[0].ForeColor);
        Assert.AreEqual("#000000", glyphs[1].ForeColor);
        Assert.AreEqual("#00ff00", glyphs[2].ForeColor);
        Assert.IsNull(glyphs[0].BackColor);
        Assert.AreEqual("#00ff00", glyphs[1].BackColor);
        Assert.IsNull(glyphs[2].BackColor);
    }

    [TestMethod]
    [DataRow(1, 5, 2)]
    [DataRow(2, 3, 0)]
    [DataRow(3, 5, 1)]
    public void AddMonochromeIcon_CentersWithoutScaling(int iconWidth, int availableWidth, int offset)
    {
        var chars = new char[1, iconWidth];
        var fore = new char[1, iconWidth];
        var back = new char[1, iconWidth];
        for (var col = 0; col < iconWidth; col++)
        {
            chars[0, col] = 'X';
            fore[0, col] = '^';
            back[0, col] = ' ';
        }
        var frame = new SpriteFrame { Chars = chars, Fore = fore, Back = back };
        var glyphs = new List<Glyph>();

        UIRenderer.AddMonochromeIcon(
            glyphs, frame, emptyChar: ' ',
            col: 10, row: 2, width: availableWidth, height: 1,
            interfaceForegroundColor: "#ffaa00", interfaceBackgroundColor: "#000000", backColor: "#123456",
            cellWidthPixels: 8, cellHeightPixels: 16);

        Assert.HasCount(iconWidth, glyphs);
        for (var col = 0; col < iconWidth; col++)
        {
            Assert.AreEqual((10 + offset + col) * 8.0, glyphs[col].PixelX);
            Assert.AreEqual(32.0, glyphs[col].PixelY);
            Assert.AreEqual("#ffaa00", glyphs[col].ForeColor);
            Assert.AreEqual("#123456", glyphs[col].BackColor);
        }
    }

    [TestMethod]
    public void AddMonochromeIcon_CustomEmptyCharPreservesBackgroundOnlyCells()
    {
        var frame = new SpriteFrame
        {
            Chars = new[,] { { '.', '.', '+' } },
            Fore = new[,] { { '.', '.', '.' } },
            Back = new[,] { { '.', '^', '^' } },
        };
        var glyphs = new List<Glyph>();

        UIRenderer.AddMonochromeIcon(
            glyphs, frame, emptyChar: '.',
            col: 0, row: 0, width: 3, height: 1,
            interfaceForegroundColor: "#ffaa00", interfaceBackgroundColor: "#112233", backColor: null,
            cellWidthPixels: 8, cellHeightPixels: 16);

        Assert.HasCount(2, glyphs);
        Assert.AreEqual(' ', glyphs[0].Character);
        Assert.AreEqual(8.0, glyphs[0].PixelX);
        Assert.AreEqual('+', glyphs[1].Character);
        Assert.AreEqual(16.0, glyphs[1].PixelX);
        Assert.AreEqual("#112233", glyphs[1].ForeColor);
        Assert.AreEqual("#ffaa00", glyphs[0].BackColor);
        Assert.AreEqual("#ffaa00", glyphs[1].BackColor);
    }
}
