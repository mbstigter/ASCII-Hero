namespace ASCII_Hero.Client.Game.Assets;

/// <summary>One frame of a world's thumbnail art: a char grid plus its parallel foreground/background-color grids.</summary>
public class ThumbnailFrame
{
    public required char[,] Chars { get; init; }
    public required char[,] Fore { get; init; }
    public required char[,] Back { get; init; }
}

/// <summary>
/// Everything a world-selection screen needs to show one world without fully loading it (compare
/// <see cref="World.World2D.LoadAsync"/>, which loads the whole playable world): its display
/// title and its fixed-size, optionally-animated thumbnail art. See docs/AssetFormat.md §3.1/§3.2.
/// Owns its own thumbnail animation state (current frame, elapsed time) since exactly one instance
/// of each world's thumbnail is ever shown on the selection screen at a time - unlike a sprite's
/// clip, there's no need to separate "shared asset data" from "one placement's playback state".
/// </summary>
public class WorldSummary
{
    public string WorldName { get; }

    /// <summary>From this world's <c>[World] Title</c> settings.ini key, falling back to <see cref="WorldName"/>.</summary>
    public string Title { get; }

    /// <summary>Every frame of this world's thumbnail, each always exactly
    /// <see cref="WorldCatalog.ThumbnailWidth"/> x <see cref="WorldCatalog.ThumbnailHeight"/>. Always
    /// has at least one frame (a blank one, if the world has no thumbnail art at all).</summary>
    public IReadOnlyList<ThumbnailFrame> ThumbnailFrames { get; }

    public char EmptyChar { get; }

    /// <summary>This world's own resolved Global+World color palette, for rendering thumbnail cells.</summary>
    public ColorPalette Palette { get; }

    /// <summary>
    /// Color code this world's own <c>[Colors] DefaultBackgroundColor</c> settings.ini key resolves
    /// to, used as the fallback for a thumbnail cell whose own background color code is
    /// absent/empty. Null when not set, in which case no fill (fully transparent) applies.
    /// </summary>
    public char? DefaultBackColor { get; }

    private readonly double? _frameDurationSeconds;
    private readonly AnimationMode _animationMode;
    private double _elapsedSeconds;
    private int _frameIndex;
    private int _direction;

    public WorldSummary(
        string worldName,
        string title,
        IReadOnlyList<ThumbnailFrame> thumbnailFrames,
        char emptyChar,
        ColorPalette palette,
        char? defaultBackColor,
        double? frameDurationSeconds,
        AnimationMode animationMode,
        int defaultFrameIndex)
    {
        WorldName = worldName;
        Title = title;
        ThumbnailFrames = thumbnailFrames;
        EmptyChar = emptyChar;
        Palette = palette;
        DefaultBackColor = defaultBackColor;
        _frameDurationSeconds = frameDurationSeconds;
        _animationMode = animationMode;

        _frameIndex = Math.Clamp(defaultFrameIndex, 0, thumbnailFrames.Count - 1);
        // Starting already at the last frame means the next PingPong tick should move backward -
        // same reasoning as Body2D.SetFrame.
        _direction = _frameIndex >= thumbnailFrames.Count - 1 ? -1 : 1;
    }

    /// <summary>The thumbnail frame currently on display; changes over time if animated (see <see cref="AdvanceThumbnailAnimation"/>).</summary>
    public ThumbnailFrame CurrentThumbnailFrame => ThumbnailFrames[_frameIndex];

    /// <summary>
    /// Advances this world's own thumbnail animation timer, exactly like <see cref="World.Body2D.AdvanceAnimation"/>
    /// does for a sprite clip. No-ops if no <c>[Animation]</c> timing was configured, there's only
    /// one frame, or the mode is <see cref="AnimationMode.Off"/>.
    /// </summary>
    public void AdvanceThumbnailAnimation(double deltaSeconds)
    {
        if (_frameDurationSeconds is null || ThumbnailFrames.Count <= 1 || _animationMode == AnimationMode.Off)
        {
            return;
        }

        _elapsedSeconds += deltaSeconds;

        while (_elapsedSeconds >= _frameDurationSeconds.Value)
        {
            _elapsedSeconds -= _frameDurationSeconds.Value;

            if (_animationMode == AnimationMode.Loop)
            {
                _frameIndex = (_frameIndex + 1) % ThumbnailFrames.Count;
            }
            else if (_animationMode == AnimationMode.Once)
            {
                if (_frameIndex < ThumbnailFrames.Count - 1)
                {
                    _frameIndex++;
                }
            }
            else // PingPong (Off already returned above)
            {
                _frameIndex += _direction;

                if (_frameIndex >= ThumbnailFrames.Count - 1)
                {
                    _frameIndex = ThumbnailFrames.Count - 1;
                    _direction = -1;
                }
                else if (_frameIndex <= 0)
                {
                    _frameIndex = 0;
                    _direction = 1;
                }
            }
        }
    }
}

/// <summary>
/// The set of worlds available to play and their lightweight <see cref="WorldSummary"/> metadata,
/// for a world-selection screen shown before <see cref="World.World2D.LoadAsync"/> loads the
/// chosen world. See docs/AssetFormat.md §3.2/§4.4.
/// </summary>
public static class WorldCatalog
{
    public const int ThumbnailWidth = 16;
    public const int ThumbnailHeight = 8;

    /// <summary>
    /// Reads the ordered list of every world available to play from <c>Global/Worlds.ini</c> (see
    /// docs/AssetFormat.md §4.4) - an explicit, authored manifest rather than a directory listing,
    /// since Blazor WebAssembly has no way to enumerate `wwwroot`'s contents at runtime (the same
    /// reasoning as the authored, not filesystem-inferred, `[Stances]` clip list).
    /// </summary>
    public static async Task<IReadOnlyList<string>> LoadWorldNamesAsync(IAssetFileProvider fileProvider)
    {
        var content = await fileProvider.TryReadTextAsync($"{AssetPathResolver.GlobalRoot}/Worlds.ini");
        var ini = IniDocument.Parse(content ?? string.Empty);
        var order = ini.TryGetValue("Levels", "Order") ?? string.Empty;
        return order.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Loads every world named in <c>Global/Worlds.ini</c>, in the order listed there.</summary>
    public static async Task<IReadOnlyList<WorldSummary>> LoadAllAsync(IAssetFileProvider fileProvider)
    {
        var worldNames = await LoadWorldNamesAsync(fileProvider);
        var summaries = new List<WorldSummary>(worldNames.Count);
        foreach (var worldName in worldNames)
        {
            summaries.Add(await LoadAsync(fileProvider, worldName));
        }

        return summaries;
    }

    /// <summary>Loads one world's title and thumbnail art without loading its full playable World2D.</summary>
    public static async Task<WorldSummary> LoadAsync(IAssetFileProvider fileProvider, string worldName)
    {
        var worldFolder = $"{AssetPathResolver.WorldsRoot}/{worldName}";

        var settingsContent = await fileProvider.TryReadTextAsync($"{worldFolder}/{worldName}_settings.ini");
        var settings = IniDocument.Parse(settingsContent ?? string.Empty);
        var emptyChar = IniValueParser.ParseEmptyChar(settings.TryGetValue("Layout", "EmptyChar"));
        var title = settings.TryGetValue("World", "Title") is { Length: > 0 } titleValue ? titleValue : worldName;

        // Both files are optional (see docs/AssetFormat.md §3.1) - a world without thumbnail art
        // yields a single blank thumbnail frame rather than failing to load. Like any other clip,
        // multiple frames are separated by "//end"; unlike a sprite clip, every frame here is
        // fixed at exactly ThumbnailWidth x ThumbnailHeight rather than inferred from content.
        var charsContent = await fileProvider.TryReadTextAsync($"{worldFolder}/{worldName}_thumb_characters.txt");
        var charFrames = charsContent is null
            ? [EmptyGrid(emptyChar)]
            : AssetTextReader.ParseFixedSizeFrames(charsContent, ThumbnailWidth, ThumbnailHeight, emptyChar);

        var foreContent = await fileProvider.TryReadTextAsync($"{worldFolder}/{worldName}_thumb_foregroundcolors.txt");
        var foreFrames = AssetTextReader.ParseFixedSizeSecondaryFrames(
            foreContent, charFrames.Count, ThumbnailWidth, ThumbnailHeight, emptyChar);

        var backContent = await fileProvider.TryReadTextAsync($"{worldFolder}/{worldName}_thumb_backgroundcolors.txt");
        var backFrames = AssetTextReader.ParseFixedSizeSecondaryFrames(
            backContent, charFrames.Count, ThumbnailWidth, ThumbnailHeight, emptyChar);

        var thumbnailFrames = new List<ThumbnailFrame>(charFrames.Count);
        for (var i = 0; i < charFrames.Count; i++)
        {
            thumbnailFrames.Add(new ThumbnailFrame { Chars = charFrames[i], Fore = foreFrames[i], Back = backFrames[i] });
        }

        var defaultBackColor = IniValueParser.ParseColorCode(settings.TryGetValue("Colors", "DefaultBackgroundColor"));

        // An optional [Animation] section (same keys/semantics as a sprite clip's own
        // [Animation]/[Animation.{clipName}] section - see docs/AssetFormat.md §2.4) times the
        // thumbnail's frames. Absent entirely, the thumbnail never animates - even if it happens
        // to have more than one frame - exactly like an un-configured sprite clip.
        var animationSection = settings.Section("Animation");
        var frameDurationSeconds = animationSection.TryGetValue("FrameDurationSeconds", out var durationText)
            ? SpriteLoader.ParseFrameDurationSeconds(durationText)
            : null;
        var animationMode = animationSection.TryGetValue("Mode", out var modeText)
            ? SpriteLoader.ParseAnimationMode(modeText)
            : AnimationMode.Loop;
        var defaultFrameIndex = animationSection.TryGetValue("DefaultFrame", out var defaultFrameText)
            ? SpriteLoader.ParseDefaultFrame(defaultFrameText) ?? 0
            : 0;

        var palette = await ColorPalette.LoadAsync(fileProvider, worldName);

        return new WorldSummary(
            worldName, title, thumbnailFrames, emptyChar, palette, defaultBackColor,
            frameDurationSeconds, animationMode, defaultFrameIndex);
    }

    private static char[,] EmptyGrid(char emptyChar)
    {
        var grid = new char[ThumbnailHeight, ThumbnailWidth];
        for (var row = 0; row < ThumbnailHeight; row++)
        {
            for (var col = 0; col < ThumbnailWidth; col++)
            {
                grid[row, col] = emptyChar;
            }
        }

        return grid;
    }
}
