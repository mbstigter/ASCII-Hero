namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// Whether a sprite asset's art represents a single tileable unit that placements can repeat to
/// build up an arbitrary-length platform/wall, instead of a single fixed shape. A
/// <see cref="Horizontal"/> asset is authored one cell wide (any height) and repeats
/// column-wise; a <see cref="Vertical"/> asset is authored one cell tall (any width) and
/// repeats row-wise. See docs/AssetFormat.md for the full rationale.
/// </summary>
public enum TileAxis
{
    None,
    Horizontal,
    Vertical,
}

/// <summary>One drawable/collidable frame of a sprite clip: char/fore/back grids plus material per cell.</summary>
public class SpriteFrame
{
    public required char[,] Chars { get; init; }
    public required char[,] Fore { get; init; }
    public required char[,] Back { get; init; }
    public required string?[,] Materials { get; init; }

    public int Width => Chars.GetLength(1);
    public int Height => Chars.GetLength(0);
}

/// <summary>One named clip (e.g. "idle", "walk_left") of a sprite asset, made up of one or more frames.</summary>
public class SpriteClip
{
    public required string Name { get; init; }
    public required IReadOnlyList<SpriteFrame> Frames { get; init; }
}

/// <summary>
/// A fully loaded sprite asset: every clip defined by its files, plus the resolved empty-char
/// used to interpret its grids. Produced by <see cref="SpriteLoader"/> from the on-disk asset
/// format described in docs/AssetFormat.md.
/// </summary>
public class SpriteAsset
{
    public required string Name { get; init; }
    public required char EmptyChar { get; init; }
    public required IReadOnlyDictionary<string, SpriteClip> Clips { get; init; }

    /// <summary>Whether (and how) this asset's frames can be repeated to build a longer platform/wall.</summary>
    public TileAxis TileAxis { get; init; } = TileAxis.None;

    public SpriteClip GetClip(string clipName) =>
        Clips.TryGetValue(clipName, out var clip)
            ? clip
            : throw new KeyNotFoundException($"Sprite '{Name}' has no clip named '{clipName}'.");
}

