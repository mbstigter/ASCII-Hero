using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Menu;

namespace ASCII_Hero.Client.Game.Rendering;

/// <summary>
/// Draws the modal end-screen message (level complete, game over, exit confirmation) in a
/// centered box on top of the world: a short ripple of shrinking boxes onto the final box (just
/// big enough for the message), which then glows while the message is shown inside it. Only
/// builds glyphs; which screen is shown, its text/colors and its timing are decided by the caller.
/// </summary>
public class EndScreenRenderer
{
    /// <summary>Number of shrinking steps from the largest ripple box down to the box that holds the message.</summary>
    private const int RippleSteps = 12;

    /// <summary>How long each ripple step lasts.</summary>
    private const double StepSeconds = 0.06;

    /// <summary>How many boxes are visible at once while the ripple converges.</summary>
    private const int RippleTrail = 4;

    /// <summary>Per step, the box shrinks by this many cells on each axis (1 per side).</summary>
    private const int StepCells = 2;

    /// <summary>Once the ripple settles, how long the box shows each tint of its glow cycle.</summary>
    private const double GlowStepSeconds = 0.2;

    /// <summary>One text row (with a space either side) inside the final ripple box, on a solid background so it stays readable over the world.</summary>
    private readonly UILabel _label = new(col: 0, row: 0, width: 20, height: 1);

    /// <summary>Border around the end screen message, reused for every ripple box.</summary>
    private readonly UIFrame _frame = new(col: 0, row: 0, width: 0, height: 0);

    /// <summary>Sets the label's solid background color.</summary>
    public void ApplyColors(InterfaceColors colors) => _label.BackColor = colors.Black;

    /// <summary>Adds the ripple/glow box and (once settled) the message for the given elapsed time.</summary>
    public void AddMessage(
        List<Glyph> glyphs, string text, string darkColor, string color, string lightColor, double elapsedSeconds,
        double viewportWidthCells, double viewportHeightCells, double cellWidthPixels, double cellHeightPixels)
    {
        var finalWidth = text.Length + 4;
        var finalHeight = 3;

        // Level 0 is the final box; each level above it is StepCells larger on each axis.
        var level = Math.Max(0, RippleSteps - (int)(elapsedSeconds / StepSeconds));
        var outermostLevel = level == 0 ? 0 : Math.Min(level + RippleTrail - 1, RippleSteps);

        for (var frameLevel = outermostLevel; frameLevel >= level; frameLevel--)
        {
            // While rippling, the leading box is bright and the trail behind it dim; once settled, the box glows dark -> normal -> light -> normal.
            _frame.ForeColor = level > 0
                ? (frameLevel == level ? lightColor : darkColor)
                : ((int)(elapsedSeconds / GlowStepSeconds) % 4) switch { 0 => darkColor, 2 => lightColor, _ => color };
            _frame.Width = finalWidth + StepCells * frameLevel;
            _frame.Height = finalHeight + StepCells * frameLevel;
            _frame.Col = Math.Floor((viewportWidthCells - _frame.Width) / 2);
            _frame.Row = Math.Floor((viewportHeightCells - _frame.Height) / 2);
            UIRenderer.AddFrame(glyphs, _frame, cellWidthPixels, cellHeightPixels);
        }

        if (level > 0)
        {
            return;
        }

        // The label sits on the frame's single interior row; its black background is a solid panel behind the text.
        var interiorWidth = text.Length + 2;
        _label.Col = _frame.Col + 1;
        _label.Row = _frame.Row + 1;
        _label.Width = interiorWidth;
        _label.ForeColor = color;
        _label.Lines.Clear();
        _label.Lines.Add($" {text} ");
        UIRenderer.AddLabel(glyphs, _label, cellWidthPixels, cellHeightPixels);
    }
}
