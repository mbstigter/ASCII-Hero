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

/// <summary>Reads asset files served as static web content from wwwroot/Assets via HttpClient.</summary>
public class HttpAssetFileProvider(HttpClient httpClient) : IAssetFileProvider
{
    public async Task<string?> TryReadTextAsync(string relativePath)
    {
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
}
