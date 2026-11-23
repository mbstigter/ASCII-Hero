using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// One gear slot the player carries, e.g. "Rock" or "Key": a "virtual" collection with no body in
/// the world, only a <see cref="Count"/> and an icon for the gear box and gear ring. The slots are
/// game-wide and fixed: listed in <c>Global/Settings.ini</c> <c>[Gear]</c>, created empty for every
/// world (see <see cref="Player2D.Gear"/>) and filled by picking up a
/// <see cref="ThrowableObject2D"/> whose <see cref="ThrowableObject2D.GearName"/> matches, a
/// <see cref="Key2D"/> for the <see cref="Key2D.GearName"/> slot, or a
/// <see cref="CollectableType.HealthPack"/>. Not a <see cref="Body2D"/>: the objects it holds
/// are kept as they are (sprite, material, tuning), so a thrown one is put back into the world
/// as itself.
/// </summary>
public sealed class Gear2D
{
    public Gear2D(string name, string icon)
    {
        Name = name;
        Icon = icon;
    }

    public Gear2D(string name, char icon) : this(name, icon.ToString()) { }

    public Gear2D(string name, string iconAssetName, string iconClipName)
    {
        Name = name;
        Icon = string.Empty;
        IconAssetName = iconAssetName;
        IconClipName = iconClipName;
    }

    /// <summary>The held objects, most recently picked up last.</summary>
    private readonly List<Body2D> _items = [];

    /// <summary>The slot name, matched case-insensitively against a throwable's <c>Gear</c> key.</summary>
    public string Name { get; }

    /// <summary>Fallback text icon for gear slots created without a sprite icon.</summary>
    public string Icon { get; }

    /// <summary>The sprite asset and clip used by this slot's monochrome icon, when configured.</summary>
    public string? IconAssetName { get; }

    public string? IconClipName { get; }

    public SpriteFrame? IconFrame { get; private set; }

    public char IconEmptyChar { get; private set; } = ' ';

    /// <summary>Loads and validates this slot's single-row monochrome icon frame.</summary>
    public void LoadIcon(SpriteAsset asset)
    {
        if (IconClipName is null)
        {
            throw new InvalidOperationException($"Gear slot '{Name}' has no sprite icon clip configured.");
        }

        if (asset.EmptyChar == '^')
        {
            throw new FormatException($"Gear slot '{Name}' uses '^' as its sprite EmptyChar, which conflicts with the monochrome color marker.");
        }

        var clip = asset.GetClip(IconClipName);
        if (clip.DefaultFrame < 0 || clip.DefaultFrame >= clip.Frames.Count)
        {
            throw new FormatException($"Gear slot '{Name}' icon '{asset.Name}/{IconClipName}' DefaultFrame {clip.DefaultFrame} does not select an existing frame.");
        }

        var frame = clip.Frames[clip.DefaultFrame];
        if (frame.Width is < 1 or > 3 || frame.Height != 1)
        {
            throw new FormatException($"Gear slot '{Name}' monochrome icon '{asset.Name}/{IconClipName}' must be one row and one to three cells wide.");
        }

        for (var row = 0; row < frame.Height; row++)
        {
            for (var col = 0; col < frame.Width; col++)
            {
                if (frame.Fore[row, col] != asset.EmptyChar && frame.Fore[row, col] != '^')
                {
                    throw new FormatException($"Gear slot '{Name}' monochrome icon '{asset.Name}/{IconClipName}' foreground colors may contain only EmptyChar or '^'.");
                }

                if (frame.Back[row, col] != asset.EmptyChar && frame.Back[row, col] != '^')
                {
                    throw new FormatException($"Gear slot '{Name}' monochrome icon '{asset.Name}/{IconClipName}' background colors may contain only EmptyChar or '^'.");
                }
            }
        }

        IconFrame = frame;
        IconEmptyChar = asset.EmptyChar;
    }

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

    /// <summary>Finds the most recently stored object that matches <paramref name="match"/> (e.g. the key for one lock), without taking it.</summary>
    public bool TryFind(Func<Body2D, bool> match, out Body2D item)
    {
        var index = _items.FindLastIndex(candidate => match(candidate));
        item = index >= 0 ? _items[index] : null!;
        return index >= 0;
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
