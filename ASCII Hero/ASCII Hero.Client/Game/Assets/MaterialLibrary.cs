namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// One material's physical properties, as read from a <c>MaterialLibrary.ini</c> section.
/// </summary>
/// <param name="Density">Relative mass per world-cell "volume"; drives <see cref="World.Body2D.Mass"/>.</param>
/// <param name="Friction">0 = frictionless, 1 = very grippy.</param>
/// <param name="Restitution">Bounciness; 0 = no bounce, 1 = perfectly elastic.</param>
/// <param name="ForegroundColor">
/// Optional single-character color code (see <c>Global/ColorPalette.ini</c>) used as this material's
/// own tier in the render color-resolution chain - lower priority than a sprite/object's own
/// color but higher than the world's default (see <see cref="Rendering.GlyphBuilder.ResolveColor"/>
/// via <see cref="Rendering.WorldRenderer"/>). Null if this material doesn't define one, in which
/// case the chain continues past it unchanged.
/// </param>
/// <param name="BackgroundColor">See <see cref="ForegroundColor"/>.</param>
/// <param name="Character">
/// Optional default glyph representing this material - used as the glyph for a materials-only
/// world object placement that names this material but no <c>Asset</c>; such a placement may
/// also override it per-instance via its own <c>Character</c> key. Has no effect on any
/// <c>Asset</c>-based placement, whose glyphs always come from its own sprite's
/// <c>_characters.txt</c>.
/// </param>
/// <param name="Viscosity">
/// Drag coefficient applied to a body while immersed in this material as its ambient medium (see
/// <see cref="World.Body2D.CurrentMedium"/>) - 0 = no resistance to motion at all, higher values
/// slow movement more strongly the faster the body moves through it. Distinct from
/// <see cref="Friction"/>, which only acts at solid-contact time; this acts continuously while
/// immersed, independent of any contact.
/// </param>
public readonly record struct Material(
    double Density,
    double Friction,
    double Restitution,
    double Viscosity = 0.0,
    char? ForegroundColor = null,
    char? BackgroundColor = null,
    char? Character = null);

/// <summary>
/// The shared material library (Global/MaterialLibrary.ini merged with an optional world-local
/// MaterialLibrary.ini: world entries override same-named sections, sections only defined
/// globally still apply). Maps a material name (as found in a sprite's <c>DefaultMaterial</c>/
/// <c>MaterialCodes</c> settings or a world's own <c>_materials.txt</c> per-cell layer, resolved
/// by <see cref="SpriteLoader"/> into <see cref="SpriteFrame.Materials"/>) to its physical
/// properties. Loading/merging is handled by the shared <see cref="IniOverrideLoader"/>.
/// </summary>
public class MaterialLibrary
{
    /// <summary>
    /// Fallback used when a body has no resolvable material (e.g. a sprite with neither
    /// <c>DefaultMaterial</c> nor a per-cell material layer): physically inert (massless,
    /// frictionless, no bounce).
    /// </summary>
    public static readonly Material Undefined = new(Density: 0.0, Friction: 0.0, Restitution: 0.0);

    private readonly Dictionary<string, Material> _materials;

    private MaterialLibrary(Dictionary<string, Material> materials) => _materials = materials;

    public static async Task<MaterialLibrary> LoadAsync(IAssetFileProvider fileProvider, string? worldName)
    {
        var materials = await IniOverrideLoader.LoadAsync<string, Material>(
            fileProvider, worldName, "MaterialLibrary.ini", Merge, StringComparer.OrdinalIgnoreCase);
        return new MaterialLibrary(materials);
    }

    /// <summary>
    /// Looks up a material's properties by name, or <see cref="Undefined"/> if the name is null
    /// or not defined in this library.
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
            var viscosity = IniValueParser.ParseDouble(section.GetValueOrDefault("Viscosity"));
            var foregroundColor = IniValueParser.ParseColorCode(section.GetValueOrDefault("ForegroundColor"));
            var backgroundColor = IniValueParser.ParseColorCode(section.GetValueOrDefault("BackgroundColor"));
            var character = section.TryGetValue("Character", out var characterText) && !string.IsNullOrEmpty(characterText)
                ? characterText[0]
                : (char?)null;
            materials[sectionName] = new Material(density, friction, restitution, viscosity, foregroundColor, backgroundColor, character);
        }
    }
}
