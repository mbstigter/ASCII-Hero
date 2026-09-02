namespace ASCII_Hero.Client.Game.Input;

/// <summary>Tracks which keyboard keys are currently held down, keyed by JS KeyboardEvent.code.</summary>
public class InputState
{
    private readonly HashSet<string> _pressedKeys = [];

    public void KeyDown(string code) => _pressedKeys.Add(code);

    public void KeyUp(string code) => _pressedKeys.Remove(code);

    public bool IsPressed(string code) => _pressedKeys.Contains(code);

    public bool IsLeftPressed => IsPressed("ArrowLeft") || IsPressed("KeyA");

    public bool IsRightPressed => IsPressed("ArrowRight") || IsPressed("KeyD");

    /// <summary>Directional up - posture/direction input only (ladder climb, hang-crawl pull-up,
    /// ground stand-up). Deliberately never includes jump keys - see <see cref="IsJumpPressed"/>.
    /// "Player 1" (arrow keys) and "Player 2" (WASD) each have their own physically-separate up
    /// key, mirrored by their own separate jump key below, rather than sharing one.</summary>
    public bool IsUpPressed => IsPressed("ArrowUp") || IsPressed("KeyW");

    public bool IsDownPressed => IsPressed("ArrowDown") || IsPressed("KeyS");

    /// <summary>Explicit jump/action input - always its own dedicated key, never doubling as
    /// <see cref="IsUpPressed"/> (unlike some platformers' convention of treating them as
    /// equivalent), so contexts needing both a directional "up" and a separate "action" input at
    /// the same time (e.g. the hang stance ladder: Up pulls into Clamber, Jump swings/jumps
    /// off) can always tell them apart - including on the ground, where Up alone no longer jumps.
    /// "Player 1" (arrow keys) uses <c>Space</c>; "Player 2" (WASD) uses <c>ControlLeft</c> as its
    /// own equivalent jump key, positioned next to WASD the same way Space sits next to the arrow
    /// keys.</summary>
    public bool IsJumpPressed => IsPressed("Space") || IsPressed("ControlLeft");
}
