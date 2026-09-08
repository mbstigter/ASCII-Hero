using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>
/// Resolves simple axis-aligned bounding box collisions between moving bodies (the player,
/// dynamic objects, kinematic objects, moving enemies), solid static bodies, and the world's own
/// bounds, plus generic hazard/collectable overlap. All collision is resolved generically against
/// <see cref="IPhysicsBody"/>/<see cref="World2D.Objects"/> - the player is just a moving body
/// whose restitution happens to be 0 (it stops dead rather than bouncing); there is no
/// special-casing by concrete type anywhere in this class.
/// </summary>
public class CollisionSystem
{
    /// <summary>
    /// Speed (in cells/second, on the axis relevant to the surface) above which a body is moving
    /// too fast to snap onto a climbable/hangable surface on first touch - it should instead keep
    /// falling/moving through, exactly like a body still rising past the peak of a jump shouldn't
    /// instantly snap onto a platform's underside. Deliberately generous (comparable to a fast
    /// fall speed under gravity) so an ordinary jump arc or ladder dismount is never blocked by
    /// this, while a body in true freefall barrelling straight through a thin pipe/ladder cell in
    /// a single frame is not mistakenly caught mid-flight.
    /// </summary>
    private const double MaxSnapSpeed = 24.0;

    /// <summary>
    /// Tiny amount <see cref="SnapOntoHangable"/> pulls the hanger's top edge *above* (i.e. a
    /// smaller Y than) the hangable surface's bottom edge, so the two rectangles remain genuinely
    /// overlapping - not merely touching - on the very next frame. <see cref="Rect2D.Overlaps"/>
    /// uses strict inequalities (<c>bodyRect.Top &lt; otherRect.Bottom</c>), so a body snapped to
    /// land exactly flush (top == other's bottom), or worse, pushed slightly past it, has zero (or
    /// negative) actual overlap and <see cref="IHangerBody.IsTouchingHangable"/> would immediately
    /// flip back to false one frame after grabbing on, dropping the player right after the snap
    /// ever became visible. Small enough to be visually imperceptible, just like the old
    /// <c>EdgeTolerance</c> it replaces here, but used to guarantee persistence instead of to gate
    /// an approximate edge comparison.
    /// </summary>
    private const double HangOverlapEpsilon = 0.01;

    /// <summary>
    /// World-space size (in cells) of one broad-phase spatial-grid bucket - see
    /// <see cref="BuildSpatialGrid"/>/<see cref="GetCandidates"/>. Chosen comfortably larger than
    /// a typical body/platform's own footprint so most bodies span only one or two buckets rather
    /// than dozens, while still being small enough that a handful of nearby objects (not the
    /// entire level) are gathered as candidates for any one body.
    /// </summary>
    private const double GridCellSize = 4.0;

    /// <summary>Reused across frames to avoid an allocation every call for what is normally a tiny list.</summary>
    private readonly List<IPhysicsBody> _movingBodies = [];

    /// <summary>Reused across frames for the solids broad-phase grid built once per <see cref="Resolve"/> call.</summary>
    private readonly Dictionary<(int X, int Y), List<Body2D>> _solidsGrid = [];

    /// <summary>Reused across frames for the moving-bodies broad-phase grid built once per <see cref="Resolve"/> call.</summary>
    private readonly Dictionary<(int X, int Y), List<IPhysicsBody>> _movingBodiesGrid = [];

    /// <summary>Reused per query to avoid a fresh allocation for every single candidate lookup.</summary>
    private readonly HashSet<Body2D> _candidateSolidsBuffer = [];

    /// <summary>Reused per query to avoid a fresh allocation for every single candidate lookup.</summary>
    private readonly HashSet<IPhysicsBody> _candidateMovingBuffer = [];

    /// <summary>
    /// A placeholder <see cref="Body2D"/> representing the world's own floor/walls/ceiling for
    /// contact-recording purposes only (see <see cref="ResolveWorldBounds"/>) - never rendered,
    /// never collided against directly, never placed in <see cref="World2D.Objects"/>. Exists so
    /// a body resting against the world's edge (not a placed platform) can still record an
    /// ordinary <see cref="ContactType.SurfaceBottom"/> contact - and therefore still derive
    /// <see cref="IPhysicsBody.IsGrounded"/> - the same way it would resting on any other solid,
    /// with no special-casing needed by <see cref="Body2D.IsGrounded"/> or its consumers.
    /// </summary>
    private static readonly Body2D WorldBoundsSentinel = new WorldBoundsBody();

    private sealed class WorldBoundsBody : Body2D
    {
        public WorldBoundsBody() => IsStatic = true;
    }

    /// <summary>
    /// TEMPORARY HACK - remove once the player moves via a mass/force accumulator instead of
    /// direct velocity assignment (see the TODO on <see cref="Physics.PhysicsSystem.Step"/> and
    /// docs/Decisions.md). Which solid the player was resting on as of the <em>previous</em>
    /// frame's <see cref="Resolve"/> call - used solely to carry the player along a moving
    /// platform's own frame-to-frame *horizontal* displacement (see the top of
    /// <see cref="Resolve"/>; vertical carry is already handled by ordinary velocity-matching +
    /// landing-snap and must not be duplicated here) before this frame's ordinary overlap-based
    /// collision pass re-detects/re-confirms grounding. This dictionary and the shift that reads
    /// it only exist to work around <c>PhysicsSystem.Step</c> overwriting
    /// <c>Player2D.Velocity.X</c> from input every frame, which wipes out the friction-based
    /// horizontal velocity-matching <c>ResolveRectAgainstSolid</c> already applies to every other
    /// <see cref="IPhysicsBody"/>. Once the player integrates velocity like everything else, that
    /// same existing velocity-matching will carry the player horizontally for free, this entire
    /// mechanism becomes redundant, and it - along with every reference to it in this file -
    /// should be deleted rather than kept "just in case". Currently keyed by
    /// <see cref="IPhysicsBody"/> for symmetry with how grounding is generally scoped, but in
    /// practice only ever populated for the player (see the check in <see cref="Resolve"/>) - not
    /// a property on <see cref="IPhysicsBody"/> itself since no other body needs it, and not
    /// something any other system reads or sets (unlike <see cref="IPhysicsBody.IsGrounded"/>,
    /// which genuinely is consulted elsewhere - jump logic, pose selection, etc.). Cleared the
    /// instant the player is no longer found resting on a solid this frame (see the bottom of
    /// <see cref="Resolve"/>), so a stale/removed solid is never carried forward.
    /// </summary>
    private readonly Dictionary<IPhysicsBody, Body2D> _groundedSolids = [];

    /// <summary>
    /// Hazard/body contact pairs still overlapping as of the frame just resolved. Used so an
    /// ordinary (non-kill) hazard contact's effect fires only on the first frame of a new contact
    /// - a "rising edge" - rather than every single frame the two remain overlapping. Unlike solid
    /// terrain, a hazard never physically pushes a body back out (see the <c>solids</c> filter in
    /// <see cref="Resolve"/>), so a body can rest against/inside one for many consecutive frames.
    /// </summary>
    private HashSet<(Body2D Hazard, IPhysicsBody Body)> _activeHazardContacts = [];

    /// <summary>
    /// Which narrow-phase test confirms an AABB overlap once broad phase finds two bodies'
    /// bounding boxes intersect - see <see cref="NarrowPhaseMode"/>. Defaults to
    /// <see cref="NarrowPhaseMode.MultiRect"/> (the original, still fully supported behavior);
    /// switch to <see cref="NarrowPhaseMode.CharacterGrid"/> to additionally require the two
    /// bodies' actual rendered characters overlap, not merely their merged collision rectangles -
    /// useful for comparing the two approaches on shapes whose true silhouette isn't a perfect
    /// rectangle. A runtime-settable property (not a compile-time constant) so it can be toggled
    /// via a debug/config setting without a rebuild.
    /// </summary>
    public NarrowPhaseMode NarrowPhaseMode { get; set; } = NarrowPhaseMode.MultiRect;

    /// <summary>
    /// Resolves all collision for one frame. <paramref name="deltaSeconds"/> is used solely for
    /// the TEMPORARY player-only horizontal platform-carry hack (see <see cref="_groundedSolids"/>)
    /// - every other collision response in this class is purely positional/velocity-based and
    /// doesn't need a time delta of its own.
    /// </summary>
    public void Resolve(World2D world, double deltaSeconds)
    {
        _movingBodies.Clear();

        // Every body (static or moving) can be on the receiving end of a recorded contact this
        // frame (e.g. a static platform records SurfaceTop when something lands on it), so every
        // body's contact set is snapshotted-and-cleared up front, before anything for this new
        // frame is resolved/recorded - see Body2D.SnapshotContactsForNextFrame.
        foreach (var contactBody in world.Objects)
        {
            contactBody.SnapshotContactsForNextFrame();
        }

        WorldBoundsSentinel.SnapshotContactsForNextFrame();

        foreach (var body in world.Objects)
        {
            // A static body (e.g. KinematicObject2D) can implement IPhysicsBody for its own real
            // Velocity - so other bodies' collision response can react to it (see
            // Body2D.IsStatic) - without itself being resolved as a mover: it must never appear
            // in the solids list's own pairing loop, world-bounds check, or moving-body-vs-moving-
            // body pairing, all of which assume the body they're correcting is actually pushable.
            if (body is not IPhysicsBody movingBody || body.IsStatic)
            {
                continue;
            }

            // TEMPORARY HACK (see the remarks on _groundedSolids) - carry the player along by its
            // previously-grounded solid's own horizontal displacement this frame, *before* the
            // IsGrounded reset/re-detection below runs. Player-only, horizontal-only:
            // PhysicsSystem.Step overwrites the player's Velocity.X directly from input every
            // frame (see its own remarks), so the friction-based velocity-matching that
            // ResolveRectAgainstSolid already applies on landing (nudging the rider's velocity
            // toward the solid's own) never gets a chance to take effect for the player
            // horizontally - without this explicit shift, a stationary (no
            // left/right held) player doesn't move with a horizontally-patrolling platform
            // underneath it. Every other IPhysicsBody (MovingEnemy2D, DynamicObject2D) moves via
            // StepMovingBodyWithForces instead, which integrates its own Velocity.X - already
            // matched to the solid's by ResolveRectAgainstSolid - into Position every frame just
            // like the player's Velocity.Y does; applying this same shift to those bodies too
            // would double-count that already-working horizontal carry and made a patrolling
            // enemy resting on a moving platform (PatrolForce = 0) slide off it instead of staying
            // put. DELETE THIS ENTIRE BLOCK once the player moves via force/mass integration
            // instead of direct velocity assignment - at that point the ordinary velocity-matching
            // used by every other body will carry the player horizontally too, with no
            // special-casing needed. Deliberately does NOT apply vertically for the same reason,
            // even for the player: gravity-integrated bodies already track a vertically-moving
            // solid via that same velocity-matching (their Velocity.Y is nudged toward the
            // solid's each frame in ResolveRectAgainstSolid, then integrated into Position by
            // PhysicsSystem *before* this runs), and the very next line below's ordinary landing
            // check snaps the body flush onto the solid's current (already-moved) top surface
            // regardless - adding a *second*, independent Position.Y shift here on top of both of
            // those double-counts the platform's own motion, overshooting the snap and fighting
            // the following frame's correction, which is exactly what caused the up/down jitter
            // this replaced. Only applies if the previously-grounded solid is still actually a
            // real, still-existing body with non-zero horizontal velocity (ordinary stationary
            // terrain has none, so this is a no-op for the overwhelming common case of resting on
            // a plain platform/floor).
            if (movingBody is Player2D
                && _groundedSolids.TryGetValue(movingBody, out var groundedSolid)
                && groundedSolid is IPhysicsBody groundedSolidBody)
            {
                var solidVelocityX = groundedSolidBody.Velocity.X;
                if (solidVelocityX != 0)
                {
                    movingBody.Position = new Vector2D(
                        movingBody.Position.X + solidVelocityX * deltaSeconds,
                        movingBody.Position.Y);
                }
            }

            // Permanent (not a hack) counterpart for the vertical axis: re-seat a still-grounded
            // body flush onto its remembered solid's *current* top surface, provided it still
            // overlaps that solid horizontally - see MaintainVerticalGroundedContact's own remarks
            // for why this direct, absolute re-seat (rather than an additive velocity/position
            // delta, which is what caused the earlier jitter regression) cannot double-count with
            // the existing velocity-matching + landing-snap collision response below. Gated on
            // movingBody.IsGrounded still being true: PhysicsSystem.Step already sets it false the
            // instant a jump (or a climb/hang stance) is initiated, before this Resolve call runs -
            // without this guard, a player who just jumped off a platform this frame would be
            // wrongly snapped straight back down onto it for still horizontally overlapping.
            // Additionally gated on the remembered solid actually being a moving IPhysicsBody with
            // non-zero velocity: ordinary stationary terrain never has this gap in the first place
            // (nothing to outrun), and a body with restitution > 0 (e.g. DynamicObject2D's
            // bouncing ball) still has IsGrounded briefly true the very frame it bounces upward
            // off of stationary terrain - re-seating it down every such frame regardless of its
            // now-upward velocity would wrongly cancel every bounce off solid ground.
            // Uses HadContactLastFrame rather than IsGrounded: this frame's contacts were already
            // cleared above (SnapshotContactsForNextFrame), so IsGrounded would read false here
            // regardless - last frame's grounded state is exactly what's needed to decide whether
            // this maintenance step applies before this frame's own landing check re-establishes
            // (or doesn't) a fresh SurfaceBottom contact.
            if (movingBody.HadContactLastFrame(ContactType.SurfaceBottom)
                && _groundedSolids.TryGetValue(movingBody, out var solidToMaintain)
                && solidToMaintain is IPhysicsBody solidToMaintainBody
                && (solidToMaintainBody.Velocity.X != 0 || solidToMaintainBody.Velocity.Y != 0))
            {
                MaintainVerticalGroundedContact(movingBody, solidToMaintain);
            }

            _movingBodies.Add(movingBody);
        }

        ResolveClimbingAndHanging(world);


        // Body2D.IsPassable, checked directly and generically rather than by excluding specific
        // categories/types one at a time. Collectables and hazards default to passable (see
        // World2D.LoadAsync), a plain wall can opt into being passable too (e.g. a level-design
        // "secret passage"), and EffectInstance2D is always passable (see its own doc comment) -
        // none of that is special-cased here, it all flows through this one flag.
        var solids = world.Objects.Where(body => body.IsStatic && !body.IsPassable).ToList();

        // Broad phase: bucket solids into a spatial grid once per frame so each moving body only
        // has to test collision against nearby solids, not every solid in the level - see
        // BuildSolidsGrid/GetCandidateSolids and docs/Decisions.md.
        BuildSolidsGrid(solids);
        BuildMovingBodiesGrid(_movingBodies);

        // Prune entries for bodies that no longer exist (e.g. a killed enemy removed via
        // World2D.ApplyPendingRemovals) so this dictionary can't grow unbounded over a long play
        // session - _movingBodies above already reflects exactly the current, still-alive set of
        // non-static IPhysicsBody instances in the world.
        if (_groundedSolids.Count > 0)
        {
            foreach (var staleBody in _groundedSolids.Keys.Except(_movingBodies).ToList())
            {
                _groundedSolids.Remove(staleBody);
            }
        }

        foreach (var body in _movingBodies)
        {
            _groundedSolids.Remove(body);
            foreach (var solid in GetCandidateSolids(body))
            {
                if (ResolveAgainstSolid(body, solid))
                {
                    _groundedSolids[body] = solid;
                }
            }
        }

        // Every moving body can also collide with every other moving body (e.g. the player and
        // the bouncing ball) - checked once per unordered pair, gathered from the same spatial
        // grid so this scales with nearby movers only, not every mover in the level.
        for (var i = 0; i < _movingBodies.Count; i++)
        {
            foreach (var other in GetCandidateMovingBodies(_movingBodies[i]))
            {
                if (_movingBodies.IndexOf(other) > i)
                {
                    ResolveBodyPair(_movingBodies[i], other);
                }
            }
        }

        foreach (var body in _movingBodies)
        {
            ResolveWorldBounds(world, body);
        }


        ResolveHazardsAndCollectables(world);
    }

    /// <summary>
    /// The spatial-grid bucket coordinate a body's current AABB (its overall <see cref="Vector2D"/>
    /// position/size, not its individual collision rects) spans - returns every bucket the body's
    /// bounding box overlaps, since a body near a bucket boundary can span more than one.
    /// </summary>
    private static IEnumerable<(int X, int Y)> GetOverlappingCells(Vector2D position, Vector2D size)
    {
        var minX = (int)Math.Floor(position.X / GridCellSize);
        var maxX = (int)Math.Floor((position.X + size.X) / GridCellSize);
        var minY = (int)Math.Floor(position.Y / GridCellSize);
        var maxY = (int)Math.Floor((position.Y + size.Y) / GridCellSize);

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                yield return (x, y);
            }
        }
    }

    /// <summary>
    /// Rebuilds <see cref="_solidsGrid"/> from scratch for this frame's <paramref name="solids"/>
    /// list - static bodies never move mid-frame, but the set of which solids exist can change
    /// between frames (spawned/removed), so this is cheap enough to just redo every <see cref="Resolve"/>
    /// call rather than trying to incrementally maintain it.
    /// </summary>
    private void BuildSolidsGrid(IReadOnlyList<Body2D> solids)
    {
        foreach (var bucket in _solidsGrid.Values)
        {
            bucket.Clear();
        }

        foreach (var solid in solids)
        {
            foreach (var cell in GetOverlappingCells(solid.Position, solid.Size))
            {
                if (!_solidsGrid.TryGetValue(cell, out var bucket))
                {
                    bucket = [];
                    _solidsGrid[cell] = bucket;
                }

                bucket.Add(solid);
            }
        }
    }

    /// <summary>Same as <see cref="BuildSolidsGrid"/>, but for this frame's moving bodies (used for moving-body-vs-moving-body pairing).</summary>
    private void BuildMovingBodiesGrid(IReadOnlyList<IPhysicsBody> movingBodies)
    {
        foreach (var bucket in _movingBodiesGrid.Values)
        {
            bucket.Clear();
        }

        foreach (var movingBody in movingBodies)
        {
            foreach (var cell in GetOverlappingCells(movingBody.Position, movingBody.Size))
            {
                if (!_movingBodiesGrid.TryGetValue(cell, out var bucket))
                {
                    bucket = [];
                    _movingBodiesGrid[cell] = bucket;
                }

                bucket.Add(movingBody);
            }
        }
    }

    /// <summary>
    /// The distinct set of solids sharing at least one spatial-grid bucket with <paramref name="body"/>
    /// this frame - the broad-phase candidate set that <see cref="ResolveAgainstSolid"/> is then
    /// actually run against, instead of every solid in the level.
    /// </summary>
    private IEnumerable<Body2D> GetCandidateSolids(IPhysicsBody body)
    {
        _candidateSolidsBuffer.Clear();
        foreach (var cell in GetOverlappingCells(body.Position, body.Size))
        {
            if (!_solidsGrid.TryGetValue(cell, out var bucket))
            {
                continue;
            }

            foreach (var solid in bucket)
            {
                _candidateSolidsBuffer.Add(solid);
            }
        }

        return _candidateSolidsBuffer;
    }

    /// <summary>Same as <see cref="GetCandidateSolids"/>, but for other moving bodies (including <paramref name="body"/> itself, filtered out by the caller).</summary>
    private IEnumerable<IPhysicsBody> GetCandidateMovingBodies(IPhysicsBody body)
    {
        _candidateMovingBuffer.Clear();
        foreach (var cell in GetOverlappingCells(body.Position, body.Size))
        {
            if (!_movingBodiesGrid.TryGetValue(cell, out var bucket))
            {
                continue;
            }

            foreach (var other in bucket)
            {
                if (!ReferenceEquals(other, body))
                {
                    _candidateMovingBuffer.Add(other);
                }
            }
        }

        return _candidateMovingBuffer;
    }

    /// <summary>
    /// Sets <see cref="IClimberBody.IsTouchingClimbable"/>/<see cref="IHangerBody.IsTouchingHangable"/>
    /// from a body's current overlap against static <see cref="Body2D.IsClimbable"/>/
    /// <see cref="Body2D.IsHangable"/> terrain - the same "recomputed fresh every frame, never
    /// persisted" pattern already used for <see cref="IPhysicsBody.IsGrounded"/>. Generic over
    /// any <see cref="IClimberBody"/>/<see cref="IHangerBody"/> in the world, not just the player -
    /// an enemy could implement either capability the same way. <see cref="Physics.PhysicsSystem"/>
    /// reads these the following frame to decide whether to actually engage
    /// <see cref="IClimberBody.IsClimbing"/>/<see cref="IHangerBody.IsHanging"/>.
    /// </summary>
    /// <remarks>
    /// A ladder can be grabbed from any side (climbing up into it, sideways into it mid-jump, or
    /// falling down onto/through it), so climbable overlap has no directional restriction - just
    /// overlap plus a generic snap-speed gate (see <see cref="MaxSnapSpeed"/>). A hangable surface
    /// is different: reaching up and grabbing a bar/pipe should only trigger when actually
    /// approaching from underneath (see <see cref="WouldSnapFromBelow"/>) - not merely brushing
    /// its top while landing on it - and likewise gated on vertical speed so a body plummeting
    /// straight through a thin pipe in one frame isn't caught mid-fall.
    /// </remarks>
    private static void ResolveClimbingAndHanging(World2D world)
    {
        var climbables = world.Objects.Where(body => body.IsStatic && body.IsClimbable).ToList();
        var hangables = world.Objects.Where(body => body.IsStatic && body.IsHangable).ToList();

        foreach (var body in world.Objects)
        {
            if (body is IClimberBody climber)
            {
                climber.IsTouchingClimbable = IsWithinSnapSpeed(climber.Velocity) &&
                    climbables.Any(climbable => Overlaps(climber, climbable));
            }

            if (body is IHangerBody hanger)
            {
                hanger.IsTouchingHangable = false;
                if (IsWithinSnapSpeed(hanger.Velocity))
                {
                    foreach (var hangable in hangables)
                    {
                        if (!Overlaps(hanger, hangable) || !WouldSnapFromBelow(hanger, hangable))
                        {
                            continue;
                        }

                        // IsTouchingHangable itself must still be reported here even while
                        // SuppressHangUntilClear is set below - exactly like IsTouchingClimbable
                        // above, which is never gated by _suppressClimbUntilClear - so that
                        // PhysicsSystem's debounce-release check ("once no longer touching at
                        // all") only fires once the body has genuinely cleared the surface's
                        // overlap, not the instant a jump/swing begins while still overlapping it.
                        hanger.IsTouchingHangable = true;

                        // While IHangerBody.SuppressHangUntilClear is set (PhysicsSystem just made
                        // the player jump/swing off or explicitly let go this same frame), skip
                        // the actual snap/stop below even though overlap is still detected above -
                        // otherwise the jump-off velocity set moments ago in PhysicsSystem would
                        // be immediately zeroed and the body re-snapped right back, making it look
                        // like the jump/swing never happened. The overlap keeps being reported
                        // above regardless, so the debounce is only released once truly clear.
                        if (hanger.SuppressHangUntilClear)
                        {
                            break;
                        }

                        // Detect and correct synchronously, in this same call, exactly like
                        // ResolveRectAgainstSolid does for solid terrain - it never waits for a
                        // flag set on a previous frame (e.g. IsGrounded) before stopping/snapping
                        // a body, it reacts to the overlap the instant it sees it. An earlier
                        // version of this deferred SnapOntoHangable until hanger.IsHanging was
                        // already true (only set the *following* frame by PhysicsSystem, since
                        // physics runs before collision each tick), intending to let a jump
                        // continue rising past first contact - but a thin, one-cell-tall pipe
                        // often only overlaps for a single frame, so by the time IsHanging
                        // finally engaged next frame the body had already moved fully clear of it
                        // (uncorrected), and the overlap was gone again before the catch could
                        // ever apply - the body would merely freeze for one stray frame (feeling
                        // like hitting an invisible ceiling) and then fall/jump straight through
                        // with no actual grab, both from below and from above. Stopping and
                        // snapping immediately here, the same frame overlap is first found, is
                        // what makes solid landings reliable and must work the same way here.
                        hanger.Velocity = new Vector2D(hanger.Velocity.X, 0);
                        SnapOntoHangable(hanger, hangable);
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Corrects <paramref name="hanger"/>'s position so its own overall topmost collision edge
    /// (see <see cref="WouldSnapFromBelow"/>) lands exactly on <paramref name="hangable"/>'s
    /// bottom edge - i.e. hanging just underneath the surface, not overlapping into/through it -
    /// mirroring how <see cref="ResolveRectAgainstSolid"/> snaps a body exactly onto a solid's
    /// surface rather than merely detecting it is "close enough". Only the vertical axis is
    /// corrected - hanging/shimmying is a deliberate lateral action (see
    /// <see cref="Physics.PhysicsSystem"/>'s hang movement), so horizontal position is left alone.
    /// </summary>
    private static void SnapOntoHangable(IHangerBody hanger, Body2D hangable)
    {
        if (!TryFindDeepestOverlap(hanger.CollisionRects, hangable.CollisionRects, out _, out var otherRect))
        {
            return;
        }

        var bodyTop = hanger.CollisionRects.Min(rect => rect.Top);
        var topOffset = bodyTop - hanger.Position.Y;
        hanger.Position = new Vector2D(hanger.Position.X, otherRect.Bottom - topOffset - HangOverlapEpsilon);
    }

    /// <summary>
    /// Whether <paramref name="body"/> is moving slowly enough to snap onto a climbable/hangable
    /// surface on first touch, rather than blowing straight through it - see
    /// <see cref="MaxSnapSpeed"/>. Checked against the body's overall speed (not just one axis),
    /// since either a fast horizontal dash into a ladder or a fast vertical fall past a pipe
    /// should equally fail to snap.
    /// </summary>
    private static bool IsWithinSnapSpeed(Vector2D velocity) =>
        Math.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y) <= MaxSnapSpeed;

    /// <summary>
    /// Combines two materials' restitution/friction values for a single collision response, per
    /// docs/Decisions.md: a plain average of both sides. Chosen over e.g. <c>Math.Min</c> because
    /// it lets either side meaningfully pull the result toward its own value (a rubber ball on
    /// concrete bounces less than rubber-on-rubber but more than concrete-on-concrete), rather
    /// than one material always dominating outright regardless of the other.
    /// </summary>
    private static double Combine(double a, double b) => (a + b) / 2.0;

    /// <summary>
    /// Any <see cref="IPhysicsBody"/> overlapping any <see cref="IHazardBody"/> is a hazard hit,
    /// checked generically without regard to concrete type on either side. Collectable pickup is
    /// narrower: only bodies implementing <see cref="ICollectorBody"/> (e.g. any player, including
    /// a second player in multiplayer) can pick up an <see cref="ICollectableBody"/> - this is the
    /// one overlap check in this class that is restricted to a capability interface on the moving
    /// side too, rather than "any" moving body, so non-player bodies (the bouncing ball, enemies)
    /// never consume collectables. Killing a hazard is likewise restricted to bodies implementing
    /// <see cref="IKillerBody"/> (e.g. the player) - a non-player moving body (the bouncing
    /// ball, another enemy) can still register an ordinary hazard contact/effect below, but never
    /// triggers the kill/removal path.
    /// </summary>
    private void ResolveHazardsAndCollectables(World2D world)
    {
        var hazards = world.Objects.OfType<IHazardBody>().Cast<Body2D>().ToList();
        var collectables = world.Objects.OfType<ICollectableBody>().Cast<Body2D>().ToList();

        if (hazards.Count == 0 && collectables.Count == 0)
        {
            _activeHazardContacts.Clear();
            return;
        }

        var currentHazardContacts = new HashSet<(Body2D Hazard, IPhysicsBody Body)>();

        // Snapshot before iterating: SpawnEffectIfConfigured below can add a new
        // EffectInstance2D to world.Objects (a kill/hit effect), and hazards can be queued for
        // removal mid-loop. Enumerating world.Objects directly while it is mutated during the
        // same enumeration throws InvalidOperationException (EnumFailedVersion).
        foreach (var body in world.Objects.ToList())
        {
            if (body is not IPhysicsBody movingBody)
            {
                continue;
            }

            foreach (var hazard in hazards)
            {
                if (ReferenceEquals(body, hazard) || !Overlaps(movingBody, hazard))
                {
                    continue;
                }

                // A killable hazard contacted from the top ("landed on") is a kill, not an
                // ordinary hazard hit: it bypasses the damage TODO entirely, is queued for
                // removal, and its own effect (if configured) persists as a permanent husk when
                // EffectPersists is set. Any other contact direction (side/underneath), or a
                // non-killable hazard, falls through to the existing, unmodified hazard-contact
                // behavior below. The hazard's own effect and the moving body's own effect are
                // deliberately mutually exclusive with kill-vs-ordinary-hit (rather than both
                // firing on every contact): a hazard's EffectClipName is reserved for its kill
                // reaction, while a moving body's EffectClipName (e.g. a player's hit spark) is
                // reserved for ordinary, non-fatal hazard contact.
                if (movingBody is IKillerBody && hazard is IKillableBody { IsKillable: true } killable && IsApproachingFromTop(movingBody, hazard))
                {
                    SpawnEffectIfConfigured(hazard, world, killable.EffectPersists);
                    world.QueueRemoval(hazard);
                    continue;
                }

                // TODO: apply damage once a health/damage system exists. Detection is generic
                // (any IPhysicsBody overlapping any IHazardBody); only the effect is not wired yet.
                var contact = (hazard, movingBody);
                currentHazardContacts.Add(contact);
                if (!_activeHazardContacts.Contains(contact))
                {
                    SpawnEffectIfConfigured(body, world);
                }
            }

            foreach (var collectable in collectables)
            {
                if (movingBody is ICollectorBody && Overlaps(movingBody, collectable))
                {
                    // Only the collectable's own effect fires here (e.g. a ring's pickup fade) -
                    // the collector side is deliberately left silent so a collector's own
                    // EffectClipName (e.g. a player's hazard-hit spark, reserved for hazard
                    // contact) never fires on an unrelated pickup.
                    SpawnEffectIfConfigured(collectable, world);
                    world.QueueRemoval(collectable);
                }
            }
        }

        _activeHazardContacts = currentHazardContacts;
    }

    /// <summary>
    /// Spawns a cosmetic <see cref="EffectInstance2D"/> at <paramref name="body"/>'s current
    /// position if it implements <see cref="IEffectTrigger"/> with a non-null clip name
    /// configured; no-ops otherwise. <paramref name="persists"/> controls whether the spawned
    /// effect remains as a permanent decorative body after its clip finishes playing (used for a
    /// killed enemy's husk) instead of self-removing like an ordinary effect.
    /// </summary>
    private static void SpawnEffectIfConfigured(Body2D body, World2D world, bool persists = false)
    {
        if (body is IEffectTrigger { EffectClipName: { } clipName })
        {
            var effect = new EffectInstance2D();
            effect.Spawn(body.Sprite, clipName, body.Position, persists);
            world.Objects.Add(effect);
        }
    }

    private static bool Overlaps(IPhysicsBody a, Body2D b)
    {
        foreach (var rectA in a.CollisionRects)
        {
            foreach (var rectB in b.CollisionRects)
            {
                if (rectA.Overlaps(rectB))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Keeps a body's bounding box within the world's cell grid, reflecting velocity on
    /// whichever axis it would otherwise cross an edge. The player never actually has a
    /// velocity component fed back in (restitution 0 just stops it dead at the edge), while
    /// dynamic objects like the bouncing ball use their own restitution to bounce off the walls,
    /// floor and ceiling of the level even where there's no platform there. The world's floor is
    /// treated the same as a platform's top surface for grounding purposes, so any body resting
    /// against it (not just the player) is considered grounded - no special-casing by type.
    /// </summary>
    private static void ResolveWorldBounds(World2D world, IPhysicsBody body)
    {
        var restitution = body.Restitution;
        var position = body.Position;
        var velocity = body.Velocity;
        var width = body.Size.X;
        var height = body.Size.Y;

        var minX = 0.0;
        var maxX = world.WidthCells - width;
        var minY = 0.0;
        var maxY = world.HeightCells - height;

        if (position.X < minX)
        {
            position.X = minX;
            velocity.X = -velocity.X * restitution;
        }
        else if (position.X > maxX)
        {
            position.X = maxX;
            velocity.X = -velocity.X * restitution;
        }

        if (position.Y < minY)
        {
            position.Y = minY;
            velocity.Y = -velocity.Y * restitution;
        }
        else if (position.Y > maxY)
        {
            position.Y = maxY;
            velocity.Y = -velocity.Y * restitution;
            // The world's own floor isn't a placed Body2D, so a shared sentinel instance stands
            // in as the "other" body for this SurfaceBottom contact - see WorldBoundsSentinel.
            body.AddContact(ContactType.SurfaceBottom, WorldBoundsSentinel);
        }

        body.Position = position;
        body.Velocity = velocity;
    }

    /// <summary>
    /// Re-seats <paramref name="body"/> flush onto <paramref name="solid"/>'s current top surface
    /// if it still overlaps that solid horizontally - the permanent fix for the discrete-collision
    /// gap that otherwise briefly drops a resting body when the solid it's standing on displaces
    /// downward farther in one frame than the body's own (velocity-matched, but not yet
    /// integrated) fall keeps up with. Called once per moving body, immediately after
    /// <see cref="_groundedSolids"/> is consulted and before this frame's ordinary
    /// overlap-based landing check runs (see the top of <see cref="Resolve"/>), using whichever
    /// solid the body was <em>already</em> confirmed grounded on as of last frame - so this never
    /// invents a new landing, it only maintains one that already existed.
    /// </summary>
    /// <remarks>
    /// This is a direct, absolute correction of <see cref="Body2D.Position"/>.Y - snapping the
    /// body's rect flush onto the solid's rect - rather than an additive delta computed from the
    /// solid's velocity (contrast the player-only horizontal hack above, which had to use a delta
    /// because it needed to preserve whatever horizontal offset the body had built up while
    /// walking on the platform). Because this instead *sets* Position.Y outright to a value
    /// derived purely from the solid's *current* rect - not from wherever the body happened to be
    /// a moment ago - it cannot double-count or fight the following ordinary collision pass the
    /// way an additive vertical shift did (see docs/Decisions.md): the ordinary landing check
    /// below will find the same overlap this produces and simply reconfirm it, a no-op. It is
    /// also universal (not player-only) since, unlike horizontal, there's no player-input-
    /// overwrite problem on the vertical axis - gravity affects every body the same way.
    /// Deliberately checks the body's own <see cref="Body2D.CollisionRects"/> (there can be more
    /// than one, e.g. the player's head+torso) against the solid's, picking the *lowest*
    /// (largest-Bottom) horizontally-overlapping body rect rather than simply the first one found:
    /// <see cref="World.CollisionShapeBuilder.DeriveRectangles"/> orders a multi-rect body's rects
    /// top-to-bottom (head before torso/legs), so naively using the first match would re-seat the
    /// body by its head rect - snapping the whole body upward and visibly "hanging" it above the
    /// surface instead of standing on it. Using the lowest rect mirrors which part of the body
    /// would actually be resting on the surface, without needing a precise deepest-penetration
    /// correction (that is <see cref="ResolveRectAgainstSolid"/>'s job, immediately afterward in
    /// the same frame). If the body has drifted entirely clear of the solid horizontally (walked
    /// off the edge), no rect pair overlaps and nothing happens here - gravity and the ordinary
    /// collision pass take back over exactly as they already do for stepping off a stationary
    /// platform's edge.
    /// </remarks>
    private static void MaintainVerticalGroundedContact(IPhysicsBody body, Body2D solid)
    {
        var solidRects = solid.CollisionRects;
        var bodyRects = body.CollisionRects;

        // Track the lowest (feet-side) body rect that overlaps the solid horizontally, along with
        // the solid rect it overlaps - a multi-rect body (e.g. the player's head+torso shape) must
        // be re-seated by whichever of its own rects actually rests on the surface (its feet/
        // torso), not simply the first rect CollisionShapeBuilder.DeriveRectangles happened to
        // produce (which is ordered top-to-bottom, i.e. head first) - snapping by the head rect
        // instead pulls the whole body upward by the head/torso height difference, visibly
        // "hanging" the body from its head above the surface instead of standing on it.
        var haveMatch = false;
        var bestBodyRect = default(Rect2D);
        var bestSolidRect = default(Rect2D);

        for (var bodyRectIndex = 0; bodyRectIndex < bodyRects.Count; bodyRectIndex++)
        {
            var bodyRect = bodyRects[bodyRectIndex];

            for (var solidRectIndex = 0; solidRectIndex < solidRects.Count; solidRectIndex++)
            {
                var solidRect = solidRects[solidRectIndex];

                // Horizontal overlap only - vertical overlap is irrelevant here (that's exactly
                // the gap this exists to paper over) and would otherwise wrongly gate on the very
                // condition this needs to work despite failing.
                if (bodyRect.Left >= solidRect.Right || bodyRect.Right <= solidRect.Left)
                {
                    continue;
                }

                if (!haveMatch || bodyRect.Bottom > bestBodyRect.Bottom)
                {
                    haveMatch = true;
                    bestBodyRect = bodyRect;
                    bestSolidRect = solidRect;
                }
            }
        }

        if (!haveMatch)
        {
            return;
        }

        // The collision rect may be offset from the body's own Position (e.g. it excludes
        // blank sprite cells), so the correction is expressed in terms of that offset,
        // same as ResolveRectAgainstSolid.
        var rectOffsetY = bestBodyRect.Y - body.Position.Y;

        // Snapped very slightly *past* flush (bodyRect.Bottom a hair below solidRect.Top,
        // by HangOverlapEpsilon) rather than exactly flush: Rect2D.Overlaps uses strict
        // inequalities, so a perfectly flush position (bodyRect.Bottom == solidRect.Top)
        // produces zero actual overlap. Without this, the very next check this frame -
        // the ordinary overlap-based landing check just below, which is what's supposed to
        // reconfirm/refresh _groundedSolids for the next frame - would find no overlap at
        // all, clear the body's grounded-solid memory, and reopen exactly the fall-through
        // gap this method exists to close, this time with nothing left to catch it at all.
        // Same fix, same reasoning as SnapOntoHangable's use of this constant.
        body.Position = new Vector2D(
            body.Position.X,
            bestSolidRect.Top - bestBodyRect.Height - rectOffsetY + HangOverlapEpsilon);
    }

    /// <summary>
    /// Resolves <paramref name="body"/> against <paramref name="solid"/>, one body collision
    /// rectangle at a time (see <see cref="ResolveRectAgainstSolid"/> for why). The velocity
    /// response uses <paramref name="body"/> and <paramref name="solid"/>'s combined restitution
    /// (see <see cref="Combine"/>) - 0 stops the body dead (e.g. the player's rubber-free flesh
    /// against most terrain), while anything above 0 reflects velocity to varying degrees of
    /// bounce - and applies a friction damping to the tangential velocity component from their
    /// combined friction. Despite the name, <paramref name="solid"/> is any immovable body from
    /// the <c>solids</c> list, not specifically a platform - a wall, crate, or any other static
    /// terrain resolves the same way. All of the bounce/friction math is done in <paramref name="solid"/>'s
    /// own velocity reference frame (see <see cref="ResolveRectAgainstSolid"/>) rather than
    /// assuming it is stationary, so a moving platform (e.g. <see cref="KinematicObject2D"/>)
    /// naturally carries/drags a resting or colliding body along via the same formulas used
    /// against ordinary stationary terrain - for stationary terrain (the overwhelming common
    /// case) the reference frame's own velocity is zero, so this reduces to exactly the same
    /// result as before.
    /// </summary>
    /// <returns>
    /// True if any of the body's rects landed on top of this solid (i.e. this solid should be
    /// remembered as what the body is now grounded on for next frame's carry - see
    /// <see cref="_groundedSolids"/>), false otherwise.
    /// </returns>
    private bool ResolveAgainstSolid(IPhysicsBody body, Body2D solid)
    {
        // A body's collision shape can be made up of several rectangles that don't all have the
        // same width/offset (e.g. the player's narrower "head" rect above its wider "torso"
        // rect - see CollisionShapeBuilder). Picking a single globally-deepest-penetrating pair
        // across every combination of the body's rects and the solid's rects is wrong: a
        // shallow/differently-axised overlap on one rect (say the head clipping a solid's top
        // corner) can "win" over a more significant overlap on another rect (the torso still
        // embedded in the solid's side), so only the head gets pushed out and the torso stays
        // stuck inside the solid. Resolving each of the body's own rects against the solid
        // independently - one at a time - ensures every part of the body's shape ends up outside
        // the solid, not just whichever part happened to look "deepest". CollisionRects is
        // re-fetched every iteration (rather than enumerating one snapshot list) because it is
        // computed fresh from the body's *current* Position - resolving rect 0 can move the
        // body, and rect 1 must then be checked/pushed out from that already-corrected position,
        // not the stale pre-frame one, otherwise the two rects' corrections fight each other and
        // the body jitters between resolved positions frame to frame.
        var restitution = Combine(body.Restitution, solid.Restitution);
        var friction = Combine(body.Friction, solid.Friction);
        // A solid that is itself an IPhysicsBody (a kinematic platform) has a real Velocity to
        // treat as the collision's reference frame; ordinary static terrain implements no
        // interface here and is implicitly stationary (Vector2D.Zero).
        var solidVelocity = solid is IPhysicsBody solidBody ? solidBody.Velocity : default;
        var rectCount = body.CollisionRects.Count;
        var landedOnTop = false;
        for (var rectIndex = 0; rectIndex < rectCount; rectIndex++)
        {
            if (ResolveRectAgainstSolid(body, restitution, friction, solidVelocity, solid, body.CollisionRects[rectIndex]))
            {
                landedOnTop = true;
            }
        }

        return landedOnTop;
    }

    /// <summary>
    /// Resolves a single one of the body's own collision rectangles against whichever of the
    /// solid's rectangles it overlaps most deeply, then pushes the body out along the
    /// shallower-penetration axis, same as the original single-rect logic. Called once per body
    /// rect by <see cref="ResolveAgainstSolid"/> so a multi-rect body (e.g. the player's
    /// head+torso shape) gets every part of itself pushed fully clear of the solid.
    /// </summary>
    /// <remarks>
    /// All velocity response here is computed on <paramref name="body"/>'s velocity <em>relative
    /// to</em> <paramref name="solidVelocity"/> - a simple Galilean frame shift - then converted
    /// back to an absolute velocity by adding <paramref name="solidVelocity"/> back at the end.
    /// For stationary terrain (<paramref name="solidVelocity"/> is zero) this is identical to
    /// operating on the body's own velocity directly, exactly as before this generalization. For
    /// a moving platform it means: the normal-direction bounce reflects the body's speed of
    /// approach *toward* the platform, not its raw absolute speed (so standing on a platform
    /// moving away underneath doesn't spuriously look like an impact); and the tangential
    /// friction damping pulls the body's velocity toward matching the platform's own velocity
    /// (not toward zero) - a grippy platform material genuinely drags a resting rider along with
    /// it, a slick one lets the rider slip relative to it, using the exact same
    /// <see cref="ApplyFriction"/> formula already used against stationary terrain.
    /// </remarks>
    /// <returns>True if this rect landed on top of the solid, false otherwise (including no overlap at all).</returns>
    private bool ResolveRectAgainstSolid(IPhysicsBody body, double restitution, double friction, Vector2D solidVelocity, Body2D solid, Rect2D bodyRect)
    {
        if (!TryFindDeepestOverlap([bodyRect], solid.CollisionRects, out var deepestBodyRect, out var bestSolidRect, (Body2D)body, solid, NarrowPhaseMode))
        {
            return false;
        }

        var overlapLeftBest = deepestBodyRect.Right - bestSolidRect.Left;
        var overlapRightBest = bestSolidRect.Right - deepestBodyRect.Left;
        var overlapTopBest = deepestBodyRect.Bottom - bestSolidRect.Top;
        var overlapBottomBest = bestSolidRect.Bottom - deepestBodyRect.Top;

        var minHorizontal = Math.Min(overlapLeftBest, overlapRightBest);
        var minVertical = Math.Min(overlapTopBest, overlapBottomBest);

        // The collision rect may be offset from the body's Position (e.g. it excludes blank
        // sprite cells), so push-outs are expressed in terms of that offset rather than Position
        // directly.
        var rectOffsetX = deepestBodyRect.X - body.Position.X;
        var rectOffsetY = deepestBodyRect.Y - body.Position.Y;

        // Body velocity relative to the solid's own reference frame - see the remarks above.
        var relativeVelocity = new Vector2D(body.Velocity.X - solidVelocity.X, body.Velocity.Y - solidVelocity.Y);

        // A one-way platform only ever blocks a body landing on its top surface while falling
        // (or resting) onto it - jumping up through it from below, or approaching from either
        // side, passes straight through untouched. Landing-from-above vs. approaching-from-below
        // is told apart using the relative vertical velocity rather than position history: falling
        // (relativeVelocity.Y >= 0, moving down in this reference frame) is a legitimate landing,
        // while moving upward through the platform is not - the standard one-way-platform test.
        if (solid.IsOneWayPlatform && (minVertical >= minHorizontal || overlapTopBest >= overlapBottomBest || relativeVelocity.Y < 0))
        {
            return false;
        }

        if (minVertical < minHorizontal)
        {
            if (overlapTopBest < overlapBottomBest)
            {
                // Landing on top of the solid. Friction damps the tangential (horizontal)
                // relative velocity component every frame the body rests here - see
                // ApplyFriction - so a grippy surface (e.g. Concrete) settles sliding motion
                // faster than a slick one, and (for a moving platform) drags the rider along
                // toward matching the platform's own horizontal velocity instead of toward zero.
                var newRectBottom = bestSolidRect.Top;
                body.Position = new Vector2D(
                    body.Position.X,
                    newRectBottom - deepestBodyRect.Height - rectOffsetY);
                body.Velocity = new Vector2D(
                    solidVelocity.X + ApplyFriction(relativeVelocity.X, friction),
                    solidVelocity.Y + -relativeVelocity.Y * restitution);
                // Every IPhysicsBody implementation in this codebase is a Body2D (see
                // Body2D.AddContact); recording the reciprocal SurfaceTop contact on the solid
                // lets it (and anything reading it) know something rests on top of it this frame.
                body.AddContact(ContactType.SurfaceBottom, solid);
                solid.AddContact(ContactType.SurfaceTop, (Body2D)body);
                return true;
            }

            {
                // Hitting the underside of the solid.
                body.Position = new Vector2D(
                    body.Position.X,
                    bestSolidRect.Bottom - rectOffsetY);
                body.Velocity = new Vector2D(body.Velocity.X, solidVelocity.Y + -relativeVelocity.Y * restitution);
                return false;
            }
        }
        else
        {
            if (overlapLeftBest < overlapRightBest)
            {
                // Colliding with the solid's left edge.
                body.Position = new Vector2D(
                    bestSolidRect.Left - deepestBodyRect.Width - rectOffsetX,
                    body.Position.Y);
            }
            else
            {
                // Colliding with the solid's right edge.
                body.Position = new Vector2D(
                    bestSolidRect.Right - rectOffsetX,
                    body.Position.Y);
            }
            body.Velocity = new Vector2D(
                solidVelocity.X + -relativeVelocity.X * restitution,
                solidVelocity.Y + ApplyFriction(relativeVelocity.Y, friction));
            return false;
        }
    }

    /// <summary>
    /// Damps a tangential (along-surface) velocity component by the combined friction of the two
    /// contacting materials - 0 leaves it untouched (frictionless), 1 stops it dead instantly.
    /// Applied once per contacting frame rather than as a continuous force, which is a
    /// deliberately simple per-docs/Decisions.md approximation: real friction depends on normal
    /// force and time, but a flat per-frame damping factor is enough to make grippy materials
    /// (e.g. Concrete) visibly settle sliding motion faster than a slick one (e.g. Ice) without
    /// needing a full friction-force integration model.
    /// </summary>
    private static double ApplyFriction(double velocityComponent, double friction) =>
        velocityComponent * (1.0 - Math.Clamp(friction, 0.0, 1.0));


    /// <summary>
    /// Finds the deepest-overlapping rectangle pair between two bodies' (possibly multi-rect)
    /// collision shapes. Shared by <see cref="ResolveAgainstSolid"/>, <see cref="ResolveBodyPair"/>,
    /// and <see cref="IsApproachingFromTop"/> so all three use the exact same
    /// deepest-penetration-axis math instead of maintaining separate copies of it. Returns false
    /// (with both rects left default) if no rectangle pair overlaps at all.
    /// </summary>
    /// <param name="ownerA">
    /// The body <paramref name="aRects"/> belongs to, or null. Only needed when <paramref name="mode"/>
    /// is <see cref="NarrowPhaseMode.CharacterGrid"/> - see <see cref="HasCharacterOverlap"/>.
    /// Callers that never pass <see cref="NarrowPhaseMode.CharacterGrid"/> may omit both owners.
    /// </param>
    /// <param name="ownerB">See <paramref name="ownerA"/>, for <paramref name="bRects"/>.</param>
    /// <param name="mode">
    /// Which narrow-phase test accepts a rectangle-pair overlap - see <see cref="NarrowPhaseMode"/>.
    /// Defaults to <see cref="NarrowPhaseMode.MultiRect"/> (rectangle overlap alone is sufficient)
    /// for callers (e.g. <see cref="SnapOntoHangable"/>) that don't care about the distinction.
    /// </param>
    private static bool TryFindDeepestOverlap(
        IReadOnlyList<Rect2D> aRects,
        IReadOnlyList<Rect2D> bRects,
        out Rect2D bestA,
        out Rect2D bestB,
        Body2D? ownerA = null,
        Body2D? ownerB = null,
        NarrowPhaseMode mode = NarrowPhaseMode.MultiRect)
    {
        bestA = default;
        bestB = default;
        var bestPenetration = double.MaxValue;
        var found = false;

        foreach (var rectA in aRects)
        {
            foreach (var rectB in bRects)
            {
                if (!rectA.Overlaps(rectB))
                {
                    continue;
                }

                // Character-grid mode additionally requires the two bodies' actual rendered
                // (non-empty) characters to overlap somewhere within this rectangle pair's
                // intersection, not just the merged rectangles themselves - see HasCharacterOverlap.
                if (mode == NarrowPhaseMode.CharacterGrid
                    && ownerA is not null && ownerB is not null
                    && !HasCharacterOverlap(ownerA, ownerB, rectA, rectB))
                {
                    continue;
                }

                var overlapLeft = rectA.Right - rectB.Left;
                var overlapRight = rectB.Right - rectA.Left;
                var overlapTop = rectA.Bottom - rectB.Top;
                var overlapBottom = rectB.Bottom - rectA.Top;

                var penetration = Math.Min(Math.Min(overlapLeft, overlapRight), Math.Min(overlapTop, overlapBottom));
                if (penetration < bestPenetration)
                {
                    bestPenetration = penetration;
                    bestA = rectA;
                    bestB = rectB;
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Ported (in spirit) from the older ConsoleGame2D prototype's <c>CheckCharacterCollision</c>:
    /// confirms two bodies' rectangle overlap actually corresponds to their true rendered
    /// silhouettes touching, by testing whether at least one world cell within the overlap region
    /// has a non-empty character on both sides, rather than trusting the merged collision
    /// rectangles alone. <see cref="CollisionShapeBuilder"/> already reduces each body's non-empty
    /// cells to a small set of rectangles, so this refinement only matters for shapes whose true
    /// silhouette doesn't exactly fill that merged rectangle (e.g. a diagonal or notched sprite) -
    /// for a solid rectangular sprite, every cell within its own collision rect is already known
    /// non-empty and this always agrees with the plain rectangle test.
    /// </summary>
    private static bool HasCharacterOverlap(Body2D ownerA, Body2D ownerB, Rect2D rectA, Rect2D rectB)
    {
        var left = Math.Max(rectA.Left, rectB.Left);
        var right = Math.Min(rectA.Right, rectB.Right);
        var top = Math.Max(rectA.Top, rectB.Top);
        var bottom = Math.Min(rectA.Bottom, rectB.Bottom);

        var gridLeft = (int)Math.Floor(left);
        var gridRight = (int)Math.Ceiling(right);
        var gridTop = (int)Math.Floor(top);
        var gridBottom = (int)Math.Ceiling(bottom);

        var aChars = ownerA.Frame.Chars;
        var bChars = ownerB.Frame.Chars;
        var aEmpty = ownerA.Sprite.EmptyChar;
        var bEmpty = ownerB.Sprite.EmptyChar;
        var aHeight = aChars.GetLength(0);
        var aWidth = aChars.GetLength(1);
        var bHeight = bChars.GetLength(0);
        var bWidth = bChars.GetLength(1);

        for (var gridY = gridTop; gridY < gridBottom; gridY++)
        {
            for (var gridX = gridLeft; gridX < gridRight; gridX++)
            {
                var localAX = gridX - (int)Math.Floor(ownerA.Position.X);
                var localAY = gridY - (int)Math.Floor(ownerA.Position.Y);
                var localBX = gridX - (int)Math.Floor(ownerB.Position.X);
                var localBY = gridY - (int)Math.Floor(ownerB.Position.Y);

                if (localAX < 0 || localAX >= aWidth || localAY < 0 || localAY >= aHeight ||
                    localBX < 0 || localBX >= bWidth || localBY < 0 || localBY >= bHeight)
                {
                    continue;
                }

                if (aChars[localAY, localAX] != aEmpty && bChars[localBY, localBX] != bEmpty)
                {
                    return true;
                }
            }
        }

        return false;
    }


    /// <summary>
    /// Whether <paramref name="body"/>'s deepest overlap with <paramref name="other"/> is on the
    /// vertical axis with <paramref name="body"/> approaching from above - i.e. "landed on top" -
    /// using the same deepest-penetration-axis approach as <see cref="ResolveAgainstSolid"/>,
    /// applied here to a killable hazard's overlap instead of solid terrain. Any other direction
    /// (side/underneath), or no overlap at all, returns false.
    /// </summary>
    private static bool IsApproachingFromTop(IPhysicsBody body, Body2D other)
    {
        if (!TryFindDeepestOverlap(body.CollisionRects, other.CollisionRects, out var bodyRect, out var otherRect))
        {
            return false;
        }

        var overlapLeft = bodyRect.Right - otherRect.Left;
        var overlapRight = otherRect.Right - bodyRect.Left;
        var overlapTop = bodyRect.Bottom - otherRect.Top;
        var overlapBottom = otherRect.Bottom - bodyRect.Top;

        var minHorizontal = Math.Min(overlapLeft, overlapRight);
        var minVertical = Math.Min(overlapTop, overlapBottom);

        return minVertical < minHorizontal && overlapTop < overlapBottom;
    }

    /// <summary>
    /// Whether <paramref name="body"/> is underneath <paramref name="other"/> - i.e. no part of
    /// <paramref name="body"/>'s own collision shape extends above <paramref name="other"/>'s top
    /// edge - the geometric check for "reaching up into a hangable surface from below", and
    /// (symmetrically) for "having just fallen far enough through it from above". Deliberately
    /// compares the body's own overall topmost edge (the minimum <see cref="Rect2D.Top"/> across
    /// *all* of its collision rects) rather than reusing <see cref="TryFindDeepestOverlap"/>'s
    /// per-pair deepest-penetration pick: for a multi-rect body (e.g. the player, whose feet sit
    /// well below its head) the deepest-overlapping pair while falling through a thin pipe is
    /// initially a lower rect (a leg/foot), whose own top edge sits nowhere near the body's actual
    /// topmost row - checking that sub-rect alone let the body snap while still mid-body or
    /// feet-level under the surface instead of only once its true top row has cleared it. Using
    /// the whole body's overall top edge instead makes falling-through-from-above and
    /// climbing/jumping-up-from-below resolve to the exact same geometric moment: the body's top
    /// row is (just) below the surface's own top edge. Combined with the caller's
    /// <see cref="IsWithinSnapSpeed"/> gate, this also rejects a body moving too fast to grab on
    /// even though it is geometrically underneath (e.g. a jump arc's peak sweeping past on the
    /// way up).
    /// </summary>
    private static bool WouldSnapFromBelow(IPhysicsBody body, Body2D other)
    {
        if (!TryFindDeepestOverlap(body.CollisionRects, other.CollisionRects, out _, out var otherRect))
        {
            return false;
        }

        var bodyTop = body.CollisionRects.Min(rect => rect.Top);
        return bodyTop >= otherRect.Top;
    }

    /// <summary>
    /// Resolves a collision between two moving bodies (e.g. the player and the bouncing ball) -
    /// the one pairing that previously fell through the cracks entirely, since each body was
    /// only ever checked against solids/world bounds, never against each other. Uses the same
    /// deepest-penetration approach as <see cref="ResolveAgainstSolid"/>, but since neither side
    /// here is immovable, the position correction is split by relative mass (a heavier body
    /// yields less ground than a lighter one - see <see cref="Body2D.Mass"/>) and the along-normal
    /// velocity response is a proper mass-weighted impulse (see <see cref="ResolveNormalImpulse"/>)
    /// using the pair's combined restitution (see <see cref="Combine"/>), rather than each body
    /// independently reflecting its own velocity. The tangential velocity component gets the same
    /// friction damping used against solids.
    /// </summary>
    private void ResolveBodyPair(IPhysicsBody a, IPhysicsBody b)
    {
        if (!TryFindDeepestOverlap(a.CollisionRects, b.CollisionRects, out var deepestRectA, out var bestRectB, (Body2D)a, (Body2D)b, NarrowPhaseMode))
        {
            return;
        }

        var overlapLeftBest = deepestRectA.Right - bestRectB.Left;
        var overlapRightBest = bestRectB.Right - deepestRectA.Left;
        var overlapTopBest = deepestRectA.Bottom - bestRectB.Top;
        var overlapBottomBest = bestRectB.Bottom - deepestRectA.Top;

        var minHorizontal = Math.Min(overlapLeftBest, overlapRightBest);
        var minVertical = Math.Min(overlapTopBest, overlapBottomBest);

        // Split the position correction by relative mass rather than always 50/50: the heavier
        // body moves less, the lighter one moves more. Falls back to an even split if both masses
        // are zero (e.g. neither body resolved a material) so a degenerate 0/0 divide never
        // happens.
        var totalMass = a.Mass + b.Mass;
        var aShare = totalMass > 0 ? b.Mass / totalMass : 0.5;
        var bShare = totalMass > 0 ? a.Mass / totalMass : 0.5;

        var restitution = Combine(a.Restitution, b.Restitution);
        var friction = Combine(a.Friction, b.Friction);

        if (minVertical < minHorizontal)
        {
            if (overlapTopBest < overlapBottomBest)
            {
                // A's bottom rests on B's top - push A up and B down, split by relative mass.
                a.Position = new Vector2D(a.Position.X, a.Position.Y - minVertical * aShare);
                b.Position = new Vector2D(b.Position.X, b.Position.Y + minVertical * bShare);
                a.AddContact(ContactType.SurfaceBottom, (Body2D)b);
                b.AddContact(ContactType.SurfaceTop, (Body2D)a);
            }
            else
            {
                // B's bottom rests on A's top.
                b.Position = new Vector2D(b.Position.X, b.Position.Y - minVertical * bShare);
                a.Position = new Vector2D(a.Position.X, a.Position.Y + minVertical * aShare);
                b.AddContact(ContactType.SurfaceBottom, (Body2D)a);
                a.AddContact(ContactType.SurfaceTop, (Body2D)b);
            }

            var (newAY, newBY) = ResolveNormalImpulse(a.Velocity.Y, b.Velocity.Y, a.Mass, b.Mass, restitution);
            a.Velocity = new Vector2D(ApplyFriction(a.Velocity.X, friction), newAY);
            b.Velocity = new Vector2D(ApplyFriction(b.Velocity.X, friction), newBY);
        }
        else
        {
            if (overlapLeftBest < overlapRightBest)
            {
                // A's right edge overlaps B's left edge - push them apart horizontally, split by relative mass.
                a.Position = new Vector2D(a.Position.X - minHorizontal * aShare, a.Position.Y);
                b.Position = new Vector2D(b.Position.X + minHorizontal * bShare, b.Position.Y);
            }
            else
            {
                b.Position = new Vector2D(b.Position.X - minHorizontal * bShare, b.Position.Y);
                a.Position = new Vector2D(a.Position.X + minHorizontal * aShare, a.Position.Y);
            }

            var (newAX, newBX) = ResolveNormalImpulse(a.Velocity.X, b.Velocity.X, a.Mass, b.Mass, restitution);
            a.Velocity = new Vector2D(newAX, ApplyFriction(a.Velocity.Y, friction));
            b.Velocity = new Vector2D(newBX, ApplyFriction(b.Velocity.Y, friction));
        }
    }

    /// <summary>
    /// Standard 1D mass-weighted impulse resolution along a single collision-normal axis, for two
    /// finite-mass bodies (used only by <see cref="ResolveBodyPair"/> - a static <see
    /// cref="Body2D.IsStatic"/> solid is never one of these two bodies, it's handled by <see
    /// cref="ResolveAgainstSolid"/> instead, so "infinite mass" is not a case this needs to
    /// handle): <c>j = -(1+e) * relativeVelocity / (1/mA + 1/mB)</c>, then <c>vA' = vA + j/mA</c>,
    /// <c>vB' = vB - j/mB</c>. A body with no resolved material (<see cref="Body2D.Mass"/> of 0,
    /// e.g. <see cref="MaterialLibrary.Undefined"/>) is treated as mass 1 here purely to keep the
    /// impulse formula well-defined - it does not affect that body's own position-correction share
    /// above, which already falls back to an even split independently.
    /// </summary>
    private static (double NewA, double NewB) ResolveNormalImpulse(double velocityA, double velocityB, double massA, double massB, double restitution)
    {
        var effectiveMassA = massA > 0 ? massA : 1.0;
        var effectiveMassB = massB > 0 ? massB : 1.0;
        var relativeVelocity = velocityA - velocityB;
        var impulse = -(1.0 + restitution) * relativeVelocity / (1.0 / effectiveMassA + 1.0 / effectiveMassB);
        var newA = velocityA + impulse / effectiveMassA;
        var newB = velocityB - impulse / effectiveMassB;
        return (newA, newB);
    }
}


