namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>A single ASCII character to draw at a pixel position on the canvas.</summary>
public readonly struct Glyph(double pixelX, double pixelY, char character)
{
    public double PixelX { get; } = pixelX;
    public double PixelY { get; } = pixelY;
    public char Character { get; } = character;
}
