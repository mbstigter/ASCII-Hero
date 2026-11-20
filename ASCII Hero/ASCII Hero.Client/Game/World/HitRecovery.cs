namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// The short safe window a body gets after surviving a hit, during which it cannot be hurt again.
/// Every <see cref="IHurtableBody"/> owns one, so the player and hazards share the same rule and
/// differ only in <see cref="DurationSeconds"/>. <see cref="Physics.CollisionSystem"/> ticks them
/// down each physics step.
/// </summary>
/// <param name="durationSeconds">How long the window lasts once started.</param>
public sealed class HitRecovery(double durationSeconds)
{
    /// <summary>Length of the window, in seconds, once <see cref="Start"/> is called.</summary>
    public double DurationSeconds { get; set; } = durationSeconds;

    /// <summary>Seconds left in the window; 0 when not recovering.</summary>
    public double RemainingSeconds { get; private set; }

    /// <summary>Whether the body is inside its safe window and cannot be hurt.</summary>
    public bool IsActive => RemainingSeconds > 0;

    /// <summary>Starts the window for its full <see cref="DurationSeconds"/>.</summary>
    public void Start() => RemainingSeconds = DurationSeconds;

    /// <summary>Ends the window at once (e.g. on respawn).</summary>
    public void Clear() => RemainingSeconds = 0;

    /// <summary>Counts the window down by <paramref name="seconds"/>.</summary>
    public void Tick(double seconds)
    {
        if (IsActive)
        {
            RemainingSeconds = Math.Max(0, RemainingSeconds - seconds);
        }
    }
}
