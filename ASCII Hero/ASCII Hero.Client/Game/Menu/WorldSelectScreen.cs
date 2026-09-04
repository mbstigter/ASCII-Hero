using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Input;

namespace ASCII_Hero.Client.Game.Menu;

/// <summary>
/// State and input handling for the world-selection screen shown at startup: a horizontal row of
/// world thumbnails (however many fit the viewport - see <see cref="Rendering.WorldSelectRenderer"/>)
/// with a selector box that always sits around the same, fixed middle slot. The default selection
/// is the first world in <see cref="Assets.WorldCatalog.LoadWorldNamesAsync"/>'s order; the row only
/// ever scrolls once the selection would otherwise move outside the visible slots. See
/// docs/AssetFormat.md §3.2.
/// </summary>
public class WorldSelectScreen(IReadOnlyList<WorldSummary> worlds, int visibleSlotCount)
{
    public IReadOnlyList<WorldSummary> Worlds { get; } = worlds;

    /// <summary>How many thumbnail slots are shown at once. Always odd so one slot is exactly centered.</summary>
    public int VisibleSlotCount { get; } = visibleSlotCount;

    /// <summary>Index into <see cref="Worlds"/> of the currently-highlighted world. Starts on the first world.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>
    /// Index into <see cref="Worlds"/> of the leftmost currently-visible slot. Keeps
    /// <see cref="SelectedIndex"/> centered in the middle slot whenever there are enough worlds on
    /// both sides to do so, clamping at either end of the list otherwise (e.g. the initial state,
    /// where <see cref="SelectedIndex"/> is 0 and there is nothing to its left to scroll in).
    /// </summary>
    public int ScrollOffset
    {
        get
        {
            var centerSlot = VisibleSlotCount / 2;
            var maxOffset = Math.Max(0, Worlds.Count - VisibleSlotCount);
            return Math.Clamp(SelectedIndex - centerSlot, 0, maxOffset);
        }
    }

    /// <summary>True once the player has confirmed <see cref="SelectedWorld"/> to play.</summary>
    public bool Confirmed { get; private set; }

    public WorldSummary SelectedWorld => Worlds[SelectedIndex];

    private bool _wasLeftPressed;
    private bool _wasRightPressed;
    private bool _wasConfirmPressed;

    /// <summary>
    /// Moves the selection on a fresh Left/Right (or A/D) press - edge-triggered so holding the
    /// key down doesn't keep scrolling every frame - marks <see cref="Confirmed"/> on a fresh press
    /// of the jump/action key, and advances every world's own thumbnail animation (so an
    /// off-selection thumbnail keeps animating too, not just the currently-boxed one).
    /// </summary>
    public void Update(InputState input, double deltaSeconds)
    {
        var leftPressed = input.IsLeftPressed;
        var rightPressed = input.IsRightPressed;
        var confirmPressed = input.IsJumpPressed;

        if (rightPressed && !_wasRightPressed)
        {
            SelectedIndex = Math.Min(SelectedIndex + 1, Worlds.Count - 1);
        }
        else if (leftPressed && !_wasLeftPressed)
        {
            SelectedIndex = Math.Max(SelectedIndex - 1, 0);
        }

        if (confirmPressed && !_wasConfirmPressed)
        {
            Confirmed = true;
        }

        _wasLeftPressed = leftPressed;
        _wasRightPressed = rightPressed;
        _wasConfirmPressed = confirmPressed;

        foreach (var world in Worlds)
        {
            world.AdvanceThumbnailAnimation(deltaSeconds);
        }
    }
}
