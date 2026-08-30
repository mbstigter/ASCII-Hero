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
        _animationDirection = 1;

        ApplyFrame(Clip.Frames[_animationFrameIndex]);
    }

    /// <summary>
    /// Advances the animation timer and cycles to the next frame if enough time has elapsed.
    /// No-ops immediately if the sprite has no animation settings, the clip has only one frame,
    /// or the sprite's <see cref="AnimationMode"/> is <see cref="AnimationMode.Off"/> (holds
    /// forever on the frame set at spawn, e.g. a dead/inanimate variant of an otherwise-animated
    /// asset).
    /// </summary>
    public void AdvanceAnimation(double deltaSeconds)
    {
        // No animation configured, only one frame, or animation explicitly disabled - nothing to animate.
        if (Sprite.FrameDurationSeconds is null || Clip.Frames.Count <= 1 || Sprite.AnimationMode == AnimationMode.Off)
        {
            return;
        }

        _animationElapsedSeconds += deltaSeconds;

        while (_animationElapsedSeconds >= Sprite.FrameDurationSeconds.Value)
        {
            _animationElapsedSeconds -= Sprite.FrameDurationSeconds.Value;

            if (Sprite.AnimationMode == AnimationMode.Loop)
            {
                _animationFrameIndex = (_animationFrameIndex + 1) % Clip.Frames.Count;
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
