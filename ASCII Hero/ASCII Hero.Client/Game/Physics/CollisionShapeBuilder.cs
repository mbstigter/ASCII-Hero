namespace ASCII_Hero.Client.Game.Physics;

/// <summary>
/// Derives a small set of axis-aligned collision rectangles from a char grid (e.g. a sprite's
/// _characters.txt layer), so collision follows the actual non-empty shape of a sprite instead of
/// its full bounding box. Rectangles are in local cell coordinates (row/column of the grid),
/// with (0,0) at the grid's top-left - callers translate them into world space via a body's
/// Position.
/// </summary>
public static class CollisionShapeBuilder
{
    /// <summary>
    /// Merges non-empty cells of <paramref name="grid"/> into rectangles: each row is first
    /// reduced to horizontal runs of non-empty cells, then consecutive rows with an identical
    /// run are merged vertically into a single rectangle. This keeps the rectangle count small
    /// for typical blocky ASCII sprites without requiring true per-pixel/polygon collision.
    /// </summary>
    public static IReadOnlyList<Rect2D> DeriveRectangles(char[,] grid, char emptyChar)
    {
        var height = grid.GetLength(0);
        var width = grid.GetLength(1);

        // Step 1: reduce each row to a list of horizontal runs (start column, run width).
        var rowRuns = new List<(int Start, int Width)>[height];
        for (var row = 0; row < height; row++)
        {
            var runs = new List<(int Start, int Width)>();
            var col = 0;
            while (col < width)
            {
                if (grid[row, col] == emptyChar)
                {
                    col++;
                    continue;
                }

                var start = col;
                while (col < width && grid[row, col] != emptyChar)
                {
                    col++;
                }

                runs.Add((start, col - start));
            }

            rowRuns[row] = runs;
        }

        // Step 2: merge vertically-identical runs (same start/width) across consecutive rows.
        var rectangles = new List<Rect2D>();
        var consumed = new bool[height][];
        for (var row = 0; row < height; row++)
        {
            consumed[row] = new bool[rowRuns[row].Count];
        }

        for (var row = 0; row < height; row++)
        {
            for (var runIndex = 0; runIndex < rowRuns[row].Count; runIndex++)
            {
                if (consumed[row][runIndex])
                {
                    continue;
                }

                var run = rowRuns[row][runIndex];
                consumed[row][runIndex] = true;

                var mergedRows = 1;
                var nextRow = row + 1;
                while (nextRow < height)
                {
                    var matchIndex = rowRuns[nextRow].FindIndex(r => r == run);
                    if (matchIndex < 0 || consumed[nextRow][matchIndex])
                    {
                        break;
                    }

                    consumed[nextRow][matchIndex] = true;
                    mergedRows++;
                    nextRow++;
                }

                rectangles.Add(new Rect2D(run.Start, row, run.Width, mergedRows));
            }
        }

        return rectangles;
    }
}
