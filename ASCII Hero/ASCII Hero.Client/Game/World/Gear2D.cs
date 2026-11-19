namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// One gear slot the player carries, e.g. "Rock" or "Key": a "virtual" collection with no body in
/// the world, only a <see cref="Count"/> and an <see cref="Icon"/> for the gear box and gear ring.
/// The slots are game-wide and fixed: listed in <c>Global/Settings.ini</c> <c>[Gear]</c>, created
/// empty for every world (see <see cref="Player2D.Gear"/>) and filled by picking up a
/// <see cref="ThrowableObject2D"/> whose <see cref="ThrowableObject2D.GearName"/> matches, or a
/// <see cref="Key2D"/> for the <see cref="Key2D.GearName"/> slot. Not a <see cref="Body2D"/>: the
/// objects it holds are kept as they are (sprite, material, tuning), so a thrown one is put back
/// into the world as itself.
/// </summary>
public sealed class Gear2D(string name, char icon)
{
    /// <summary>The held objects, most recently picked up last.</summary>
    private readonly List<Body2D> _items = [];

    /// <summary>The slot name, matched case-insensitively against a throwable's <c>Gear</c> key.</summary>
    public string Name { get; } = name;

    /// <summary>The character shown for this slot in the gear box and gear ring.</summary>
    public char Icon { get; } = icon;

    /// <summary>How many objects this slot currently holds.</summary>
    public int Count => _items.Count;

    /// <summary>Stores a picked-up object.</summary>
    public void Add(Body2D item) => _items.Add(item);

    /// <summary>Takes the most recently stored object out, if any.</summary>
    public bool TryTake(out Body2D item) => TryTake(_ => true, out item);

    /// <summary>Takes out the most recently stored object that matches <paramref name="match"/>, if any (e.g. the key for one lock).</summary>
    public bool TryTake(Func<Body2D, bool> match, out Body2D item)
    {
        var index = _items.FindLastIndex(candidate => match(candidate));
        if (index < 0)
        {
            item = null!;
            return false;
        }

        item = _items[index];
        _items.RemoveAt(index);
        return true;
    }

    /// <summary>The object <see cref="TryTake(out Body2D)"/> would take next, without taking it.</summary>
    public bool TryPeek(out Body2D item)
    {
        item = _items.Count > 0 ? _items[^1] : null!;
        return _items.Count > 0;
    }

    /// <summary>Replaces this slot's contents with everything <paramref name="other"/> holds (portal carry-over), keeping their order.</summary>
    public void TakeOverFrom(Gear2D other)
    {
        _items.Clear();
        _items.AddRange(other._items);
    }
}
