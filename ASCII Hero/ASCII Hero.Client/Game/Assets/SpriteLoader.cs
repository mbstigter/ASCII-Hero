namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// Loads a sprite asset's files (settings.ini + one or more clips' characters/foregroundcolors/
/// backgroundcolors layers) into
/// Global/World fallback rule (via <see cref="AssetPathResolver"/>) and the layer parsing/padding
/// rules (via <see cref="AssetTextReader"/>). Reused identically for the player, static
/// platforms, and any other sprite-backed object.
/// </summary>
public class SpriteLoader(IAssetFileProvider fileProvider)
{
    /// <summary>
    /// Loads the given asset, reading only the requested clips (the caller - typically the world
    /// loader - knows which clips are actually needed, e.g. from a world's own objects.ini).
    /// </summary>
    public async Task<SpriteAsset> LoadAsync(string assetName, IReadOnlyList<string> clipNames, string? worldName)
    {
        var folder = await AssetPathResolver.ResolveSpriteFolderAsync(fileProvider, assetName, worldName);
        var settingsContent = await fileProvider.TryReadTextAsync($"{folder}/{AssetPathResolver.GetFileStem(assetName)}_settings.ini");
        var settings = IniDocument.Parse(settingsContent ?? string.Empty);

        var emptyChar = IniValueParser.ParseEmptyChar(settings.TryGetValue("Layout", "EmptyChar"));
        var tileAxis = ParseTileAxis(settings.TryGetValue("Layout", "TileAxis"));
        var defaultForeColor = IniValueParser.ParseColorCode(settings.TryGetValue("Colors", "DefaultForegroundColor"));
        var defaultBackColor = IniValueParser.ParseColorCode(settings.TryGetValue("Colors", "DefaultBackgroundColor"));
        var defaultMaterial = settings.TryGetValue("Physics", "DefaultMaterial");
        var defaultFrameDurationSeconds = ParseFrameDurationSeconds(settings.TryGetValue("Animation", "FrameDurationSeconds"));
        var defaultAnimationMode = ParseAnimationMode(settings.TryGetValue("Animation", "Mode"));
        var defaultDefaultFrame = ParseDefaultFrame(settings.TryGetValue("Animation", "DefaultFrame"));
        var (poses, defaultPose) = ParsePoses(assetName, settings.Section("Poses"));
        var clipFolders = ParseClipFolders(settings.Section("ClipFolders"));

        var allClipNames = new List<string>(clipNames);
        if (poses is not null)
        {
            foreach (var poseDef in poses.Values)
            {
                allClipNames.Add(poseDef.IdleClip);
                allClipNames.AddRange(poseDef.DirectionalClips.Values);
            }
        }

        var distinctClipNames = allClipNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var clips = new Dictionary<string, SpriteClip>(StringComparer.OrdinalIgnoreCase);
        foreach (var clipName in distinctClipNames)
        {
            // Per-clip [Animation.{clipName}] overrides fall back to the asset-wide [Animation]
            // section for any key it doesn't itself set.
            var clipAnimationSection = settings.Section($"Animation.{clipName}");
            var frameDurationSeconds = clipAnimationSection.TryGetValue("FrameDurationSeconds", out var clipDurationText)
                ? ParseFrameDurationSeconds(clipDurationText)
                : defaultFrameDurationSeconds;
            var animationMode = clipAnimationSection.TryGetValue("Mode", out var clipModeText)
                ? ParseAnimationMode(clipModeText)
                : defaultAnimationMode;
            var clipDefaultFrame = clipAnimationSection.TryGetValue("DefaultFrame", out var clipDefaultFrameText)
                ? ParseDefaultFrame(clipDefaultFrameText)
                : defaultDefaultFrame;
            var effectDurationSeconds = clipAnimationSection.TryGetValue("EffectDurationSeconds", out var effectDurationText)
                ? ParseFrameDurationSeconds(effectDurationText)
                : null;
            // Per-clip [Layout.{clipName}] TileAxis likewise falls back to the asset-wide [Layout].
            var clipTileAxis = settings.Section($"Layout.{clipName}").TryGetValue("TileAxis", out var clipTileAxisText)
                ? ParseTileAxis(clipTileAxisText)
                : tileAxis;

            var clipFolder = ResolveClipFolder(folder, clipName, clipFolders);

            clips[clipName] = await LoadClipAsync(
                clipFolder, assetName, clipName, emptyChar,
                frameDurationSeconds, animationMode, clipDefaultFrame ?? 0, effectDurationSeconds, clipTileAxis);
        }

        return new SpriteAsset
        {
            Name = assetName,
            EmptyChar = emptyChar,
            Clips = clips,
            Poses = poses,
            DefaultPose = defaultPose,
            PoseSounds = new Dictionary<string, string>(
                settings.Section("Sounds").Where(entry => !string.IsNullOrWhiteSpace(entry.Value)),
                StringComparer.OrdinalIgnoreCase),
            DefaultForeColor = defaultForeColor,
            DefaultBackColor = defaultBackColor,
            DefaultMaterial = defaultMaterial,
        };
    }

    private async Task<SpriteClip> LoadClipAsync(
        string folder,
        string assetName,
        string clipName,
        char emptyChar,
        double? frameDurationSeconds,
        AnimationMode animationMode,
        int defaultFrame,
        double? effectDurationSeconds,
        TileAxis tileAxis)
    {
        var baseName = $"{folder}/{AssetPathResolver.GetFileStem(assetName)}_{clipName}";

        var charsTask = fileProvider.TryReadTextAsync($"{baseName}_characters.txt");
        var foreTask = fileProvider.TryReadTextAsync($"{baseName}_foregroundcolors.txt");
        var backTask = fileProvider.TryReadTextAsync($"{baseName}_backgroundcolors.txt");
        await Task.WhenAll(charsTask, foreTask, backTask);

        var charsContent = charsTask.Result
            ?? throw new FileNotFoundException($"Missing required characters layer for {assetName}/{clipName}.");
        var foreContent = foreTask.Result;
        var backContent = backTask.Result;

        var charFrames = AssetTextReader.ParseCharsLayer(charsContent, emptyChar);
        var foreFrames = AssetTextReader.ParseSecondaryLayer(foreContent, charFrames, emptyChar);
        var backFrames = AssetTextReader.ParseSecondaryLayer(backContent, charFrames, emptyChar);

        var frames = new List<SpriteFrame>(charFrames.Count);
        for (var i = 0; i < charFrames.Count; i++)
        {
            frames.Add(new SpriteFrame
            {
                Chars = charFrames[i],
                Fore = foreFrames[i],
                Back = backFrames[i],
            });
        }

        return new SpriteClip
        {
            Name = clipName,
            Frames = frames,
            FrameDurationSeconds = frameDurationSeconds,
            AnimationMode = animationMode,
            DefaultFrame = defaultFrame,
            EffectDurationSeconds = effectDurationSeconds,
            TileAxis = tileAxis,
        };
    }

    /// <summary>
    /// Parses the optional <c>[ClipFolders]</c> section, mapping a clip-name prefix (e.g. "walk",
    /// matching "walk_idle"/"walk_left"/"walk_right") to the subfolder its files live in, for
    /// sprites busy enough to want to group clips by pose instead of keeping every clip's files
    /// flat in the asset's root folder. Absent entirely for simple single-/few-clip assets (e.g.
    /// Pipe), which keep the flat layout.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ParseClipFolders(
        IReadOnlyDictionary<string, string> clipFoldersSection) =>
        new Dictionary<string, string>(clipFoldersSection, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the folder a given clip's files should be read from: the asset's own root
    /// <paramref name="assetFolder"/> by default, or a subfolder if <paramref name="clipFolders"/>
    /// maps the clip name (or its "_"-prefix, e.g. "walk" for "walk_left") to one.
    /// </summary>
    private static string ResolveClipFolder(
        string assetFolder, string clipName, IReadOnlyDictionary<string, string> clipFolders)
    {
        if (clipFolders.Count == 0)
        {
            return assetFolder;
        }

        if (clipFolders.TryGetValue(clipName, out var exactSubFolder))
        {
            return $"{assetFolder}/{exactSubFolder}";
        }

        var prefix = clipName;
        var underscoreIndex = clipName.IndexOf('_');
        if (underscoreIndex > 0)
        {
            prefix = clipName[..underscoreIndex];
        }

        return clipFolders.TryGetValue(prefix, out var subFolder)
            ? $"{assetFolder}/{subFolder}"
            : assetFolder;
    }

    private static (IReadOnlyDictionary<string, PoseDefinition>? Poses, string? DefaultPose) ParsePoses(
        string assetName,
        IReadOnlyDictionary<string, string> posesSection)
    {
        if (posesSection.Count == 0)
        {
            return (null, null);
        }

        var defaultPose = posesSection.TryGetValue("Default", out var defaultValue) ? defaultValue : null;
        var poses = new Dictionary<string, PoseDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in posesSection)
        {
            if (string.Equals(key, "Default", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var clipNames = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (clipNames.Length == 0)
            {
                continue;
            }

            // Each clip name's own trailing suffix (_idle/_left/_right/_up/_down) says which
            // Facing it's for, rather than a fixed slot position/count, so a pose can freely
            // declare any subset of the five facings it actually needs (just Left/Right for
            // walking, just Up/Down for climbing, or all four directions plus Idle for something
            // like swimming).
            string? idleClip = null;
            var directionalClips = new Dictionary<Facing, string>();
            foreach (var clipName in clipNames)
            {
                var facing = ResolveFacingFromClipName(clipName);
                if (facing == Facing.Idle)
                {
                    idleClip = clipName;
                }
                else
                {
                    directionalClips[facing] = clipName;
                }
            }

            // A pose with no clip whose suffix actually resolved to Idle (e.g. an author typo,
            // or a pose authored with only directional clips) falls back to its first-listed
            // clip as Idle, so a malformed line still resolves to *something* playable rather than
            // throwing at spawn.
            idleClip ??= clipNames[0];

            poses[key] = new PoseDefinition
            {
                IdleClip = idleClip,
                DirectionalClips = directionalClips,
            };
        }

        if (string.IsNullOrWhiteSpace(defaultPose))
        {
            throw new InvalidDataException($"Sprite '{assetName}' declares [Poses] but no Default pose.");
        }

        if (!poses.ContainsKey(defaultPose))
        {
            throw new InvalidDataException($"Sprite '{assetName}' [Poses] Default '{defaultPose}' is not a declared pose.");
        }

        return (poses, defaultPose);
    }

    /// <summary>
    /// Resolves which <see cref="Facing"/> a pose's clip name is for, from its own trailing
    /// suffix (<c>_idle</c>/<c>_left</c>/<c>_right</c>/<c>_up</c>/<c>_down</c>). A clip with none
    /// of these suffixes is treated as <see cref="Facing.Idle"/> (the neutral/only orientation for
    /// a pose that declares just one clip).
    /// </summary>
    private static Facing ResolveFacingFromClipName(string clipName)
    {
        if (clipName.EndsWith("_left", StringComparison.OrdinalIgnoreCase)) return Facing.Left;
        if (clipName.EndsWith("_right", StringComparison.OrdinalIgnoreCase)) return Facing.Right;
        if (clipName.EndsWith("_up", StringComparison.OrdinalIgnoreCase)) return Facing.Up;
        if (clipName.EndsWith("_down", StringComparison.OrdinalIgnoreCase)) return Facing.Down;
        return Facing.Idle;
    }

    private static TileAxis ParseTileAxis(string? rawValue) =>
        Enum.TryParse<TileAxis>(rawValue, ignoreCase: true, out var parsed) ? parsed : TileAxis.None;

    internal static double? ParseFrameDurationSeconds(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return double.TryParse(rawValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    internal static AnimationMode ParseAnimationMode(string? rawValue) =>
        Enum.TryParse<AnimationMode>(rawValue, ignoreCase: true, out var parsed) ? parsed : AnimationMode.Loop;

    internal static int? ParseDefaultFrame(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return int.TryParse(rawValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
