using ASCII_Hero.Client.Game.Constants;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A loose object the player can pick up and throw (e.g. a rock or a tomato). It is an ordinary
/// <see cref="DynamicObject2D"/> - it falls, bounces and slides by its material and settles
/// like any other loose object - plus what makes it a throwable: which <see cref="Gear2D"/> slot it
/// is carried in, how it is thrown, and how much it hurts a hazard it hits.
/// </summary>
/// <remarks>
/// Lifecycle: lying loose, touching it picks it up (only when an enabled gear slot matches its
/// <see cref="GearName"/>); it leaves the world and is kept, as this same instance, in that
/// <see cref="Gear2D"/>. <see cref="World2D.TryThrow"/> puts it back into the world in flight
/// (<see cref="IsThrown"/>). In flight it hurts a killable hazard it hits hard enough, and cannot be
/// picked up. Once it comes to rest it is lying loose again - or, when not <see cref="Reusable"/>,
/// it is used up (also on its first hazard hit). See <see cref="Physics.CollisionSystem"/>.
/// </remarks>
public class ThrowableObject2D : DynamicObject2D, IEffectTrigger
{
    /// <summary>Name of the gear slot (see <see cref="Gear2D.Name"/>) this object is carried in, from the placement's <c>Gear</c> key.</summary>
    public string GearName { get; set; } = string.Empty;

    /// <summary>Health removed from a killable hazard this object hits in flight (the <c>Damage</c> key).</summary>
    public double Damage { get; set; } = GameDefaults.DefaultThrowableDamage;

    /// <summary>Minimum impact speed (cells/second) at which a hit hurts a hazard (the <c>HitSpeed</c> key).</summary>
    public double HitSpeed { get; set; } = GameDefaults.DefaultThrowableHitSpeed;

    /// <summary>
    /// Whether this object can be picked up again after a throw (a rock, a ball) rather than being
    /// used up (a tomato) - the <c>Reusable</c> key, default true.
    /// </summary>
    public bool Reusable { get; set; } = true;

    /// <summary>Launch speed (cells/second) of a throw, before the thrower's own velocity is added (the <c>ThrowSpeed</c> key).</summary>
    public double ThrowSpeed { get; set; } = GameDefaults.DefaultThrowSpeed;

    /// <summary>Launch angle in degrees above horizontal (the <c>ThrowAngle</c> key).</summary>
    public double ThrowAngleDegrees { get; set; } = GameDefaults.DefaultThrowAngleDegrees;

    /// <summary>Optional sound played on a meaningful physical impact, from the <c>HitSound</c> key.</summary>
    public string? HitSoundName { get; set; }

    /// <summary>Optional sound played when it is thrown, from the <c>UseSound</c> key.</summary>
    public string? UseSoundName { get; set; }

    /// <summary>
    /// Optional clip (on its own sprite) played where a single-use object is used up (the
    /// <c>EffectClip</c> key), e.g. a splat.
    /// </summary>
    public string? EffectClipName { get; set; }

    /// <summary>
    /// Whether it is in flight after a throw: it can hurt hazards and cannot be picked up. Cleared
    /// once it comes to rest.
    /// </summary>
    public bool IsThrown { get; private set; }

    /// <summary>
    /// Puts this object back into play as a throw from <paramref name="position"/> at
    /// <paramref name="velocity"/>. It is awake and starts measuring its motion from here, so the jump
    /// from wherever it was last is not mistaken for movement.
    /// </summary>
    public void Launch(Vector2D position, Vector2D velocity)
    {
        Position = position;
        Velocity = velocity;
        IsThrown = true;
        Wake();
        MarkResolvedPosition();
    }

    /// <summary>
    /// Whether a thrown object has come to rest: asleep, or grounded and slower than
    /// <see cref="GameDefaults.ThrowableRestSpeed"/>.
    /// </summary>
    public bool HasComeToRest =>
        IsSleeping || (IsGrounded && Math.Abs(Velocity.X) < GameDefaults.ThrowableRestSpeed && Math.Abs(Velocity.Y) < GameDefaults.ThrowableRestSpeed);

    /// <summary>Ends the flight: from now on it lies loose and can be picked up again.</summary>
    public void Land() => IsThrown = false;
}
