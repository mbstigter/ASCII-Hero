namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// Resolves which folder a sprite asset's files should be read from, applying the Global vs.
/// World override rule: a world-local Sprites/{AssetName}/ folder is used instead of
/// Global/Sprites/{AssetName}/ when present, detected by probing for that folder's settings.ini
/// file.
/// </summary>
public static class AssetPathResolver
{
    public const string GlobalRoot = "Assets/Global";
    public const string WorldsRoot = "Assets/Worlds";

    public static async Task<string> ResolveSpriteFolderAsync(
        IAssetFileProvider fileProvider, string assetName, string? worldName)
    {
        if (worldName is not null)
        {
            var worldFolder = $"{WorldsRoot}/{worldName}/Sprites/{assetName}";
            var worldSettingsPath = $"{worldFolder}/{assetName}_settings.ini";
            if (await fileProvider.TryReadTextAsync(worldSettingsPath) is not null)
            {
                return worldFolder;
            }
        }

        return $"{GlobalRoot}/Sprites/{assetName}";
    }
}
