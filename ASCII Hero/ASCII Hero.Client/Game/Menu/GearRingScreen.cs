using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Menu;

/// <summary>
/// State and input handling for the gear ring: the player's gear slots laid out on a ring that
/// opens out of the HUD's gear box while gameplay is paused, for as long as the gear ring key (Tab)
/// is held. Left/Right moves the selection around the ring; releasing the gear ring key confirms it
/// and Escape cancels, both closing the ring again. Only tracks state and timing; drawing is done by
/// <see cref="Rendering.GearRingRenderer"/>.
/// </summary>
public class GearRingScreen
{
    /// <summary>How long the ring takes to open out of the gear box.</summary>
    public const double OpenSeconds = 0.35;

    /// <summary>How long the ring takes to close back into the gear box.</summary>
    public const double CloseSeconds = 0.3;

    /// <summary>The slots on the ring, clockwise from the top.</summary>
    public IReadOnlyList<Gear2D> Slots { get; private set; } = [];

    /// <summary>Index into <see cref="Slots"/> of the highlighted slot.</summary>
    public int SelectedIndex { get; private set; }

    public Gear2D SelectedSlot => Slots[SelectedIndex];

    /// <summary>Whether the ring is closing (or closed) after a confirm or cancel; input is ignored from then on.</summary>
    public bool IsClosing { get; private set; }

    /// <summary>Whether the close animation has finished, so play can resume.</summary>
    public bool IsClosed => IsClosing && _elapsedSeconds >= CloseSeconds;

    /// <summary>Whether the ring was closed by confirming <see cref="SelectedSlot"/> rather than cancelling.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>Whether the open animation has finished and the ring is not closing.</summary>
    public bool IsFullyOpen => !IsClosing && _elapsedSeconds >= OpenSeconds;

    /// <summary>
    /// How far the ring is open, from 0 (collapsed into the gear box) to 1 (fully open), eased so it
    /// starts fast and settles gently both ways.
    /// </summary>
    public double Openness => IsClosing
        ? EaseOut(1.0 - Math.Min(_elapsedSeconds / CloseSeconds, 1.0))
        : EaseOut(Math.Min(_elapsedSeconds / OpenSeconds, 1.0));

    /// <summary>
    /// Seconds the highlight has stayed on <see cref="SelectedSlot"/> with the ring fully open; 0
    /// while the ring opens or closes and again whenever the highlight moves.
    /// </summary>
    public double StillSeconds { get; private set; }

    /// <summary>Seconds since the ring started opening, or since it started closing.</summary>
    private double _elapsedSeconds;

    private bool _wasLeftPressed;
    private bool _wasRightPressed;
    private bool _wasEscapePressed;

    /// <summary>
    /// Starts opening the ring on <paramref name="slots"/> with <paramref name="selected"/> highlighted
    /// (the first slot when it is not on the ring). Meant to be called while the gear ring key is
    /// down: the ring stays open until it is released. Other keys already held only count once
    /// released and pressed again.
    /// </summary>
    public void Open(IReadOnlyList<Gear2D> slots, Gear2D? selected, InputState input)
    {
        if (slots.Count == 0)
        {
            throw new ArgumentException("The gear ring needs at least one slot.", nameof(slots));
        }

        Slots = slots;
        SelectedIndex = selected is null ? 0 : Math.Max(0, IndexOf(slots, selected));
        IsClosing = false;
        Confirmed = false;
        StillSeconds = 0;
        _elapsedSeconds = 0;
        _wasLeftPressed = input.IsLeftKeyDown;
        _wasRightPressed = input.IsRightKeyDown;
        _wasEscapePressed = input.IsEscapeKeyDown;
    }

    /// <summary>Advances the open/close animation and handles fresh Left/Right and Escape presses and the gear ring key's release.</summary>
    public void Update(InputState input, double deltaSeconds)
    {
        _elapsedSeconds += deltaSeconds;

        var leftPressed = input.IsLeftKeyDown;
        var rightPressed = input.IsRightKeyDown;
        var escapePressed = input.IsEscapeKeyDown;
        var previousIndex = SelectedIndex;

        if (!IsClosing)
        {
            if (rightPressed && !_wasRightPressed)
            {
                SelectedIndex = (SelectedIndex + 1) % Slots.Count;
            }
            else if (leftPressed && !_wasLeftPressed)
            {
                SelectedIndex = (SelectedIndex - 1 + Slots.Count) % Slots.Count;
            }

            if (escapePressed && !_wasEscapePressed)
            {
                StartClosing(confirmed: false);
            }
            else if (!input.IsGearRingKeyDown)
            {
                StartClosing(confirmed: true);
            }
        }

        StillSeconds = IsFullyOpen && SelectedIndex == previousIndex
            ? StillSeconds + Math.Min(deltaSeconds, _elapsedSeconds - OpenSeconds)
            : 0;

        _wasLeftPressed = leftPressed;
        _wasRightPressed = rightPressed;
        _wasEscapePressed = escapePressed;
    }

    /// <summary>Starts closing, from however far the ring had opened, so an early close doesn't jump.</summary>
    private void StartClosing(bool confirmed)
    {
        var openness = Math.Min(_elapsedSeconds / OpenSeconds, 1.0);
        IsClosing = true;
        Confirmed = confirmed;
        _elapsedSeconds = (1.0 - openness) * CloseSeconds;
    }

    private static int IndexOf(IReadOnlyList<Gear2D> slots, Gear2D slot)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            if (ReferenceEquals(slots[i], slot))
            {
                return i;
            }
        }

        return -1;
    }

    private static double EaseOut(double progress) => 1.0 - Math.Pow(1.0 - progress, 3);
}
