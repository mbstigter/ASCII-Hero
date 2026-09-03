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

    /// <summary>Reused across frames to avoid an allocation every call for what is normally a tiny list.</summary>
    private readonly List<IPhysicsBody> _movingBodies = [];

    /// <summary>
    /// Hazard/body contact pairs still overlapping as of the frame just resolved. Used so an
    /// ordinary (non-kill) hazard contact's effect fires only on the first frame of a new contact
    /// - a "rising edge" - rather than every single frame the two remain overlapping. Unlike solid
    /// terrain, a hazard never physically pushes a body back out (see the <c>solids</c> filter in
    /// <see cref="Resolve"/>), so a body can rest against/inside one for many consecutive frames.
    /// </summary>
    private HashSet<(Body2D Hazard, IPhysicsBody Body)> _activeHazardContacts = [];

    public void Resolve(World2D world)
    {
        _movingBodies.Clear();

        foreach (var body in world.Objects)
        {
            if (body is not IPhysicsBody movingBody)
            {
                continue;
            }

            movingBody.IsGrounded = false;
            _movingBodies.Add(movingBody);
        }

        ResolveClimbingAndHanging(world);

        // Solid terrain a moving body can stand on/collide against - any static body not marked
        // Body2D.IsPassable, checked directly and generically rather than by excluding specific
        // categories/types one at a time. Collectables and hazards default to passable (see
        // World2D.LoadAsync), a plain wall can opt into being passable too (e.g. a level-design
        // "secret passage"), and EffectInstance2D is always passable (see its own doc comment) -
        // none of that is special-cased here, it all flows through this one flag.
        var solids = world.Objects.Where(body => body.IsStatic && !body.IsPassable).ToList();

        foreach (var body in _movingBodies)
        {
            foreach (var solid in solids)
            {
                ResolveAgainstSolid(body, solid);
            }
        }

        // Every moving body can also collide with every other moving body (e.g. the player and
        // the bouncing ball) - checked once per unordered pair.
        for (var i = 0; i < _movingBodies.Count; i++)
        {
            for (var j = i + 1; j < _movingBodies.Count; j++)
            {
                ResolveBodyPair(_movingBodies[i], _movingBodies[j]);
            }
        }

        foreach (var body in _movingBodies)
        {
            ResolveWorldBounds(world, body);
        }


        ResolveHazardsAndCollectables(world);
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
            body.IsGrounded = true;
        }

        body.Position = position;
        body.Velocity = velocity;
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
    /// terrain resolves the same way.
    /// </summary>
    private static void ResolveAgainstSolid(IPhysicsBody body, Body2D solid)
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
        var rectCount = body.CollisionRects.Count;
        for (var rectIndex = 0; rectIndex < rectCount; rectIndex++)
        {
            ResolveRectAgainstSolid(body, restitution, friction, solid, body.CollisionRects[rectIndex]);
        }
    }

    /// <summary>
    /// Resolves a single one of the body's own collision rectangles against whichever of the
    /// solid's rectangles it overlaps most deeply, then pushes the body out along the
    /// shallower-penetration axis, same as the original single-rect logic. Called once per body
    /// rect by <see cref="ResolveAgainstSolid"/> so a multi-rect body (e.g. the player's
    /// head+torso shape) gets every part of itself pushed fully clear of the solid.
    /// </summary>
    private static void ResolveRectAgainstSolid(IPhysicsBody body, double restitution, double friction, Body2D solid, Rect2D bodyRect)
    {
        if (!TryFindDeepestOverlap([bodyRect], solid.CollisionRects, out var deepestBodyRect, out var bestSolidRect))
        {
            return;
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

        if (minVertical < minHorizontal)
        {
            if (overlapTopBest < overlapBottomBest)
            {
                // Landing on top of the solid. Friction damps the tangential (horizontal)
                // velocity component every frame the body rests here - see ApplyFriction - so a
                // grippy surface (e.g. Concrete) settles sliding motion faster than a slick one.
                var newRectBottom = bestSolidRect.Top;
                body.Position = new Vector2D(
                    body.Position.X,
                    newRectBottom - deepestBodyRect.Height - rectOffsetY);
                body.Velocity = new Vector2D(ApplyFriction(body.Velocity.X, friction), -body.Velocity.Y * restitution);
                body.IsGrounded = true;
            }
            else
            {
                // Hitting the underside of the solid.
                body.Position = new Vector2D(
                    body.Position.X,
                    bestSolidRect.Bottom - rectOffsetY);
                body.Velocity = new Vector2D(body.Velocity.X, -body.Velocity.Y * restitution);
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
            body.Velocity = new Vector2D(-body.Velocity.X * restitution, ApplyFriction(body.Velocity.Y, friction));
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
    private static bool TryFindDeepestOverlap(
        IReadOnlyList<Rect2D> aRects,
        IReadOnlyList<Rect2D> bRects,
        out Rect2D bestA,
        out Rect2D bestB)
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
    private static void ResolveBodyPair(IPhysicsBody a, IPhysicsBody b)
    {
        if (!TryFindDeepestOverlap(a.CollisionRects, b.CollisionRects, out var deepestRectA, out var bestRectB))
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
                a.IsGrounded = true;
            }
            else
            {
                // B's bottom rests on A's top.
                b.Position = new Vector2D(b.Position.X, b.Position.Y - minVertical * bShare);
                a.Position = new Vector2D(a.Position.X, a.Position.Y + minVertical * aShare);
                b.IsGrounded = true;
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


