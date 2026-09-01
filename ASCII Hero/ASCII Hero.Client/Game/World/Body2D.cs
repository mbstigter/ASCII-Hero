using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Physics;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// Base class for anything that lives in the game world at a floating-point position and is
/// backed by a loaded sprite frame (characters/foregroundcolors/backgroundcolors/materials
/// grid). Size and collision shape are both derived once from the frame's actual grid data via
/// <see cref="CollisionShapeBuilder"/>, so every sprite-backed object - the player, static
/// platforms, enemies, collectables - shares one loading/shape-derivation path instead of each
/// subclass repeating it.
/// </summary>
public abstract class Body2D
{
    private IReadOnlyList<Rect2D> _localCollisionRects = [];
    private double _animationElapsedSeconds;
    private int _animationFrameIndex;
    private int _animationDirection = 1;
    private int _repeatCount = 1;

    /// <summary>Position of the body's top-left corner, in world cells (not pixels).</summary>
    public Vector2D Position { get; set; }

    /// <summary>Size of the body's bounding box, in world cells.</summary>
    public Vector2D Size { get; set; } = new(1, 1);

    /// <summary>Whether this body is immovable terrain (true) or subject to physics/movement (false).</summary>
    public bool IsStatic { get; protected init; }

    /// <summary>
    /// Whether a static body blocks movement. Defaults to false, so an ordinary <c>IsStatic</c>
    /// body (a platform, wall) still blocks by default via <see cref="Physics.CollisionSystem"/>'s
    /// <c>solids</c> filter, which only checks this flag directly - never a body's concrete type
    /// or category, so any static placement (a collectable, a hazard, a plain wall used as a
    /// level-design "secret passage") can be made non-blocking per-instance without a new class.
    /// Meaningless on a non-static body, which was never blocking to begin with.
    /// </summary>
    public bool IsPassable { get; set; }

    /// <summary>
    /// Whether the player can climb this static body (e.g. a ladder) - straight up/down movement,
    /// gravity suspended while overlapping. Checked generically by <see cref="Physics.PhysicsSystem"/>
    /// against the player's current overlap each frame, independent of concrete type - any static
    /// placement (not just a dedicated "ladder" asset) can opt in via this flag. A climbable body
    /// is not automatically passable; set <see cref="IsPassable"/> too if it shouldn't also block
    /// movement (the usual case for an actual ladder).
    /// </summary>
    public bool IsClimbable { get; set; }

    /// <summary>
    /// Whether the player can hang and move laterally from this static body (e.g. a pipe/bar) -
    /// gravity mostly suspended while overlapping from below. Checked generically by
    /// <see cref="Physics.PhysicsSystem"/> against the player's current overlap each frame,
    /// independent of concrete type, the same way <see cref="IsClimbable"/> is. A hangable body is
    /// not automatically passable; set <see cref="IsPassable"/> too if it shouldn't also block
    /// movement (the usual case for an actual pipe/bar).
    /// </summary>
    public bool IsHangable { get; set; }

    /// <summary>The sprite asset this object was spawned from.</summary>
    public SpriteAsset Sprite { get; private set; } = null!;

    /// <summary>The clip currently being displayed/collided against (e.g. "idle").</summary>
    public SpriteClip Clip { get; private set; } = null!;

    /// <summary>The specific frame within <see cref="Clip"/> currently active.</summary>
    public SpriteFrame Frame { get; private set; } = null!;

    /// <summary>
    /// Collision shape, as one or more rectangles in local cell coordinates (relative to
    /// <see cref="Position"/>, not world space). Derived from the active frame's actual grid
    /// data via <see cref="CollisionShapeBuilder"/>, excluding blank/empty cells from physics.
    /// </summary>
    public IReadOnlyList<Rect2D> LocalCollisionRects => _localCollisionRects;

    /// <summary>The body's collision shape translated into world-space rectangles.</summary>
    public IReadOnlyList<Rect2D> CollisionRects
    {
        get
        {
            var position = Position;
            return LocalCollisionRects.Select(rect => rect.Translate(position)).ToList();
        }
    }

    /// <summary>Overall bounding box of the body in world space, spanning all collision rectangles.</summary>
    public double Left => Position.X;
    public double Right => Position.X + Size.X;
    public double Top => Position.Y;
    public double Bottom => Position.Y + Size.Y;

    /// <summary>
    /// Assigns the sprite/clip/frame this object renders and collides as, deriving Size and
    /// collision rectangles from the frame's char grid. Called once at spawn time; for a
    /// non-animating object (a static shape variant) this is the only call needed. Animated
    /// objects can call this again later to switch frames/clips. When the sprite declares a
    /// <see cref="Assets.TileAxis"/> and <paramref name="repeatCount"/> is greater than 1, the
    /// frame's authored unit is repeated along that axis first (see
    /// <see cref="SpriteFrameTiler"/>), letting one small tileable unit build up an
    /// arbitrary-length platform/wall.
    /// </summary>
    protected void SetFrame(SpriteAsset sprite, string clipName, int frameIndex = 0, int repeatCount = 1)
    {
        Sprite = sprite;
        Clip = sprite.GetClip(clipName);
        _animationFrameIndex = frameIndex;
        _repeatCount = repeatCount;
        _animationElapsedSeconds = 0;
        // Starting already at the last frame means the next PingPong tick should move backward,
        // not forward-then-immediately-clamp (which would otherwise waste one full tick doing
        // nothing visible - noticeable on short clips, e.g. a 2-frame walk cycle started at index 1).
        _animationDirection = _animationFrameIndex >= Clip.Frames.Count - 1 ? -1 : 1;

        ApplyFrame(Clip.Frames[_animationFrameIndex]);
    }

    /// <summary>
    /// Advances the animation timer and cycles to the next frame if enough time has elapsed.
    /// No-ops immediately if the clip has no animation settings, only one frame, or the clip's
    /// <see cref="AnimationMode"/> is <see cref="AnimationMode.Off"/> (holds forever on the
    /// frame set at spawn, e.g. a dead/inanimate variant of an otherwise-animated asset).
    /// </summary>
    public void AdvanceAnimation(double deltaSeconds)
    {
        // No animation configured, only one frame, or animation explicitly disabled - nothing to animate.
        if (Clip.FrameDurationSeconds is null || Clip.Frames.Count <= 1 || Clip.AnimationMode == AnimationMode.Off)
        {
            return;
        }

        _animationElapsedSeconds += deltaSeconds;

        while (_animationElapsedSeconds >= Clip.FrameDurationSeconds.Value)
        {
            _animationElapsedSeconds -= Clip.FrameDurationSeconds.Value;

            if (Clip.AnimationMode == AnimationMode.Loop)
            {
                _animationFrameIndex = (_animationFrameIndex + 1) % Clip.Frames.Count;
            }
            else if (Clip.AnimationMode == AnimationMode.Once)
            {
                // Advance toward the last frame and then clamp there - unlike Loop, never wraps
                // back to the first frame, so a one-shot transformation (e.g. a killed enemy's
                // crumble-to-husk clip) visibly plays through once and then holds indefinitely.
                if (_animationFrameIndex < Clip.Frames.Count - 1)
                {
                    _animationFrameIndex++;
                }
            }
            else // PingPong (Off already returned above)
            {
                _animationFrameIndex += _animationDirection;

                // Bounce at the ends.
                if (_animationFrameIndex >= Clip.Frames.Count - 1)
                {
                    _animationFrameIndex = Clip.Frames.Count - 1;
                    _animationDirection = -1;
                }
                else if (_animationFrameIndex <= 0)
                {
                    _animationFrameIndex = 0;
                    _animationDirection = 1;
                }
            }

            ApplyFrame(Clip.Frames[_animationFrameIndex]);
        }
    }

    /// <summary>
    /// Switches this body to display/collide as the clip for the given stance/facing pair (see
    /// docs/AssetFormat.md §2.6), re-deriving Size and collision rectangles from that clip's
    /// active frame exactly like <see cref="SetFrame"/> - a stance with a different silhouette
    /// (e.g. a shorter "Crawl" stance) is picked up automatically, with no separate pre-transition
    /// collision check required. No-ops if <paramref name="sprite"/> declares no matching stance
    /// (preserving single-clip behavior for assets without <c>[Stances]</c>), or if the resolved
    /// clip is already active (avoiding resetting that clip's own animation timer every call).
    /// </summary>
    public void SetPose(SpriteAsset sprite, string stance, Facing facing)
    {
        if (sprite.Stances is null || !sprite.Stances.TryGetValue(stance, out var stanceDef))
        {
            return;
        }

        var clipName = stanceDef.GetClipName(facing);
        if (Sprite == sprite && Clip is not null && string.Equals(Clip.Name, clipName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // DefaultFrame is tuned per-clip (e.g. to center a 3-frame Left/Idle/Right head-turn so
        // PingPong bounces symmetrically), but different clips of the same asset can have fewer
        // frames (e.g. a 2-frame walk cycle). Clamp so switching to a shorter clip never starts
        // out-of-range - and, critically, never starts a PingPong clip already pinned at its last
        // frame, which would otherwise waste its first bounce tick returning to that same frame.
        var targetClip = sprite.GetClip(clipName);
        var startFrame = Math.Min(targetClip.DefaultFrame, targetClip.Frames.Count - 1);

        SetFrame(sprite, clipName, startFrame);
    }

    /// <summary>
    /// Applies a specific frame (with optional tiling) to this body, updating Frame, Size, and
    /// collision rectangles. Used by both SetFrame (at spawn) and AdvanceAnimation (each frame
    /// advance during playback).
    /// </summary>
    private void ApplyFrame(SpriteFrame sourceFrame)
    {
        Frame = SpriteFrameTiler.Tile(sourceFrame, Sprite.TileAxis, _repeatCount);
        Size = new Vector2D(Frame.Width, Frame.Height);
        _localCollisionRects = CollisionShapeBuilder.DeriveRectangles(Frame.Chars, Sprite.EmptyChar);
    }
}
