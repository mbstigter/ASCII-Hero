using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Menu;

namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// Builds the glyph list for the world-selection screen: logo, a row of world thumbnails with
/// titles, a selector box around the current selection, scroll arrows and a controls hint.
/// Lays out directly in viewport cell coordinates (no camera). See docs/AssetFormat.md §3.2.
/// </summary>
public static class WorldSelectRenderer
{
    // ---------------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------------

    // One cell of padding on each side of a slot's thumbnail; holds the selector box border.
    private const int SlotHorizontalPadding = 1;
    internal const int SlotPitch = WorldCatalog.ThumbnailWidth + SlotHorizontalPadding * 2;

    private const int TitleRowCount = 1;
    private const int SlotVerticalPadding = 1;
    internal const int BlockHeight = TitleRowCount + SlotVerticalPadding + WorldCatalog.ThumbnailHeight + SlotVerticalPadding;

    // A selected title too long for its slot slides back and forth, pausing equally at each end.
    private const double TitleScrollCharsPerSecond = 4.0;
    private const double TitleScrollPauseSeconds = 1.0;

    // ---------------------------------------------------------------------------------------
    // Logo and controls hint
    // ---------------------------------------------------------------------------------------

    private static readonly string[] LogoLines =
    [
        @"    ___   _____ ______________   __  __              ",
        @"   /   | / ___// ____/  _/  _/  / / / /__  _________ ",
        @"  / /| | \__ \/ /    / / / /   / /_/ / _ \/ ___/ __ \",
        @" / ___ |___/ / /____/ /_/ /   / __  /  __/ /  / /_/ /",
        @"/_/  |_/____/\____/___/___/  /_/ /_/\___/_/   \____/ ",
    ];
    private static readonly int LogoWidth = LogoLines.Max(line => line.Length);
    private const int LogoHeight = 5;
    private const int LogoGapRows = 1;

    private static readonly string[] ControlsLines =
    [
        "           ↑                        W",
        "Controls: ←↓→ + Space + Enter  or: ASD + Left Control + E",
    ];

    // ---------------------------------------------------------------------------------------
    // Scroll arrows
    // ---------------------------------------------------------------------------------------

    // Chevrons drawn outside the outermost visible thumbnail when more worlds lie in that direction.
    private static readonly string[] LeftScrollArrow =
    [
        " / / ",
        "/ /  ",
        "\\ \\  ",
        " \\ \\ ",
    ];
    private static readonly string[] RightScrollArrow =
    [
        " \\ \\ ",
        "  \\ \\",
        "  / /",
        " / / ",
    ];
    private static readonly int ScrollArrowWidth = LeftScrollArrow.Max(line => line.Length);
    private static readonly int ScrollArrowHeight = LeftScrollArrow.Length;
    private const int ScrollArrowGap = 1;

    // ---------------------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Picks 5, 3 or 1 visible slots: the largest odd count that fits the viewport width and
    /// does not exceed the number of worlds.
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

        AddLogo(glyphs, viewportWidthCells, startRow, cellWidthPixels, cellHeightPixels);
        AddControls(glyphs, viewportWidthCells, startRow + BlockHeight, cellWidthPixels, cellHeightPixels);

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

            var titleScrollSeconds = worldIndex == screen.SelectedIndex ? screen.TitleScrollSeconds : (double?)null;
            AddTitle(glyphs, world, titleScrollSeconds, thumbCol, titleRow, cellWidthPixels, cellHeightPixels);

            if (worldIndex == screen.SelectedIndex)
            {
                AddSelectionBox(glyphs, slotOriginCol, thumbRow, cellWidthPixels, cellHeightPixels);
            }

            AddThumbnail(glyphs, world, thumbCol, thumbRow, cellWidthPixels, cellHeightPixels);

            if (slot == 0 && screen.ScrollOffset > 0)
            {
                AddScrollArrow(glyphs, LeftScrollArrow, thumbCol - ScrollArrowGap - ScrollArrowWidth, thumbRow, cellWidthPixels, cellHeightPixels);
            }

            if (slot == screen.VisibleSlotCount - 1 && screen.ScrollOffset + screen.VisibleSlotCount < screen.Worlds.Count)
            {
                AddScrollArrow(glyphs, RightScrollArrow, thumbCol + WorldCatalog.ThumbnailWidth + ScrollArrowGap, thumbRow, cellWidthPixels, cellHeightPixels);
            }
        }

        return glyphs;
    }

    // ---------------------------------------------------------------------------------------
    // Drawing helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>Adds the logo, centered horizontally and placed above the thumbnail block.</summary>
    private static void AddLogo(
        List<Glyph> glyphs, double viewportWidthCells, double blockStartRow,
        double cellWidthPixels, double cellHeightPixels)
    {
        var label = new UILabel((viewportWidthCells - LogoWidth) / 2, blockStartRow - LogoGapRows - LogoHeight, width: LogoWidth, height: LogoHeight, foreColor: RenderConstants.DefaultForeColor);
        label.Lines.AddRange(LogoLines);
        UIRenderer.AddLabel(glyphs, label, cellWidthPixels, cellHeightPixels);
    }

    /// <summary>Adds the controls hint, centered horizontally at the given row.</summary>
    private static void AddControls(
        List<Glyph> glyphs, double viewportWidthCells, double row,
        double cellWidthPixels, double cellHeightPixels)
    {
        var width = ControlsLines.Max(line => line.Length);
        var label = new UILabel((viewportWidthCells - width) / 2, row, width: width, height: ControlsLines.Length, foreColor: RenderConstants.DefaultForeColor);
        label.Lines.AddRange(ControlsLines);
        UIRenderer.AddLabel(glyphs, label, cellWidthPixels, cellHeightPixels);
    }

    /// <summary>Adds a scroll chevron outside a thumbnail's edge, vertically centered on it.</summary>
    private static void AddScrollArrow(
        List<Glyph> glyphs, string[] arrowLines, double arrowCol, double thumbRow,
        double cellWidthPixels, double cellHeightPixels)
    {
        var arrowRow = thumbRow + (WorldCatalog.ThumbnailHeight - ScrollArrowHeight) / 2.0;

        for (var row = 0; row < arrowLines.Length; row++)
        {
            var line = arrowLines[row];
            for (var col = 0; col < line.Length; col++)
            {
                var character = line[col];
                if (character == ' ')
                {
                    continue;
                }

                glyphs.Add(GlyphBuilder.BuildGlyph(
                    (arrowCol + col) * cellWidthPixels, (arrowRow + row) * cellHeightPixels, character, RenderConstants.DefaultForeColor, null));
            }
        }
    }

    /// <summary>
    /// Adds a world's title centered above its thumbnail. A title wider than the thumbnail is
    /// truncated, unless <paramref name="scrollSeconds"/> is given (the selected world), in which
    /// case a thumbnail-wide window slides back and forth along it.
    /// </summary>
    private static void AddTitle(
        List<Glyph> glyphs, WorldSummary world, double? scrollSeconds, double thumbCol, double titleRow,
        double cellWidthPixels, double cellHeightPixels)
    {
        var overflow = world.Title.Length - WorldCatalog.ThumbnailWidth;
        var offset = overflow > 0 && scrollSeconds is { } seconds ? GetTitleScrollOffset(overflow, seconds) : 0;
        var title = overflow > 0
            ? world.Title.Substring(offset, WorldCatalog.ThumbnailWidth)
            : world.Title;
        var titleStartCol = thumbCol + (WorldCatalog.ThumbnailWidth - title.Length) / 2.0;

        var label = new UILabel(titleStartCol, titleRow, width: title.Length, height: 1, foreColor: RenderConstants.DefaultForeColor);
        label.Lines.Add(title);
        UIRenderer.AddLabel(glyphs, label, cellWidthPixels, cellHeightPixels);
    }

    /// <summary>
    /// Character offset into an over-long title at the given time: pause at the start, slide to the
    /// end, pause there for the same time, slide back, repeat. Whole characters only, so the text
    /// stays on the cell grid.
    /// </summary>
    private static int GetTitleScrollOffset(int overflow, double seconds)
    {
        var slideSeconds = overflow / TitleScrollCharsPerSecond;
        var cycleSeconds = 2 * (TitleScrollPauseSeconds + slideSeconds);
        var t = seconds % cycleSeconds;

        double position;
        if (t < TitleScrollPauseSeconds)
        {
            position = 0;
        }
        else if ((t -= TitleScrollPauseSeconds) < slideSeconds)
        {
            position = t * TitleScrollCharsPerSecond;
        }
        else if ((t -= slideSeconds) < TitleScrollPauseSeconds)
        {
            position = overflow;
        }
        else
        {
            position = overflow - (t - TitleScrollPauseSeconds) * TitleScrollCharsPerSecond;
        }

        return Math.Clamp((int)Math.Round(position), 0, overflow);
    }

    /// <summary>Adds the selector box around a slot's thumbnail.</summary>
    private static void AddSelectionBox(
        List<Glyph> glyphs, double slotOriginCol, double thumbRow,
        double cellWidthPixels, double cellHeightPixels)
    {
        var top = thumbRow - SlotVerticalPadding;
        var frame = new UIFrame(slotOriginCol, top, SlotPitch, WorldCatalog.ThumbnailHeight + SlotVerticalPadding + 1, foreColor: RenderConstants.DefaultForeColor);
        UIRenderer.AddFrame(glyphs, frame, cellWidthPixels, cellHeightPixels);
    }

    /// <summary>Adds a world's current thumbnail animation frame, skipping empty cells.</summary>
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

                var foreColor = GlyphBuilder.ResolveColor(world.Palette, RenderConstants.DefaultForeColor, GlyphBuilder.NullIfEmpty(frame.Fore[row, col], world.EmptyChar));
                var backColor = GlyphBuilder.ResolveColor(world.Palette, RenderConstants.DefaultBackColor, GlyphBuilder.NullIfEmpty(frame.Back[row, col], world.EmptyChar), world.DefaultBackColor);
                glyphs.Add(GlyphBuilder.BuildGlyph(
                    (thumbCol + col) * cellWidthPixels, (thumbRow + row) * cellHeightPixels, character, foreColor, backColor));
            }
        }
    }
}
