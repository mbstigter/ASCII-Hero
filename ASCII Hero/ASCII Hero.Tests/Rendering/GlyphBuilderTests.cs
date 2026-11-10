using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Rendering;

namespace ASCII_Hero.Tests.Rendering;

[TestClass]
public sealed class GlyphBuilderTests
{
    [TestMethod]
    public async Task ResolveColor_NoFillCode_StopsChainWithNoFill()
    {
        var palette = await ColorPalette.LoadAsync(new DiskAssetFileProvider(), null);

        var color = GlyphBuilder.ResolveColor(palette, null, null, IniValueParser.NoFillColorCode, 'S');

        Assert.IsNull(color);
    }

    [TestMethod]
    public async Task ResolveColor_CodeBeforeNoFill_Wins()
    {
        var palette = await ColorPalette.LoadAsync(new DiskAssetFileProvider(), null);

        var color = GlyphBuilder.ResolveColor(palette, null, '0', IniValueParser.NoFillColorCode);

        Assert.AreEqual(palette.TryGetColor('0'), color);
    }
}
