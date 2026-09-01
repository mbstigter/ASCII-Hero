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
    /// <summary>Reused across frames to avoid an allocation every call for what is normally a tiny list.</summary>
    private readonly List<(IPhysicsBody Body, double Restitution)> _movingBodies = [];

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
            _movingBodies.Add((movingBody, GetRestitution(movingBody)));
        }

        // Solid terrain a moving body can stand on/collide against - any static body not marked
        // Body2D.IsPassable, checked directly and generically rather than by excluding specific
        // categories/types one at a time. Collectables and hazards default to passable (see
        // World2D.LoadAsync), a plain wall can opt into being passable too (e.g. a level-design
        // "secret passage"), and EffectInstance2D is always passable (see its own doc comment) -
        // none of that is special-cased here, it all flows through this one flag.
        var solids = world.Objects.Where(body => body.IsStatic && !body.IsPassable).ToList();

        foreach (var (body, restitution) in _movingBodies)
        {
            foreach (var solid in solids)
            {
                ResolveAgainstSolid(body, restitution, solid);
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

        foreach (var (body, restitution) in _movingBodies)
        {
            ResolveWorldBounds(world, body, restitution);
        }

        ResolveHazardsAndCollectables(world);
    }

    /// <summary>
    /// Bounciness used for collision response, generic over any concrete moving body: bodies that
    /// expose their own <c>Restitution</c> (<see cref="DynamicObject2D"/>, <see cref="MovingEnemy2D"/>)
    /// use it, everything else (the player, kinematic objects) stops dead (0.0).
    /// </summary>
    private static double GetRestitution(IPhysicsBody body) => body switch
    {
        DynamicObject2D dynamicObject => dynamicObject.Restitution,
        MovingEnemy2D movingEnemy => movingEnemy.Restitution,
        _ => 0.0,
    };

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
    private static void ResolveWorldBounds(World2D world, IPhysicsBody body, double restitution)
    {
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
    /// Finds the deepest-overlapping pair of (body rect, solid rect) and resolves just that
    /// one pair. A body's collision shape may be made up of several rectangles (e.g. derived
    /// from sprite data), so every rect on each side is checked. <paramref name="restitution"/>
    /// drives the velocity response uniformly: 0 stops the body dead (the player's case), while
    /// anything above 0 reflects velocity to varying degrees of bounce (dynamic objects). Despite
    /// the name, <paramref name="solid"/> is any immovable body from the <c>solids</c> list, not
    /// specifically a platform - a wall, crate, or any other static terrain resolves the same way.
    /// </summary>
    private static void ResolveAgainstSolid(IPhysicsBody body, double restitution, Body2D solid)
    {
        if (!TryFindDeepestOverlap(body.CollisionRects, solid.CollisionRects, out var deepestBodyRect, out var bestSolidRect))
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
                // Landing on top of the solid.
                var newRectBottom = bestSolidRect.Top;
                body.Position = new Vector2D(
                    body.Position.X,
                    newRectBottom - deepestBodyRect.Height - rectOffsetY);
                body.Velocity = new Vector2D(body.Velocity.X, -body.Velocity.Y * restitution);
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
            body.Velocity = new Vector2D(-body.Velocity.X * restitution, body.Velocity.Y);
        }
    }

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
    /// Resolves a collision between two moving bodies (e.g. the player and the bouncing ball) -
    /// the one pairing that previously fell through the cracks entirely, since each body was
    /// only ever checked against solids/world bounds, never against each other. Uses the same
    /// deepest-penetration approach as <see cref="ResolveAgainstSolid"/>, but since neither
    /// side here is immovable, the position correction is split evenly between both bodies and
    /// each body's velocity is reflected using its own restitution - consistent with how that
    /// same body already bounces off platforms and world bounds.
    /// </summary>
    private static void ResolveBodyPair((IPhysicsBody Body, double Restitution) a, (IPhysicsBody Body, double Restitution) b)
    {
        if (!TryFindDeepestOverlap(a.Body.CollisionRects, b.Body.CollisionRects, out var deepestRectA, out var bestRectB))
        {
            return;
        }

        var overlapLeftBest = deepestRectA.Right - bestRectB.Left;
        var overlapRightBest = bestRectB.Right - deepestRectA.Left;
        var overlapTopBest = deepestRectA.Bottom - bestRectB.Top;
        var overlapBottomBest = bestRectB.Bottom - deepestRectA.Top;

        var minHorizontal = Math.Min(overlapLeftBest, overlapRightBest);
        var minVertical = Math.Min(overlapTopBest, overlapBottomBest);

        if (minVertical < minHorizontal)
        {
            var half = minVertical / 2;
            if (overlapTopBest < overlapBottomBest)
            {
                // A's bottom rests on B's top - push A up and B down by half the overlap each.
                a.Body.Position = new Vector2D(a.Body.Position.X, a.Body.Position.Y - half);
                b.Body.Position = new Vector2D(b.Body.Position.X, b.Body.Position.Y + half);
                a.Body.IsGrounded = true;
            }
            else
            {
                // B's bottom rests on A's top.
                b.Body.Position = new Vector2D(b.Body.Position.X, b.Body.Position.Y - half);
                a.Body.Position = new Vector2D(a.Body.Position.X, a.Body.Position.Y + half);
                b.Body.IsGrounded = true;
            }

            a.Body.Velocity = new Vector2D(a.Body.Velocity.X, -a.Body.Velocity.Y * a.Restitution);
            b.Body.Velocity = new Vector2D(b.Body.Velocity.X, -b.Body.Velocity.Y * b.Restitution);
        }
        else
        {
            var half = minHorizontal / 2;
            if (overlapLeftBest < overlapRightBest)
            {
                // A's right edge overlaps B's left edge - push them apart horizontally.
                a.Body.Position = new Vector2D(a.Body.Position.X - half, a.Body.Position.Y);
                b.Body.Position = new Vector2D(b.Body.Position.X + half, b.Body.Position.Y);
            }
            else
            {
                b.Body.Position = new Vector2D(b.Body.Position.X - half, b.Body.Position.Y);
                a.Body.Position = new Vector2D(a.Body.Position.X + half, a.Body.Position.Y);
            }

            a.Body.Velocity = new Vector2D(-a.Body.Velocity.X * a.Restitution, a.Body.Velocity.Y);
            b.Body.Velocity = new Vector2D(-b.Body.Velocity.X * b.Restitution, b.Body.Velocity.Y);
        }
    }
}
