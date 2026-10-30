namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// Resolves which folder a sprite asset's files should be read from, applying the Global vs.
/// World override rule: a world-local Sprites/{AssetName}/ folder is used instead of
/// Global/Sprites/{AssetName}/ when present, detected by probing for that folder's settings.ini
/// file. An asset name may include a group folder (e.g. "Fish/Goldfish"), in which case the
/// asset's files are still named after its last segment only (see <see cref="GetFileStem"/>).
/// </summary>
public static class AssetPathResolver
{
    public const string GlobalRoot = "Assets/Global";
    public const string WorldsRoot = "Assets/Worlds";

    /// <summary>
    /// Returns the name an asset's own files are prefixed with: the asset name's last
    /// "/"-separated segment ("Fish/Goldfish" -> "Goldfish"), or the whole name if ungrouped.
    /// </summary>
    public static string GetFileStem(string assetName) => assetName[(assetName.LastIndexOf('/') + 1)..];

    public static async Task<string> ResolveSpriteFolderAsync(
        IAssetFileProvider fileProvider, string assetName, string? worldName)
    {
        if (worldName is not null)
        {
            var worldFolder = $"{WorldsRoot}/{worldName}/Sprites/{assetName}";
            var worldSettingsPath = $"{worldFolder}/{GetFileStem(assetName)}_settings.ini";
            if (await fileProvider.TryReadTextAsync(worldSettingsPath) is not null)
            {
                return worldFolder;
            }
        }

        return $"{GlobalRoot}/Sprites/{assetName}";
    }
}
