using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>
/// Resolves axis-aligned bounding box collisions between moving bodies, static solids, the
/// world's own bounds, and hazards/collectables. Collision is resolved generically against
/// <see cref="IPhysicsBody"/>/<see cref="World2D.Objects"/>, with no special-casing by concrete
/// type.
/// </summary>
public class CollisionSystem
{
    /// <summary>Reused across frames to avoid an allocation every call for what is normally a tiny list.</summary>
    private readonly List<IPhysicsBody> _movingBodies = [];

    /// <summary>Broad-phase spatial grid over this frame's static, non-passable solids - see <see cref="SpatialGrid{T}"/>.</summary>
    private readonly SpatialGrid<Body2D> _solidsGrid = new(PhysicsConstants.GridCellSize);

    /// <summary>Broad-phase spatial grid over this frame's moving bodies - see <see cref="SpatialGrid{T}"/>.</summary>
    private readonly SpatialGrid<IPhysicsBody> _movingBodiesGrid = new(PhysicsConstants.GridCellSize);

    /// <summary>
    /// A placeholder <see cref="Body2D"/> representing the world's own floor/walls/ceiling for
    /// contact-recording purposes only (see <see cref="ResolveWorldBounds"/>) - never rendered,
    /// never collided against directly, never placed in <see cref="World2D.Objects"/>. Lets a body
    /// resting against the world's edge record an ordinary <see cref="ContactType.SurfaceBottom"/>
    /// contact, and therefore derive <see cref="IPhysicsBody.IsGrounded"/>, the same way it would
    /// resting on any other solid.
    /// </summary>
    private static readonly Body2D WorldBoundsSentinel = new WorldBoundsBody();

    private sealed class WorldBoundsBody : Body2D
    {
        public WorldBoundsBody() => IsStatic = true;
    }

    /// <summary>
    /// Hazard/body contact pairs still overlapping as of the frame just resolved. Used so an
    /// ordinary (non-kill) hazard contact's effect fires only on the first frame of a new contact
    /// - a "rising edge" - rather than every frame the two remain overlapping. A hazard never
    /// physically pushes a body back out, so a body can rest against/inside one for many
    /// consecutive frames.
    /// </summary>
    private HashSet<(Body2D Hazard, IPhysicsBody Body)> _activeHazardContacts = [];

    // ---------------------------------------------------------------------------------------
    // Per-frame orchestration
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves all collision for one frame.
    /// </summary>
    public void Resolve(World2D world)
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
            // A static body can implement IPhysicsBody to expose its own Velocity (so other
            // bodies' collision response can react to it - e.g. a kinematic platform) without
            // itself being resolved as a mover: it never appears in the solids pairing loop,
            // world-bounds check, or moving-body-vs-moving-body pairing.
            if (body is not IPhysicsBody movingBody || body.IsStatic)
            {
                continue;
            }

            _movingBodies.Add(movingBody);
        }

        ResolveClimbingAndHanging(world);

        // A solid is any static, non-passable body - a plain wall, a kinematic platform, or
        // static terrain generally.
        var solids = world.Objects.Where(body => body.IsStatic && !body.IsPassable).ToList();

        // Broad phase: bucket solids into a spatial grid once per frame so each moving body only
        // has to test collision against nearby solids, not every solid in the level - see
        // SpatialGrid<T>. Everything from here through GetCandidates below is broad phase only
        // (whole-AABB/bucket reasoning); the actual per-rectangle/per-character narrow-phase
        // tests happen afterward, inside ResolveAgainstSolid/ResolveAgainstMover and the
        // TryFindContact/HasCharacterOverlap methods they call.
        _solidsGrid.Rebuild(solids, body => body.Position, body => body.Size);
        _movingBodiesGrid.Rebuild(_movingBodies, body => body.Position, body => body.Size);

        // Solids/movers narrow phase is resolved over several iterations rather than once: a body
        // touching several contacts at once (a corner formed by two solids, a body sandwiched
        // between two others, a body resting on the ground while also overlapping a mover) has
        // each of those contacts detected-and-corrected independently below, so one contact's
        // correction can reintroduce or worsen an overlap at another that was already resolved
        // earlier in the same pass. Re-running the same detection-and-resolution passes several
        // times lets those corrections converge, each later iteration re-detecting overlap from
        // the current, already partially corrected, positions.
        for (var iteration = 0; iteration < PhysicsConstants.SolverIterations; iteration++)
        {
            foreach (var body in _movingBodies)
            {
                foreach (var solid in GetCandidateSolids(body))
                {
                    ResolveAgainstSolid(body, solid);
                }
            }

            // Every moving body can also collide with every other moving body (e.g. the player
            // and the bouncing ball) - checked once per unordered pair, gathered from the same
            // spatial grid so this scales with nearby movers only, not every mover in the level.
            for (var i = 0; i < _movingBodies.Count; i++)
            {
                foreach (var other in GetCandidateMovingBodies(_movingBodies[i]))
                {
                    if (_movingBodies.IndexOf(other) > i)
                    {
                        ResolveAgainstMover(_movingBodies[i], other);
                    }
                }
            }
        }

        foreach (var body in _movingBodies)
        {
            ResolveWorldBounds(world, body);
        }


        ResolveHazardsAndCollectables(world);
    }

    // ---------------------------------------------------------------------------------------
    // Broad phase: nearby-candidate lookup only (see SpatialGrid<T>) - no rectangle/character
    // testing happens here; that is all narrow phase, further down this file.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The broad-phase candidate solids for <paramref name="body"/> this frame - the spatial-grid
    /// bucket contents (see <see cref="SpatialGrid{T}"/>), not every solid in the level.
    /// </summary>
    private IEnumerable<Body2D> GetCandidateSolids(IPhysicsBody body) =>
        _solidsGrid.GetCandidates(body.Position, body.Size);

    /// <summary>Same as <see cref="GetCandidateSolids"/>, but for other moving bodies (excluding <paramref name="body"/> itself).</summary>
    private IEnumerable<IPhysicsBody> GetCandidateMovingBodies(IPhysicsBody body)
    {
        foreach (var other in _movingBodiesGrid.GetCandidates(body.Position, body.Size))
        {
            if (!ReferenceEquals(other, body))
            {
                yield return other;
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Narrow phase: climbing/hanging
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Sets <see cref="IClimberBody.IsTouchingClimbable"/>/<see cref="IHangerBody.IsTouchingHangable"/>
    /// from a body's current overlap against static <see cref="Body2D.IsClimbable"/>/
    /// <see cref="Body2D.IsHangable"/> terrain, recomputed fresh every frame. Generic over any
    /// <see cref="IClimberBody"/>/<see cref="IHangerBody"/> in the world, not just the player.
    /// <see cref="Physics.PhysicsSystem"/> reads these the following frame to decide whether to
    /// actually engage <see cref="IClimberBody.IsClimbing"/>/<see cref="IHangerBody.IsHanging"/>.
    /// </summary>
    /// <remarks>
    /// A ladder can be grabbed from any side (climbing up into it, sideways into it mid-jump, or
    /// falling down onto/through it), so climbable overlap has no directional restriction - just
    /// overlap plus a speed gate (see <see cref="PhysicsConstants.MaxSnapSpeed"/>). A hangable surface only
    /// triggers when approaching from underneath (see <see cref="WouldSnapFromBelow"/>), not
    /// merely brushing its top while landing on it, and is likewise gated on speed.
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

                        // IsTouchingHangable is reported here even while SuppressHangUntilClear
                        // is set below, so PhysicsSystem's debounce-release check ("once no
                        // longer touching at all") only fires once the body has genuinely
                        // cleared the surface's overlap, not the instant a jump/swing begins
                        // while still overlapping it.
                        hanger.IsTouchingHangable = true;

                        // While IHangerBody.SuppressHangUntilClear is set (PhysicsSystem just made
                        // the player jump/swing off, or explicitly let go, this same frame), skip
                        // the actual snap/stop below even though overlap is still detected above -
                        // otherwise the jump-off velocity set moments ago in PhysicsSystem would
                        // be immediately zeroed and the body re-snapped right back. The overlap
                        // keeps being reported above regardless, so the debounce is only released
                        // once truly clear.
                        if (hanger.SuppressHangUntilClear)
                        {
                            break;
                        }

                        // Detected and corrected synchronously, in this same call, rather than
                        // waiting for a flag set on a previous frame.
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
    /// bottom edge, hanging just underneath the surface rather than overlapping into/through it.
    /// Only the vertical axis is corrected - hanging/shimmying is a deliberate lateral action (see
    /// <see cref="Physics.PhysicsSystem"/>'s hang movement), so horizontal position is left alone.
    /// </summary>
    private static void SnapOntoHangable(IHangerBody hanger, Body2D hangable)
    {
        if (!TryFindContact(hanger.CollisionRects, hangable.CollisionRects, out var contact))
        {
            return;
        }

        var bodyTop = hanger.CollisionRects.Min(rect => rect.Top);
        var topOffset = bodyTop - hanger.Position.Y;
        hanger.Position = new Vector2D(hanger.Position.X, contact.OtherRect.Bottom - topOffset - PhysicsConstants.HangOverlapEpsilon);
    }

    /// <summary>
    /// Whether <paramref name="body"/> is moving slowly enough to snap onto a climbable/hangable
    /// surface on first touch, rather than passing straight through it - see
    /// <see cref="PhysicsConstants.MaxSnapSpeed"/>. Checked against overall speed, not just one axis.
    /// </summary>
    private static bool IsWithinSnapSpeed(Vector2D velocity) =>
        Math.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y) <= PhysicsConstants.MaxSnapSpeed;

    /// <summary>
    /// Combines two materials' restitution/friction values for a single collision response as a
    /// plain average of both sides, so either side can meaningfully pull the result toward its
    /// own value rather than one material always dominating outright.
    /// </summary>
    private static double Combine(double a, double b) => (a + b) / 2.0;

    // ---------------------------------------------------------------------------------------
    // Narrow phase: hazards/collectables
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Any <see cref="IPhysicsBody"/> overlapping any <see cref="IHazardBody"/> is a hazard hit.
    /// Collectable pickup requires the moving side to implement <see cref="ICollectorBody"/>, so
    /// non-player bodies (e.g. the bouncing ball, enemies) never consume collectables. Killing a
    /// hazard requires the moving side to implement <see cref="IKillerBody"/>; a non-killer moving
    /// body can still register an ordinary hazard contact/effect, but never the kill/removal path.
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

        // Snapshot before iterating: SpawnEffectIfConfigured below can add a new EffectInstance2D
        // to world.Objects, and hazards can be queued for removal mid-loop.
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

                // A killable hazard contacted from the top ("landed on") is a kill: it is queued
                // for removal and its own effect (if configured) persists as a permanent husk when
                // EffectPersists is set. Any other contact direction, or a non-killable hazard,
                // falls through to the ordinary hazard-contact handling below. A hazard's own
                // effect and the moving body's own effect are mutually exclusive with kill-vs-
                // ordinary-hit: a hazard's EffectClipName is reserved for its kill reaction, while
                // a moving body's EffectClipName (e.g. a player's hit spark) is reserved for
                // ordinary, non-fatal hazard contact.
                if (movingBody is IKillerBody && hazard is IKillableBody { IsKillable: true } killable && IsApproachingFromTop(movingBody, hazard))
                {
                    SpawnEffectIfConfigured(hazard, world, killable.EffectPersists);
                    world.QueueRemoval(hazard);
                    continue;
                }

                // TODO: apply damage once a health/damage system exists. Detection is generic;
                // only the effect is not wired yet.
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
                    // EffectClipName (e.g. a player's hazard-hit spark) never fires on an
                    // unrelated pickup.
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

    // ---------------------------------------------------------------------------------------
    // Narrow phase: world bounds and static-solid resolution
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Keeps a body's bounding box within the world's cell grid, reflecting velocity on whichever
    /// axis it would otherwise cross an edge, scaled by the body's own restitution. The world's
    /// floor is treated the same as a platform's top surface for grounding purposes, so any body
    /// resting against it is considered grounded.
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
    /// Resolves <paramref name="body"/> against <paramref name="solid"/>, treating the solid as
    /// immovable/infinite-mass - its own <see cref="Body2D.Position"/> is never touched by
    /// collision. A solid that is itself an <see cref="IPhysicsBody"/> (a kinematic platform) has
    /// a real <see cref="IPhysicsBody.Velocity"/> used as the collision's reference frame;
    /// ordinary static terrain is implicitly stationary (<see cref="Vector2D.Zero"/>).
    /// </summary>
    private static void ResolveAgainstSolid(IPhysicsBody body, Body2D solid)
    {
        var solidVelocity = solid is IPhysicsBody solidBody ? solidBody.Velocity : default;
        ResolveAgainstOtherBody(body, solid, other: null, solidVelocity, otherInverseMass: 0.0);
    }

    /// <summary>
    /// Resolves <paramref name="body"/> against another body - <paramref name="otherContactTarget"/>,
    /// which is either an immovable solid (<paramref name="other"/> null, <paramref name="otherInverseMass"/>
    /// 0 - see <see cref="ResolveAgainstSolid"/>) or a finite-mass mover (<paramref name="other"/>
    /// non-null - see <see cref="ResolveAgainstMover"/>). Both go through this shared method; only
    /// the mass/velocity given to <see cref="ResolveContact"/> differs between them.
    /// </summary>
    /// <remarks>
    /// A body's collision shape can be made up of several rectangles that don't all have the same
    /// width/offset/extent (e.g. a 3-wide "arms" rectangle for one row, plus a narrower rectangle
    /// spanning that row and the next, from <see cref="CollisionShapeBuilder"/>'s column-run
    /// pass). Resolving each of these rectangles as if it were its own fully independent contact
    /// against <paramref name="otherContactTarget"/> - either applying a full velocity/friction
    /// response to every one of them, or resolving only one and leaving every other rectangle
    /// completely untouched that iteration - are both wrong, in opposite directions: the former
    /// can apply the along-normal velocity impulse more than once for what is really one physical
    /// contact (an unearned extra velocity kick, e.g. jump-height inflation); the latter can leave
    /// a genuinely still-overlapping rectangle fully uncorrected whenever it never happens to be
    /// the single deepest one in a given iteration, which visibly reappeared as a body able to
    /// sink slightly into a plain wall while walking into it.
    /// </remarks>
    /// <remarks>
    /// Instead, exactly one physical contact is resolved per call, and its axis/direction is
    /// derived directly from every one of the body's own rectangles that overlaps this iteration,
    /// without ever synthesizing a single bounding rectangle for the compound shape first: a
    /// unioned "envelope" rectangle silently fills in any notch the body's real, non-rectangular
    /// silhouette has (e.g. the walk pose's arm rectangle reaching further to one side than the
    /// row above/below it), which distorts the axis choice asymmetrically depending on which of
    /// the body's rows happen to be touching first - exactly the kind of approach-direction-
    /// dependent behavior this is meant to eliminate. Instead, for each of the four possible push
    /// directions (left/right/up/down) the <em>worst</em> (largest) push distance required to
    /// clear every real overlapping rectangle pair in that direction is computed directly from
    /// each pair's own true overlap - never from a fabricated bounding shape - and whichever of
    /// those four candidate pushes is <em>smallest</em> is this iteration's one true
    /// minimum-translation-vector: the axis/direction that separates the compound shape as a
    /// whole with the least total motion, while still guaranteeing every individual overlapping
    /// rectangle ends up cleared (each candidate push is already the worst-case for its
    /// direction). This is symmetric by construction - approaching a wall from the left or right
    /// produces the same shape of answer - and only ever applies one velocity/friction response
    /// and one contact recording per solid per iteration. Any residual overlap along a genuinely
    /// different axis (a different physical contact entirely) is left for a later solver
    /// iteration - see <see cref="Resolve"/> - which re-detects fully fresh contacts against the
    /// body's just-corrected position, so it is not permanently ignored.
    /// </remarks>
    private static void ResolveAgainstOtherBody(
        IPhysicsBody body,
        Body2D otherContactTarget,
        IPhysicsBody? other,
        Vector2D otherVelocity,
        double otherInverseMass)
    {
        var restitution = Combine(body.Restitution, otherContactTarget.Restitution);
        var friction = Combine(body.Friction, otherContactTarget.Friction);
        var bodyRects = body.CollisionRects;

        // First pass: every one of the body's own rects that overlaps this iteration, all
        // measured against the same not-yet-corrected position, so they're directly comparable.
        var contacts = new List<Contact>();
        for (var rectIndex = 0; rectIndex < bodyRects.Count; rectIndex++)
        {
            if (!TryFindContact([bodyRects[rectIndex]], otherContactTarget.CollisionRects, out var contact, (Body2D)body, otherContactTarget))
            {
                continue;
            }

            // A one-way platform only blocks a body landing on its top surface while falling (or
            // resting) onto it - jumping up through it from below, or approaching from either
            // side, passes straight through untouched. Landing-from-above is contact.Normal ==
            // (0, -1); whether the body is actually moving down into it, rather than still rising
            // up through it from below, is told apart by relative vertical velocity.
            if (otherContactTarget.IsOneWayPlatform)
            {
                var isLandingFromAbove = contact.Normal.X == 0 && contact.Normal.Y < 0;
                var relativeVerticalVelocity = body.Velocity.Y - otherVelocity.Y;
                if (!isLandingFromAbove || relativeVerticalVelocity < 0)
                {
                    continue;
                }
            }

            contacts.Add(contact);
        }

        if (contacts.Count == 0)
        {
            return;
        }

        // Second pass: for each of the four push directions, the worst-case (largest) push
        // distance needed to separate every real overlapping rectangle pair in that direction -
        // computed straight from each pair's own overlap, never from a fabricated bounding shape.
        var worstPushLeft = double.MinValue;
        var worstPushRight = double.MinValue;
        var worstPushUp = double.MinValue;
        var worstPushDown = double.MinValue;
        var leftContactIndex = 0;
        var rightContactIndex = 0;
        var upContactIndex = 0;
        var downContactIndex = 0;
        for (var i = 0; i < contacts.Count; i++)
        {
            var contact = contacts[i];
            var pushLeft = contact.BodyRect.Right - contact.OtherRect.Left;
            var pushRight = contact.OtherRect.Right - contact.BodyRect.Left;
            var pushUp = contact.BodyRect.Bottom - contact.OtherRect.Top;
            var pushDown = contact.OtherRect.Bottom - contact.BodyRect.Top;

            if (pushLeft > worstPushLeft)
            {
                worstPushLeft = pushLeft;
                leftContactIndex = i;
            }

            if (pushRight > worstPushRight)
            {
                worstPushRight = pushRight;
                rightContactIndex = i;
            }

            if (pushUp > worstPushUp)
            {
                worstPushUp = pushUp;
                upContactIndex = i;
            }

            if (pushDown > worstPushDown)
            {
                worstPushDown = pushDown;
                downContactIndex = i;
            }
        }

        // Whichever of the four candidate pushes is smallest is this iteration's true MTV - see
        // remarks.
        var (bestPush, bestNormal, bestContactIndex) = (worstPushLeft, new Vector2D(-1, 0), leftContactIndex);
        if (worstPushRight < bestPush)
        {
            (bestPush, bestNormal, bestContactIndex) = (worstPushRight, new Vector2D(1, 0), rightContactIndex);
        }

        if (worstPushUp < bestPush)
        {
            (bestPush, bestNormal, bestContactIndex) = (worstPushUp, new Vector2D(0, -1), upContactIndex);
        }

        if (worstPushDown < bestPush)
        {
            (bestPush, bestNormal, bestContactIndex) = (worstPushDown, new Vector2D(0, 1), downContactIndex);
        }

        if (bestPush <= 0)
        {
            return;
        }

        var resolvedContact = contacts[bestContactIndex] with { Normal = bestNormal, Depth = bestPush };
        ResolveContact(resolvedContact, body, otherContactTarget, other, otherVelocity, otherInverseMass, restitution, friction);
    }

    // ---------------------------------------------------------------------------------------
    // Narrow phase: shared contact resolution, used identically whether the other side is an
    // immovable solid or another finite-mass mover.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A body's effective mass for collision-response purposes, treating an unresolved zero
    /// <see cref="Body2D.Mass"/> as 1.0 to keep the impulse/position-correction formulas below
    /// well-defined.
    /// </summary>
    private static double GetInverseMass(double mass) => 1.0 / (mass > 0 ? mass : 1.0);

    /// <summary>
    /// Resolves one already-detected <see cref="Contact"/> - pushes <paramref name="body"/> (and,
    /// if present, <paramref name="other"/>) apart along <see cref="Contact.Normal"/> by their
    /// relative inverse mass (always), then - only while the pair is still actually approaching
    /// along <see cref="Contact.Normal"/> - applies a mass-weighted impulse for the along-normal
    /// velocity response (using <paramref name="restitution"/>) and damps the tangential
    /// (along-surface) velocity component via Coulomb friction (using <paramref name="friction"/>),
    /// before recording the resulting <see cref="ContactType"/> on both sides from
    /// <see cref="Contact.Normal"/>'s sign. When <paramref name="otherInverseMass"/> is 0 (an
    /// immovable solid), <paramref name="body"/> alone absorbs the full position correction and
    /// velocity response; otherwise both sides share it by their relative inverse mass.
    /// </summary>
    /// <remarks>
    /// A rectangle pair can be reported overlapping by <see cref="TryFindContact"/> even on a
    /// frame where the two bodies are already moving apart along that same normal - e.g. a
    /// leftover sliver of overlap against a solid's corner, still detected the instant after
    /// <paramref name="body"/> has already launched away from it (a jump taken immediately beside
    /// a slightly higher, tightly adjacent platform is the case that surfaced this). The velocity
    /// response is only ever meant to arrest an approach, never to react to a contact that is
    /// already resolving itself via the bodies' own existing motion - applying it anyway would
    /// inject an extra, unwanted velocity kick (e.g. an unearned second jump boost) on top of
    /// whatever motion already separated them. Position correction alone is unconditional and
    /// always applied, since any actual overlap depth still needs resolving regardless of which
    /// way the bodies are currently moving.
    /// </remarks>
    /// <param name="otherContactTarget">
    /// The <see cref="Body2D"/> to record the reciprocal <see cref="ContactType"/> on - the solid
    /// itself, or <paramref name="other"/> cast to <see cref="Body2D"/>.
    /// </param>
    /// <param name="other">The other side of the contact if it's a finite-mass mover, or null if it's an immovable solid.</param>
    /// <param name="otherVelocity">
    /// The other side's velocity - <paramref name="other"/>'s own <see cref="IPhysicsBody.Velocity"/>
    /// when it is one, or a solid's own reference-frame velocity otherwise.
    /// </param>
    /// <param name="otherInverseMass">The other side's inverse mass - 0 for an immovable solid, <see cref="GetInverseMass"/> of its mass otherwise.</param>
    private static void ResolveContact(
        Contact contact,
        IPhysicsBody body,
        Body2D otherContactTarget,
        IPhysicsBody? other,
        Vector2D otherVelocity,
        double otherInverseMass,
        double restitution,
        double friction)
    {
        var bodyInverseMass = GetInverseMass(body.Mass);
        var totalInverseMass = bodyInverseMass + otherInverseMass;
        if (totalInverseMass <= 0)
        {
            // Both sides immovable - not expected to be reachable, kept as a defensive guard
            // against a divide-by-zero below.
            return;
        }

        var bodyShare = bodyInverseMass / totalInverseMass;
        var otherShare = otherInverseMass / totalInverseMass;

        // Position correction: push body and other directly apart along Normal, split by their
        // relative inverse mass.
        body.Position += contact.Normal * (contact.Depth * bodyShare);
        if (other is not null)
        {
            other.Position -= contact.Normal * (contact.Depth * otherShare);
        }

        // Along-normal velocity response: a mass-weighted impulse along Normal, using the pair's
        // combined restitution - but only while the two bodies are actually still approaching
        // each other along Normal (normalRelativeSpeed < 0: body's own velocity relative to
        // other's still points from other toward body's pre-correction side, the opposite of the
        // separation direction). Skipping the impulse once normalRelativeSpeed >= 0 (already
        // separating, or exactly grazing) matters because a rectangle pair can still be reported
        // as overlapping (e.g. a leftover sliver of overlap against a solid corner) on the very
        // frame body has already launched away from it - most visibly a jump taken immediately
        // beside a slightly higher, tightly adjacent platform, where the player's rect still
        // clips that platform's corner. Applying the ordinary impulse formula there anyway would
        // treat that stale, separating contact as a fresh landing and inject an extra unwanted
        // velocity kick on top of the jump - it is only ever correct to apply this impulse to
        // actually arrest an approach, never to a contact that's already resolving itself via the
        // bodies' own existing motion. Position correction above still always runs unconditionally
        // so any actual overlap depth is still resolved.
        var relativeVelocity = body.Velocity - otherVelocity;
        var normalRelativeSpeed = relativeVelocity.X * contact.Normal.X + relativeVelocity.Y * contact.Normal.Y;
        if (normalRelativeSpeed >= 0)
        {
            RecordContact(body, otherContactTarget, contact.Normal);
            return;
        }

        var normalImpulse = -(1.0 + restitution) * normalRelativeSpeed / totalInverseMass;
        body.Velocity += contact.Normal * (normalImpulse * bodyInverseMass);
        if (other is not null)
        {
            other.Velocity -= contact.Normal * (normalImpulse * otherInverseMass);
        }

        // Tangential (along-surface) velocity response: Coulomb friction (see
        // ApplyCoulombFriction), capped by the normal impulse magnitude just applied to body's own
        // velocity, then split between body/other by the same inverse-mass shares used for
        // position correction above.
        var tangent = new Vector2D(-contact.Normal.Y, contact.Normal.X);
        var tangentialRelativeSpeed = relativeVelocity.X * tangent.X + relativeVelocity.Y * tangent.Y;
        var normalImpulseMagnitudePerMass = Math.Abs(normalImpulse * bodyInverseMass);
        var newTangentialRelativeSpeed = ApplyCoulombFriction(tangentialRelativeSpeed, normalImpulseMagnitudePerMass, friction);
        var tangentialDelta = tangentialRelativeSpeed - newTangentialRelativeSpeed;
        body.Velocity -= tangent * (tangentialDelta * bodyShare);
        if (other is not null)
        {
            other.Velocity += tangent * (tangentialDelta * otherShare);
        }

        RecordContact(body, otherContactTarget, contact.Normal);
    }

    /// <summary>
    /// Records the reciprocal <see cref="ContactType"/> pair for a resolved <see cref="Contact"/>
    /// on both <paramref name="body"/> and <paramref name="other"/>, derived from
    /// <paramref name="normal"/>'s sign - e.g. <paramref name="normal"/> pointing up
    /// (<c>(0, -1)</c>, body moves up to separate) means <paramref name="other"/> is below
    /// <paramref name="body"/>: <see cref="ContactType.SurfaceBottom"/> on <paramref name="body"/>,
    /// <see cref="ContactType.SurfaceTop"/> on <paramref name="other"/>.
    /// </summary>
    private static void RecordContact(IPhysicsBody body, Body2D other, Vector2D normal)
    {
        if (normal.Y < 0)
        {
            body.AddContact(ContactType.SurfaceBottom, other);
            other.AddContact(ContactType.SurfaceTop, (Body2D)body);
        }
        else if (normal.Y > 0)
        {
            body.AddContact(ContactType.SurfaceTop, other);
            other.AddContact(ContactType.SurfaceBottom, (Body2D)body);
        }
        else if (normal.X < 0)
        {
            body.AddContact(ContactType.SurfaceRight, other);
            other.AddContact(ContactType.SurfaceLeft, (Body2D)body);
        }
        else if (normal.X > 0)
        {
            body.AddContact(ContactType.SurfaceLeft, other);
            other.AddContact(ContactType.SurfaceRight, (Body2D)body);
        }
    }

    /// <summary>
    /// Damps a tangential (along-surface) relative velocity component using Coulomb friction,
    /// clamped by the normal impulse that was just applied at this same contact.
    /// <paramref name="normalImpulseMagnitudePerMass"/> is the magnitude of the change in
    /// normal-relative velocity from the impulse that just resolved this contact; the maximum
    /// tangential speed change friction may cause this frame is <paramref name="friction"/> times
    /// that (the classic <c>|f| &lt;= mu * N</c> Coulomb constraint, expressed in velocity terms).
    /// If the tangential relative velocity is already smaller than that cap, it is cancelled
    /// entirely (static friction); otherwise it is reduced by exactly the capped amount (kinetic
    /// friction), never overshooting past zero or reversing direction.
    /// </summary>
    private static double ApplyCoulombFriction(double tangentialRelativeVelocity, double normalImpulseMagnitudePerMass, double friction)
    {
        var maxFrictionDelta = Math.Clamp(friction, 0.0, 1.0) * normalImpulseMagnitudePerMass;
        var delta = Math.Clamp(tangentialRelativeVelocity, -maxFrictionDelta, maxFrictionDelta);
        return tangentialRelativeVelocity - delta;
    }

    /// <summary>
    /// Finds the deepest-overlapping rectangle pair between two bodies' (possibly multi-rect)
    /// collision shapes and derives the resulting <see cref="Contact"/> - <see cref="Contact.Normal"/>
    /// and <see cref="Contact.Depth"/> - purely from that pair's overlap geometry. Whichever axis
    /// (vertical vs. horizontal) has the shallower overlap is the contact's axis (the standard
    /// minimum-translation-vector choice), and whichever side of that axis has the shallower
    /// overlap determines <see cref="Contact.Normal"/>'s sign. Shared by
    /// <see cref="ResolveAgainstSolid"/>, <see cref="ResolveAgainstMover"/>,
    /// <see cref="IsApproachingFromTop"/>, <see cref="SnapOntoHangable"/>, and
    /// <see cref="WouldSnapFromBelow"/>. Returns false (with no <paramref name="contact"/>) if no
    /// rectangle pair overlaps at all.
    /// </summary>
    /// <param name="ownerA">
    /// The body <paramref name="aRects"/> belongs to, or null. When both owners are supplied, a
    /// recta
    /// <see cref="HasCharacterOverlap"/>) - requiring the two bodies' actual rendered characters
    /// to overlap, not merely their merged collision rectangles - before it is accepted. Callers
    /// that don't care about that distinction (e.g. <see cref="SnapOntoHangable"/>) may omit both
    /// owners to skip it.
    /// </param>
    /// <param name="ownerB">See <paramref name="ownerA"/>, for <paramref name="bRects"/>.</param>
    private static bool TryFindContact(
        IReadOnlyList<Rect> aRects,
        IReadOnlyList<Rect> bRects,
        out Contact contact,
        Body2D? ownerA = null,
        Body2D? ownerB = null)
    {
        contact = default;
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

                // Additionally requires the two bodies' actual rendered (non-empty) characters to
                // overlap somewhere within this rectangle pair's intersection, not just the merged
                // collision rectangles themselves - see HasCharacterOverlap.
                if (ownerA is not null && ownerB is not null
                    && !HasCharacterOverlap(ownerA, ownerB, rectA, rectB))
                {
                    continue;
                }

                var overlapLeft = rectA.Right - rectB.Left;
                var overlapRight = rectB.Right - rectA.Left;
                var overlapTop = rectA.Bottom - rectB.Top;
                var overlapBottom = rectB.Bottom - rectA.Top;

                var minHorizontal = Math.Min(overlapLeft, overlapRight);
                var minVertical = Math.Min(overlapTop, overlapBottom);
                var penetration = Math.Min(minHorizontal, minVertical);
                if (penetration < bestPenetration)
                {
                    bestPenetration = penetration;
                    var normal = minVertical < minHorizontal
                        ? new Vector2D(0, overlapTop < overlapBottom ? -1 : 1)
                        : new Vector2D(overlapLeft < overlapRight ? -1 : 1, 0);
                    contact = new Contact(normal, penetration, rectA, rectB);
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Confirms two bodies' rectangle overlap actually corresponds to their true rendered
    /// silhouettes touching, by testing whether at least one world cell within the overlap region
    /// has a non-empty character on both sides, rather than trusting the merged collision
    /// rectangles alone. This only matters for shapes whose true silhouette doesn't exactly fill
    /// its merged collision rectangle (e.g. a diagonal or notched sprite); for a solid rectangular
    /// sprite every cell within its own collision rect is already known non-empty, so this always
    /// agrees with the plain rectangle test.
    /// </summary>
    private static bool HasCharacterOverlap(Body2D ownerA, Body2D ownerB, Rect rectA, Rect rectB)
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
                // gridX/gridY are integer world-cell coordinates, but Position is a continuous
                // (fractional) world coordinate. The correct local grid index is
                // floor(worldCell - Position), not worldCell - floor(Position): those differ by
                // one whenever Position has a nonzero fractional part, which would otherwise
                // shift every character lookup by a full row/column.
                var localAX = (int)Math.Floor(gridX - ownerA.Position.X);
                var localAY = (int)Math.Floor(gridY - ownerA.Position.Y);
                var localBX = (int)Math.Floor(gridX - ownerB.Position.X);
                var localBY = (int)Math.Floor(gridY - ownerB.Position.Y);

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
    /// vertical axis with <paramref name="body"/> approaching from above - i.e. "landed on top".
    /// Any other direction (side/underneath), or no overlap at all, returns false.
    /// </summary>
    private static bool IsApproachingFromTop(IPhysicsBody body, Body2D other)
    {
        return TryFindContact(body.CollisionRects, other.CollisionRects, out var contact)
            && contact.Normal.X == 0 && contact.Normal.Y < 0;
    }

    /// <summary>
    /// Whether <paramref name="body"/> is underneath <paramref name="other"/> - i.e. no part of
    /// <paramref name="body"/>'s own collision shape extends above <paramref name="other"/>'s top
    /// edge. Compares the body's own overall topmost edge (the minimum <see cref="Rect.Top"/>
    /// across all of its collision rects), so a multi-rect body (e.g. one whose feet sit well
    /// below its head) only snaps once its true top row has cleared the surface, not as soon as
    /// any single sub-rect overlaps it. Combined with the caller's <see cref="IsWithinSnapSpeed"/>
    /// gate, this also rejects a body moving too fast to grab on even though it is geometrically
    /// underneath.
    /// </summary>
    private static bool WouldSnapFromBelow(IPhysicsBody body, Body2D other)
    {
        if (!TryFindContact(body.CollisionRects, other.CollisionRects, out var contact))
        {
            return false;
        }

        var bodyTop = body.CollisionRects.Min(rect => rect.Top);
        return bodyTop >= contact.OtherRect.Top;
    }

    // ---------------------------------------------------------------------------------------
    // Narrow phase: moving-body-vs-moving-body resolution
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves a collision between two moving bodies (e.g. the player and the bouncing ball). A
    /// thin wrapper over the same <see cref="ResolveAgainstOtherBody"/> used against solid terrain,
    /// just with <paramref name="b"/>'s real (finite) mass/velocity instead of an immovable
    /// solid's.
    /// </summary>
    private static void ResolveAgainstMover(IPhysicsBody a, IPhysicsBody b) =>
        ResolveAgainstOtherBody(a, (Body2D)b, b, b.Velocity, GetInverseMass(b.Mass));
}


