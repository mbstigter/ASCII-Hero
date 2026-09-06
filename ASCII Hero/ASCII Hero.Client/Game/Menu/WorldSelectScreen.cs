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
    /// Index into <see cref="Worlds"/> of the leftmost currently-visible slot. Scrolls the
    /// minimum amount needed to keep <see cref="SelectedIndex"/> in view - rather than always
    /// re-centering it - so the selector box moves exactly one slot per Left/Right press and only
    /// reaches the rightmost/leftmost slot once <see cref="SelectedIndex"/> is actually at the end
    /// of the list. A purely position-based (re-centering) formula would occasionally leave the
    /// box visually stalled on one press and then jump on the next whenever the world count and
    /// <see cref="VisibleSlotCount"/> don't divide evenly (e.g. 4 worlds, 3 visible slots).
    /// </summary>
    public int ScrollOffset { get; private set; }

    /// <summary>True once the player has confirmed <see cref="SelectedWorld"/> to play.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>
    /// Clears <see cref="Confirmed"/> so the player can pick again - used by <see cref="GameLoop"/>
    /// if the confirmed world's <see cref="World.World2D.LoadAsync"/> fails, so a failed load
    /// returns to a normally-interactive selection screen instead of one stuck thinking a
    /// (failed) confirmation is still pending.
    /// </summary>
    public void ResetConfirmation() => Confirmed = false;

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

        // Scroll the minimum amount needed to keep SelectedIndex within the visible slot range -
        // see ScrollOffset's own doc comment for why this can't just be recomputed from
        // SelectedIndex alone.
        var maxOffset = Math.Max(0, Worlds.Count - VisibleSlotCount);
        ScrollOffset = Math.Clamp(ScrollOffset, SelectedIndex - (VisibleSlotCount - 1), SelectedIndex);
        ScrollOffset = Math.Clamp(ScrollOffset, 0, maxOffset);

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
