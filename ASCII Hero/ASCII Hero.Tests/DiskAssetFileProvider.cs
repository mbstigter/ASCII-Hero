using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Tests;

/// <summary>Reads the client's real shipped assets from wwwroot on disk, standing in for the browser's HTTP provider.</summary>
internal sealed class DiskAssetFileProvider : IAssetFileProvider
{
    public static readonly string WwwRoot = FindWwwRoot();

    public Task<string?> TryReadTextAsync(string relativePath)
    {
        var path = Path.Combine(WwwRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return Task.FromResult(File.Exists(path) ? File.ReadAllText(path) : null);
    }

    private static string FindWwwRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "ASCII Hero.Client", "wwwroot");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not find ASCII Hero.Client/wwwroot above the test output folder.");
    }
}
