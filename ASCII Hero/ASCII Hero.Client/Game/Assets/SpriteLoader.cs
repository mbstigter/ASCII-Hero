namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// Loads a sprite asset's files (settings.ini + one or more clips' characters/foregroundcolors/
/// backgroundcolors/materials layers) into an in-memory <see cref="SpriteAsset"/>, applying the
/// Global/Level fallback rule (via <see cref="AssetPathResolver"/>) and the layer parsing/padding
/// rules (via <see cref="AssetTextReader"/>). Reused identically for the player, static
/// platforms, and any other sprite-backed object - there is only one loading concept, per
/// AssetFormat.md section 5.
/// </summary>
public class SpriteLoader(IAssetFileProvider fileProvider)
{
    /// <summary>
    /// Loads the given asset, reading only the requested clips (the caller - typically the world
    /// loader - knows which clips are actually needed, e.g. from Level1_objects.ini).
    /// </summary>
    public async Task<SpriteAsset> LoadAsync(string assetName, IReadOnlyList<string> clipNames, string? levelName)
    {
        var folder = await AssetPathResolver.ResolveSpriteFolderAsync(fileProvider, assetName, levelName);
        var settingsContent = await fileProvider.TryReadTextAsync($"{folder}/{assetName}_settings.ini");
        var settings = IniDocument.Parse(settingsContent ?? string.Empty);

        var emptyChar = ParseEmptyChar(settings.TryGetValue("Layout", "EmptyChar"));
        var tileAxis = ParseTileAxis(settings.TryGetValue("Layout", "TileAxis"));
        var defaultMaterial = settings.TryGetValue("Physics", "DefaultMaterial");
        var materialCodes = settings.Section("MaterialCodes");
        var defaultFrameDurationSeconds = ParseFrameDurationSeconds(settings.TryGetValue("Animation", "FrameDurationSeconds"));
        var defaultAnimationMode = ParseAnimationMode(settings.TryGetValue("Animation", "Mode"));
        var defaultDefaultFrame = ParseDefaultFrame(settings.TryGetValue("Animation", "DefaultFrame"));
        var (stances, defaultStance) = ParseStances(settings.Section("Stances"));

        var allClipNames = new List<string>(clipNames);
        if (stances is not null)
        {
            foreach (var stanceDef in stances.Values)
            {
                allClipNames.Add(stanceDef.IdleClip);
                if (stanceDef.LeftClip is not null) allClipNames.Add(stanceDef.LeftClip);
                if (stanceDef.RightClip is not null) allClipNames.Add(stanceDef.RightClip);
            }
        }

        var distinctClipNames = allClipNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var clips = new Dictionary<string, SpriteClip>(StringComparer.OrdinalIgnoreCase);
        foreach (var clipName in distinctClipNames)
        {
            // Per-clip [Animation.{clipName}] overrides fall back to the asset-wide [Animation]
            // section for any key it doesn't itself set (see docs/AssetFormat.md §2.4).
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

            clips[clipName] = await LoadClipAsync(
                folder, assetName, clipName, emptyChar, defaultMaterial, materialCodes,
                frameDurationSeconds, animationMode, clipDefaultFrame ?? 0);
        }

        return new SpriteAsset
        {
            Name = assetName,
            EmptyChar = emptyChar,
            Clips = clips,
            TileAxis = tileAxis,
            Stances = stances,
            DefaultStance = defaultStance,
        };
    }

    private async Task<SpriteClip> LoadClipAsync(
        string folder,
        string assetName,
        string clipName,
        char emptyChar,
        string? defaultMaterial,
        IReadOnlyDictionary<string, string> materialCodes,
        double? frameDurationSeconds,
        AnimationMode animationMode,
        int defaultFrame)
    {
        var baseName = $"{folder}/{assetName}_{clipName}";

        var charsContent = await fileProvider.TryReadTextAsync($"{baseName}_characters.txt")
            ?? throw new FileNotFoundException($"Missing required characters layer for {assetName}/{clipName}.");
        var foreContent = await fileProvider.TryReadTextAsync($"{baseName}_foregroundcolors.txt");
        var backContent = await fileProvider.TryReadTextAsync($"{baseName}_backgroundcolors.txt");
        var materialContent = await fileProvider.TryReadTextAsync($"{baseName}_materials.txt");

        var charFrames = AssetTextReader.ParseCharsLayer(charsContent, emptyChar);
        var foreFrames = AssetTextReader.ParseSecondaryLayer(foreContent, charFrames, emptyChar);
        var backFrames = AssetTextReader.ParseSecondaryLayer(backContent, charFrames, emptyChar);
        var materialFrames = materialContent is null
            ? null
            : AssetTextReader.ParseSecondaryLayer(materialContent, charFrames, emptyChar);

        var frames = new List<SpriteFrame>(charFrames.Count);
        for (var i = 0; i < charFrames.Count; i++)
        {
            frames.Add(new SpriteFrame
            {
                Chars = charFrames[i],
                Fore = foreFrames[i],
                Back = backFrames[i],
                Materials = ResolveMaterials(charFrames[i], materialFrames?[i], emptyChar, defaultMaterial, materialCodes),
            });
        }

        return new SpriteClip
        {
            Name = clipName,
            Frames = frames,
            FrameDurationSeconds = frameDurationSeconds,
            AnimationMode = animationMode,
            DefaultFrame = defaultFrame,
        };
    }

    private static string?[,] ResolveMaterials(
        char[,] chars,
        char[,]? materialGrid,
        char emptyChar,
        string? defaultMaterial,
        IReadOnlyDictionary<string, string> materialCodes)
    {
        var height = chars.GetLength(0);
        var width = chars.GetLength(1);
        var result = new string?[height, width];

        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                if (chars[row, col] == emptyChar)
                {
                    continue;
                }

                if (materialGrid is null)
                {
                    // Whole-object shorthand: every non-empty cell uses DefaultMaterial.
                    result[row, col] = defaultMaterial;
                    continue;
                }

                var code = materialGrid[row, col];
                if (code == emptyChar)
                {
                    continue;
                }

                var codeKey = code.ToString();
                if (materialCodes.TryGetValue(codeKey, out var mapped) &&
                    !mapped.StartsWith("(inherit", StringComparison.OrdinalIgnoreCase))
                {
                    result[row, col] = mapped;
                }
                else
                {
                    result[row, col] = defaultMaterial;
                }
            }
        }

        return result;
    }

    private static (IReadOnlyDictionary<string, StanceDefinition>? Stances, string? DefaultStance) ParseStances(
        IReadOnlyDictionary<string, string> stancesSection)
    {
        if (stancesSection.Count == 0)
        {
            return (null, null);
        }

        var defaultStance = stancesSection.TryGetValue("Default", out var defaultValue) ? defaultValue : null;
        var stances = new Dictionary<string, StanceDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in stancesSection)
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

            stances[key] = new StanceDefinition
            {
                IdleClip = clipNames[0],
                LeftClip = clipNames.Length > 1 ? clipNames[1] : null,
                RightClip = clipNames.Length > 2 ? clipNames[2] : null,
            };
        }

        return (stances, defaultStance);
    }

    private static char ParseEmptyChar(string? rawValue)
    {
        if (string.IsNullOrEmpty(rawValue))
        {
            return ' ';
        }

        return rawValue[0];
    }

    private static TileAxis ParseTileAxis(string? rawValue) =>
        Enum.TryParse<TileAxis>(rawValue, ignoreCase: true, out var parsed) ? parsed : TileAxis.None;

    private static double? ParseFrameDurationSeconds(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return double.TryParse(rawValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static AnimationMode ParseAnimationMode(string? rawValue) =>
        Enum.TryParse<AnimationMode>(rawValue, ignoreCase: true, out var parsed) ? parsed : AnimationMode.Loop;

    private static int? ParseDefaultFrame(string? rawValue)
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
