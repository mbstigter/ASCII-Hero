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

    /// <summary>
    /// The dominant material name found among the active frame's non-empty cells (see
    /// <see cref="Assets.SpriteFrame.Materials"/>) - the single most common resolved material
    /// name across the frame's cells, or null if the frame has no material data at all. Recomputed
    /// whenever <see cref="ApplyFrame"/> runs (spawn, or a later frame/clip switch). A frame
    /// authored from a mix of materials (e.g. a composite sprite) is deliberately reduced to one
    /// representative name rather than tracked per-cell for collision purposes - simplest rule
    /// that still lets every existing single-material asset resolve exactly as authored; revisit
    /// only if a concrete asset genuinely needs per-cell-granular collision response.
    /// </summary>
    public string? MaterialName { get; private set; }

    /// <summary>
    /// Per-instance color code (see <c>Global/Colors.ini</c>) overriding this body's sprite's own
    /// <see cref="Assets.SpriteAsset.DefaultForeColor"/>/<see cref="Assets.SpriteAsset.DefaultBackColor"/>,
    /// set from this placement's <c>ForegroundColor</c>/<c>BackgroundColor</c> ini key (see
    /// <see cref="World.World2D.LoadAsync"/>). Null if the placement didn't specify one, in which
    /// case the sprite/level/hardcoded fallback chain applies unchanged (see
    /// <see cref="Rendering.WorldRenderer"/>). Mirrors <see cref="MaterialName"/>'s per-instance
    /// <c>Material</c> override - lets one sprite asset (e.g. the one shared <c>Ball</c>) be
    /// placed multiple times with a different color each time, without needing a separate asset
    /// per color.
    /// </summary>
    public char? ForeColorOverride { get; set; }

    /// <summary>See <see cref="ForeColorOverride"/>.</summary>
    public char? BackColorOverride { get; set; }

    /// <summary>
    /// Relative mass per world-cell "volume", resolved from <see cref="MaterialName"/> via
    /// <see cref="World.World2D.Materials"/> once this body is placed into a level (see
    /// <see cref="World.World2D.LoadAsync"/>). Defaults to 0 until resolved.
    /// </summary>
    public double Density { get; set; }

    /// <summary>Sliding resistance (0 = frictionless, 1 = very grippy), resolved the same way as <see cref="Density"/>.</summary>
    public double Friction { get; set; }

    /// <summary>
    /// Bounciness applied on collision (0 = no bounce, 1 = perfectly elastic), resolved the same
    /// way as <see cref="Density"/> unless a placement explicitly overrides it via the
    /// <c>Restitution</c> ini key (see <see cref="World.World2D.LoadAsync"/>).
    /// </summary>
    public double Restitution { get; set; }

    /// <summary>
    /// This body's mass, used by <see cref="Physics.CollisionSystem"/>'s impulse resolution and
    /// (for non-player bodies) <see cref="Physics.PhysicsSystem"/>'s force integration:
    /// <see cref="Density"/> times the body's current footprint area (<see cref="Size"/>'s width
    /// times height) - the simplest reasonable 2D proxy for volume, per docs/Decisions.md. Static
    /// bodies are always treated as effectively immovable regardless of this value (gated by
    /// <see cref="IsStatic"/>, not by mass), so a static placement's mass is never actually used
    /// in collision math.
    /// </summary>
    public double Mass => Density * Size.X * Size.Y;

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
        MaterialName = ResolveDominantMaterial(Frame.Materials);
    }

    /// <summary>
    /// Reduces a frame's per-cell material grid to one representative name: the most common
    /// non-null value among its cells, ties broken by first-encountered (row-major) order for a
    /// stable, deterministic result. Returns null if every cell is null (no material data at all,
    /// e.g. an asset with neither <c>DefaultMaterial</c> nor a per-cell <c>_materials.txt</c>).
    /// </summary>
    private static string? ResolveDominantMaterial(string?[,] materials)
    {
        var counts = new Dictionary<string, int>();
        var height = materials.GetLength(0);
        var width = materials.GetLength(1);

        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                var name = materials[row, col];
                if (name is null)
                {
                    continue;
                }

                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }

        if (counts.Count == 0)
        {
            return null;
        }

        var best = default(KeyValuePair<string, int>);
        var bestCount = -1;
        foreach (var entry in counts)
        {
            if (entry.Value > bestCount)
            {
                best = entry;
                bestCount = entry.Value;
            }
        }

        return best.Key;
    }
}
