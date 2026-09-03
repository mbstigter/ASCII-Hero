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

/// <summary>
/// How multi-frame clips advance through their frames over time. <see cref="Loop"/> cycles
/// sequentially (0,1,2,0,1,2,...); <see cref="PingPong"/> bounces back and forth (0,1,2,1,0,1,...);
/// <see cref="Once"/> advances sequentially like <see cref="Loop"/> but stops and holds on the
/// last frame instead of wrapping back to the first (e.g. a one-shot transformation - a killed
/// enemy crumbling down to its final husk appearance - that should visibly play through once and
/// then stay there, as opposed to <see cref="Off"/>'s "never animate at all"); <see cref="Off"/>
/// disables playback entirely, holding on <see cref="SpriteAsset.DefaultFrame"/> forever even
/// though the clip has multiple frames (e.g. a dead/inanimate variant of an otherwise-animated
/// asset). See docs/AssetFormat.md for the full rationale.
/// </summary>
public enum AnimationMode
{
    Loop,
    PingPong,
    Off,
    Once,
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

/// <summary>One named clip (e.g. "walk_idle", "walk_left") of a sprite asset, made up of one or more frames.</summary>
public class SpriteClip
{
    public required string Name { get; init; }
    public required IReadOnlyList<SpriteFrame> Frames { get; init; }

    /// <summary>
    /// Duration each frame of this clip displays before advancing to the next, in seconds. Null
    /// means this clip does not animate (the frame set at spawn stays active forever). Only
    /// meaningful when the clip has more than one frame. Resolved per-clip from an optional
    /// <c>[Animation.{clipName}]</c> section, falling back to the asset-wide <c>[Animation]</c>
    /// section - see docs/AssetFormat.md §2.4.
    /// </summary>
    public double? FrameDurationSeconds { get; init; }

    /// <summary>How this clip cycles through its frames. Defaults to Loop.</summary>
    public AnimationMode AnimationMode { get; init; } = AnimationMode.Loop;

    /// <summary>
    /// The frame index this clip starts at when spawned, e.g. a "Center" frame in a
    /// Left/Center/Right clip so PingPong mode bounces symmetrically (Center, Right, Center,
    /// Left, ...). Defaults to 0.
    /// </summary>
    public int DefaultFrame { get; init; }
}

/// <summary>
/// Which way a stance's clip should face: <see cref="Idle"/> is a stance's neutral pose (facing
/// the viewer for a stance that moves horizontally, or whatever a stance's own neutral direction
/// is otherwise, e.g. facing the ladder while climbing); <see cref="Left"/>/<see cref="Right"/>/
/// <see cref="Up"/>/<see cref="Down"/> are directional variants. Unlike a fixed pair of axes, a
/// stance can freely declare any subset of these five - not just one axis - which is what a
/// four-directional stance (e.g. swimming) needs. See <see cref="StanceDefinition"/> and
/// docs/AssetFormat.md §2.6.
/// </summary>
public enum Facing
{
    Idle,
    Left,
    Right,
    Up,
    Down,
}

/// <summary>
/// One named stance (e.g. "Walk", "Crawl", "Climb", "Swim") of a sprite asset, mapping each
/// <see cref="Facing"/> to the clip name that should be shown. Built from clip names' own
/// suffixes (<c>_idle</c>/<c>_left</c>/<c>_right</c>/<c>_up</c>/<c>_down</c>) rather than a fixed
/// position/count, so a stance can declare any subset of the five facings it actually needs - one
/// axis (e.g. `Left`/`Right` for walking), the other (e.g. `Up`/`Down` for climbing), or all four
/// at once (e.g. swimming). Any facing not declared by this stance falls back to
/// <see cref="IdleClip"/>. See docs/AssetFormat.md §2.6.
/// </summary>
public class StanceDefinition
{
    public required string IdleClip { get; init; }

    /// <summary>
    /// Clips for non-idle facings this stance actually declared, keyed by <see cref="Facing"/>
    /// (never contains <see cref="Facing.Idle"/> - that's always <see cref="IdleClip"/>).
    /// </summary>
    public IReadOnlyDictionary<Facing, string> DirectionalClips { get; init; } = new Dictionary<Facing, string>();

    public string GetClipName(Facing facing) =>
        facing != Facing.Idle && DirectionalClips.TryGetValue(facing, out var clip) ? clip : IdleClip;
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

    /// <summary>
    /// Color code (see <c>Global/Colors.ini</c>) used for a cell whose own <c>_foregroundcolors.txt</c>
    /// (or <c>_backgroundcolors.txt</c>) is absent/empty at that position, from this asset's own
    /// <c>[Colors] DefaultForegroundColor</c>/<c>DefaultBackgroundColor</c> settings.ini keys. Null
    /// when not set, in which case rendering falls back further to the level's own default (see
    /// <see cref="World.World2D.DefaultForeColor"/>/<see cref="World.World2D.DefaultBackColor"/>) and
    /// finally to the hardcoded renderer default. See docs/AssetFormat.md §2.5/§4.
    /// </summary>
    public char? DefaultForeColor { get; init; }

    /// <summary>See <see cref="DefaultForeColor"/>.</summary>
    public char? DefaultBackColor { get; init; }

    /// <summary>
    /// Optional stance/facing metadata (see docs/AssetFormat.md §2.6) mapping each stance name
    /// (e.g. "Walk", "Crawl") to the clips shown for its Idle/Left/Right facings. Null for assets
    /// that don't declare a <c>[Stances]</c> section - such assets only ever show whichever single
    /// clip was explicitly requested at spawn time, with no stance/facing switching.
    /// </summary>
    public IReadOnlyDictionary<string, StanceDefinition>? Stances { get; init; }

    /// <summary>The stance active at spawn, from <c>[Stances] Default</c>. Null when <see cref="Stances"/> is null.</summary>
    public string? DefaultStance { get; init; }

    public SpriteClip GetClip(string clipName) =>
        Clips.TryGetValue(clipName, out var clip)
            ? clip
            : throw new KeyNotFoundException($"Sprite '{Name}' has no clip named '{clipName}'.");

    /// <summary>Resolves the clip name to show for a given stance/facing, per docs/AssetFormat.md §2.6.</summary>
    public string GetClipName(string stance, Facing facing)
    {
        if (Stances is null)
        {
            throw new KeyNotFoundException($"Sprite '{Name}' has no [Stances] section defined.");
        }

        if (!Stances.TryGetValue(stance, out var stanceDef))
        {
            throw new KeyNotFoundException($"Sprite '{Name}' has no stance named '{stance}'.");
        }

        return stanceDef.GetClipName(facing);
    }
}

