using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// Shared glyph-construction helpers used by both <see cref="WorldRenderer"/> (in-game world,
/// camera-relative) and <see cref="WorldSelectRenderer"/> (world-selection screen, screen-relative):
/// resolving a cell's color against a <see cref="ColorPalette"/> through a precedence chain, and
/// turning a cell position/character/colors into a pixel-positioned <see cref="Glyph"/>.
/// </summary>
public static class GlyphBuilder
{
    /// <summary>
    /// Default foreground color used by any renderer (<see cref="WorldRenderer"/>,
    /// <see cref="WorldSelectRenderer"/>, <see cref="UIRenderer"/>) when nothing more specific is
    /// resolved (no palette match, or no explicit color set) - the single app-wide place this hex
    /// value is defined, so it isn't hardcoded independently in each renderer.
    /// </summary>
    public const string DefaultForeColor = "#00ff00";

    /// <summary>
    /// Default background color used by any renderer when nothing more specific is resolved -
    /// null means no fill (fully transparent, letting the canvas show through). The single
    /// app-wide place this default is defined, so it isn't hardcoded independently in each renderer.
    /// </summary>
    public const string? DefaultBackColor = null;

    /// <summary>
    /// Resolves a cell's color by trying each code in <paramref name="codesInPrecedenceOrder"/> in
    /// order, returning the first one that's non-null and resolves in <paramref name="palette"/>,
    /// or <paramref name="hardcodedFallback"/> if none do. A caller with a per-cell color code that
    /// might be its grid's "no code here" marker should pass <c>null</c> for that slot itself
    /// (rather than the raw code) so it's skipped the same as any other absent code - see
    /// docs/AssetFormat.md §2.5/§4 for the format/precedence this implements for world rendering.
    /// </summary>
    public static string? ResolveColor(ColorPalette palette, string? hardcodedFallback, params ReadOnlySpan<char?> codesInPrecedenceOrder)
    {
        foreach (var code in codesInPrecedenceOrder)
        {
            if (code is { } value && palette.TryGetColor(value) is { } color)
            {
                return color;
            }
        }

        return hardcodedFallback;
    }

    /// <summary>Builds a glyph at an already-resolved pixel position, falling back to <see cref="DefaultForeColor"/>/<see cref="DefaultBackColor"/> if <paramref name="foreColor"/>/<paramref name="backColor"/> are null.</summary>
    public static Glyph BuildGlyph(double pixelX, double pixelY, char character, string? foreColor, string? backColor) =>
        new(pixelX, pixelY, character, foreColor ?? DefaultForeColor, backColor ?? DefaultBackColor);

    /// <summary>
    /// Returns <paramref name="code"/> unless it equals the grid's "no code here" marker
    /// (<paramref name="emptyChar"/>), in which case null - so an absent per-cell code is skipped
    /// by <see cref="ResolveColor"/>'s precedence chain the same as any other unset code.
    /// </summary>
    public static char? NullIfEmpty(char code, char emptyChar) => code != emptyChar ? code : null;

    /// <summary>
    /// Appends the glyphs for a single-line box-drawing border spanning the given cell rect
    /// (inclusive on all sides), in screen/panel cell coordinates. Shared by
    /// <see cref="WorldSelectRenderer"/>'s selector box and <see cref="UIRenderer"/>'s
    /// <see cref="UIFrame"/> frames, since both draw the identical box shape, just with different
    /// colors/placement.
    /// </summary>
    public static void AddBox(
        List<Glyph> glyphs, double left, double top, double right, double bottom,
        string? foreColor, string? backColor,
        double cellWidthPixels, double cellHeightPixels)
    {
        void Add(double col, double row, char character) =>
            glyphs.Add(BuildGlyph(col * cellWidthPixels, row * cellHeightPixels, character, foreColor, backColor));

        Add(left, top, '┌');
        Add(right, top, '┐');
        Add(left, bottom, '└');
        Add(right, bottom, '┘');

        for (var col = left + 1; col < right; col++)
        {
            Add(col, top, '─');
            Add(col, bottom, '─');
        }

        for (var row = top + 1; row < bottom; row++)
        {
            Add(left, row, '│');
            Add(right, row, '│');
        }
    }
}
