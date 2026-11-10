namespace ASCII_Hero.Client.Game.Assets;

/// <summary>Fetches raw asset file content by relative path.</summary>
public interface IAssetFileProvider
{
    /// <summary>
    /// Returns the file's text content, or null if the file does not exist (404). Any other
    /// failure (server error, network problem) throws, so a broken download is never mistaken for
    /// an optional file that is simply absent.
    /// </summary>
    Task<string?> TryReadTextAsync(string relativePath);
}

/// <summary>
/// Reads asset files served as static web content from wwwroot/Assets via HttpClient. When
/// <paramref name="manifestPath"/> names a file listing every asset path (one per line), a file
/// that is not listed is reported absent without any request. A published site lists its files
/// this way so that the many optional files a world does not have cost no (failing) round trip;
/// without a manifest (e.g. local development) every file is simply requested.
/// </summary>
public class HttpAssetFileProvider(HttpClient httpClient, string? manifestPath = null) : IAssetFileProvider
{
    private HashSet<string>? _manifest;
    private bool _manifestLoaded;

    public async Task<string?> TryReadTextAsync(string relativePath)
    {
        await EnsureManifestLoadedAsync();
        if (_manifest is not null && !_manifest.Contains(relativePath))
        {
            return null;
        }

        using var response = await httpClient.GetAsync(relativePath);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Could not load asset '{relativePath}': HTTP {(int)response.StatusCode} {response.ReasonPhrase}.",
                inner: null,
                statusCode: response.StatusCode);
        }

        return await response.Content.ReadAsStringAsync();
    }

    // A missing or unreadable manifest just means "no filtering"; the real requests still work.
    private async Task EnsureManifestLoadedAsync()
    {
        if (_manifestLoaded || manifestPath is null)
        {
            return;
        }

        _manifestLoaded = true;
        try
        {
            using var response = await httpClient.GetAsync(manifestPath);
            if (response.IsSuccessStatusCode)
            {
                var lines = (await response.Content.ReadAsStringAsync())
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                _manifest = new HashSet<string>(lines, StringComparer.Ordinal);
            }
        }
        catch (HttpRequestException)
        {
        }
    }
}
