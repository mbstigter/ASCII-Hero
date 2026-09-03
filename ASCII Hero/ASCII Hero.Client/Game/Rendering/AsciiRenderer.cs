using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Camera;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// Translates the floating-point game world into a list of ASCII glyphs positioned in pixel
/// space, based on the camera's current view. The world itself is not restricted to a grid;
/// this mapping is purely a visual concept applied at render time.
/// </summary>
public class AsciiRenderer
{
    /// <summary>Default foreground color used when a cell has no color code resolvable in the palette.</summary>
    private const string DefaultForeColor = "#00ff00";

    /// <summary>Default background color used when a cell has no color code resolvable in the palette
    /// (null means no fill - fully transparent, letting the canvas show through).</summary>
    private const string? DefaultBackColor = null;

    /// <summary>Size of one world cell in pixels.</summary>
    public double CellWidthPixels { get; set; } = 16;
    public double CellHeightPixels { get; set; } = 24;

    public List<Glyph> BuildFrame(World2D world, Camera2D camera, double viewportWidthCells, double viewportHeightCells)
    {
        var glyphs = new List<Glyph>();

        AddBackgroundGlyphs(glyphs, world, camera, viewportWidthCells, viewportHeightCells);

        var viewLeft = camera.Position.X;
        var viewTop = camera.Position.Y;
        var viewRight = camera.Position.X + viewportWidthCells;
        var viewBottom = camera.Position.Y + viewportHeightCells;

        foreach (var body in world.Objects)
        {
            // Skip any game object whose bounding box doesn't intersect the visible viewport at
            // all, before touching its (possibly much larger) sprite frame grid - avoids doing
            // per-cell work for objects that are nowhere near the camera.
            if (body.Position.X + body.Size.X <= viewLeft || body.Position.X >= viewRight ||
                body.Position.Y + body.Size.Y <= viewTop || body.Position.Y >= viewBottom)
            {
                continue;
            }

            AddGameObjectGlyphs(glyphs, body, world, camera);
        }

        return glyphs;
    }

    private void AddBackgroundGlyphs(List<Glyph> glyphs, World2D world, Camera2D camera, double viewportWidthCells, double viewportHeightCells)
    {
        var chars = world.BackgroundChars;
        var height = chars.GetLength(0);
        var width = chars.GetLength(1);

        // Only the rows/columns actually visible through the camera's current viewport need to
        // become glyphs; a world larger than the viewport would otherwise have every off-screen
        // cell built (and later clipped by the canvas) every single frame regardless.
        var startRow = Math.Max(0, (int)Math.Floor(camera.Position.Y));
        var endRow = Math.Min(height, (int)Math.Ceiling(camera.Position.Y + viewportHeightCells));
        var startCol = Math.Max(0, (int)Math.Floor(camera.Position.X));
        var endCol = Math.Min(width, (int)Math.Ceiling(camera.Position.X + viewportWidthCells));

        for (var row = startRow; row < endRow; row++)
        {
            for (var col = startCol; col < endCol; col++)
            {
                var character = chars[row, col];
                if (character == world.EmptyChar)
                {
                    continue;
                }

                var foreColor = ResolveColor(world.Palette, world.BackgroundFore[row, col], world.EmptyChar, world.DefaultForeColor, DefaultForeColor);
                var backColor = ResolveColor(world.Palette, world.BackgroundBack[row, col], world.EmptyChar, world.DefaultBackColor, DefaultBackColor);
                var cellPosition = new Vector2D(col, row);
                glyphs.Add(ToGlyph(cellPosition, character, foreColor, backColor, camera));
            }
        }
    }

    private void AddGameObjectGlyphs(List<Glyph> glyphs, Body2D gameObject, World2D world, Camera2D camera)
    {
        var frame = gameObject.Frame;
        var emptyChar = gameObject.Sprite.EmptyChar;
        var palette = world.Palette;

        for (var row = 0; row < frame.Height; row++)
        {
            for (var col = 0; col < frame.Width; col++)
            {
                var character = frame.Chars[row, col];
                if (character == emptyChar)
                {
                    continue;
                }

                var cellPosition = new Vector2D(gameObject.Position.X + col, gameObject.Position.Y + row);

                // A sprite's anchor (its Position) doesn't have to be its top-left-most solid
                // cell, so a sprite placed near an edge can otherwise have cells that fall
                // outside the world's cell grid entirely; skip those rather than draw them.
                if (cellPosition.X < 0 || cellPosition.X >= world.WidthCells ||
                    cellPosition.Y < 0 || cellPosition.Y >= world.HeightCells)
                {
                    continue;
                }

                var foreColor = ResolveColor(palette, frame.Fore[row, col], emptyChar, gameObject.ForeColorOverride, gameObject.Sprite.DefaultForeColor, world.DefaultForeColor, DefaultForeColor);
                var backColor = ResolveColor(palette, frame.Back[row, col], emptyChar, gameObject.BackColorOverride, gameObject.Sprite.DefaultBackColor, world.DefaultBackColor, DefaultBackColor);
                glyphs.Add(ToGlyph(cellPosition, character, foreColor, backColor, camera));
            }
        }
    }

    /// <summary>
    /// Resolves a cell's color, in precedence order: the cell's own color code (if not
    /// <paramref name="emptyChar"/> and resolvable in the palette), then this placement's own
    /// per-instance override (<paramref name="instanceOverrideCode"/>, e.g. one placement of a
    /// shared sprite asset picking a distinct color from another placement of that same asset),
    /// then the sprite's own <c>DefaultForegroundColor</c>/<c>DefaultBackgroundColor</c>
    /// (<paramref name="assetDefaultCode"/>, for background cells there is no owning sprite so
    /// this is always null), then the level's own default (<paramref name="levelDefaultCode"/>),
    /// then <paramref name="hardcodedFallback"/>. Each color-code step is itself skipped if that
    /// code doesn't resolve in the palette, falling through to the next step - see
    /// docs/AssetFormat.md §2.5/§4 for the format/precedence.
    /// </summary>
    private static string? ResolveColor(
        ColorPalette palette, char cellCode, char emptyChar, char? instanceOverrideCode, char? assetDefaultCode, char? levelDefaultCode, string? hardcodedFallback)
    {
        if (cellCode != emptyChar && palette.TryGetColor(cellCode) is { } cellColor)
        {
            return cellColor;
        }

        if (instanceOverrideCode is { } instanceCode && palette.TryGetColor(instanceCode) is { } instanceColor)
        {
            return instanceColor;
        }

        if (assetDefaultCode is { } foreCode && palette.TryGetColor(foreCode) is { } assetColor)
        {
            return assetColor;
        }

        if (levelDefaultCode is { } levelCode && palette.TryGetColor(levelCode) is { } levelColor)
        {
            return levelColor;
        }

        return hardcodedFallback;
    }

    private static string? ResolveColor(
        ColorPalette palette, char cellCode, char emptyChar, char? levelDefaultCode, string? hardcodedFallback) =>
        ResolveColor(palette, cellCode, emptyChar, instanceOverrideCode: null, assetDefaultCode: null, levelDefaultCode, hardcodedFallback);

    private Glyph ToGlyph(Vector2D worldPosition, char character, string? foreColor, string? backColor, Camera2D camera)
    {
        var relative = worldPosition - camera.Position;
        var pixelX = relative.X * CellWidthPixels;
        var pixelY = relative.Y * CellHeightPixels;
        return new Glyph(pixelX, pixelY, character, foreColor ?? DefaultForeColor, backColor);
    }
}
