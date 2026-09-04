namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// A gauge/progress bar (health, stamina, a level timer, ...) drawn directly in screen (viewport)
/// cell coordinates - unaffected by any camera. An independent UI primitive alongside
/// <see cref="UIFrame"/>/<see cref="UILabel"/>. Values are an arbitrary <see cref="MinValue"/>..
/// <see cref="MaxValue"/> range rather than a fixed 0-100%, so a caller never has to normalize
/// (e.g. "14 of 20 stamina") before setting <see cref="CurrentValue"/> - the fill ratio is
/// computed internally. Mutable so a caller can update <see cref="CurrentValue"/> in place each
/// frame without re-creating it.
/// </summary>
public class UIBar(double col, double row, double width, double height, double minValue, double maxValue, UIBarOrientation orientation = UIBarOrientation.Horizontal, string? foreColor = null, string? backColor = null)
{
    /// <summary>Column of the bar's top-left cell, in viewport cells.</summary>
    public double Col { get; set; } = col;

    /// <summary>Row of the bar's top-left cell, in viewport cells.</summary>
    public double Row { get; set; } = row;

    /// <summary>
    /// Total width, in cells. For a <see cref="UIBarOrientation.Horizontal"/> bar this is the
    /// length the fill grows along; for a <see cref="UIBarOrientation.Vertical"/> bar it's the
    /// fixed thickness (normally 1).
    /// </summary>
    public double Width { get; set; } = width;

    /// <summary>
    /// Total height, in cells. For a <see cref="UIBarOrientation.Vertical"/> bar this is the
    /// length the fill grows along; for a <see cref="UIBarOrientation.Horizontal"/> bar it's the
    /// fixed thickness (normally 1).
    /// </summary>
    public double Height { get; set; } = height;

    /// <summary>Which dimension (<see cref="Width"/> or <see cref="Height"/>) the fill grows along.</summary>
    public UIBarOrientation Orientation { get; set; } = orientation;

    /// <summary>The value <see cref="CurrentValue"/> reads as a fully-empty bar.</summary>
    public double MinValue { get; set; } = minValue;

    /// <summary>The value <see cref="CurrentValue"/> reads as a fully-filled bar.</summary>
    public double MaxValue { get; set; } = maxValue;

    /// <summary>
    /// The bar's current reading, in the caller's own units (health points, stamina, seconds
    /// remaining, ...) rather than a percentage - clamped to <see cref="MinValue"/>/
    /// <see cref="MaxValue"/> when drawn, so a caller doesn't need to clamp it themselves.
    /// </summary>
    public double CurrentValue { get; set; } = maxValue;

    /// <summary>Foreground color of the filled cells, or null to fall back to <see cref="GlyphBuilder.DefaultForeColor"/>.</summary>
    public string? ForeColor { get; set; } = foreColor;

    /// <summary>Background fill of the empty cells, or null for no fill (transparent).</summary>
    public string? BackColor { get; set; } = backColor;
}

/// <summary>Which dimension a <see cref="UIBar"/>'s fill grows along.</summary>
public enum UIBarOrientation
{
    /// <summary>Fill grows left to right, along <see cref="UIBar.Width"/>.</summary>
    Horizontal,

    /// <summary>Fill grows bottom to top, along <see cref="UIBar.Height"/> (like a vertical thermometer).</summary>
    Vertical,
}
