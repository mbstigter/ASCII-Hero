namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// One or more lines of text drawn directly in screen (viewport) cell coordinates - unaffected by
/// any camera. An independent UI primitive (compare <see cref="UIFrame"/>): a label doesn't
/// require a frame around it (e.g. a centered "GAME OVER" message), and a frame doesn't require a
/// label inside it (e.g. a plain decorative border). Mutable so a caller can update
/// <see cref="Lines"/> in place each frame (e.g. a live score) without re-creating it.
/// </summary>
public class UILabel(double col, double row, int width, int height, string? foreColor = null, string? backColor = null)
{
    /// <summary>Column of the label's top-left cell, in viewport cells.</summary>
    public double Col { get; set; } = col;

    /// <summary>Row of the label's top-left cell, in viewport cells.</summary>
    public double Row { get; set; } = row;

    /// <summary>Width, in cells, a line may occupy before being truncated.</summary>
    public int Width { get; set; } = width;

    /// <summary>Maximum number of lines rendered from <see cref="Lines"/>, top to bottom; any beyond this are dropped.</summary>
    public int Height { get; set; } = height;

    /// <summary>The lines of text to draw, top to bottom. A line longer than <see cref="Width"/> is truncated.</summary>
    public List<string> Lines { get; } = [];

    /// <summary>Foreground color of the text, or null to fall back to <see cref="GlyphBuilder.DefaultForeColor"/>.</summary>
    public string? ForeColor { get; set; } = foreColor;

    /// <summary>Background fill behind the text cells, or null for no fill (transparent).</summary>
    public string? BackColor { get; set; } = backColor;
}
