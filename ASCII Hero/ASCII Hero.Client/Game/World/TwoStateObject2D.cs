using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A static object that is either off or on and shows a different clip of its own sprite asset
/// for each state - the shared base of <see cref="Switch2D"/> (off/on) and <see cref="Gate2D"/>
/// (closed/open). The off clip is the placement's <c>Clip</c>; the on clip is its optional
/// <c>ClipWhenOn</c> (without one the state still changes, but the sprite stays the same).
/// </summary>
public abstract class TwoStateObject2D : StaticObject2D
{
    private string? _onClipName;
    private string? _offClipName = "default";
    private int _span;
    private double _secondsLeft;

    /// <summary>Seconds the object stays on before switching off by itself, from the placement's <c>OnSeconds</c>. Null (the default) means no timer.</summary>
    public double? OnSeconds { get; set; }

    /// <summary>Seconds the object stays off before switching on by itself, from the placement's <c>OffSeconds</c>. Null means the same as <see cref="OnSeconds"/>.</summary>
    public double? OffSeconds { get; set; }

    /// <summary>Whether this object is currently on (switched on / open).</summary>
    public bool IsOn { get; private set; }

    /// <summary>Whether the object is currently drawn as nothing: it is off and has no off clip.</summary>
    public bool IsHidden => !IsOn && _offClipName is null;

    /// <summary>Assigns the sprite asset, both state clips, initial state and world position for this instance.</summary>
    public void SpawnTwoState(SpriteAsset sprite, string? offClipName, string? onClipName, bool initialOn, Vector2D position, int span = 0)
    {
        _offClipName = offClipName;
        _onClipName = onClipName;
        _span = span;
        IsOn = initialOn;
        var clipName = ActiveClipName;
        SetFrame(sprite, clipName, sprite.GetClip(clipName).DefaultFrame, span);
        Position = position;
        _secondsLeft = CurrentPhaseSeconds;
        OnStateChanged();
    }

    /// <summary>Advances the on/off timer (if any), toggling the state each time a phase runs out.</summary>
    public void Tick(double deltaSeconds)
    {
        if (OnSeconds is not > 0)
        {
            return;
        }

        _secondsLeft -= deltaSeconds;
        while (_secondsLeft <= 0)
        {
            var overshoot = _secondsLeft;
            Toggle();
            _secondsLeft += overshoot;
        }
    }

    /// <summary>Sets the state, swapping to the matching clip. No-ops if the state is unchanged.</summary>
    public void SetState(bool on)
    {
        if (IsOn == on)
        {
            return;
        }

        IsOn = on;
        _secondsLeft = CurrentPhaseSeconds;
        var clipName = ActiveClipName;
        SetFrame(Sprite, clipName, Sprite.GetClip(clipName).DefaultFrame, _span);
        OnStateChanged();
    }

    /// <summary>Flips the state.</summary>
    public void Toggle() => SetState(!IsOn);

    /// <summary>Called after the state has been applied (and once after spawning), for subclass-specific consequences.</summary>
    protected virtual void OnStateChanged()
    {
    }

    private double CurrentPhaseSeconds => (IsOn ? OnSeconds : OffSeconds ?? OnSeconds) ?? 0;

    private string ActiveClipName => IsOn && _onClipName is not null ? _onClipName : _offClipName ?? _onClipName!;
}
