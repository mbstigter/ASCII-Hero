using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Menu;

namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// Builds the "Loading..." screen shown while a confirmed world loads: the world-selection layout
/// (see <see cref="WorldSelectRenderer"/>) plus a progress bar and label underneath.
/// </summary>
public static class WorldLoadingRenderer
{
    private const int LoadingBarWidth = 30;
    private const int LoadingBarGapRows = 4;
    private const string LoadingLabelText = "Loading...";

    /// <summary>
    /// Creates the progress bar, centered under the thumbnail row. The caller updates its
    /// <see cref="UIBar.CurrentValue"/> as loading progresses.
    /// </summary>
    public static UIBar CreateLoadingBar(InterfaceColors colors, double viewportWidthCells, double viewportHeightCells, double stepCount)
    {
        var startRow = (viewportHeightCells - WorldSelectRenderer.BlockHeight) / 2;

        var col = (viewportWidthCells - LoadingBarWidth) / 2;
        var row = startRow + WorldSelectRenderer.BlockHeight + LoadingBarGapRows;

        return new UIBar(col, row, LoadingBarWidth, height: 1, minValue: 0, maxValue: stepCount, foreColor: colors.Green)
        {
            CurrentValue = 0,
        };
    }

    /// <summary>Draws the world-selection layout with the progress bar and its "Loading..." label.</summary>
    public static List<Glyph> BuildLoadingFrame(
        WorldSelectScreen screen, InterfaceColors colors, UIBar loadingBar, double viewportWidthCells, double viewportHeightCells,
        double cellWidthPixels, double cellHeightPixels)
    {
        var glyphs = WorldSelectRenderer.BuildFrame(screen, colors, viewportWidthCells, viewportHeightCells, cellWidthPixels, cellHeightPixels);

        var labelCol = loadingBar.Col + (loadingBar.Width - LoadingLabelText.Length) / 2.0;
        var labelRow = loadingBar.Row - 1;
        var label = new UILabel(labelCol, labelRow, width: LoadingLabelText.Length, height: 1, foreColor: colors.Green);
        label.Lines.Add(LoadingLabelText);
        UIRenderer.AddLabel(glyphs, label, cellWidthPixels, cellHeightPixels);

        UIRenderer.AddBar(glyphs, loadingBar, cellWidthPixels, cellHeightPixels);

        return glyphs;
    }
}
