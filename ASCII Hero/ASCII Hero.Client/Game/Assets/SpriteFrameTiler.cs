namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// Repeats a tileable sprite frame's grids along its declared <see cref="TileAxis"/> to build up
/// an arbitrary-length platform/wall from one small authored unit. A <see cref="TileAxis.Horizontal"/>
/// unit (authored one cell wide) is repeated column-wise; a <see cref="TileAxis.Vertical"/> unit
/// (authored one cell tall) is repeated row-wise. See docs/AssetFormat.md for the format rationale.
/// </summary>
public static class SpriteFrameTiler
{
    /// <summary>
    /// Returns a new frame with the source frame's unit repeated <paramref name="count"/> times
    /// along <paramref name="axis"/>. Returns the original frame unchanged when the axis is
    /// <see cref="TileAxis.None"/> or count is 1 or less.
    /// </summary>
    public static SpriteFrame Tile(SpriteFrame frame, TileAxis axis, int count)
    {
        if (axis == TileAxis.None || count <= 1)
        {
            return frame;
        }

        return axis switch
        {
            TileAxis.Horizontal => TileHorizontal(frame, count),
            TileAxis.Vertical => TileVertical(frame, count),
            _ => frame,
        };
    }

    private static SpriteFrame TileHorizontal(SpriteFrame frame, int count)
    {
        var unitWidth = frame.Width;
        var height = frame.Height;
        var width = unitWidth * count;

        var chars = new char[height, width];
        var fore = new char[height, width];
        var back = new char[height, width];
        var materials = new string?[height, width];

        for (var row = 0; row < height; row++)
        {
            for (var repeat = 0; repeat < count; repeat++)
            {
                var colOffset = repeat * unitWidth;
                for (var col = 0; col < unitWidth; col++)
                {
                    chars[row, colOffset + col] = frame.Chars[row, col];
                    fore[row, colOffset + col] = frame.Fore[row, col];
                    back[row, colOffset + col] = frame.Back[row, col];
                    materials[row, colOffset + col] = frame.Materials[row, col];
                }
            }
        }

        return new SpriteFrame { Chars = chars, Fore = fore, Back = back, Materials = materials };
    }

    private static SpriteFrame TileVertical(SpriteFrame frame, int count)
    {
        var unitHeight = frame.Height;
        var width = frame.Width;
        var height = unitHeight * count;

        var chars = new char[height, width];
        var fore = new char[height, width];
        var back = new char[height, width];
        var materials = new string?[height, width];

        for (var repeat = 0; repeat < count; repeat++)
        {
            var rowOffset = repeat * unitHeight;
            for (var row = 0; row < unitHeight; row++)
            {
                for (var col = 0; col < width; col++)
                {
                    chars[rowOffset + row, col] = frame.Chars[row, col];
                    fore[rowOffset + row, col] = frame.Fore[row, col];
                    back[rowOffset + row, col] = frame.Back[row, col];
                    materials[rowOffset + row, col] = frame.Materials[row, col];
                }
            }
        }

        return new SpriteFrame { Chars = chars, Fore = fore, Back = back, Materials = materials };
    }
}
