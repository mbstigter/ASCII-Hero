using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Camera;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// Translates the floating-point game world into a list of ASCII glyphs positioned in pixel
/// space, based on the camera's current view. The world itself is not restricted to a grid;
/// this mapping is purely a visual concept applied at render time. Compare
/// <see cref="WorldSelectRenderer"/>, which lays out glyphs directly in screen space with no
/// camera, for the level-selection screen instead.
/// </summary>
public class WorldRenderer
{
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

                var cellForeCode = GlyphBuilder.NullIfEmpty(world.BackgroundFore[row, col], world.EmptyChar);
                var cellBackCode = GlyphBuilder.NullIfEmpty(world.BackgroundBack[row, col], world.EmptyChar);
                var foreColor = GlyphBuilder.ResolveColor(world.Palette, GlyphBuilder.DefaultForeColor, cellForeCode, world.DefaultForeColor);
                var backColor = GlyphBuilder.ResolveColor(world.Palette, GlyphBuilder.DefaultBackColor, cellBackCode, world.DefaultBackColor);
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

                var cellForeCode = GlyphBuilder.NullIfEmpty(frame.Fore[row, col], emptyChar);
                var cellBackCode = GlyphBuilder.NullIfEmpty(frame.Back[row, col], emptyChar);
                var foreColor = GlyphBuilder.ResolveColor(palette, GlyphBuilder.DefaultForeColor, cellForeCode, gameObject.ForeColorOverride, gameObject.Sprite.DefaultForeColor, world.DefaultForeColor);
                var backColor = GlyphBuilder.ResolveColor(palette, GlyphBuilder.DefaultBackColor, cellBackCode, gameObject.BackColorOverride, gameObject.Sprite.DefaultBackColor, world.DefaultBackColor);
                glyphs.Add(ToGlyph(cellPosition, character, foreColor, backColor, camera));
            }
        }
    }

    private Glyph ToGlyph(Vector2D worldPosition, char character, string? foreColor, string? backColor, Camera2D camera)
    {
        var relative = worldPosition - camera.Position;
        var pixelX = relative.X * CellWidthPixels;
        var pixelY = relative.Y * CellHeightPixels;
        return GlyphBuilder.BuildGlyph(pixelX, pixelY, character, foreColor, backColor);
    }
}
