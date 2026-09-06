using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Menu;

namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// Builds the glyph list for the world-selection screen: a horizontal row of world thumbnails,
/// each with its title above it, and a static selector box around whichever slot the current
/// selection lands in. Unlike <see cref="WorldRenderer"/> this has no camera - it lays out
/// directly in screen (viewport) cell coordinates, since the selection screen isn't part of any
/// <see cref="World.World2D"/>. See docs/AssetFormat.md §3.2.
/// </summary>
public static class WorldSelectRenderer
{
    private const string WorldSelectForeColor = "#00ff00";
    // One cell of breathing room to either side of a slot's 16-wide thumbnail, used to draw the
    // selector box's left/right border without needing extra space reserved between slots.
    private const int SlotHorizontalPadding = 1;
    private const int SlotPitch = WorldCatalog.ThumbnailWidth + SlotHorizontalPadding * 2;

    private const int TitleRowCount = 1;
    private const int SlotVerticalPadding = 1;
    private const int BlockHeight = TitleRowCount + SlotVerticalPadding + WorldCatalog.ThumbnailHeight + SlotVerticalPadding;

    // Layout for the "Loading [World]" progress bar shown under the thumbnail row while
    // World2D.LoadAsync is in flight (see GameLoop's LoadingWorld GameMode).
    private const int LoadingBarWidth = 30;
    private const int LoadingBarGapRows = 2;
    private const string LoadingLabelText = "Loading...";

    // ASCII logo shown above the thumbnail row/selector frame - purely decorative, drawn via a
    // single multi-line UILabel same as any other screen-space text.
    private static readonly string[] LogoLines =
    [
        @"   ___   _____ ______________   __  __              ",
        @"  /   | / ___// ____/  _/  _/  / / / /__  _________ ",
        @" / /| | \__ \/ /    / / / /   / /_/ / _ \/ ___/ __ \",
        @"/ ___ |___/ / /____/ /_/ /   / __  /  __/ /  / /_/ /",
        @"/_/  |_/____/\____/___/___/  /_/ /_/\___/_/   \____/ ",
    ];
    private static readonly int LogoWidth = LogoLines.Max(line => line.Length);
    private const int LogoHeight = 5;
    private const int LogoGapRows = 1;

    /// <summary>
    /// Picks 5, 3, or 1 visible slots - whichever largest odd count both fits the viewport width
    /// and doesn't exceed how many worlds actually exist (so a short catalog doesn't reserve
    /// slots nothing will ever occupy).
    /// </summary>
    public static int ComputeVisibleSlotCount(double viewportWidthCells, int worldCount)
    {
        var maxByWorldCount = worldCount <= 0 ? 1 : (worldCount % 2 == 1 ? worldCount : worldCount - 1);

        foreach (var candidate in new[] { 5, 3, 1 })
        {
            if (candidate * SlotPitch <= viewportWidthCells)
            {
                return Math.Min(candidate, maxByWorldCount);
            }
        }

        return Math.Min(1, maxByWorldCount);
    }

    public static List<Glyph> BuildFrame(
        WorldSelectScreen screen, double viewportWidthCells, double viewportHeightCells,
        double cellWidthPixels, double cellHeightPixels)
    {
        var glyphs = new List<Glyph>();

        var totalRowWidth = screen.VisibleSlotCount * SlotPitch;
        var startCol = (viewportWidthCells - totalRowWidth) / 2;
        var startRow = (viewportHeightCells - BlockHeight) / 2;

        var logoLabel = new UILabel((viewportWidthCells - LogoWidth) / 2, startRow - LogoGapRows - LogoHeight, width: LogoWidth, height: LogoHeight, foreColor: WorldSelectForeColor);
        logoLabel.Lines.AddRange(LogoLines);
        UIRenderer.AddLabel(glyphs, logoLabel, cellWidthPixels, cellHeightPixels);

        for (var slot = 0; slot < screen.VisibleSlotCount; slot++)
        {
            var worldIndex = screen.ScrollOffset + slot;
            if (worldIndex < 0 || worldIndex >= screen.Worlds.Count)
            {
                continue;
            }

            var world = screen.Worlds[worldIndex];
            var slotOriginCol = startCol + slot * SlotPitch;
            var thumbCol = slotOriginCol + SlotHorizontalPadding;
            var titleRow = startRow;
            var thumbRow = startRow + TitleRowCount + SlotVerticalPadding;

            AddTitle(glyphs, world, thumbCol, titleRow, cellWidthPixels, cellHeightPixels);

            if (worldIndex == screen.SelectedIndex)
            {
                AddSelectionBox(glyphs, slotOriginCol, thumbRow, cellWidthPixels, cellHeightPixels);
            }

            AddThumbnail(glyphs, world, thumbCol, thumbRow, cellWidthPixels, cellHeightPixels);
        }

        return glyphs;
    }

    /// <summary>
    /// Builds a fresh <see cref="UIBar"/> for the "Loading [World]" readout shown under the
    /// thumbnail row while the selected world's <see cref="World.World2D"/> is being loaded (see
    /// <c>GameLoop</c>'s LoadingWorld <c>GameMode</c>) - centered the same way the
    /// thumbnail row itself is, so it lines up regardless of viewport width/visible slot count.
    /// The caller owns the returned instance and updates its <see cref="UIBar.CurrentValue"/> as
    /// loading progresses; this method only computes the (static) layout.
    /// </summary>
    public static UIBar CreateLoadingBar(WorldSelectScreen screen, double viewportWidthCells, double viewportHeightCells, double stepCount)
    {
        var totalRowWidth = screen.VisibleSlotCount * SlotPitch;
        var startRow = (viewportHeightCells - BlockHeight) / 2;

        var col = (viewportWidthCells - LoadingBarWidth) / 2;
        var row = startRow + BlockHeight + LoadingBarGapRows;

        return new UIBar(col, row, LoadingBarWidth, height: 1, minValue: 0, maxValue: stepCount, foreColor: WorldSelectForeColor)
        {
            CurrentValue = 0,
        };
    }

    /// <summary>
    /// Draws the world-selection screen's static layout (thumbnails/titles/selection box, same
    /// as <see cref="BuildFrame"/>) plus the given loading progress bar and its "Loading..."
    /// label underneath - used while <c>GameLoop</c> is in its LoadingWorld <c>GameMode</c> so
    /// the last-confirmed selection stays visible and the bar can visibly fill in, instead of
    /// freezing the last drawn frame.
    /// </summary>
    public static List<Glyph> BuildLoadingFrame(
        WorldSelectScreen screen, UIBar loadingBar, double viewportWidthCells, double viewportHeightCells,
        double cellWidthPixels, double cellHeightPixels)
    {
        var glyphs = BuildFrame(screen, viewportWidthCells, viewportHeightCells, cellWidthPixels, cellHeightPixels);

        var labelCol = loadingBar.Col + (loadingBar.Width - LoadingLabelText.Length) / 2.0;
        var labelRow = loadingBar.Row - 1;
        var label = new UILabel(labelCol, labelRow, width: LoadingLabelText.Length, height: 1, foreColor: WorldSelectForeColor);
        label.Lines.Add(LoadingLabelText);
        UIRenderer.AddLabel(glyphs, label, cellWidthPixels, cellHeightPixels);

        UIRenderer.AddBar(glyphs, loadingBar, cellWidthPixels, cellHeightPixels);

        return glyphs;
    }

    private static void AddTitle(
        List<Glyph> glyphs, WorldSummary world, double thumbCol, double titleRow,
        double cellWidthPixels, double cellHeightPixels)
    {
        var title = world.Title.Length > WorldCatalog.ThumbnailWidth
            ? world.Title[..WorldCatalog.ThumbnailWidth]
            : world.Title;
        var titleStartCol = thumbCol + (WorldCatalog.ThumbnailWidth - title.Length) / 2.0;

        var label = new UILabel(titleStartCol, titleRow, width: title.Length, height: 1, foreColor: WorldSelectForeColor);
        label.Lines.Add(title);
        UIRenderer.AddLabel(glyphs, label, cellWidthPixels, cellHeightPixels);
    }

    private static void AddSelectionBox(
        List<Glyph> glyphs, double slotOriginCol, double thumbRow,
        double cellWidthPixels, double cellHeightPixels)
    {
        var top = thumbRow - SlotVerticalPadding;
        var frame = new UIFrame(slotOriginCol, top, SlotPitch, WorldCatalog.ThumbnailHeight + SlotVerticalPadding + 1, foreColor: WorldSelectForeColor);
        UIRenderer.AddFrame(glyphs, frame, cellWidthPixels, cellHeightPixels);
    }

    private static void AddThumbnail(
        List<Glyph> glyphs, WorldSummary world, double thumbCol, double thumbRow,
        double cellWidthPixels, double cellHeightPixels)
    {
        var frame = world.CurrentThumbnailFrame;

        for (var row = 0; row < WorldCatalog.ThumbnailHeight; row++)
        {
            for (var col = 0; col < WorldCatalog.ThumbnailWidth; col++)
            {
                var character = frame.Chars[row, col];
                if (character == world.EmptyChar)
                {
                    continue;
                }

                var foreColor = GlyphBuilder.ResolveColor(world.Palette, GlyphBuilder.DefaultForeColor, GlyphBuilder.NullIfEmpty(frame.Fore[row, col], world.EmptyChar));
                var backColor = GlyphBuilder.ResolveColor(world.Palette, GlyphBuilder.DefaultBackColor, GlyphBuilder.NullIfEmpty(frame.Back[row, col], world.EmptyChar), world.DefaultBackColor);
                glyphs.Add(GlyphBuilder.BuildGlyph(
                    (thumbCol + col) * cellWidthPixels, (thumbRow + row) * cellHeightPixels, character, foreColor, backColor));
            }
        }
    }
}
