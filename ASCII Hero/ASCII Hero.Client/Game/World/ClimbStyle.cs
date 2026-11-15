namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// How a climber's body looks while gripping a <see cref="Body2D.IsClimbable"/> surface - purely
/// visual, never physics. Set via the optional <c>ClimbStyle</c> key on an object type in
/// <c>{World}_objects.ini</c> (see docs/AssetFormat.md §3); the value is exactly the name of the
/// player pose shown while climbing it.
/// </summary>
public enum ClimbStyle
{
    /// <summary>Ladder-style climbing (the default): the "Climb" pose.</summary>
    Climb,

    /// <summary>Pole-style climbing for pipes, ropes and vines: the optional "Shimmy" pose, falling back to "Climb" if the sprite has none.</summary>
    Shimmy,
}
