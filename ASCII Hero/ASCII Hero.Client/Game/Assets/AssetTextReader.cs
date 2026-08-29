namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// Reads and parses the raw text content of asset layer files
/// (_characters/_foregroundcolors/_backgroundcolors/_materials.txt) into per-frame char grids,
/// applying the padding and "//end" frame-splitting rules from AssetFormat.md section 2.1. This
/// is the low-level grid parser only - it knows nothing about folders, Global/Level fallback, or
/// settings.ini; see <see cref="AssetPathResolver"/> and <see cref="SpriteLoader"/> for those.
/// </summary>
public static class AssetTextReader
{
    private const string FrameSeparator = "//end";

    /// <summary>
    /// Splits characters-layer content into frames and infers each frame's width/height from its own
    /// content (widest line, line count), padding shorter lines on the right with
    /// <paramref name="emptyChar"/>. This layer is authoritative for dimensions - other layers
    /// are padded to match via <see cref="ParseSecondaryLayer"/>.
    /// </summary>
    public static IReadOnlyList<char[,]> ParseCharsLayer(string content, char emptyChar)
    {
        var frames = new List<char[,]>();

        foreach (var block in SplitIntoFrameBlocks(content))
        {
            var height = block.Count;
            var width = 0;
            foreach (var line in block)
            {
                width = Math.Max(width, line.Length);
            }

            var grid = new char[height, width];
            for (var row = 0; row < height; row++)
            {
                var line = block[row];
                for (var col = 0; col < width; col++)
                {
                    grid[row, col] = col < line.Length ? line[col] : emptyChar;
                }
            }

            frames.Add(grid);
        }

        return frames;
    }

    /// <summary>
    /// Parses an optional secondary layer (foregroundcolors/backgroundcolors/materials), padding
    /// it to match the
    /// dimensions of the corresponding characters-layer frames. Missing frames, missing rows, and
    /// missing/short lines are all padded with <paramref name="emptyChar"/>, and a wholly
    /// missing file (<paramref name="content"/> is null) yields all-empty grids matching the
    /// characters layer's shape.
    /// </summary>
    public static IReadOnlyList<char[,]> ParseSecondaryLayer(
        string? content, IReadOnlyList<char[,]> charsFrames, char emptyChar)
    {
        var blocks = content is null ? [] : SplitIntoFrameBlocks(content);
        var frames = new List<char[,]>(charsFrames.Count);

        for (var frameIndex = 0; frameIndex < charsFrames.Count; frameIndex++)
        {
            var charsFrame = charsFrames[frameIndex];
            var height = charsFrame.GetLength(0);
            var width = charsFrame.GetLength(1);
            var block = frameIndex < blocks.Count ? blocks[frameIndex] : [];

            var grid = new char[height, width];
            for (var row = 0; row < height; row++)
            {
                var line = row < block.Count ? block[row] : string.Empty;
                for (var col = 0; col < width; col++)
                {
                    grid[row, col] = col < line.Length ? line[col] : emptyChar;
                }
            }

            frames.Add(grid);
        }

        return frames;
    }

    /// <summary>
    /// Pads or truncates raw grid content (e.g. Level1_objects.txt) to the given fixed
    /// dimensions, per AssetFormat.md section 3: an object-placement grid's dimensions are not
    /// inferred from its own content but instead fixed to match the world background's
    /// dimensions, with missing rows/columns padded with <paramref name="emptyChar"/>.
    /// </summary>
    public static char[,] ParseFixedSizeGrid(string content, int width, int height, char emptyChar)
    {
        var lines = content.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        var grid = new char[height, width];

        for (var row = 0; row < height; row++)
        {
            var line = row < lines.Length ? lines[row] : string.Empty;
            for (var col = 0; col < width; col++)
            {
                grid[row, col] = col < line.Length ? line[col] : emptyChar;
            }
        }

        return grid;
    }

    private static List<List<string>> SplitIntoFrameBlocks(string content)
    {
        var blocks = new List<List<string>>();
        var current = new List<string>();

        foreach (var line in content.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
        {
            if (line.Trim() == FrameSeparator)
            {
                blocks.Add(current);
                current = [];
                continue;
            }

            current.Add(line);
        }

        blocks.Add(current);
        return blocks;
    }
}
