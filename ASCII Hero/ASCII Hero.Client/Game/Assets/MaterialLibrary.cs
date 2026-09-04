namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// One material's physical properties, as read from a <c>Materials.ini</c> section (see
/// docs/AssetFormat.md and Assets/Global/Materials.ini's own header comment).
/// </summary>
/// <param name="Density">Relative mass per world-cell "volume"; drives <see cref="World.Body2D.Mass"/>.</param>
/// <param name="Friction">0 = frictionless, 1 = very grippy.</param>
/// <param name="Restitution">Bounciness; 0 = no bounce, 1 = perfectly elastic.</param>
public readonly record struct Material(double Density, double Friction, double Restitution);

/// <summary>
/// The shared material library (Global/Materials.ini merged with an optional level-local
/// Materials.ini, per docs/AssetFormat.md section 1.1: level entries override same-named
/// sections, sections only defined globally still apply). Maps a material name (as found in a
/// sprite's <c>DefaultMaterial</c>/<c>MaterialCodes</c> settings or a level's own
/// <c>_materials.txt</c> per-cell layer, resolved by <see cref="SpriteLoader"/> into
/// <see cref="SpriteFrame.Materials"/>) to its physical properties. Loading/merging itself is
/// handled by the shared <see cref="IniOverrideLoader"/>, which <see cref="ColorPalette"/> also
/// uses for its identical Global-then-Level fallback rule.
/// </summary>
public class MaterialLibrary
{
    /// <summary>
    /// Fallback used when a body has no resolvable material (e.g. a sprite with neither
    /// <c>DefaultMaterial</c> nor a per-cell material layer) - physically inert (massless,
    /// frictionless, no bounce) so an unconfigured body doesn't silently gain unexpected physics
    /// behavior rather than failing loudly.
    /// </summary>
    public static readonly Material Undefined = new(Density: 0.0, Friction: 0.0, Restitution: 0.0);

    private readonly Dictionary<string, Material> _materials;

    private MaterialLibrary(Dictionary<string, Material> materials) => _materials = materials;

    public static async Task<MaterialLibrary> LoadAsync(IAssetFileProvider fileProvider, string? worldName)
    {
        var materials = await IniOverrideLoader.LoadAsync<string, Material>(
            fileProvider, worldName, "Materials.ini", Merge, StringComparer.OrdinalIgnoreCase);
        return new MaterialLibrary(materials);
    }

    /// <summary>
    /// Looks up a material's properties by name, or <see cref="Undefined"/> if the name is null
    /// or not defined in this library (rather than throwing - a body with no configured material
    /// should behave physically inert, not crash the level load).
    /// </summary>
    public Material Get(string? materialName) =>
        materialName is not null && _materials.TryGetValue(materialName, out var material)
            ? material
            : Undefined;

    private static void Merge(Dictionary<string, Material> materials, IniDocument ini)
    {
        foreach (var sectionName in ini.SectionNames)
        {
            var section = ini.Section(sectionName);
            var density = IniValueParser.ParseDouble(section.GetValueOrDefault("Density"));
            var friction = IniValueParser.ParseDouble(section.GetValueOrDefault("Friction"));
            var restitution = IniValueParser.ParseDouble(section.GetValueOrDefault("Restitution"));
            materials[sectionName] = new Material(density, friction, restitution);
        }
    }
}
