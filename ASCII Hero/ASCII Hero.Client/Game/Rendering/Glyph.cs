namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>A single ASCII character to draw at a pixel position on the canvas, with resolved colors.</summary>
public readonly struct Glyph(double pixelX, double pixelY, char character, string foreColor, string? backColor)
{
    public double PixelX { get; } = pixelX;
    public double PixelY { get; } = pixelY;
    public char Character { get; } = character;

    /// <summary>CSS color string for the glyph's foreground (text) color.</summary>
    public string ForeColor { get; } = foreColor;

    /// <summary>CSS color string for the glyph's background cell fill, or null for no fill (transparent).</summary>
    public string? BackColor { get; } = backColor;
}
