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
    /// <summary>Size of one world cell in pixels.</summary>
    public double CellWidthPixels { get; set; } = 16;
    public double CellHeightPixels { get; set; } = 24;

    public List<Glyph> BuildFrame(World2D world, Camera2D camera)
    {
        var glyphs = new List<Glyph>();

        foreach (var platform in world.Platforms)
        {
            AddRectangleOfGlyphs(glyphs, platform.Position, platform.Size, StaticObject2D.Glyph, camera);
        }

        var player = world.Player;
        glyphs.Add(ToGlyph(player.Position, Player2D.Glyph, camera));

        return glyphs;
    }

    private void AddRectangleOfGlyphs(List<Glyph> glyphs, Vector2D position, Vector2D size, char character, Camera2D camera)
    {
        var columns = (int)Math.Round(size.X);
        var rows = (int)Math.Round(size.Y);

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < columns; col++)
            {
                var cellPosition = new Vector2D(position.X + col, position.Y + row);
                glyphs.Add(ToGlyph(cellPosition, character, camera));
            }
        }
    }

    private Glyph ToGlyph(Vector2D worldPosition, char character, Camera2D camera)
    {
        var relative = worldPosition - camera.Position;
        var pixelX = relative.X * CellWidthPixels;
        var pixelY = relative.Y * CellHeightPixels;
        return new Glyph(pixelX, pixelY, character);
    }
}
