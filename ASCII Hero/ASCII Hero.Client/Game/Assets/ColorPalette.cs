namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// The shared color palette (Global/Colors.ini merged with an optional level-local
/// Colors.ini, per AssetFormat.md section 1.1: level entries override same-named codes,
/// codes only defined globally still apply). Maps a single-character color code (as found in
/// any _foregroundcolors.txt/_backgroundcolors.txt) to a CSS hex color string.
/// </summary>
public class ColorPalette
{
    private readonly Dictionary<char, string> _colors;

    private ColorPalette(Dictionary<char, string> colors) => _colors = colors;

    public static async Task<ColorPalette> LoadAsync(IAssetFileProvider fileProvider, string? levelName)
    {
        var colors = new Dictionary<char, string>();

        var globalContent = await fileProvider.TryReadTextAsync($"{AssetPathResolver.GlobalRoot}/Colors.ini");
        Merge(colors, globalContent);

        if (levelName is not null)
        {
            var levelContent = await fileProvider.TryReadTextAsync($"{AssetPathResolver.LevelsRoot}/{levelName}/Colors.ini");
            Merge(colors, levelContent);
        }

        return new ColorPalette(colors);
    }

    /// <summary>Looks up the CSS color for a code, or null if the code is undefined (caller should fall back to a default).</summary>
    public string? TryGetColor(char code) => _colors.TryGetValue(code, out var value) ? value : null;

    private static void Merge(Dictionary<char, string> colors, string? content)
    {
        if (content is null)
        {
            return;
        }

        var ini = IniDocument.Parse(content);
        foreach (var (key, value) in ini.Section("Colors"))
        {
            if (key.Length == 1)
            {
                colors[key[0]] = value;
            }
        }
    }
}
