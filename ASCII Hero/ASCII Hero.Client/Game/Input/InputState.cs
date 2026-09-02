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

    /// <summary>Directional up - posture/direction input (ladder climb, hang-crawl pull-up,
    /// ground stand-up), which also doubles as a jump trigger everywhere <see cref="IsJumpPressed"/>
    /// applies (see its own doc comment for why this is safe). "Player 1" (arrow keys) and
    /// "Player 2" (WASD) each have their own physically-separate up key, mirrored by their own
    /// separate jump key below, rather than sharing one.</summary>
    public bool IsUpPressed => IsPressed("ArrowUp") || IsPressed("KeyW");

    public bool IsDownPressed => IsPressed("ArrowDown") || IsPressed("KeyS");

    /// <summary>Explicit jump/action input - includes both a dedicated jump key and <see
    /// cref="IsUpPressed"/> (arrow-key/WASD convention: Up also jumps). This is safe everywhere
    /// a context needs to tell "directional up" and "jump" apart at the same time (e.g. the hang
    /// stance ladder: Up pulls into Clamber, Jump swings/jumps off) because every such call site
    /// gives the directional/posture action priority over the jump action when both would apply
    /// to the same key press - so Up always still means "pull up"/"climb"/"stand up" first, and
    /// only ever falls through to a jump/let-go action in states where no directional meaning of
    /// Up exists to take priority (e.g. an ordinary standing/Walk jump). "Player 1" (arrow keys)
    /// uses <c>Space</c> (in addition to <c>ArrowUp</c>); "Player 2" (WASD) uses
    /// <c>ControlLeft</c> (in addition to <c>KeyW</c>) as its own equivalent jump key, positioned
    /// next to WASD the same way Space sits next to the arrow keys.</summary>
    public bool IsJumpPressed => IsPressed("Space") || IsPressed("ControlLeft") || IsUpPressed;
}
