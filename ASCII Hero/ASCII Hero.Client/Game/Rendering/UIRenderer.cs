namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// Builds glyphs for the UI primitives - <see cref="UIFrame"/>, <see cref="UILabel"/>, and
/// <see cref="UIBar"/> - laid out directly in viewport cell coordinates, unaffected by any camera.
/// Used by any screen that draws in screen space rather than world space. Meant to be drawn after
/// <see cref="WorldRenderer.BuildFrame"/>'s glyphs when overlaying the world, so screen-space
/// elements sit on top.
/// </summary>
public static class UIRenderer
{
    /// <summary>Character used for a <see cref="UIBar"/>'s filled cells.</summary>
    private const char FilledChar = '█';

    /// <summary>Character used for a <see cref="UIBar"/>'s empty cells.</summary>
    private const char EmptyChar = '░';

    public static void AddFrame(List<Glyph> glyphs, UIFrame frame, double cellWidthPixels, double cellHeightPixels)
    {
        GlyphBuilder.AddBox(
            glyphs, frame.Col, frame.Row, frame.Col + frame.Width - 1, frame.Row + frame.Height - 1,
            frame.ForeColor, frame.BackColor, cellWidthPixels, cellHeightPixels);
    }

    public static void AddLabel(List<Glyph> glyphs, UILabel label, double cellWidthPixels, double cellHeightPixels)
    {
        var lineCount = Math.Min(label.Lines.Count, label.Height);
        for (var i = 0; i < lineCount; i++)
        {
            var line = label.Lines[i];
            var content = line.Length > label.Width ? line[..label.Width] : line;

            for (var j = 0; j < content.Length; j++)
            {
                var pixelX = (label.Col + j) * cellWidthPixels;
                var pixelY = (label.Row + i) * cellHeightPixels;
                glyphs.Add(GlyphBuilder.BuildGlyph(pixelX, pixelY, content[j], label.ForeColor, label.BackColor));
            }
        }
    }

    public static void AddBar(List<Glyph> glyphs, UIBar bar, double cellWidthPixels, double cellHeightPixels)
    {
        var range = bar.MaxValue - bar.MinValue;
        var ratio = range > 0 ? Math.Clamp((bar.CurrentValue - bar.MinValue) / range, 0, 1) : 0;

        if (bar.Orientation == UIBarOrientation.Horizontal)
        {
            var length = (int)bar.Width;
            var filledCount = (int)Math.Round(ratio * length);

            // Horizontal bars fill left to right, so the left-most column is the first to fill.
            for (var col = 0; col < length; col++)
            {
                var character = col < filledCount ? FilledChar : EmptyChar;
                var pixelX = (bar.Col + col) * cellWidthPixels;
                var pixelY = bar.Row * cellHeightPixels;
                glyphs.Add(GlyphBuilder.BuildGlyph(pixelX, pixelY, character, bar.ForeColor, bar.BackColor));
            }
        }
        else
        {
            var length = (int)bar.Height;
            var filledCount = (int)Math.Round(ratio * length);

            // Vertical bars fill bottom to top, so the bottom-most row is the first to fill.
            for (var row = 0; row < length; row++)
            {
                var isFilled = row >= length - filledCount;
                var character = isFilled ? FilledChar : EmptyChar;
                var pixelX = bar.Col * cellWidthPixels;
                var pixelY = (bar.Row + row) * cellHeightPixels;
                glyphs.Add(GlyphBuilder.BuildGlyph(pixelX, pixelY, character, bar.ForeColor, bar.BackColor));
            }
        }
    }
}
