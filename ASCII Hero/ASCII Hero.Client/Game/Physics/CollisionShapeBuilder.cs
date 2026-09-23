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
    /// Merges non-empty cells of <paramref name="grid"/> into rectangles by run-length encoding
    /// the shape along <em>both</em> grid axes and combining the two results - see
    /// <see cref="DeriveRunRectangles"/> for the shared algorithm. Each pass alone is the classic,
    /// cheap "merge same-width runs into a rectangle" decomposition; running it along both axes is
    /// what makes the result reliable for a shape whose outline bulges non-monotonically (a
    /// circular "ball" sprite, a diamond) rather than only for the common case of a blocky,
    /// rectangular sprite (a platform, a wall, the player).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A single-axis decomposition is not just incomplete but actively misleading for contact
    /// resolution. <see cref="CollisionSystem"/>'s narrow phase picks a contact's push-out
    /// direction via the standard minimum-translation-vector rule: whichever axis (horizontal or
    /// vertical) has the <em>shallower</em> overlap between two rectangles is treated as the true
    /// direction of contact. A row-only decomposition of a curved silhouette tends to produce
    /// very short, wide rectangles along the shape's flanks (each row's run width differs
    /// slightly from its neighbors as the curve bulges, so the vertical same-width merge rarely
    /// fires) - and a short rectangle's vertical overlap against a tall body is always shallow,
    /// so the resolver always picks a vertical push-out even when the body is, in truth, deeply
    /// embedded horizontally. The practical symptom was a player able to stand on a ball's top
    /// corner (a genuinely shallow, correctly-vertical contact there) yet walk straight through
    /// its horizontal middle (a deep contact that was still being resolved as if it were vertical
    /// and shallow).
    /// </para>
    /// <para>
    /// Column-run rectangles - tall and narrow along the same flanks - are the other half of the
    /// same shape's true outline: there, the horizontal overlap is shallow and the vertical
    /// overlap is deep, so the minimum-translation-vector rule now correctly picks a horizontal
    /// push-out instead. Combining both passes therefore gives contact resolution, at every point
    /// along a silhouette's outline, at least one rectangle whose short axis actually matches the
    /// true direction of contact there - not just at the shape's flat top/bottom or flat
    /// left/right extremes.
    /// </para>
    /// <para>
    /// For a blocky rectangular sprite both passes agree and produce byte-identical rectangles,
    /// which are deduplicated (see <see cref="DeriveRectangles"/>'s own body) rather than left
    /// duplicated in the result: <see cref="CollisionSystem"/> resolves each of a body's own
    /// rectangles independently, one at a time, per solver iteration, so an undetected duplicate
    /// would silently double that contact's position correction and velocity impulse every
    /// iteration - invisible for a resting body, but capable of adding real extra velocity (e.g.
    /// an unwanted jump-height boost) to a fast-moving contact. After deduplication, a
    /// non-curved sprite pays no meaningful extra cost over the original single-pass algorithm.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Rect> DeriveRectangles(char[,] grid, char emptyChar)
    {
        var rowRunRectangles = DeriveRunRectangles(grid, emptyChar, transposed: false);
        var columnRunRectangles = DeriveRunRectangles(grid, emptyChar, transposed: true);

        // For a blocky rectangular sprite the two passes produce byte-identical rectangles, which
        // must be deduplicated here rather than left for callers to discover on their own:
        // CollisionSystem.ResolveAgainstOtherBody resolves each of a body's own rectangles
        // independently, one at a time, per solver iteration - an undetected duplicate rectangle
        // would silently get detected and resolved twice per iteration, doubling that contact's
        // position correction and velocity impulse. This is invisible for a body at rest (there is
        // nothing left to double once velocity is already zero), which is why it only surfaced as
        // an extra jump-height boost on a fast-moving contact.
        var rectangles = new List<Rect>(rowRunRectangles.Count + columnRunRectangles.Count);
        rectangles.AddRange(rowRunRectangles);
        foreach (var candidate in columnRunRectangles)
        {
            if (!rectangles.Exists(existing => IsSameRect(existing, candidate)))
            {
                rectangles.Add(candidate);
            }
        }

        return rectangles;
    }

    /// <summary>Exact-match comparison (no epsilon needed - both passes derive rectangles from the same integer grid coordinates) used to deduplicate <see cref="DeriveRectangles"/>'s two passes.</summary>
    private static bool IsSameRect(Rect a, Rect b) =>
        a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;

    /// <summary>
    /// The shared run-length decomposition used for both of <see cref="DeriveRectangles"/>'s
    /// passes: reduces every "line" along the primary axis to runs of non-empty cells, then
    /// merges adjacent lines whose run has an identical start/length into a single rectangle.
    /// </summary>
    /// <param name="transposed">
    /// <see langword="false"/> for the row-run pass (primary axis = rows, runs are horizontal,
    /// merged vertically across rows - i.e. the original, row-only algorithm). <see
    /// langword="true"/> for the column-run pass (primary axis = columns, runs are vertical,
    /// merged horizontally across columns) - exactly the transpose of the row-run pass, reusing
    /// the same logic by swapping which grid axis is walked first and how the final <see
    /// cref="Rect"/> is assembled.
    /// </param>
    private static IReadOnlyList<Rect> DeriveRunRectangles(char[,] grid, char emptyChar, bool transposed)
    {
        var height = grid.GetLength(0);
        var width = grid.GetLength(1);

        // "Primary" is the axis runs are found along (rows for the row-run pass, columns for the
        // column-run pass); "secondary" is the axis runs are merged across.
        var primaryCount = transposed ? width : height;
        var secondaryCount = transposed ? height : width;

        char CellAt(int primary, int secondary) => transposed ? grid[secondary, primary] : grid[primary, secondary];

        // Step 1: reduce each primary line to a list of runs (start, length) along the secondary axis.
        var lineRuns = new List<(int Start, int Length)>[primaryCount];
        for (var primary = 0; primary < primaryCount; primary++)
        {
            var runs = new List<(int Start, int Length)>();
            var secondary = 0;
            while (secondary < secondaryCount)
            {
                if (CellAt(primary, secondary) == emptyChar)
                {
                    secondary++;
                    continue;
                }

                var start = secondary;
                while (secondary < secondaryCount && CellAt(primary, secondary) != emptyChar)
                {
                    secondary++;
                }

                runs.Add((start, secondary - start));
            }

            lineRuns[primary] = runs;
        }

        // Step 2: merge identical runs (same start/length) across consecutive primary lines.
        var rectangles = new List<Rect>();
        var consumed = new bool[primaryCount][];
        for (var primary = 0; primary < primaryCount; primary++)
        {
            consumed[primary] = new bool[lineRuns[primary].Count];
        }

        for (var primary = 0; primary < primaryCount; primary++)
        {
            for (var runIndex = 0; runIndex < lineRuns[primary].Count; runIndex++)
            {
                if (consumed[primary][runIndex])
                {
                    continue;
                }

                var run = lineRuns[primary][runIndex];
                consumed[primary][runIndex] = true;

                var mergedLines = 1;
                var nextPrimary = primary + 1;
                while (nextPrimary < primaryCount)
                {
                    var matchIndex = lineRuns[nextPrimary].FindIndex(r => r == run);
                    if (matchIndex < 0 || consumed[nextPrimary][matchIndex])
                    {
                        break;
                    }

                    consumed[nextPrimary][matchIndex] = true;
                    mergedLines++;
                    nextPrimary++;
                }

                // Row-run pass: primary = row, secondary = column -> (x=run.Start, y=primary, w=run.Length, h=mergedLines).
                // Column-run pass: primary = column, secondary = row -> (x=primary, y=run.Start, w=mergedLines, h=run.Length).
                rectangles.Add(transposed
                    ? new Rect(primary, run.Start, mergedLines, run.Length)
                    : new Rect(run.Start, primary, run.Length, mergedLines));
            }
        }

        return rectangles;
    }
}

