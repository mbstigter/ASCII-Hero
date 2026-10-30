namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// Repeats a tileable sprite frame's grids along its declared <see cref="TileAxis"/> to build up
/// an arbitrary-length platform/wall/beam from one small authored unit (any number of cells
/// long). A <see cref="TileAxis.Horizontal"/> unit is repeated column-wise; a
/// <see cref="TileAxis.Vertical"/> unit is repeated row-wise. The result is exactly the requested
/// span in cells, so a span that isn't a whole multiple of the unit ends with a partial unit.
/// </summary>
public static class SpriteFrameTiler
{
    /// <summary>
    /// Returns a new frame whose length along <paramref name="axis"/> is exactly
    /// <paramref name="span"/> cells, filled by repeating the source frame's unit (cell
    /// <c>i</c> copies unit cell <c>i % unitLength</c>). Returns the original frame unchanged when
    /// the axis is <see cref="TileAxis.None"/> or span is 0 or less (no span set).
    /// </summary>
    public static SpriteFrame Tile(SpriteFrame frame, TileAxis axis, int span)
    {
        if (axis == TileAxis.None || span <= 0)
        {
            return frame;
        }

        return axis switch
        {
            TileAxis.Horizontal => TileHorizontal(frame, span),
            TileAxis.Vertical => TileVertical(frame, span),
            _ => frame,
        };
    }

    private static SpriteFrame TileHorizontal(SpriteFrame frame, int width)
    {
        var unitWidth = frame.Width;
        var height = frame.Height;

        var chars = new char[height, width];
        var fore = new char[height, width];
        var back = new char[height, width];
        var materials = new string?[height, width];

        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                var unitCol = col % unitWidth;
                chars[row, col] = frame.Chars[row, unitCol];
                fore[row, col] = frame.Fore[row, unitCol];
                back[row, col] = frame.Back[row, unitCol];
                materials[row, col] = frame.Materials[row, unitCol];
            }
        }

        return new SpriteFrame { Chars = chars, Fore = fore, Back = back, Materials = materials };
    }

    private static SpriteFrame TileVertical(SpriteFrame frame, int height)
    {
        var unitHeight = frame.Height;
        var width = frame.Width;

        var chars = new char[height, width];
        var fore = new char[height, width];
        var back = new char[height, width];
        var materials = new string?[height, width];

        for (var row = 0; row < height; row++)
        {
            var unitRow = row % unitHeight;
            for (var col = 0; col < width; col++)
            {
                chars[row, col] = frame.Chars[unitRow, col];
                fore[row, col] = frame.Fore[unitRow, col];
                back[row, col] = frame.Back[unitRow, col];
                materials[row, col] = frame.Materials[unitRow, col];
            }
        }

        return new SpriteFrame { Chars = chars, Fore = fore, Back = back, Materials = materials };
    }
}
