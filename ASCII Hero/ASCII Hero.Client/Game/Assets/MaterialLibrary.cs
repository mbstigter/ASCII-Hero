namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// One material's physical properties, as read from a <c>MaterialLibrary.ini</c> section.
/// </summary>
/// <param name="Density">Relative mass per world-cell "volume"; drives <see cref="World.Body2D.Mass"/>.</param>
/// <param name="Friction">Coulomb grip coefficient: 0 = frictionless, 1 = very grippy; values above 1 are allowed.</param>
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
/// <param name="Hardness">
/// Relative impact stiffness (smooth scale, roughly 0.1 = very soft like foam/mud, 1 = medium like
/// rubber/wood/flesh, 10 = very hard like stone/steel). When two materials collide, the softer one
/// deforms more and so its <see cref="Restitution"/> dominates the combined bounce - see
/// <see cref="Physics.CollisionSystem"/>'s <c>CombineRestitution</c>. Defaults to 1.
/// </param>
public readonly record struct Material(
    double Density,
    double Friction,
    double Restitution,
    double Viscosity = 0.0,
    char? ForegroundColor = null,
    char? BackgroundColor = null,
    char? Character = null,
    double Hardness = 1.0);

/// <summary>
/// The shared material library (Global/MaterialLibrary.ini merged with an optional world-local
/// MaterialLibrary.ini: world entries override same-named sections, sections only defined
/// globally still apply). Maps a material name (as found in a sprite's <c>DefaultMaterial</c>
/// setting, resolved
/// by <see cref="SpriteLoader"/> into <see cref="SpriteFrame.Materials"/>) to its physical
/// properties. Loading/merging is handled by the shared <see cref="IniOverrideLoader"/>.
/// </summary>
public class MaterialLibrary
{
    /// <summary>
    /// Fallback used when a body has no material at all (a sprite with no
    /// <c>DefaultMaterial</c>): physically inert (massless, frictionless, no bounce). A material
    /// name that is set but not defined is a world load error, not this fallback.
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
    /// Whether a material with this name is defined. World loading uses it to reject unknown
    /// material names (a typo in <c>Material</c> or <c>DefaultMaterial</c>) as a load error.
    /// </summary>
    public bool Contains(string materialName) => _materials.ContainsKey(materialName);

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
            var hardness = section.TryGetValue("Hardness", out var hardnessText) && IniValueParser.TryParseDouble(hardnessText, out var parsedHardness) && parsedHardness > 0
                ? parsedHardness
                : 1.0;
            var foregroundColor = IniValueParser.ParseColorCode(section.GetValueOrDefault("ForegroundColor"));
            var backgroundColor = IniValueParser.ParseColorCode(section.GetValueOrDefault("BackgroundColor"));
            var character = section.TryGetValue("Character", out var characterText) && !string.IsNullOrEmpty(characterText)
                ? characterText[0]
                : (char?)null;
            materials[sectionName] = new Material(density, friction, restitution, viscosity, foregroundColor, backgroundColor, character, hardness);
        }
    }
}
