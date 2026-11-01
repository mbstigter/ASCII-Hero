namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// The interface colors (Global/InterfaceColors.ini): CSS hex colors for menus, HUD, overlays and
/// message boxes. Separate from <see cref="ColorPalette"/>, which is for world and sprite assets.
/// Each color section (<c>[Green]</c>, <c>[Amber]</c>, ...) holds a Dark, Normal and Light tint;
/// <c>[Defaults] Foreground</c> names the section used for the whole interface (Green if unset), and
/// <c>[Defaults] Background</c> is the canvas color. Any value that is missing (or the
/// whole file) falls back to <see cref="Constants.RenderConstants.DefaultForegroundColor"/> for every
/// tint and <see cref="Constants.RenderConstants.DefaultBackgroundColor"/> for the background, so
/// the game is then simply green on black.
/// </summary>
public class InterfaceColors
{
    private InterfaceColors(IniDocument ini)
    {
        Green = GetTints(ini, "Green");
        Amber = GetTints(ini, "Amber");
        Red = GetTints(ini, "Red");
        Blue = GetTints(ini, "Blue");
        White = GetTints(ini, "White");
        Interface = GetTints(ini, ini.TryGetValue("Defaults", "Foreground") ?? "Green");
        Background = ini.TryGetValue("Defaults", "Background") ?? Constants.RenderConstants.DefaultBackgroundColor;
    }

    /// <summary>The interface color (menu, logo, HUD, loading bar, ...), chosen by <c>[Defaults] Foreground</c>. Also the fallback color of uncolored world characters.</summary>
    public ColorTints Interface { get; }

    /// <summary>The color behind everything (<c>[Defaults] Background</c>), unless a world has its own backdrop.</summary>
    public string Background { get; }

    /// <summary>The Level Complete box.</summary>
    public ColorTints Green { get; }

    /// <summary>The exit confirmation box.</summary>
    public ColorTints Amber { get; }

    /// <summary>The Game Over box.</summary>
    public ColorTints Red { get; }

    /// <summary>Available as the interface color.</summary>
    public ColorTints Blue { get; }

    /// <summary>Available as the interface color.</summary>
    public ColorTints White { get; }

    /// <summary>Colors used when no InterfaceColors.ini is available: green on black.</summary>
    public static InterfaceColors Fallback { get; } = new(IniDocument.Parse(string.Empty));

    public static async Task<InterfaceColors> LoadAsync(IAssetFileProvider fileProvider)
    {
        var content = await fileProvider.TryReadTextAsync($"{AssetPathResolver.GlobalRoot}/InterfaceColors.ini");
        return content is null ? Fallback : new InterfaceColors(IniDocument.Parse(content));
    }

    private static ColorTints GetTints(IniDocument ini, string name) =>
        new(Get(ini, name, $"{name}Dark"), Get(ini, name, $"{name}Normal"), Get(ini, name, $"{name}Light"));

    private static string Get(IniDocument ini, string section, string key) =>
        ini.TryGetValue(section, key) ?? Constants.RenderConstants.DefaultForegroundColor;
}
