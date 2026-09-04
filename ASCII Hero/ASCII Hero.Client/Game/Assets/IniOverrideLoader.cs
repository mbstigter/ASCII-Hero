namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// Shared implementation of the Global-then-World ini override/fallback rule from
/// docs/AssetFormat.md section 1.1: reads <c>Global/{fileName}</c>, then - if a world is given -
/// merges an optional <c>Worlds/{worldName}/{fileName}</c> over it, with the world file's entries
/// taking precedence for same-named keys/sections while anything only defined globally still
/// applies. Used by both <see cref="ColorPalette"/> and <see cref="MaterialLibrary"/>, which
/// otherwise differ only in how they turn a parsed <see cref="IniDocument"/> into their own
/// dictionary entries.
/// </summary>
public static class IniOverrideLoader
{
    /// <summary>
    /// Loads and merges <c>Global/{fileName}</c> and (if present) <c>Worlds/{worldName}/{fileName}</c>
    /// into a fresh dictionary, applying <paramref name="mergeInto"/> once per file (global first,
    /// then world) so later calls naturally override earlier ones.
    /// </summary>
    public static async Task<Dictionary<TKey, TValue>> LoadAsync<TKey, TValue>(
        IAssetFileProvider fileProvider,
        string? worldName,
        string fileName,
        Action<Dictionary<TKey, TValue>, IniDocument> mergeInto,
        IEqualityComparer<TKey>? keyComparer = null)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, TValue>(keyComparer);

        var globalContent = await fileProvider.TryReadTextAsync($"{AssetPathResolver.GlobalRoot}/{fileName}");
        if (globalContent is not null)
        {
            mergeInto(result, IniDocument.Parse(globalContent));
        }

        if (worldName is not null)
        {
            var worldContent = await fileProvider.TryReadTextAsync($"{AssetPathResolver.WorldsRoot}/{worldName}/{fileName}");
            if (worldContent is not null)
            {
                mergeInto(result, IniDocument.Parse(worldContent));
            }
        }

        return result;
    }
}
