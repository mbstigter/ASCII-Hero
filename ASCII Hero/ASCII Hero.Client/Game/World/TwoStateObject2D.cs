using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A static object that is either off or on and shows a different clip of its own sprite asset
/// for each state - the shared base of <see cref="Switch2D"/> (off/on) and <see cref="Gate2D"/>
/// (closed/open). The off clip is the placement's <c>Clip</c>; the on clip is its optional
/// <c>OnClip</c> (without one the state still changes, but the sprite stays the same).
/// </summary>
public abstract class TwoStateObject2D : StaticObject2D
{
    private string? _onClipName;
    private string _offClipName = "default";
    private int _span;

    /// <summary>Whether this object is currently on (switched on / open).</summary>
    public bool IsOn { get; private set; }

    /// <summary>Assigns the sprite asset, both state clips, initial state and world position for this instance.</summary>
    public void SpawnTwoState(SpriteAsset sprite, string offClipName, string? onClipName, bool initialOn, Vector2D position, int span = 0)
    {
        _offClipName = offClipName;
        _onClipName = onClipName;
        _span = span;
        IsOn = initialOn;
        var clipName = ActiveClipName;
        SetFrame(sprite, clipName, sprite.GetClip(clipName).DefaultFrame, span);
        Position = position;
        OnStateChanged();
    }

    /// <summary>Sets the state, swapping to the matching clip. No-ops if the state is unchanged.</summary>
    public void SetState(bool on)
    {
        if (IsOn == on)
        {
            return;
        }

        IsOn = on;
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

    private string ActiveClipName => IsOn && _onClipName is not null ? _onClipName : _offClipName;
}
