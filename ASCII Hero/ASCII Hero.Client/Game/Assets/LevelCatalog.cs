namespace ASCII_Hero.Client.Game.Assets;

/// <summary>One frame of a level's thumbnail art: a char grid plus its parallel foreground-color grid.</summary>
public class ThumbnailFrame
{
    public required char[,] Chars { get; init; }
    public required char[,] Fore { get; init; }
}

/// <summary>
/// Everything a level-selection screen needs to show one level without fully loading it (compare
/// <see cref="World.World2D.LoadAsync"/>, which loads the whole playable level): its display
/// title and its fixed-size, optionally-animated thumbnail art. See docs/AssetFormat.md §3.1/§3.2.
/// Owns its own thumbnail animation state (current frame, elapsed time) since exactly one instance
/// of each level's thumbnail is ever shown on the selection screen at a time - unlike a sprite's
/// clip, there's no need to separate "shared asset data" from "one placement's playback state".
/// </summary>
public class LevelSummary
{
    public string LevelName { get; }

    /// <summary>From this level's <c>[Level] Title</c> settings.ini key, falling back to <see cref="LevelName"/>.</summary>
    public string Title { get; }

    /// <summary>Every frame of this level's thumbnail, each always exactly
    /// <see cref="LevelCatalog.ThumbnailWidth"/> x <see cref="LevelCatalog.ThumbnailHeight"/>. Always
    /// has at least one frame (a blank one, if the level has no thumbnail art at all).</summary>
    public IReadOnlyList<ThumbnailFrame> ThumbnailFrames { get; }

    public char EmptyChar { get; }

    /// <summary>This level's own resolved Global+Level color palette, for rendering thumbnail cells.</summary>
    public ColorPalette Palette { get; }

    private readonly double? _frameDurationSeconds;
    private readonly AnimationMode _animationMode;
    private double _elapsedSeconds;
    private int _frameIndex;
    private int _direction;

    public LevelSummary(
        string levelName,
        string title,
        IReadOnlyList<ThumbnailFrame> thumbnailFrames,
        char emptyChar,
        ColorPalette palette,
        double? frameDurationSeconds,
        AnimationMode animationMode,
        int defaultFrameIndex)
    {
        LevelName = levelName;
        Title = title;
        ThumbnailFrames = thumbnailFrames;
        EmptyChar = emptyChar;
        Palette = palette;
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
    /// Advances this level's own thumbnail animation timer, exactly like <see cref="World.Body2D.AdvanceAnimation"/>
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
/// The set of levels available to play and their lightweight <see cref="LevelSummary"/> metadata,
/// for a level-selection screen shown before <see cref="World.World2D.LoadAsync"/> loads the
/// chosen level. See docs/AssetFormat.md §3.2/§4.4.
/// </summary>
public static class LevelCatalog
{
    public const int ThumbnailWidth = 16;
    public const int ThumbnailHeight = 8;

    /// <summary>
    /// Reads the ordered list of every level available to play from <c>Global/Levels.ini</c> (see
    /// docs/AssetFormat.md §4.4) - an explicit, authored manifest rather than a directory listing,
    /// since Blazor WebAssembly has no way to enumerate `wwwroot`'s contents at runtime (the same
    /// reasoning as the authored, not filesystem-inferred, `[Stances]` clip list).
    /// </summary>
    public static async Task<IReadOnlyList<string>> LoadLevelNamesAsync(IAssetFileProvider fileProvider)
    {
        var content = await fileProvider.TryReadTextAsync($"{AssetPathResolver.GlobalRoot}/Levels.ini");
        var ini = IniDocument.Parse(content ?? string.Empty);
        var order = ini.TryGetValue("Levels", "Order") ?? string.Empty;
        return order.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Loads every level named in <c>Global/Levels.ini</c>, in the order listed there.</summary>
    public static async Task<IReadOnlyList<LevelSummary>> LoadAllAsync(IAssetFileProvider fileProvider)
    {
        var levelNames = await LoadLevelNamesAsync(fileProvider);
        var summaries = new List<LevelSummary>(levelNames.Count);
        foreach (var levelName in levelNames)
        {
            summaries.Add(await LoadAsync(fileProvider, levelName));
        }

        return summaries;
    }

    /// <summary>Loads one level's title and thumbnail art without loading its full playable World2D.</summary>
    public static async Task<LevelSummary> LoadAsync(IAssetFileProvider fileProvider, string levelName)
    {
        var levelFolder = $"{AssetPathResolver.LevelsRoot}/{levelName}";

        var settingsContent = await fileProvider.TryReadTextAsync($"{levelFolder}/{levelName}_settings.ini");
        var settings = IniDocument.Parse(settingsContent ?? string.Empty);
        var emptyChar = ParseEmptyChar(settings.TryGetValue("Layout", "EmptyChar"));
        var title = settings.TryGetValue("Level", "Title") is { Length: > 0 } titleValue ? titleValue : levelName;

        // Both files are optional (see docs/AssetFormat.md §3.1) - a level without thumbnail art
        // yields a single blank thumbnail frame rather than failing to load. Like any other clip,
        // multiple frames are separated by "//end"; unlike a sprite clip, every frame here is
        // fixed at exactly ThumbnailWidth x ThumbnailHeight rather than inferred from content.
        var charsContent = await fileProvider.TryReadTextAsync($"{levelFolder}/{levelName}_thumb_characters.txt");
        var charFrames = charsContent is null
            ? [EmptyGrid(emptyChar)]
            : AssetTextReader.ParseFixedSizeFrames(charsContent, ThumbnailWidth, ThumbnailHeight, emptyChar);

        var foreContent = await fileProvider.TryReadTextAsync($"{levelFolder}/{levelName}_thumb_foregroundcolors.txt");
        var foreFrames = AssetTextReader.ParseFixedSizeSecondaryFrames(
            foreContent, charFrames.Count, ThumbnailWidth, ThumbnailHeight, emptyChar);

        var thumbnailFrames = new List<ThumbnailFrame>(charFrames.Count);
        for (var i = 0; i < charFrames.Count; i++)
        {
            thumbnailFrames.Add(new ThumbnailFrame { Chars = charFrames[i], Fore = foreFrames[i] });
        }

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

        var palette = await ColorPalette.LoadAsync(fileProvider, levelName);

        return new LevelSummary(
            levelName, title, thumbnailFrames, emptyChar, palette,
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

    private static char ParseEmptyChar(string? rawValue) =>
        string.IsNullOrEmpty(rawValue) ? ' ' : rawValue[0];
}
