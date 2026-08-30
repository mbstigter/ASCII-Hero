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

        // Solid terrain a moving body can stand on/collide against. Both non-solid categories are
        // excluded here rather than being treated as platforms: collectables (never blocking,
        // just picked up) and hazards (even static ones like a toxic plant, which should be
        // walked into rather than stood on, consistent with a moving hazard like MovingEnemy2D).
        var solids = world.Objects.Where(body => body.IsStatic && body is not ICollectableBody and not IHazardBody).ToList();

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
    /// never consume collectables.
    /// </summary>
    private static void ResolveHazardsAndCollectables(World2D world)
    {
        var hazards = world.Objects.OfType<IHazardBody>().Cast<Body2D>().ToList();
        var collectables = world.Objects.OfType<ICollectableBody>().Cast<Body2D>().ToList();

        if (hazards.Count == 0 && collectables.Count == 0)
        {
            return;
        }

        foreach (var body in world.Objects)
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

                // TODO: apply damage once a health/damage system exists. Detection is generic
                // (any IPhysicsBody overlapping any IHazardBody); only the effect is not wired yet.
            }

            foreach (var collectable in collectables)
            {
                if (movingBody is ICollectorBody && Overlaps(movingBody, collectable))
                {
                    world.QueueRemoval(collectable);
                }
            }
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
        Rect2D? bestBodyRect = null;
        Rect2D bestSolidRect = default;
        var bestPenetration = double.MaxValue;

        foreach (var bodyRect in body.CollisionRects)
        {
            foreach (var solidRect in solid.CollisionRects)
            {
                if (!bodyRect.Overlaps(solidRect))
                {
                    continue;
                }

                var overlapLeft = bodyRect.Right - solidRect.Left;
                var overlapRight = solidRect.Right - bodyRect.Left;
                var overlapTop = bodyRect.Bottom - solidRect.Top;
                var overlapBottom = solidRect.Bottom - bodyRect.Top;

                var penetration = Math.Min(Math.Min(overlapLeft, overlapRight), Math.Min(overlapTop, overlapBottom));
                if (penetration < bestPenetration)
                {
                    bestPenetration = penetration;
                    bestBodyRect = bodyRect;
                    bestSolidRect = solidRect;
                }
            }
        }

        if (bestBodyRect is not { } deepestBodyRect)
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
        Rect2D? bestRectA = null;
        Rect2D bestRectB = default;
        var bestPenetration = double.MaxValue;

        foreach (var rectA in a.Body.CollisionRects)
        {
            foreach (var rectB in b.Body.CollisionRects)
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
                    bestRectA = rectA;
                    bestRectB = rectB;
                }
            }
        }

        if (bestRectA is not { } deepestRectA)
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
