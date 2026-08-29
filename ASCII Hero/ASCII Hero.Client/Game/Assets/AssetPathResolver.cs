namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// Resolves which folder an asset's files should be read from, applying the Global vs. Level
/// override/fallback rule from AssetFormat.md section 1.1: a level-local
/// Sprites/{AssetName}/ folder (if present) is used instead of Global/Sprites/{AssetName}/,
/// with the level-local folder's mere presence acting as the override signal (checked here by
/// probing for that folder's settings.ini file).
/// </summary>
public static class AssetPathResolver
{
    public const string GlobalRoot = "Assets/Global";
    public const string LevelsRoot = "Assets/Levels";

    public static async Task<string> ResolveSpriteFolderAsync(
        IAssetFileProvider fileProvider, string assetName, string? levelName)
    {
        if (levelName is not null)
        {
            var levelFolder = $"{LevelsRoot}/{levelName}/Sprites/{assetName}";
            var levelSettingsPath = $"{levelFolder}/{assetName}_settings.ini";
            if (await fileProvider.TryReadTextAsync(levelSettingsPath) is not null)
            {
                return levelFolder;
            }
        }

        return $"{GlobalRoot}/Sprites/{assetName}";
    }
}
