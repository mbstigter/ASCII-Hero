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

    public bool IsJumpPressed => IsPressed("Space") || IsPressed("ArrowUp") || IsPressed("KeyW");

    public bool IsCrawlPressed => IsPressed("ArrowDown") || IsPressed("KeyS");
}
