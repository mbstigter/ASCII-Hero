using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>
/// A reusable broad-phase spatial hash grid: buckets arbitrary items by the world-space cells
/// their AABB (position/size) overlaps, then answers "what's near this AABB" without testing
/// against every item in the level. This is the broad phase proper - it only ever reasons about
/// whole bounding boxes and grid buckets, never about a body's actual collision shape/rectangles
/// or rendered characters (that finer-grained work is the narrow phase, done afterward by
/// <see cref="CollisionSystem"/>'s Resolve*/TryFindContact/HasCharacterOverlap methods
/// against just the small candidate set this grid narrows things down to).
/// <see cref="CollisionSystem"/> keeps one instance of this per broad-phase category (solids,
/// moving bodies) rather than duplicating the bucket-and-query logic for each.
/// </summary>
public class SpatialGrid<T>
{
    private readonly double _cellSize;
    private readonly Dictionary<(int X, int Y), List<T>> _buckets = [];

    /// <summary>Reused per query to avoid a fresh allocation for every single candidate lookup.</summary>
    private readonly HashSet<T> _candidateBuffer = [];

    public SpatialGrid(double cellSize)
    {
        _cellSize = cellSize;
    }

    /// <summary>
    /// Clears every existing bucket and re-buckets <paramref name="items"/> - cheap enough to just
    /// redo every frame (rather than incrementally maintained) since the set of items can change
    /// (spawned/removed/moved) between calls. Bucket entries for cells that were ever occupied are
    /// kept (emptied, not removed) so their lists can be reused.
    /// </summary>
    public void Rebuild(IReadOnlyList<T> items, Func<T, Vector2D> getPosition, Func<T, Vector2D> getSize)
    {
        foreach (var bucket in _buckets.Values)
        {
            bucket.Clear();
        }

        foreach (var item in items)
        {
            var (minX, maxX, minY, maxY) = GetCellRange(getPosition(item), getSize(item));
            for (var x = minX; x <= maxX; x++)
            {
                for (var y = minY; y <= maxY; y++)
                {
                    if (!_buckets.TryGetValue((x, y), out var bucket))
                    {
                        bucket = [];
                        _buckets[(x, y)] = bucket;
                    }

                    bucket.Add(item);
                }
            }
        }
    }

    /// <summary>
    /// The distinct set of items sharing at least one bucket with an AABB at
    /// <paramref name="position"/>/<paramref name="size"/> - the broad-phase candidate set for the
    /// narrow phase to actually test, instead of every item this grid was built from.
    /// The returned collection is this grid's shared, reused buffer: it is only valid until the
    /// next <see cref="GetCandidates"/> or <see cref="Rebuild"/> call on the same grid, so callers
    /// must finish enumerating it (or copy it) before querying again, and must not query the same
    /// grid from inside that enumeration. Enumeration order follows bucket visit order and the
    /// order items were added during <see cref="Rebuild"/>; collision resolution is order-sensitive,
    /// so changing how candidates are collected or ordered can change gameplay outcomes.
    /// </summary>
    public HashSet<T> GetCandidates(Vector2D position, Vector2D size)
    {
        _candidateBuffer.Clear();
        var (minX, maxX, minY, maxY) = GetCellRange(position, size);
        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                if (!_buckets.TryGetValue((x, y), out var bucket))
                {
                    continue;
                }

                foreach (var item in bucket)
                {
                    _candidateBuffer.Add(item);
                }
            }
        }

        return _candidateBuffer;
    }

    /// <summary>The inclusive range of grid cell coordinates an AABB at <paramref name="position"/>/<paramref name="size"/> spans.</summary>
    private (int MinX, int MaxX, int MinY, int MaxY) GetCellRange(Vector2D position, Vector2D size) =>
    (
        (int)Math.Floor(position.X / _cellSize),
        (int)Math.Floor((position.X + size.X) / _cellSize),
        (int)Math.Floor(position.Y / _cellSize),
        (int)Math.Floor((position.Y + size.Y) / _cellSize)
    );
}
