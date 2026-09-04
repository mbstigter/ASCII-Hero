namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// A single-line box-drawing border drawn directly in screen (viewport) cell coordinates -
/// unaffected by any camera. An independent UI primitive: nothing ties a frame to any particular
/// label (compare <see cref="UILabel"/>), so any screen (world HUD, level-select, a future menu,
/// ...) can draw just a frame, just a label, or both side by side. Mutable so a caller can
/// reposition/recolor it in place frame to frame without re-creating it.
/// </summary>
public class UIFrame(double col, double row, double width, double height, string? foreColor = null, string? backColor = null)
{
    /// <summary>Column of the frame's top-left corner, in viewport cells.</summary>
    public double Col { get; set; } = col;

    /// <summary>Row of the frame's top-left corner, in viewport cells.</summary>
    public double Row { get; set; } = row;

    /// <summary>Total width, in cells, including the border itself. At least 2 (both border columns, no interior).</summary>
    public double Width { get; set; } = width;

    /// <summary>Total height, in cells, including the border itself. At least 2 (both border rows, no interior).</summary>
    public double Height { get; set; } = height;

    /// <summary>Foreground color of the border characters, or null to fall back to <see cref="GlyphBuilder.DefaultForeColor"/>.</summary>
    public string? ForeColor { get; set; } = foreColor;

    /// <summary>Background fill of the border cells, or null for no fill (transparent).</summary>
    public string? BackColor { get; set; } = backColor;
}
