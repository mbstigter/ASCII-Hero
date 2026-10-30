namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// The named interface colors (Global/InterfaceColors.ini): CSS hex colors for menus, HUD,
/// overlays and message boxes. Separate from <see cref="ColorPalette"/>, which is for world and
/// sprite assets. Every color is required; a missing entry is a load error. Loading is handled by
/// the shared <see cref="IniOverrideLoader"/> (global file only).
/// </summary>
public class InterfaceColors
{
    private readonly Dictionary<string, string> _colors;

    private InterfaceColors(Dictionary<string, string> colors)
    {
        _colors = colors;

        Black = Get("Black");
        GreenDark = Get("GreenDark");
        Green = Get("Green");
        GreenLight = Get("GreenLight");
        AmberDark = Get("AmberDark");
        Amber = Get("Amber");
        AmberLight = Get("AmberLight");
        RedDark = Get("RedDark");
        Red = Get("Red");
        RedLight = Get("RedLight");
        BlueDark = Get("BlueDark");
        Blue = Get("Blue");
        BlueLight = Get("BlueLight");
    }

    public string Black { get; }

    public string GreenDark { get; }
    public string Green { get; }
    public string GreenLight { get; }

    public string AmberDark { get; }
    public string Amber { get; }
    public string AmberLight { get; }

    public string RedDark { get; }
    public string Red { get; }
    public string RedLight { get; }

    public string BlueDark { get; }
    public string Blue { get; }
    public string BlueLight { get; }

    public static async Task<InterfaceColors> LoadAsync(IAssetFileProvider fileProvider)
    {
        var colors = await IniOverrideLoader.LoadAsync<string, string>(fileProvider, null, "InterfaceColors.ini", Merge, StringComparer.OrdinalIgnoreCase);
        return new InterfaceColors(colors);
    }

    private string Get(string name) =>
        _colors.TryGetValue(name, out var value)
            ? value
            : throw new InvalidOperationException($"Global/InterfaceColors.ini is missing a required [Colors] {name} value.");

    private static void Merge(Dictionary<string, string> colors, IniDocument ini)
    {
        foreach (var (key, value) in ini.Section("Colors"))
        {
            colors[key] = value;
        }
    }
}
