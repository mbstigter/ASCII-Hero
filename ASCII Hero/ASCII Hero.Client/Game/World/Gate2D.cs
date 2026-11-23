using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Physics;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A door, hatch or similar passage that is solid while closed and passable while open. It is
/// opened in one of two ways: with a key (<see cref="LockName"/> set - the player presses the
/// action key next to it with the key gear selected and holding the matching <see cref="Key2D"/>, see
/// <see cref="World2D.TryOperateGate"/>; the same press closes it again, unless something solid is in
/// the way), or by a <see cref="Switch2D"/>
/// (<see cref="LinkName"/> set - it follows that switch's state and can close again). A gate has
/// at most one of the two.
/// </summary>
public class Gate2D : TwoStateObject2D, ILinkTarget
{
    /// <summary>Name of the key that opens this gate, from the placement's <c>Lock</c> key. Null if it is not key-operated.</summary>
    public string? LockName { get; set; }

    /// <summary>Name of the switch that drives this gate, from the placement's <c>Link</c> key. Null if it is not switch-operated.</summary>
    public string? LinkName { get; set; }

    private bool _closing;

    private bool UsesSingleMotionClip => OnClipName is null && Clip.AnimationMode == AnimationMode.Once
        && Clip.FrameDurationSeconds is > 0 && Clip.Frames.Count > 1;

    public override void SpawnTwoState(SpriteAsset sprite, string? offClipName, string? onClipName, bool initialOn, Vector2D position, int span = 0)
    {
        base.SpawnTwoState(sprite, offClipName, onClipName, initialOn, position, span);
        if (UsesSingleMotionClip)
        {
            SetFrame(sprite, Clip.Name, initialOn ? Clip.Frames.Count - 1 : 0, span, reverse: !initialOn);
        }
    }

    private SpriteFrame ClosedFrame
    {
        get
        {
            var clip = Sprite.GetClip(OffClipName!);
            return SpriteFrameTiler.Tile(clip.Frames[UsesSingleMotionClip ? 0 : clip.DefaultFrame], clip.TileAxis, Span);
        }
    }

    /// <summary>The gate's closed collision shape in world coordinates, even while it is open.</summary>
    public IReadOnlyList<Rect> ClosedCollisionRects => [.. CollisionShapeBuilder
        .DeriveRectangles(ClosedFrame.Chars, Sprite.EmptyChar)
        .Select(rect => rect.Translate(Position))];

    internal void InitializeLinkedState(bool on)
    {
        SetState(on);
        if (UsesSingleMotionClip)
        {
            SetFrame(Sprite, Clip.Name, on ? Clip.Frames.Count - 1 : 0, Span, reverse: !on);
        }
    }

    public override void SetState(bool on)
    {
        if (IsOn == on)
        {
            return;
        }

        var singleMotionClip = UsesSingleMotionClip;
        var wasClosing = _closing;
        var previousFrame = AnimationFrameIndex;
        var previousClipName = Clip.Name;
        _closing = false;
        base.SetState(on);

        if (singleMotionClip)
        {
            SetFrame(Sprite, previousClipName, previousFrame, Span, reverse: !on);
            return;
        }

        if (OnClipName is null)
        {
            return;
        }

        var openClip = Sprite.GetClip(OnClipName);
        if (openClip.AnimationMode != AnimationMode.Once || openClip.FrameDurationSeconds is not > 0 || openClip.Frames.Count <= 1)
        {
            return;
        }

        if (on && wasClosing)
        {
            SetFrame(Sprite, OnClipName, previousFrame, Span);
        }
        else if (!on)
        {
            var startFrame = string.Equals(previousClipName, OnClipName, StringComparison.OrdinalIgnoreCase)
                ? previousFrame : openClip.Frames.Count - 1;
            if (startFrame > 0)
            {
                _closing = true;
                SetFrame(Sprite, OnClipName, startFrame, Span, reverse: true);
            }
        }
    }

    public override bool AdvanceAnimation(double deltaSeconds)
    {
        var advanced = base.AdvanceAnimation(deltaSeconds);
        if (_closing && AnimationFrameIndex == 0)
        {
            _closing = false;
            var closedClip = Sprite.GetClip(OffClipName!);
            SetFrame(Sprite, OffClipName!, closedClip.DefaultFrame, Span);
        }
        return advanced;
    }

    protected override SpriteFrame GetCollisionFrame(SpriteFrame displayedFrame) => IsOn ? displayedFrame : ClosedFrame;

    protected override void OnStateChanged() => IsPassable = IsOn;
}
