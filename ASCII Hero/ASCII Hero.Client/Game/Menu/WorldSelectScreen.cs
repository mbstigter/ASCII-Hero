using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Browser;

namespace ASCII_Hero.Client.Game.Menu;

/// <summary>
/// State and input handling for the world-selection screen: a row of world thumbnails with a
/// selector box. Left/Right moves the selection; confirm keys confirm it.
/// See docs/AssetFormat.md §3.2.
/// </summary>
public class WorldSelectScreen(IReadOnlyList<WorldSummary> worlds, int visibleSlotCount)
{
    public IReadOnlyList<WorldSummary> Worlds { get; } = worlds;

    /// <summary>How many thumbnail slots are shown at once (always odd).</summary>
    public int VisibleSlotCount { get; } = visibleSlotCount;

    /// <summary>Index into <see cref="Worlds"/> of the highlighted world. Starts on the first world.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>
    /// Index into <see cref="Worlds"/> of the leftmost visible slot. Scrolls only as far as needed
    /// to keep <see cref="SelectedIndex"/> in view.
    /// </summary>
    public int ScrollOffset { get; private set; }

    /// <summary>True once the player has confirmed <see cref="SelectedWorld"/>.</summary>
    public bool Confirmed { get; private set; }

    public WorldSummary SelectedWorld => Worlds[SelectedIndex];

    private bool _wasLeftPressed;
    private bool _wasRightPressed;
    private bool _wasConfirmPressed;

    /// <summary>Clears <see cref="Confirmed"/> so the player can pick again (e.g. after a failed load).</summary>
    public void ResetConfirmation() => Confirmed = false;

    /// <summary>
    /// Handles fresh Left/Right and confirm presses, updates the scroll position, and advances
    /// every world's thumbnail animation.
    /// </summary>
    public void Update(InputState input, double deltaSeconds)
    {
        var leftPressed = input.IsLeftPressed;
        var rightPressed = input.IsRightPressed;
        var confirmPressed = input.IsConfirmPressed;

        if (rightPressed && !_wasRightPressed)
        {
            SelectedIndex = Math.Min(SelectedIndex + 1, Worlds.Count - 1);
        }
        else if (leftPressed && !_wasLeftPressed)
        {
            SelectedIndex = Math.Max(SelectedIndex - 1, 0);
        }

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
