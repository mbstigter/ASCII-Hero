using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A kinematic body (e.g. a patrolling moving platform) in the usual physics-engine sense: it
/// drives its own prescribed motion from <see cref="Velocity"/> every frame - never gravity or
/// force integration like <see cref="DynamicObject2D"/>/<see cref="MovingEnemy2D"/>, and never
/// input like <see cref="Player2D"/> - yet <see cref="Body2D.IsStatic"/> is true, so
/// <see cref="Physics.CollisionSystem"/> never itself corrects this body's own position/velocity
/// in response to a collision; it only ever affects the *other* side (see
/// <see cref="Body2D.IsStatic"/>'s own doc comment). This combination - static (immune to
/// collision response) plus a real <see cref="IPhysicsBody.Velocity"/> other bodies can react to -
/// is what lets a resting rider be carried by/dragged along a moving platform via the same
/// friction/restitution math used against ordinary stationary terrain (see
/// <see cref="Physics.CollisionSystem"/>'s reference-frame generalization), rather than needing a
/// bespoke "carry the rider" mechanism.
/// </summary>
public class KinematicObject2D : Body2D, IPhysicsBody
{
    /// <summary>
    /// How close (in world cells) this body must get to a configured patrol bound before
    /// reversing direction on that axis - mirrors <see cref="MovingEnemy2D"/>'s equivalent
    /// threshold, so a patrolling platform reverses just shy of its bound instead of oscillating
    /// exactly on it.
    /// </summary>
    private const double PatrolTurnThreshold = 0.25;

    private bool _patrolMovingTowardMaxX = true;
    private bool _patrolMovingTowardMaxY = true;

    /// <summary>Current velocity, in world cells per second. Constant unless patrolling or changed externally.</summary>
    public Vector2D Velocity { get; set; }

    /// <summary>Whether this object is currently resting on a platform or the world's floor.</summary>
    public bool IsGrounded { get; set; }

    /// <summary>
    /// Left bound of this body's horizontal patrol range, in world cells, or null if this body
    /// doesn't patrol on the X axis at all (its horizontal velocity component then simply stays
    /// whatever it was configured/spawned with - usually 0). Kept independent of the Y-axis
    /// bounds below so a future non-linear patrol shape (e.g. a rectangular circuit) can bounce
    /// each axis on its own schedule without redesigning this body.
    /// </summary>
    public double? PatrolMinX { get; private set; }

    /// <summary>Right bound of this body's horizontal patrol range, in world cells. See <see cref="PatrolMinX"/>.</summary>
    public double? PatrolMaxX { get; private set; }

    /// <summary>Top bound of this body's vertical patrol range, in world cells, or null if this body doesn't patrol on the Y axis. See <see cref="PatrolMinX"/>.</summary>
    public double? PatrolMinY { get; private set; }

    /// <summary>Bottom bound of this body's vertical patrol range, in world cells. See <see cref="PatrolMinY"/>.</summary>
    public double? PatrolMaxY { get; private set; }

    /// <summary>Speed (world cells/second) this body patrols at along the X axis, when <see cref="PatrolMinX"/>/<see cref="PatrolMaxX"/> are set.</summary>
    public double PatrolSpeedX { get; private set; }

    /// <summary>Speed (world cells/second) this body patrols at along the Y axis, when <see cref="PatrolMinY"/>/<see cref="PatrolMaxY"/> are set.</summary>
    public double PatrolSpeedY { get; private set; }

    public KinematicObject2D()
    {
        IsStatic = true;
    }

    /// <summary>Assigns the loaded sprite asset/clip/frame, initial position and constant velocity.</summary>
    public void Spawn(SpriteAsset sprite, string clipName, int frameIndex, Vector2D position, Vector2D velocity, int repeatCount = 1)
    {
        SetFrame(sprite, clipName, frameIndex, repeatCount);
        Position = position;
        Velocity = velocity;
    }

    /// <summary>
    /// Configures back-and-forth patrol on either or both axes independently - a null bound pair
    /// on an axis leaves that axis un-patrolled (its velocity component untouched by <see cref="Move"/>).
    /// </summary>
    /// <param name="patrolMinX">Left bound of the horizontal patrol range, or null to not patrol on X.</param>
    /// <param name="patrolMaxX">Right bound of the horizontal patrol range, or null to not patrol on X.</param>
    /// <param name="patrolSpeedX">Horizontal patrol speed, in world cells/second.</param>
    /// <param name="patrolMinY">Top bound of the vertical patrol range, or null to not patrol on Y.</param>
    /// <param name="patrolMaxY">Bottom bound of the vertical patrol range, or null to not patrol on Y.</param>
    /// <param name="patrolSpeedY">Vertical patrol speed, in world cells/second.</param>
    /// <param name="initialDirectionTowardMaxX">
    /// Which way to start heading on the X axis. If null (the default), starts heading toward
    /// whichever bound is farther from the spawn position, so a body spawned near one end still
    /// immediately patrols across the full range instead of instantly hitting the near bound and
    /// turning back within the first frame or two. An explicit value (true = toward
    /// <see cref="PatrolMaxX"/>, false = toward <see cref="PatrolMinX"/>) overrides that inference -
    /// e.g. so a level author can make two platforms that share the same range start in opposite
    /// phase, or start moving away from the player instead of toward them - mirrors
    /// <see cref="MovingEnemy2D.SetPatrol"/>'s own <c>initialDirectionRight</c> parameter.
    /// </param>
    /// <param name="initialDirectionTowardMaxY">
    /// Which way to start heading on the Y axis. Same semantics as
    /// <paramref name="initialDirectionTowardMaxX"/>, but for <see cref="PatrolMinY"/>/<see cref="PatrolMaxY"/>
    /// (true = toward <see cref="PatrolMaxY"/>, false = toward <see cref="PatrolMinY"/>).
    /// </param>
    public void SetPatrol(
        double? patrolMinX, double? patrolMaxX, double patrolSpeedX,
        double? patrolMinY, double? patrolMaxY, double patrolSpeedY,
        bool? initialDirectionTowardMaxX = null, bool? initialDirectionTowardMaxY = null)
    {
        PatrolMinX = patrolMinX;
        PatrolMaxX = patrolMaxX;
        PatrolSpeedX = patrolSpeedX;
        PatrolMinY = patrolMinY;
        PatrolMaxY = patrolMaxY;
        PatrolSpeedY = patrolSpeedY;

        // Start heading toward whichever bound is farther, so a body spawned near one end still
        // immediately patrols across the full range instead of instantly hitting the near bound
        // and turning back within the first frame or two - unless the caller explicitly requested
        // a starting direction instead - mirrors MovingEnemy2D.SetPatrol.
        if (patrolMinX is { } minX && patrolMaxX is { } maxX)
        {
            _patrolMovingTowardMaxX = initialDirectionTowardMaxX ?? (Position.X - minX <= maxX - Position.X);
        }

        if (patrolMinY is { } minY && patrolMaxY is { } maxY)
        {
            _patrolMovingTowardMaxY = initialDirectionTowardMaxY ?? (Position.Y - minY <= maxY - Position.Y);
        }
    }

    /// <summary>
    /// Advances this body's prescribed motion by one frame: recomputes each configured axis's
    /// velocity component toward its current target bound (reversing once within
    /// <see cref="PatrolTurnThreshold"/> of it), leaves any un-patrolled axis's velocity component
    /// untouched, then integrates <see cref="Body2D.Position"/> from the resulting
    /// <see cref="Velocity"/> - called once per frame by <see cref="Physics.PhysicsSystem"/>
    /// instead of the gravity/force-based integration every other moving body goes through.
    /// </summary>
    public void Move(double deltaSeconds)
    {
        var velocity = Velocity;

        if (PatrolMinX is { } minX && PatrolMaxX is { } maxX)
        {
            var targetX = _patrolMovingTowardMaxX ? maxX : minX;
            if (Math.Abs(Position.X - targetX) <= PatrolTurnThreshold)
            {
                _patrolMovingTowardMaxX = !_patrolMovingTowardMaxX;
                targetX = _patrolMovingTowardMaxX ? maxX : minX;
            }

            velocity.X = (targetX > Position.X ? 1.0 : targetX < Position.X ? -1.0 : 0.0) * PatrolSpeedX;
        }

        if (PatrolMinY is { } minY && PatrolMaxY is { } maxY)
        {
            var targetY = _patrolMovingTowardMaxY ? maxY : minY;
            if (Math.Abs(Position.Y - targetY) <= PatrolTurnThreshold)
            {
                _patrolMovingTowardMaxY = !_patrolMovingTowardMaxY;
                targetY = _patrolMovingTowardMaxY ? maxY : minY;
            }

            velocity.Y = (targetY > Position.Y ? 1.0 : targetY < Position.Y ? -1.0 : 0.0) * PatrolSpeedY;
        }

        Velocity = velocity;
        Position = new Vector2D(
            Position.X + velocity.X * deltaSeconds,
            Position.Y + velocity.Y * deltaSeconds);
    }
}
