namespace ASCII_Hero.Client.Game.Browser;

/// <summary>Tracks which keyboard keys are currently held down, keyed by JS KeyboardEvent.code.</summary>
public class InputState
{
    private readonly HashSet<string> _pressedKeys = [];

    public void KeyDown(string code) => _pressedKeys.Add(code);

    public void KeyUp(string code) => _pressedKeys.Remove(code);

    public bool IsKeyDown(string code) => _pressedKeys.Contains(code);

    public bool IsLeftKeyDown => IsKeyDown("ArrowLeft") || IsKeyDown("KeyA");

    public bool IsRightKeyDown => IsKeyDown("ArrowRight") || IsKeyDown("KeyD");

    /// <summary>Directional up - posture/direction input only (climb, hang grab, clamber pull-up,
    /// ground stand-up). Deliberately never includes jump keys - see <see cref="IsJumpKeyDown"/>.
    /// "Player 1" (arrow keys) and "Player 2" (WASD) each have their own physically-separate up
    /// key, mirrored by their own separate jump key below, rather than sharing one.</summary>
    public bool IsUpKeyDown => IsKeyDown("ArrowUp") || IsKeyDown("KeyW");

    public bool IsDownKeyDown => IsKeyDown("ArrowDown") || IsKeyDown("KeyS");

    /// <summary>Explicit jump/action input - always its own dedicated key, never doubling as
    /// <see cref="IsUpKeyDown"/> (unlike some platformers' convention of treating them as
    /// equivalent), so contexts needing both a directional "up" and a separate "action" input at
    /// the same time (e.g. the hang stance sequence: Up pulls into Clamber, Jump swings/jumps
    /// off) can always tell them apart - including on the ground, where Up alone no longer jumps.
    /// "Player 1" (arrow keys) uses <c>Space</c>; "Player 2" (WASD) uses <c>ControlLeft</c> as its
    /// own equivalent jump key, positioned next to WASD the same way Space sits next to the arrow
    /// keys.</summary>
    public bool IsJumpKeyDown => IsKeyDown("Space") || IsKeyDown("ControlLeft");

    /// <summary>Escape - asks to exit the current world (confirm with a confirm key, cancel with Escape
    /// again). See docs/Decisions.md.</summary>
    public bool IsEscapeKeyDown => IsKeyDown("Escape");

    /// <summary>The "action" input (opens a gate the player holds the key for, or operates a nearby
    /// button/lever). "Player 1" (arrow
    /// keys) uses <c>Enter</c>; "Player 2" (WASD) uses <c>E</c>. Distinct from jump and directional
    /// input.</summary>
    public bool IsActionKeyDown => IsKeyDown("Enter") || IsKeyDown("KeyE");

    /// <summary>Selecting/confirming in menus, the end screens and the exit confirmation: any of the
    /// action keys (Enter, E) or jump keys (Space, Left Control), so whichever hand position the player
    /// is in works.</summary>
    public bool IsConfirmKeyDown => IsActionKeyDown || IsJumpKeyDown;

    /// <summary>Dev/testing shortcut - toggles the FPS debug overlay (see
    /// <see cref="GameLoop.OnFrame"/>). "F" is otherwise unused by any gameplay input.</summary>
    public bool IsFpsToggleKeyDown => IsKeyDown("KeyF");

    /// <summary>Dev/testing shortcut - toggles the world object-count debug overlay (see
    /// <see cref="GameLoop.OnFrame"/>). "O" is otherwise unused by any gameplay input.</summary>
    public bool IsObjectCounterToggleKeyDown => IsKeyDown("KeyO");
}
