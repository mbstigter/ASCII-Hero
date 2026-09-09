using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>
/// A reusable broad-phase spatial hash grid: buckets arbitrary items by the world-space cells
/// their AABB (position/size) overlaps, then answers "what's near this AABB" without testing
/// against every item in the level. This is the broad phase proper - it only ever reasons about
/// whole bounding boxes and grid buckets, never about a body's actual collision shape/rectangles
/// or rendered characters (that finer-grained work is the narrow phase, done afterward by
/// <see cref="CollisionSystem"/>'s Resolve*/TryFindDeepestOverlap/HasCharacterOverlap methods
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
    /// Rebuilds every bucket from scratch for <paramref name="items"/> - cheap enough to just redo
    /// every frame (rather than incrementally maintained) since the set of items an frame can
    /// change (spawned/removed/moved) between calls.
    /// </summary>
    public void Rebuild(IReadOnlyList<T> items, Func<T, Vector2D> getPosition, Func<T, Vector2D> getSize)
    {
        foreach (var bucket in _buckets.Values)
        {
            bucket.Clear();
        }

        foreach (var item in items)
        {
            foreach (var cell in GetOverlappingCells(getPosition(item), getSize(item)))
            {
                if (!_buckets.TryGetValue(cell, out var bucket))
                {
                    bucket = [];
                    _buckets[cell] = bucket;
                }

                bucket.Add(item);
            }
        }
    }

    /// <summary>
    /// The distinct set of items sharing at least one bucket with an AABB at
    /// <paramref name="position"/>/<paramref name="size"/> - the broad-phase candidate set for the
    /// narrow phase to actually test, instead of every item this grid was built from.
    /// </summary>
    public IEnumerable<T> GetCandidates(Vector2D position, Vector2D size)
    {
        _candidateBuffer.Clear();
        foreach (var cell in GetOverlappingCells(position, size))
        {
            if (!_buckets.TryGetValue(cell, out var bucket))
            {
                continue;
            }

            foreach (var item in bucket)
            {
                _candidateBuffer.Add(item);
            }
        }

        return _candidateBuffer;
    }

    /// <summary>Every grid cell coordinate an AABB at <paramref name="position"/>/<paramref name="size"/> spans.</summary>
    private IEnumerable<(int X, int Y)> GetOverlappingCells(Vector2D position, Vector2D size)
    {
        var minX = (int)Math.Floor(position.X / _cellSize);
        var maxX = (int)Math.Floor((position.X + size.X) / _cellSize);
        var minY = (int)Math.Floor(position.Y / _cellSize);
        var maxY = (int)Math.Floor((position.Y + size.Y) / _cellSize);

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                yield return (x, y);
            }
        }
    }
}
