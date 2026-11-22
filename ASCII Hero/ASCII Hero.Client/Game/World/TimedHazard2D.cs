namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A hazard (e.g. a retracting spike) that is dangerous only while on. Typically driven by
/// <see cref="TwoStateObject2D.OnSeconds"/>/<see cref="TwoStateObject2D.OffSeconds"/>, so it
/// alternates between its off clip (harmless) and its <c>ClipWhenOn</c> clip (hurts on contact).
/// </summary>
public class TimedHazard2D : TwoStateObject2D, IHazardBody
{
    /// <inheritdoc />
    public double Damage { get; set; } = Constants.GameDefaults.DefaultHazardDamage;

    /// <inheritdoc />
    public bool IsHarmful => IsOn;
}
