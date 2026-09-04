using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Menu;

namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// Builds the glyph list for the level-selection screen: a horizontal row of level thumbnails,
/// each with its title above it, and a static selector box around whichever slot the current
/// selection lands in. Unlike <see cref="AsciiRenderer"/> this has no camera - it lays out
/// directly in screen (viewport) cell coordinates, since the selection screen isn't part of any
/// <see cref="World.World2D"/>. See docs/AssetFormat.md §3.2.
/// </summary>
public static class LevelSelectRenderer
{
    private const string TitleColor = "#00ff00";
    private const string SelectionBoxColor = "#ffffff";

    // One cell of breathing room to either side of a slot's 16-wide thumbnail, used to draw the
    // selector box's left/right border without needing extra space reserved between slots.
    private const int SlotHorizontalPadding = 1;
    private const int SlotPitch = LevelCatalog.ThumbnailWidth + SlotHorizontalPadding * 2;

    private const int TitleRowCount = 1;
    private const int SlotVerticalPadding = 1;
    private const int BlockHeight = TitleRowCount + SlotVerticalPadding + LevelCatalog.ThumbnailHeight + SlotVerticalPadding;

    /// <summary>
    /// Picks 5, 3, or 1 visible slots - whichever largest odd count both fits the viewport width
    /// and doesn't exceed how many levels actually exist (so a short catalog doesn't reserve
    /// slots nothing will ever occupy).
    /// </summary>
    public static int ComputeVisibleSlotCount(double viewportWidthCells, int levelCount)
    {
        var maxByLevelCount = levelCount <= 0 ? 1 : (levelCount % 2 == 1 ? levelCount : levelCount - 1);

        foreach (var candidate in new[] { 5, 3, 1 })
        {
            if (candidate * SlotPitch <= viewportWidthCells)
            {
                return Math.Min(candidate, maxByLevelCount);
            }
        }

        return Math.Min(1, maxByLevelCount);
    }

    public static List<Glyph> BuildFrame(
        LevelSelectScreen screen, double viewportWidthCells, double viewportHeightCells,
        double cellWidthPixels, double cellHeightPixels)
    {
        var glyphs = new List<Glyph>();

        var totalRowWidth = screen.VisibleSlotCount * SlotPitch;
        var startCol = (viewportWidthCells - totalRowWidth) / 2;
        var startRow = (viewportHeightCells - BlockHeight) / 2;

        for (var slot = 0; slot < screen.VisibleSlotCount; slot++)
        {
            var levelIndex = screen.ScrollOffset + slot;
            if (levelIndex < 0 || levelIndex >= screen.Levels.Count)
            {
                continue;
            }

            var level = screen.Levels[levelIndex];
            var slotOriginCol = startCol + slot * SlotPitch;
            var thumbCol = slotOriginCol + SlotHorizontalPadding;
            var titleRow = startRow;
            var thumbRow = startRow + TitleRowCount + SlotVerticalPadding;

            AddTitle(glyphs, level, thumbCol, titleRow, cellWidthPixels, cellHeightPixels);
            AddThumbnail(glyphs, level, thumbCol, thumbRow, cellWidthPixels, cellHeightPixels);

            if (levelIndex == screen.SelectedIndex)
            {
                AddSelectionBox(glyphs, slotOriginCol, thumbRow, cellWidthPixels, cellHeightPixels);
            }
        }

        return glyphs;
    }

    private static void AddTitle(
        List<Glyph> glyphs, LevelSummary level, double thumbCol, double titleRow,
        double cellWidthPixels, double cellHeightPixels)
    {
        var title = level.Title.Length > LevelCatalog.ThumbnailWidth
            ? level.Title[..LevelCatalog.ThumbnailWidth]
            : level.Title;
        var titleStartCol = thumbCol + (LevelCatalog.ThumbnailWidth - title.Length) / 2.0;

        for (var i = 0; i < title.Length; i++)
        {
            AddGlyph(glyphs, titleStartCol + i, titleRow, title[i], TitleColor, null, cellWidthPixels, cellHeightPixels);
        }
    }

    private static void AddThumbnail(
        List<Glyph> glyphs, LevelSummary level, double thumbCol, double thumbRow,
        double cellWidthPixels, double cellHeightPixels)
    {
        var frame = level.CurrentThumbnailFrame;

        for (var row = 0; row < LevelCatalog.ThumbnailHeight; row++)
        {
            for (var col = 0; col < LevelCatalog.ThumbnailWidth; col++)
            {
                var character = frame.Chars[row, col];
                if (character == level.EmptyChar)
                {
                    continue;
                }

                var foreColor = ResolveColor(level.Palette, frame.Fore[row, col], level.EmptyChar) ?? TitleColor;
                AddGlyph(glyphs, thumbCol + col, thumbRow + row, character, foreColor, null, cellWidthPixels, cellHeightPixels);
            }
        }
    }

    private static void AddSelectionBox(
        List<Glyph> glyphs, double slotOriginCol, double thumbRow,
        double cellWidthPixels, double cellHeightPixels)
    {
        var left = slotOriginCol;
        var right = slotOriginCol + SlotPitch - 1;
        var top = thumbRow - SlotVerticalPadding;
        var bottom = thumbRow + LevelCatalog.ThumbnailHeight;

        AddGlyph(glyphs, left, top, '┌', SelectionBoxColor, null, cellWidthPixels, cellHeightPixels);
        AddGlyph(glyphs, right, top, '┐', SelectionBoxColor, null, cellWidthPixels, cellHeightPixels);
        AddGlyph(glyphs, left, bottom, '└', SelectionBoxColor, null, cellWidthPixels, cellHeightPixels);
        AddGlyph(glyphs, right, bottom, '┘', SelectionBoxColor, null, cellWidthPixels, cellHeightPixels);

        for (var col = left + 1; col < right; col++)
        {
            AddGlyph(glyphs, col, top, '─', SelectionBoxColor, null, cellWidthPixels, cellHeightPixels);
            AddGlyph(glyphs, col, bottom, '─', SelectionBoxColor, null, cellWidthPixels, cellHeightPixels);
        }

        for (var row = top + 1; row < bottom; row++)
        {
            AddGlyph(glyphs, left, row, '│', SelectionBoxColor, null, cellWidthPixels, cellHeightPixels);
            AddGlyph(glyphs, right, row, '│', SelectionBoxColor, null, cellWidthPixels, cellHeightPixels);
        }
    }

    private static string? ResolveColor(ColorPalette palette, char code, char emptyChar) =>
        code != emptyChar ? palette.TryGetColor(code) : null;

    private static void AddGlyph(
        List<Glyph> glyphs, double col, double row, char character, string foreColor, string? backColor,
        double cellWidthPixels, double cellHeightPixels)
    {
        glyphs.Add(new Glyph(col * cellWidthPixels, row * cellHeightPixels, character, foreColor, backColor));
    }
}
