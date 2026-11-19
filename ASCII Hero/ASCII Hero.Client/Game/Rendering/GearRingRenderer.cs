using ASCII_Hero.Client.Game.Menu;

namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// Draws the gear ring (<see cref="GearRingScreen"/>) on top of the paused world: one small box per
/// gear slot, spaced evenly around a circle and snapped to whole cells, with the highlighted slot lit
/// up and its name and count in the middle. While opening (and closing) the circle grows out of (and
/// shrinks back into) the HUD's gear box, the highlighted box staying lit as it travels; once the
/// highlight stands still on the open ring its border glows. Only builds glyphs; state and timing
/// come from <see cref="GearRingScreen"/>.
/// </summary>
public class GearRingRenderer
{
    /// <summary>Radius of the fully open ring, in cell widths; the vertical radius is scaled by the cell aspect so the ring is round on screen.</summary>
    private const double RingRadiusCellWidths = 11.0;

    /// <summary>How many rows below the viewport's middle the fully open ring is centered, leaving room above it for the HUD.</summary>
    private const double CenterOffsetRows = 1.0;

    /// <summary>Size of each slot's box, including its border: the icon sits in the single interior row.</summary>
    private const int TileWidth = 5;
    private const int TileHeight = 3;

    private readonly UIFrame _tile = new(col: 0, row: 0, width: TileWidth, height: TileHeight);

    /// <summary>The icon row inside a slot's box.</summary>
    private readonly UILabel _icon = new(col: 0, row: 0, width: TileWidth - 2, height: 1);

    /// <summary>The highlighted slot's name (first line) and count (second line), centered in the ring.</summary>
    private readonly UILabel _caption = new(col: 0, row: 0, width: 0, height: 1);

    /// <summary>
    /// Adds the ring for <paramref name="ring"/>'s current state. <paramref name="originCol"/>/<paramref name="originRow"/>
    /// is the point (in viewport cells) the ring grows out of - the middle of the HUD's gear box.
    /// <paramref name="backColor"/> is drawn behind the boxes and caption (the world's <c>InterfaceBackdrop</c>; null = transparent).
    /// </summary>
    public void AddRing(
        List<Glyph> glyphs, GearRingScreen ring, double originCol, double originRow,
        string darkColor, string color, string lightColor, string? backColor,
        double viewportWidthCells, double viewportHeightCells, double cellWidthPixels, double cellHeightPixels)
    {
        var openness = ring.Openness;
        if (openness <= 0)
        {
            return;
        }

        var centerCol = originCol + ((viewportWidthCells / 2) - originCol) * openness;
        var centerRow = originRow + ((viewportHeightCells / 2) + CenterOffsetRows - originRow) * openness;
        var radiusCols = RingRadiusCellWidths * openness;
        var radiusRows = radiusCols * cellWidthPixels / cellHeightPixels;
        var selectedColor = UIRenderer.SelectionColor(ring.StillSeconds, darkColor, color, lightColor);

        _tile.BackColor = backColor;
        _icon.BackColor = backColor;
        for (var i = 0; i < ring.Slots.Count; i++)
        {
            var slot = ring.Slots[i];
            var isSelected = i == ring.SelectedIndex;
            var angle = (-90.0 + (i * 360.0 / ring.Slots.Count)) * Math.PI / 180.0;

            _tile.Col = Snap(centerCol + (radiusCols * Math.Cos(angle)) - (TileWidth / 2.0));
            _tile.Row = Snap(centerRow + (radiusRows * Math.Sin(angle)) - (TileHeight / 2.0));
            _tile.ForeColor = isSelected ? selectedColor : darkColor;
            UIRenderer.AddFrame(glyphs, _tile, cellWidthPixels, cellHeightPixels);

            _icon.Col = _tile.Col + 1;
            _icon.Row = _tile.Row + 1;
            _icon.ForeColor = isSelected ? lightColor : slot.Count > 0 ? color : darkColor;
            _icon.Lines.Clear();
            _icon.Lines.Add($" {slot.Icon} ");
            UIRenderer.AddLabel(glyphs, _icon, cellWidthPixels, cellHeightPixels);
        }

        if (!ring.IsFullyOpen)
        {
            return;
        }

        _caption.BackColor = backColor;
        var nameRow = Snap(centerRow - 1);
        AddCaptionLine(glyphs, $" {ring.SelectedSlot.Name} ", centerCol, nameRow, color, cellWidthPixels, cellHeightPixels);
        AddCaptionLine(glyphs, $" x{ring.SelectedSlot.Count} ", centerCol, nameRow + 1, darkColor, cellWidthPixels, cellHeightPixels);
    }

    /// <summary>Rounds to the nearest whole cell, halves always up, so the ring stays symmetric.</summary>
    private static double Snap(double cells) => Math.Floor(cells + 0.5);

    private void AddCaptionLine(List<Glyph> glyphs, string text, double centerCol, double row, string foreColor, double cellWidthPixels, double cellHeightPixels)
    {
        _caption.Col = Snap(centerCol - (text.Length / 2.0));
        _caption.Row = row;
        _caption.Width = text.Length;
        _caption.ForeColor = foreColor;
        _caption.Lines.Clear();
        _caption.Lines.Add(text);
        UIRenderer.AddLabel(glyphs, _caption, cellWidthPixels, cellHeightPixels);
    }
}
