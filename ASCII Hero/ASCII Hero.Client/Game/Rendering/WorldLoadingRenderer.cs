using ASCII_Hero.Client.Game.Menu;

namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// Builds the glyph list for the "Loading [World]" screen shown while a confirmed world's
/// <see cref="World.World2D"/> is being loaded (see <c>GameLoop</c>'s LoadingWorld
/// <c>GameMode</c>): the frozen world-selection layout (via <see cref="WorldSelectRenderer.BuildFrame"/>)
/// plus a filling progress bar and "Loading..." label underneath. Kept as its own type - mirroring
/// how <see cref="WorldRenderer"/> and <see cref="WorldSelectRenderer"/> each own exactly one
/// <c>GameMode</c>'s rendering - even though it reuses <see cref="WorldSelectRenderer"/>'s static
/// layout as a backdrop, since loading is conceptually its own screen/state, not a variant of the
/// selection screen itself.
/// </summary>
public static class WorldLoadingRenderer
{
    // Layout for the progress bar shown under the thumbnail row while World2D.LoadAsync is in
    // flight.
    private const int LoadingBarWidth = 30;
    private const int LoadingBarGapRows = 2;
    private const string LoadingLabelText = "Loading...";

    /// <summary>
    /// Builds a fresh <see cref="UIBar"/> for the "Loading [World]" readout shown under the
    /// thumbnail row while the selected world's <see cref="World.World2D"/> is being loaded -
    /// centered the same way the thumbnail row itself is, so it lines up regardless of viewport
    /// width/visible slot count. The caller owns the returned instance and updates its
    /// <see cref="UIBar.CurrentValue"/> as loading progresses; this method only computes the
    /// (static) layout.
    /// </summary>
    public static UIBar CreateLoadingBar(WorldSelectScreen screen, double viewportWidthCells, double viewportHeightCells, double stepCount)
    {
        var startRow = (viewportHeightCells - WorldSelectRenderer.BlockHeight) / 2;

        var col = (viewportWidthCells - LoadingBarWidth) / 2;
        var row = startRow + WorldSelectRenderer.BlockHeight + LoadingBarGapRows;

        return new UIBar(col, row, LoadingBarWidth, height: 1, minValue: 0, maxValue: stepCount, foreColor: WorldSelectRenderer.WorldSelectForeColor)
        {
            CurrentValue = 0,
        };
    }

    /// <summary>
    /// Draws the world-selection screen's static layout (thumbnails/titles/selection box, via
    /// <see cref="WorldSelectRenderer.BuildFrame"/>) plus the given loading progress bar and its
    /// "Loading..." label underneath - used while <c>GameLoop</c> is in its LoadingWorld
    /// <c>GameMode</c> so the last-confirmed selection stays visible and the bar can visibly
    /// fill in, instead of freezing the last drawn frame.
    /// </summary>
    public static List<Glyph> BuildLoadingFrame(
        WorldSelectScreen screen, UIBar loadingBar, double viewportWidthCells, double viewportHeightCells,
        double cellWidthPixels, double cellHeightPixels)
    {
        var glyphs = WorldSelectRenderer.BuildFrame(screen, viewportWidthCells, viewportHeightCells, cellWidthPixels, cellHeightPixels);

        var labelCol = loadingBar.Col + (loadingBar.Width - LoadingLabelText.Length) / 2.0;
        var labelRow = loadingBar.Row - 1;
        var label = new UILabel(labelCol, labelRow, width: LoadingLabelText.Length, height: 1, foreColor: WorldSelectRenderer.WorldSelectForeColor);
        label.Lines.Add(LoadingLabelText);
        UIRenderer.AddLabel(glyphs, label, cellWidthPixels, cellHeightPixels);

        UIRenderer.AddBar(glyphs, loadingBar, cellWidthPixels, cellHeightPixels);

        return glyphs;
    }
}
