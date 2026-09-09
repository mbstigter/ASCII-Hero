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

    /// <summary>
    /// This frame's resolved contacts, keyed by <see cref="Physics.ContactType"/> flag with the
    /// set of other bodies contacted for that flag (e.g. <see cref="Physics.ContactType.SurfaceBottom"/>
    /// maps to whichever solid(s) this body is currently resting on). Rebuilt from scratch every
    /// frame by <see cref="Physics.CollisionSystem"/> as contacts are resolved - never mutated by
    /// any other system, and never carried forward as "still true until told otherwise": a stale
    /// entry from a contact that stopped applying is removed the same frame, not left to linger.
    /// See docs/Decisions.md for why this replaces the old ambiguous overlap-depth-only axis
    /// inference and the mutable <see cref="IPhysicsBody.IsGrounded"/> field it used to feed.
    /// </summary>
    private readonly Dictionary<Physics.ContactType, List<Body2D>> _contacts = [];

    /// <summary>
    /// A read-only snapshot of <see cref="_contacts"/> as it stood at the end of the *previous*
    /// frame, taken by <see cref="SnapshotContactsForNextFrame"/> right before this frame's
    /// contacts are cleared and rebuilt. Used purely to disambiguate direction/approach (e.g. "was
    /// this body already resting on top of that solid last frame" for one-way platforms, or "was a
    /// moving platform already remembered as what this body rests on" for vertical/horizontal
    /// carry) - never consulted to decide *this* frame's own contact state, which is always
    /// resolved fresh.
    /// </summary>
    private IReadOnlyDictionary<Physics.ContactType, List<Body2D>> _previousContacts =
        new Dictionary<Physics.ContactType, List<Body2D>>();

    /// <summary>
    /// Whether this body's current-frame contacts include the given <paramref name="type"/> with
    /// any other body at all (an OR across every set flag if <paramref name="type"/> is a
    /// combination). The single source of truth other systems should query instead of reading a
    /// separately maintained flag - e.g. <see cref="IPhysicsBody.IsGrounded"/> is simply
    /// <c>HasContact(ContactType.SurfaceBottom)</c>.
    /// </summary>
    public bool HasContact(Physics.ContactType type)
    {
        foreach (var flag in EnumerateFlags(type))
        {
            if (_contacts.TryGetValue(flag, out var bodies) && bodies.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether this body's contacts *as of the end of last frame* included the given
    /// <paramref name="type"/> with <paramref name="other"/> specifically (or with anything, if
    /// <paramref name="other"/> is null). Used only for direction/approach disambiguation - see
    /// <see cref="_previousContacts"/>.
    /// </summary>
    public bool HadContactLastFrame(Physics.ContactType type, Body2D? other = null)
    {
        foreach (var flag in EnumerateFlags(type))
        {
            if (!_previousContacts.TryGetValue(flag, out var bodies))
            {
                continue;
            }

            if (other is null ? bodies.Count > 0 : bodies.Contains(other))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The other bodies currently contacted for the given single <paramref name="type"/> flag, or empty if none.</summary>
    public IReadOnlyList<Body2D> GetContactingBodies(Physics.ContactType type) =>
        _contacts.TryGetValue(type, out var bodies) ? bodies : [];

    /// <summary>
    /// Removes a recorded contact for the given (single-flag) <paramref name="type"/> - either
    /// just with <paramref name="other"/>, or every body recorded for that flag if
    /// <paramref name="other"/> is null. Used sparingly, only when a system needs this frame's
    /// derived state (e.g. <see cref="IsGrounded"/>) to reflect a change immediately rather than
    /// waiting for the next frame's contact resolution - e.g. <see cref="Physics.PhysicsSystem"/>
    /// clearing a just-jumped player's <see cref="Physics.ContactType.SurfaceBottom"/> contact so
    /// its pose immediately shows airborne instead of grounded for the one frame before collision
    /// re-resolves. This is still just editing this frame's contact set, not reintroducing a
    /// separately mutable flag.
    /// </summary>
    public void RemoveContact(Physics.ContactType type, Body2D? other = null)
    {
        if (!_contacts.TryGetValue(type, out var bodies))
        {
            return;
        }

        if (other is null)
        {
            bodies.Clear();
        }
        else
        {
            bodies.Remove(other);
        }
    }

    /// <summary>
    /// Records that <paramref name="other"/> is contacted for the given (single-flag)
    /// <paramref name="type"/> this frame. Called by <see cref="Physics.CollisionSystem"/> as
    /// contacts are resolved each frame - never by a body on itself.
    /// </summary>
    public void AddContact(Physics.ContactType type, Body2D other)
    {
        if (!_contacts.TryGetValue(type, out var bodies))
        {
            bodies = [];
            _contacts[type] = bodies;
        }

        if (!bodies.Contains(other))
        {
            bodies.Add(other);
        }
    }

    /// <summary>
    /// Clears every recorded contact for this frame, first preserving the current set as
    /// "previous frame" (see <see cref="_previousContacts"/>) for the next frame's direction
    /// disambiguation. Called once per body at the start of <see cref="Physics.CollisionSystem.Resolve"/>,
    /// before any contact for the new frame is resolved/recorded.
    /// </summary>
    public void SnapshotContactsForNextFrame()
    {
        var snapshot = new Dictionary<Physics.ContactType, List<Body2D>>(_contacts.Count);
        foreach (var (type, bodies) in _contacts)
        {
            snapshot[type] = [.. bodies];
        }

        _previousContacts = snapshot;
        _contacts.Clear();
    }

    /// <summary>
    /// Whether this body currently rests on something solid, be it a platform's top surface, the
    /// world's own floor, or the top of another moving body. Derived fresh from this frame's
    /// resolved contacts (<c>HasContact(ContactType.SurfaceBottom)</c>) rather than a stored,
    /// mutable flag - there is nothing to reset each frame and no risk of stale state from a
    /// missed reset/set ordering, which is exactly the class of bug the old stored flag caused
    /// (see docs/Decisions.md). Any future hysteresis/game-feel exception (e.g. coyote-time jump
    /// forgiveness) must be its own separately named field - never reusing this property's name
    /// or storage.
    /// </summary>
    public bool IsGrounded => HasContact(Physics.ContactType.SurfaceBottom);

    private static IEnumerable<Physics.ContactType> EnumerateFlags(Physics.ContactType type)
    {
        foreach (Physics.ContactType flag in Enum.GetValues<Physics.ContactType>())
        {
            if (flag != Physics.ContactType.None && type.HasFlag(flag))
            {
                yield return flag;
            }
        }
    }

    /// <summary>Position of the body's top-left corner, in world cells (not pixels).</summary>
    public Vector2D Position { get; set; }

    /// <summary>Size of the body's bounding box, in world cells.</summary>
    public Vector2D Size { get; set; } = new(1, 1);

    /// <summary>
    /// Whether this body is immune to collision response - <see cref="Physics.CollisionSystem"/>
    /// never corrects its own position/velocity when something collides with it (it only ever
    /// modifies the *other* side of a solid collision). This is orthogonal to whether the body
    /// actually moves: an ordinary platform/wall is both static and stationary (its <see cref="Position"/>
    /// never changes), but a body can also be static *and* implement <see cref="IPhysicsBody"/>
    /// with a real, non-zero <see cref="IPhysicsBody.Velocity"/> - a "kinematic body" in the usual
    /// physics-engine sense (see <see cref="KinematicObject2D"/>): it drives its own prescribed
    /// motion every frame and other bodies collide against/are carried by it, but it is never
    /// itself pushed, bounced, or halted by anything it touches.
    /// </summary>
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

    /// <summary>
    /// Whether this body only blocks approach from above - a body resting on top of it lands and
    /// stays grounded normally, but a body approaching from below (jumping up through it, or
    /// already underneath) passes straight through instead of colliding. Disambiguated using last
    /// frame's contacts (see <see cref="HadContactLastFrame"/>): a <see cref="Physics.ContactType.SurfaceBottom"/>
    /// contact against this body is only created/kept if the other body was already resting on
    /// top of it as of last frame, never freshly created for a body newly arriving from below.
    /// Has no effect unless <see cref="IsStatic"/> is also true (moving one-way platforms are not
    /// currently supported).
    /// </summary>
    public bool IsOneWayPlatform { get; set; }

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
    /// <see cref="World.World2D.LoadAsync"/>). Defaults to 0 until resolved, but an object type's
    /// ini section may instead set an explicit <c>Density</c> override to depart from its resolved
    /// material's density outright (e.g. a body whose effective density changes per-instance or at
    /// runtime, like water that's been heated) while still inheriting that material's other
    /// properties.
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
    /// (for non-player bodies) <see cref="Physics.PhysicsSystem"/>'s force integration. Defaults
    /// to <see cref="Density"/> times the body's current footprint area (<see cref="Size"/>'s
    /// width times height) - the simplest reasonable 2D proxy for volume, per docs/Decisions.md -
    /// but a placement may instead set an explicit <c>Mass</c> ini override (see
    /// <see cref="World.World2D.LoadAsync"/>) when the density-times-footprint default would be
    /// unrealistic for that body's actual shape/weight. Static bodies are always treated as
    /// effectively immovable regardless of this value (gated by <see cref="IsStatic"/>, not by
    /// mass), so a static placement's mass is never actually used in collision math.
    /// </summary>
    public double Mass
    {
        get => _massOverride ?? Density * Size.X * Size.Y;
        set => _massOverride = value;
    }

    private double? _massOverride;

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
    /// Switches this body to display/collide as the clip for the given pose/facing pair (see
    /// docs/AssetFormat.md §2.6), re-deriving Size and collision rectangles from that clip's
    /// active frame exactly like <see cref="SetFrame"/> - a pose with a different silhouette
    /// (e.g. a shorter "Crawl" pose) is picked up automatically, with no separate pre-transition
    /// collision check required. No-ops if <paramref name="sprite"/> declares no matching pose
    /// (preserving single-clip behavior for assets without <c>[Poses]</c>), or if the resolved
    /// clip is already active (avoiding resetting that clip's own animation timer every call).
    /// </summary>
    public void SetPose(SpriteAsset sprite, string pose, Facing facing)
    {
        if (sprite.Poses is null || !sprite.Poses.TryGetValue(pose, out var poseDef))
        {
            return;
        }

        var clipName = poseDef.GetClipName(facing);
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
    /// Resolves a left/right <see cref="Facing"/> from a horizontal velocity, the shared rule used
    /// by every horizontally-facing body (the player while walking/crawling/hanging, and any
    /// <see cref="IPosedBody"/> moving body such as <see cref="MovingEnemy2D"/>) so this mapping is
    /// defined exactly once rather than re-implemented per body type.
    /// </summary>
    public static Facing ResolveHorizontalFacing(double velocityX) =>
        velocityX < 0 ? Facing.Left : velocityX > 0 ? Facing.Right : Facing.Idle;

    /// <summary>
    /// The horizontal velocity of whichever solid this body currently rests on top of (see
    /// <see cref="Physics.ContactType.SurfaceBottom"/>), or 0 if not grounded or resting on
    /// stationary terrain. This is the reference frame a resting/patrolling/walking body's own
    /// *intent* (its target speed, or whether it is holding still) should be judged relative to -
    /// a body standing still with zero walk/patrol intent while carried along by a fast-moving
    /// platform has an absolute <see cref="Velocity"/>.X matching the platform's own speed, which
    /// would otherwise misreport as "walking"/"patrolling" in that direction (see
    /// <see cref="ResolveHorizontalFacing"/>) or fight the platform's carry as if it were an
    /// unwanted push (see <see cref="MovingEnemy2D.UpdatePatrolDirection"/>). Picks the first
    /// grounded contact that is itself an <see cref="IPhysicsBody"/> with a real velocity (e.g.
    /// <see cref="KinematicObject2D"/>); ordinary stationary terrain has none, so this falls back
    /// to 0 exactly as it would without a moving platform involved at all.
    /// </summary>
    public double GetSurfaceVelocityX()
    {
        foreach (var solid in GetContactingBodies(Physics.ContactType.SurfaceBottom))
        {
            if (solid is IPhysicsBody solidBody)
            {
                return solidBody.Velocity.X;
            }
        }

        return 0.0;
    }

    /// <summary>
    /// Resolves an up/down <see cref="Facing"/> from a vertical velocity - the vertical
    /// counterpart to <see cref="ResolveHorizontalFacing"/>, used by any body whose pose faces
    /// along the Y axis instead of X (e.g. the player's "Climb" pose, whose idle-vs-arm-over-arm
    /// distinction is a movement direction read from velocity, not a sideways-facing one).
    /// </summary>
    public static Facing ResolveVerticalFacing(double velocityY) =>
        velocityY < 0 ? Facing.Up : velocityY > 0 ? Facing.Down : Facing.Idle;

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
