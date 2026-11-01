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

    /// <summary>Each moving body's index in <see cref="_movingBodies"/>, so each unordered pair is resolved once without a list search.</summary>
    private readonly Dictionary<IPhysicsBody, int> _movingBodyIndices = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// How many contact responses this solver iteration changed a position or velocity
    /// noticeably (see <see cref="PhysicsConstants.SolverVelocityTolerance"/>); zero means the
    /// iteration has converged.
    /// </summary>
    private int _changesThisIteration;

    /// <summary>Broad-phase spatial grid over this frame's static, non-passable solids - see <see cref="SpatialGrid{T}"/>.</summary>
    private readonly SpatialGrid<Body2D> _solidsGrid = new(PhysicsConstants.GridCellSize);

    /// <summary>Broad-phase spatial grid over this frame's moving bodies - see <see cref="SpatialGrid{T}"/>.</summary>
    private readonly SpatialGrid<IPhysicsBody> _movingBodiesGrid = new(PhysicsConstants.GridCellSize);

    /// <summary>
    /// Hazard/body contact pairs touching last frame, so an ordinary (non-kill) hazard contact's
    /// effect fires only on the first frame of a new contact - a "rising edge" - rather than every
    /// frame the two remain touching (resting on/against a solid hazard, or inside a passable one).
    /// </summary>
    private HashSet<(Body2D Hazard, IPhysicsBody Body)> _activeHazardContacts = [];

    /// <summary>Puts resting groups of bodies to sleep and wakes disturbed ones - see <see cref="SleepSystem"/>.</summary>
    private readonly SleepSystem _sleep = new();

    /// <summary>The length of the step currently being resolved.</summary>
    private double _stepSeconds;

    /// <summary>
    /// Wakes a sleeping body that something awake has just reached during this step's solve. Its
    /// kept contacts are cleared first, exactly as every awake body's were at the start of
    /// <see cref="Resolve"/>, so this step records its contacts fresh.
    /// </summary>
    private void WakeDuringResolve(Body2D body)
    {
        body.ClearContactsForNewStep(_stepSeconds);
        body.Wake();
    }

    /// <summary>Forgets hazard contacts carried between frames, so a newly loaded world starts fresh.</summary>
    public void Reset() => _activeHazardContacts.Clear();

    // ---------------------------------------------------------------------------------------
    // Per-frame orchestration
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves all collision for one physics (sub)step of <paramref name="stepSeconds"/>.
    /// </summary>
    public void Resolve(World2D world, double stepSeconds)
    {
        _stepSeconds = stepSeconds;
        _movingBodies.Clear();
        _movingBodyIndices.Clear();

        // Every body (static or moving) can be on the receiving end of a recorded contact this
        // frame (e.g. a static platform records SurfaceTop when something lands on it), so every
        // body's contact set is cleared up front, before anything for this new
        // frame is resolved/recorded - see Body2D.ClearContactsForNewStep.
        // A sleeping body is not re-resolved, so it keeps its last contacts as they were (still
        // grounded, still linked to its island) - see SleepSystem.
        foreach (var contactBody in world.Objects)
        {
            if (!contactBody.IsSleeping)
            {
                contactBody.ClearContactsForNewStep(stepSeconds);
            }
        }

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

            _movingBodyIndices[movingBody] = _movingBodies.Count;
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
            _changesThisIteration = 0;
            ResolveAgainstSolids();

            // Every moving body can also collide with every other moving body (e.g. the player
            // and the bouncing ball) - checked once per unordered pair, gathered from the same
            // spatial grid so this scales with nearby movers only, not every mover in the level.
            // Two sleeping bodies are at rest against each other by definition and are skipped; an
            // awake body pairs with every sleeping neighbour, waking it on overlap.
            for (var i = 0; i < _movingBodies.Count; i++)
            {
                var body = _movingBodies[i];
                if (((Body2D)body).IsSleeping)
                {
                    continue;
                }

                foreach (var other in _movingBodiesGrid.GetCandidates(body.Position, body.Size))
                {
                    var otherBody = (Body2D)other;
                    if (otherBody.IsSleeping)
                    {
                        if (!BoundsOverlap((Body2D)body, otherBody))
                        {
                            continue;
                        }

                        WakeDuringResolve(otherBody);
                        ResolveAgainstMover(body, other);
                    }
                    // Self has index i, so the ordering check also excludes the body itself.
                    else if (_movingBodyIndices[other] > i)
                    {
                        ResolveAgainstMover(body, other);
                    }
                }
            }

            // Converged: no contact response in this iteration moved a body out of an overlap
            // deeper than the slop or changed a velocity noticeably, so further iterations would
            // not change the outcome in any meaningful way.
            if (_changesThisIteration == 0)
            {
                break;
            }
        }

        // Final cleanup: mover-vs-mover
        // into a solid. Solids are immovable, so one last solids-only pass guarantees no body
        // ends the frame overlapping static terrain.
        ResolveAgainstSolids();

        foreach (var body in _movingBodies)
        {
            if (!((Body2D)body).IsSleeping)
            {
                ResolveWorldBounds(world, body);
            }
        }

        _sleep.Update(_movingBodies, stepSeconds);

        ResolveHazardsAndCollectables(world);

        foreach (var body in world.Objects)
        {
            body.MarkResolvedPosition();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Broad phase: nearby-candidate lookup only
    // testing happens here; that is all narrow phase, further down this file.
    // Both helpers return each grid's shared candidate buffer (lazily, for moving bodies), so a
    // caller must not start a second query on the same grid while still enumerating the first.
    // Candidate order feeds directly into the order-sensitive contact resolution below.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The broad-phase candidate solids for <paramref name="body"/> this frame - the spatial-grid
    /// bucket contents (see <see cref="SpatialGrid{T}"/>), not every solid in the level.
    /// </summary>
    private HashSet<Body2D> GetCandidateSolids(IPhysicsBody body) =>
        _solidsGrid.GetCandidates(body.Position, body.Size);

    /// <summary>
    /// Resolves every moving body against its candidate solids. A sleeping body is at rest against
    /// stationary terrain, so only a moving solid (a kinematic platform) can reach it - overlapping
    /// one wakes it and resolves it normally.
    /// </summary>
    private void ResolveAgainstSolids()
    {
        foreach (var body in _movingBodies)
        {
            var bodyBase = (Body2D)body;
            foreach (var solid in GetCandidateSolids(body))
            {
                if (bodyBase.IsSleeping)
                {
                    if (solid is not IPhysicsBody || !BoundsOverlap(bodyBase, solid))
                    {
                        continue;
                    }

                    WakeDuringResolve(bodyBase);
                }

                ResolveAgainstSolid(body, solid);
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
            // Speed gates are measured relative to the surface's carry velocity (its own motion
            // plus any conveyor motion - see Body2D.GetCarryVelocity), so a moving ladder/pipe or
            // a conveyor/rope can be grabbed as easily as a fixed one. The touched surface is
            // recorded as a grip contact (ContactType.Climbable/Hangable), which PhysicsSystem
            // uses as the reference frame and reaction target for the grip motor.
            if (body is IClimberBody climber)
            {
                climber.IsTouchingClimbable = false;
                foreach (var climbable in climbables)
                {
                    if (!Overlaps(climber, climbable) || !IsWithinSnapSpeed(climber.Velocity - climbable.GetCarryVelocity()))
                    {
                        continue;
                    }

                    climber.IsTouchingClimbable = true;
                    climber.AddContact(ContactType.Climbable, climbable);
                    break;
                }
            }

            if (body is IHangerBody hanger)
            {
                hanger.IsTouchingHangable = false;
                foreach (var hangable in hangables)
                {
                    var carryVelocity = hangable.GetCarryVelocity();
                    if (!IsWithinSnapSpeed(hanger.Velocity - carryVelocity)
                        || !Overlaps(hanger, hangable) || !WouldSnapFromBelow(hanger, hangable))
                    {
                        continue;
                    }

                    // IsTouchingHangable is reported here even while SuppressHangUntilClear
                    // is set below, so PhysicsSystem's debounce-release check ("once no
                    // longer touching at all") only fires once the body has genuinely
                    // cleared the surface's overlap, not the instant a jump/swing begins
                    // while still overlapping it.
                    hanger.IsTouchingHangable = true;
                    hanger.AddContact(ContactType.Hangable, hangable);

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
                    // waiting for a flag set on a previous frame. Vertical velocity is matched
                    // to the surface's carry velocity rather than zeroed, so a rising/falling
                    // pipe holds the body at rest relative to itself.
                    hanger.Velocity = new Vector2D(hanger.Velocity.X, carryVelocity.Y);
                    SnapOntoHangable(hanger, hangable);
                    break;
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
    /// Combines two materials' friction coefficients for a single contact as their geometric mean
    /// (<c>sqrt(a * b)</c>) - the common physics-engine convention: a slippery surface (near 0)
    /// keeps the pair slippery however grippy the other side is, and two equal materials keep
    /// their own value. Not capped at 1, since real friction coefficients can exceed 1.
    /// </summary>
    internal static double CombineFriction(double a, double b) => Math.Sqrt(Math.Max(a, 0.0) * Math.Max(b, 0.0));

    /// <summary>
    /// Combines two bodies' restitution for a single contact, weighted by softness (1 / Hardness):
    /// in an impact the softer body deforms more and so stores/returns most of the energy, which
    /// means its own restitution dominates. A rubber ball bounces well on hard concrete and a bit
    /// less on wood, which gives a little and absorbs some of the impact itself.
    /// </summary>
    private static double CombineRestitution(Body2D a, Body2D b)
    {
        var softnessA = 1.0 / (a.Hardness > 0 ? a.Hardness : 1.0);
        var softnessB = 1.0 / (b.Hardness > 0 ? b.Hardness : 1.0);
        return (softnessA * a.Restitution + softnessB * b.Restitution) / (softnessA + softnessB);
    }

    /// <summary>
    /// Restitution actually applied for an impact at <paramref name="approachSpeed"/> (cells/s,
    /// positive): below <see cref="PhysicsConstants.RestingBounceSpeedThreshold"/> the impact is
    /// fully inelastic, so resting bodies settle instead of micro-bouncing forever on the speed
    /// gravity adds each step.
    /// </summary>
    private static double EffectiveRestitution(double restitution, double approachSpeed) =>
        approachSpeed < PhysicsConstants.RestingBounceSpeedThreshold ? 0.0 : restitution;

    // ---------------------------------------------------------------------------------------
    // Narrow phase: hazards/collectables
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Any <see cref="IPhysicsBody"/> touching any <see cref="IHazardBody"/> (see
    /// <see cref="IsTouchingHazard"/>) is a hazard hit.
    /// Collectable pickup requires the moving side to implement <see cref="ICollectorBody"/>, so
    /// non-player bodies (e.g. the bouncing ball, hazards) never consume collectables. Killing a
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
                if (ReferenceEquals(body, hazard) || !IsTouchingHazard(movingBody, hazard))
                {
                    continue;
                }

                // A killable hazard landed on hard enough (closing speed at or above its
                // KillImpactSpeed) is a kill: it is queued for removal and its own effect (if
                // configured) persists as a permanent husk when EffectPersists is set. Any other
                // contact direction, a gentler landing, or a non-killable hazard falls through to
                // the ordinary hazard-contact handling below. A hazard's own effect and the moving
                // body's own effect are mutually exclusive with kill-vs-ordinary-hit: a hazard's
                // EffectClipName is reserved for its kill reaction, while a moving body's
                // EffectClipName (e.g. a player's hit spark) is reserved for ordinary, non-fatal
                // hazard contact.
                if (movingBody is IKillerBody && hazard is IKillableBody { IsKillable: true } killable && IsKillingImpact(movingBody, hazard, killable))
                {
                    InteractionHandler.OnHazardKilled(world, hazard, killable);
                    continue;
                }

                // Ordinary (non-killing) hazard contact: consequences are gated to the first
                // frame of a fresh contact so a sustained contact doesn't repeat every frame.
                var contact = (hazard, movingBody);
                currentHazardContacts.Add(contact);
                if (!_activeHazardContacts.Contains(contact))
                {
                    InteractionHandler.OnHazardHit(world, body, hazard);
                }
            }

            foreach (var collectable in collectables)
            {
                if (movingBody is not ICollectorBody || !Overlaps(movingBody, collectable))
                {
                    continue;
                }

                InteractionHandler.OnCollected(world, movingBody, collectable);
            }
        }

        _activeHazardContacts = currentHazardContacts;
    }

    /// <summary>
    /// Whether the solver physically resolves <paramref name="hazard"/> against movers: any
    /// moving body, or a static body that is not passable - the same split the solids and
    /// mover-pair passes in <see cref="Resolve"/> use.
    /// </summary>
    private static bool IsSolidHazard(Body2D hazard) => !hazard.IsStatic || !hazard.IsPassable;

    /// <summary>
    /// A solid hazard is touched only through a surface contact the solver resolved this frame -
    /// it is pushed apart, never left overlapping, so overlap alone would miss a resting/sliding
    /// contact. A passable hazard gets no surface contact and is touched by plain overlap. Climb/
    /// hang grip contacts never count as touching.
    /// </summary>
    private static bool IsTouchingHazard(IPhysicsBody body, Body2D hazard) =>
        IsSolidHazard(hazard) ? body.HasSurfaceContactWith(hazard) : Overlaps(body, hazard);

    /// <summary>
    /// Whether <paramref name="body"/> landed on top of <paramref name="hazard"/> at a closing
    /// speed of at least its <see cref="IKillableBody.KillImpactSpeed"/>. A solid hazard uses the
    /// impact speed recorded for the body's bottom contact with it (side impacts are stored
    /// separately and never count); a passable hazard uses the overlap direction and the body's
    /// downward speed relative to the hazard.
    /// </summary>
    private static bool IsKillingImpact(IPhysicsBody body, Body2D hazard, IKillableBody killable)
    {
        if (IsSolidHazard(hazard))
        {
            return body.GetContactImpactSpeed(ContactType.SurfaceBottom, hazard) >= killable.KillImpactSpeed;
        }

        if (!IsApproachingFromTop(body, hazard))
        {
            return false;
        }

        var hazardVelocityY = hazard is IPhysicsBody movingHazard ? movingHazard.Velocity.Y : 0.0;
        return body.Velocity.Y - hazardVelocityY >= killable.KillImpactSpeed;
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
    /// Fallback safety net that keeps a body's bounding box within the world's cell grid. All four
    /// edges are treated identically: the body is clamped back inside and only the velocity
    /// component heading into that edge is reflected, scaled by the body's own restitution (zero
    /// below <see cref="PhysicsConstants.RestingBounceSpeedThreshold"/>, see
    /// <see cref="EffectiveRestitution"/>). World edges have no material, apply no friction and
    /// record no contacts, so a body touching only an edge is never grounded - levels are expected
    /// to provide their own floors, walls and ceilings.
    /// </summary>
    private static void ResolveWorldBounds(World2D world, IPhysicsBody body)
    {
        var restitution = body.Restitution;
        var position = body.Position;
        var velocity = body.Velocity;

        (position.X, velocity.X) = ClampAxisToBounds(position.X, velocity.X, 0.0, world.WidthCells - body.Size.X, restitution);
        (position.Y, velocity.Y) = ClampAxisToBounds(position.Y, velocity.Y, 0.0, world.HeightCells - body.Size.Y, restitution);

        body.Position = position;
        body.Velocity = velocity;
    }

    private static (double Position, double Velocity) ClampAxisToBounds(double position, double velocity, double min, double max, double restitution)
    {
        if (position < min)
        {
            position = min;
            if (velocity < 0)
            {
                velocity = -velocity * EffectiveRestitution(restitution, -velocity);
            }
        }
        else if (position > max)
        {
            position = max;
            if (velocity > 0)
            {
                velocity = -velocity * EffectiveRestitution(restitution, velocity);
            }
        }

        return (position, velocity);
    }

    /// <summary>
    /// Resolves <paramref name="body"/> against <paramref name="solid"/>, treating the solid as
    /// immovable/infinite-mass - its own <see cref="Body2D.Position"/> is never touched by
    /// collision. A solid that is itself an <see cref="IPhysicsBody"/> (a kinematic platform) has
    /// a real <see cref="IPhysicsBody.Velocity"/> used as the collision's reference frame;
    /// ordinary static terrain is implicitly stationary (<see cref="Vector2D.Zero"/>). Either may
    /// add a moving surface (<see cref="Body2D.ConveyorVelocity"/>, e.g. a conveyor belt), which
    /// friction then drags resting bodies along with (see <see cref="Body2D.GetCarryVelocity"/>).
    /// </summary>
    private void ResolveAgainstSolid(IPhysicsBody body, Body2D solid)
    {
        var solidVelocity = solid.GetCarryVelocity();
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
    private void ResolveAgainstOtherBody(
        IPhysicsBody body,
        Body2D otherContactTarget,
        IPhysicsBody? other,
        Vector2D otherVelocity,
        double otherInverseMass)
    {
        // Broad rejection: every collision rectangle lies within its body's Position/Size box, so
        // if the two boxes don't overlap no rectangle pair can either.
        var bodyBase = (Body2D)body;
        if (!BoundsOverlap(bodyBase, otherContactTarget))
        {
            return;
        }

        var bodyRects = body.CollisionRects;
        var otherRects = otherContactTarget.CollisionRects;

        // Every overlapping rectangle pair - each of the body's own rects against each of the other
        // side's rects - all measured against the same not-yet-corrected positions so they're
        // directly comparable. For each of the four push directions the worst-case (largest) push
        // distance needed to separate every real overlapping pair in that direction is tracked,
        // computed straight from each pair's own overlap. Every pair counts, on both sides: keeping
        // only the shallowest of the other side's rects would let a deep overlap hide behind a
        // grazing one, and make the outcome depend on which body of the pair is resolved as "body".
        var found = false;
        var worstPushLeft = double.MinValue;
        var worstPushRight = double.MinValue;
        var worstPushUp = double.MinValue;
        var worstPushDown = double.MinValue;
        Contact leftContact = default, rightContact = default, upContact = default, downContact = default;
        for (var rectIndex = 0; rectIndex < bodyRects.Count; rectIndex++)
        {
            var bodyRect = bodyRects[rectIndex];
            for (var otherIndex = 0; otherIndex < otherRects.Count; otherIndex++)
            {
                var otherRect = otherRects[otherIndex];
                if (!bodyRect.Overlaps(otherRect)
                    || !HasCharacterOverlap(bodyBase, otherContactTarget, bodyRect, otherRect))
                {
                    continue;
                }

                found = true;
                var contact = new Contact(default, 0, bodyRect, otherRect);
                var pushLeft = bodyRect.Right - otherRect.Left;
                var pushRight = otherRect.Right - bodyRect.Left;
                var pushUp = bodyRect.Bottom - otherRect.Top;
                var pushDown = otherRect.Bottom - bodyRect.Top;

                if (pushLeft > worstPushLeft)
                {
                    worstPushLeft = pushLeft;
                    leftContact = contact;
                }

                if (pushRight > worstPushRight)
                {
                    worstPushRight = pushRight;
                    rightContact = contact;
                }

                if (pushUp > worstPushUp)
                {
                    worstPushUp = pushUp;
                    upContact = contact;
                }

                if (pushDown > worstPushDown)
                {
                    worstPushDown = pushDown;
                    downContact = contact;
                }
            }
        }

        if (!found)
        {
            return;
        }

        // Whichever of the four candidate pushes is smallest is this iteration's true MTV - see
        // remarks.
        var (bestPush, bestNormal, bestContact) = (worstPushLeft, new Vector2D(-1, 0), leftContact);
        if (worstPushRight < bestPush)
        {
            (bestPush, bestNormal, bestContact) = (worstPushRight, new Vector2D(1, 0), rightContact);
        }

        if (worstPushUp < bestPush)
        {
            (bestPush, bestNormal, bestContact) = (worstPushUp, new Vector2D(0, -1), upContact);
        }

        if (worstPushDown < bestPush)
        {
            (bestPush, bestNormal, bestContact) = (worstPushDown, new Vector2D(0, 1), downContact);
        }

        if (bestPush <= 0)
        {
            return;
        }

        var restitution = CombineRestitution(bodyBase, otherContactTarget);
        var friction = CombineFriction(body.Friction, otherContactTarget.Friction);
        var resolvedContact = bestContact with { Normal = bestNormal, Depth = bestPush };
        if (ResolveContact(resolvedContact, body, otherContactTarget, other, otherVelocity, otherInverseMass, restitution, friction))
        {
            _changesThisIteration++;
        }
    }

    /// <summary>Whether two bodies' Position/Size boxes overlap with positive area.</summary>
    private static bool BoundsOverlap(Body2D a, Body2D b) =>
        a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

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
    /// <returns>
    /// Whether the response noticeably changed anything - a position correction (overlap deeper
    /// than <see cref="PhysicsConstants.ContactSlopCells"/>) or a velocity change above
    /// <see cref="PhysicsConstants.SolverVelocityTolerance"/> - used for the solver's convergence check.
    /// </returns>
    private static bool ResolveContact(
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
            return false;
        }

        var bodyShare = bodyInverseMass / totalInverseMass;
        var otherShare = otherInverseMass / totalInverseMass;

        // Position correction: push body and other directly apart along Normal, split by their
        // relative inverse mass. Overlap up to the contact slop is left alone (the contact is
        // still recorded below); deeper overlap is corrected back to half the slop, so the next
        // iteration does not find the same contact needing correction again - see
        // PhysicsConstants.ContactSlopCells.
        var changed = false;
        if (contact.Depth > PhysicsConstants.ContactSlopCells)
        {
            var correction = contact.Depth - PhysicsConstants.ContactSlopCells / 2;
            body.Position += contact.Normal * (correction * bodyShare);
            if (other is not null)
            {
                other.Position -= contact.Normal * (correction * otherShare);
            }

            changed = true;
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
            return changed;
        }

        var normalImpulse = -(1.0 + EffectiveRestitution(restitution, -normalRelativeSpeed)) * normalRelativeSpeed / totalInverseMass;
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
        // Friction drags the pair toward a shared surface speed. The body underneath (the support)
        // is judged by its actual movement, not its Velocity, which this very contact's friction
        // keeps cancelling. Which side is the support follows from the contact normal (pointing
        // up means other is below body), regardless of the order the pair was resolved in.
        var bodyTangentSource = body.Velocity;
        var otherTangentSource = otherVelocity;
        if (other is not null)
        {
            if (contact.Normal.Y < 0)
            {
                otherTangentSource = new Vector2D(otherContactTarget.ReferenceVelocityX, otherVelocity.Y);
            }
            else if (contact.Normal.Y > 0)
            {
                bodyTangentSource = new Vector2D(((Body2D)body).ReferenceVelocityX, body.Velocity.Y);
            }
        }

        // A walking/crawling player's foot friction is already the traction-limited walk force
        // (see PhysicsSystem.UpdateWalkForce, including its reaction on the support); applying
        // contact friction to that ground contact as well would count the same friction twice.
        var playerWalksOnContact =
            (contact.Normal.Y < 0 && IsFootLocomotingPlayer(body)) ||
            (contact.Normal.Y > 0 && other is not null && IsFootLocomotingPlayer(other));

        var frictionRelativeVelocity = bodyTangentSource - otherTangentSource;
        var tangentialRelativeSpeed = frictionRelativeVelocity.X * tangent.X + frictionRelativeVelocity.Y * tangent.Y;
        var normalImpulseMagnitudePerMass = Math.Abs(normalImpulse * bodyInverseMass);
        var newTangentialRelativeSpeed = playerWalksOnContact
            ? tangentialRelativeSpeed
            : ApplyCoulombFriction(tangentialRelativeSpeed, normalImpulseMagnitudePerMass, friction);
        var tangentialDelta = tangentialRelativeSpeed - newTangentialRelativeSpeed;
        // Friction also takes back the distance travelled this step with the velocity it removes
        // (see Body2D.UndoFrictionDisplacement), so a body held by enough grip does not creep.
        body.Velocity -= tangent * (tangentialDelta * bodyShare);
        ((Body2D)body).UndoFrictionDisplacement(-tangent.X * tangentialDelta * bodyShare);
        if (other is not null)
        {
            other.Velocity += tangent * (tangentialDelta * otherShare);
            ((Body2D)other).UndoFrictionDisplacement(tangent.X * tangentialDelta * otherShare);
        }

        RecordContact(body, otherContactTarget, contact.Normal, -normalRelativeSpeed);

        // Total velocity change this response applied to the pair, along and across the normal.
        var velocityChange = Math.Abs(normalImpulse) * totalInverseMass + Math.Abs(tangentialDelta);
        return changed || velocityChange > PhysicsConstants.SolverVelocityTolerance;
    }

    /// <summary>
    /// Whether <paramref name="body"/> is the player moving on its feet, whose
    /// ground friction is already its traction-limited walk force - see
    /// <see cref="World.Player2D.UsesFeetLocomotion"/>.
    /// </summary>
    private static bool IsFootLocomotingPlayer(IPhysicsBody body) =>
        body is Player2D { UsesFeetLocomotion: true };

    /// <summary>
    /// Records the reciprocal <see cref="ContactType"/> pair for a resolved <see cref="Contact"/>
    /// on both <paramref name="body"/> and <paramref name="other"/>, derived from
    /// <paramref name="normal"/>'s sign - e.g. <paramref name="normal"/> pointing up
    /// (<c>(0, -1)</c>, body moves up to separate) means <paramref name="other"/> is below
    /// <paramref name="body"/>: <see cref="ContactType.SurfaceBottom"/> on <paramref name="body"/>,
    /// <see cref="ContactType.SurfaceTop"/> on <paramref name="other"/>. Both sides also record
    /// <paramref name="impactSpeed"/>, the pre-response closing speed along the normal, keyed by
    /// their own contact side.
    /// </summary>
    private static void RecordContact(IPhysicsBody body, Body2D other, Vector2D normal, double impactSpeed = 0.0)
    {
        if (normal.Y < 0)
        {
            body.AddContact(ContactType.SurfaceBottom, other, impactSpeed);
            other.AddContact(ContactType.SurfaceTop, (Body2D)body, impactSpeed);
        }
        else if (normal.Y > 0)
        {
            body.AddContact(ContactType.SurfaceTop, other, impactSpeed);
            other.AddContact(ContactType.SurfaceBottom, (Body2D)body, impactSpeed);
        }
        else if (normal.X < 0)
        {
            body.AddContact(ContactType.SurfaceRight, other, impactSpeed);
            other.AddContact(ContactType.SurfaceLeft, (Body2D)body, impactSpeed);
        }
        else if (normal.X > 0)
        {
            body.AddContact(ContactType.SurfaceLeft, other, impactSpeed);
            other.AddContact(ContactType.SurfaceRight, (Body2D)body, impactSpeed);
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
        var maxFrictionDelta = Math.Max(friction, 0.0) * normalImpulseMagnitudePerMass;
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
    /// rectangle pair is additionally checked for character overlap (see
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
            if (TryFindContact(rectA, bRects, out var rectContact, ownerA, ownerB)
                && rectContact.Depth < bestPenetration)
            {
                bestPenetration = rectContact.Depth;
                contact = rectContact;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// <see cref="TryFindContact(IReadOnlyList{Rect}, IReadOnlyList{Rect}, out Contact, Body2D?, Body2D?)"/>
    /// for a single rectangle <paramref name="rectA"/> on the first side.
    /// </summary>
    private static bool TryFindContact(
        Rect rectA,
        IReadOnlyList<Rect> bRects,
        out Contact contact,
        Body2D? ownerA = null,
        Body2D? ownerB = null)
    {
        contact = default;
        var bestPenetration = double.MaxValue;
        var found = false;

        for (var i = 0; i < bRects.Count; i++)
        {
            var rectB = bRects[i];
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

        return found;
    }

    /// <summary>
    /// Confirms two bodies' rectangle overlap actually corresponds to their true rendered
    /// silhouettes touching: true when some non-empty character cell of A and some non-empty
    /// character cell of B overlap with positive area inside the overlap region. Each body's
    /// cells are unit squares anchored at its own continuous (fractional) Position, so the test
    /// is independent of where the bodies sit relative to the integer world lattice and of the
    /// direction they approach from. For a solid rectangular sprite this always agrees with the
    /// plain rectangle test.
    /// </summary>
    private static bool HasCharacterOverlap(Body2D ownerA, Body2D ownerB, Rect rectA, Rect rectB)
    {
        const double areaEpsilon = 1e-9;

        var left = Math.Max(rectA.Left, rectB.Left);
        var right = Math.Min(rectA.Right, rectB.Right);
        var top = Math.Max(rectA.Top, rectB.Top);
        var bottom = Math.Min(rectA.Bottom, rectB.Bottom);
        if (right - left <= areaEpsilon || bottom - top <= areaEpsilon)
        {
            return false;
        }

        var aChars = ownerA.Frame.Chars;
        var bChars = ownerB.Frame.Chars;
        var aEmpty = ownerA.Sprite.EmptyChar;
        var bEmpty = ownerB.Sprite.EmptyChar;
        var aHeight = aChars.GetLength(0);
        var aWidth = aChars.GetLength(1);
        var bHeight = bChars.GetLength(0);
        var bWidth = bChars.GetLength(1);
        var aPos = ownerA.Position;
        var bPos = ownerB.Position;

        var aYStart = Math.Max(0, (int)Math.Floor(top - aPos.Y));
        var aYEnd = Math.Min(aHeight, (int)Math.Ceiling(bottom - aPos.Y));
        var aXStart = Math.Max(0, (int)Math.Floor(left - aPos.X));
        var aXEnd = Math.Min(aWidth, (int)Math.Ceiling(right - aPos.X));

        for (var ay = aYStart; ay < aYEnd; ay++)
        {
            var cellTop = Math.Max(top, aPos.Y + ay);
            var cellBottom = Math.Min(bottom, aPos.Y + ay + 1);
            if (cellBottom - cellTop <= areaEpsilon)
            {
                continue;
            }

            var bYStart = Math.Max(0, (int)Math.Floor(cellTop - bPos.Y));
            var bYEnd = Math.Min(bHeight, (int)Math.Ceiling(cellBottom - bPos.Y));

            for (var ax = aXStart; ax < aXEnd; ax++)
            {
                if (aChars[ay, ax] == aEmpty)
                {
                    continue;
                }

                var cellLeft = Math.Max(left, aPos.X + ax);
                var cellRight = Math.Min(right, aPos.X + ax + 1);
                if (cellRight - cellLeft <= areaEpsilon)
                {
                    continue;
                }

                var bXStart = Math.Max(0, (int)Math.Floor(cellLeft - bPos.X));
                var bXEnd = Math.Min(bWidth, (int)Math.Ceiling(cellRight - bPos.X));

                for (var by = bYStart; by < bYEnd; by++)
                {
                    var overlapY = Math.Min(cellBottom, bPos.Y + by + 1) - Math.Max(cellTop, bPos.Y + by);
                    if (overlapY <= areaEpsilon)
                    {
                        continue;
                    }

                    for (var bx = bXStart; bx < bXEnd; bx++)
                    {
                        var overlapX = Math.Min(cellRight, bPos.X + bx + 1) - Math.Max(cellLeft, bPos.X + bx);
                        if (overlapX > areaEpsilon && bChars[by, bx] != bEmpty)
                        {
                            return true;
                        }
                    }
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
    private void ResolveAgainstMover(IPhysicsBody a, IPhysicsBody b) =>
        ResolveAgainstOtherBody(a, (Body2D)b, b, b.Velocity, GetInverseMass(b.Mass));
}


