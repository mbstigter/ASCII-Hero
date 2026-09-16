namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// Builds an in-memory <see cref="SpriteAsset"/> for a "materials-only" world object placement -
/// one that names a <c>Material</c> plus <c>Width</c>/<c>Height</c> directly in its
/// <c>_objects.ini</c> section instead of an <c>Asset</c>/<c>Clip</c> pair backed by on-disk
/// sprite files (see docs/AssetFormat.md §3.x). The synthesized asset has exactly one clip
/// ("default") with one frame, fully filled with the material's own <see cref="Material.DefaultChar"/>
/// and tagged with the material's name in every cell's materials grid - so it flows through the
/// exact same <see cref="World.Body2D.SetFrame"/>/collision/render/material-resolution path as
/// any sprite loaded by <see cref="SpriteLoader"/>, with zero changes required to that shared
/// pipeline. No tiling/<see cref="TileAxis"/> concept applies here - <c>Width</c>/<c>Height</c>
/// already give the placement's final size directly, unlike a repeatable sprite unit.
/// </summary>
public static class SyntheticSpriteFactory
{
    /// <summary>
    /// A sentinel that can never equal any authored glyph, so a materials-only asset's whole
    /// <paramref name="width"/> x <paramref name="height"/> rectangle is always solid (no
    /// per-cell holes) - unlike a normal sprite's own <c>EmptyChar</c>, which is an ordinary
    /// printable character that could otherwise collide with a real cell's own glyph.
    /// </summary>
    public const char EmptyChar = '\0';

    /// <summary>
    /// Builds the synthesized asset for one material name + size. <paramref name="glyph"/> is the
    /// material's own <see cref="Material.DefaultChar"/> - callers must resolve and validate that
    /// before calling this (see <see cref="World.World2D.LoadAsync"/>), since a material with no
    /// <see cref="Material.DefaultChar"/> configured has nothing to render as.
    /// </summary>
    public static SpriteAsset Build(string materialName, char glyph, int width, int height)
    {
        var chars = new char[height, width];
        var fore = new char[height, width];
        var back = new char[height, width];
        var materials = new string?[height, width];

        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                chars[row, col] = glyph;
                fore[row, col] = EmptyChar;
                back[row, col] = EmptyChar;
                materials[row, col] = materialName;
            }
        }

        var frame = new SpriteFrame { Chars = chars, Fore = fore, Back = back, Materials = materials };
        var clip = new SpriteClip { Name = "default", Frames = [frame] };

        return new SpriteAsset
        {
            Name = $"Material:{materialName}:{width}x{height}",
            EmptyChar = EmptyChar,
            Clips = new Dictionary<string, SpriteClip>(StringComparer.OrdinalIgnoreCase) { ["default"] = clip },
        };
    }
}
