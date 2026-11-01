namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A physics body that can hang and shimmy laterally from an <see cref="Body2D.IsHangable"/>
/// surface - held by grip (gravity stays on) while actually hanging. A small,
/// focused capability interface (see
/// rather than one combined interface). Only <see cref="Player2D"/> implements this today, but
/// nothing here is player-specific.
/// </summary>
public interface IHangerBody : IPhysicsBody
{
    /// <summary>
    /// Whether the body is currently overlapping an <see cref="Body2D.IsHangable"/> surface with
    /// its hands at the surface's level and slowly enough to grip (see <see cref="Physics.CollisionSystem"/>'s
    /// hand-level and grip-speed checks), regardless of whether it is actually hanging right now. Set
    /// each frame by <see cref="Physics.CollisionSystem"/>, consumed the following frame by
    /// <see cref="Physics.PhysicsSystem"/>, which engages <see cref="IsHanging"/> only on a
    /// deliberate grab (Up held while touching), like climbing.
    /// </summary>
    bool IsTouchingHangable { get; set; }

    /// <summary>
    /// Whether the body is actually hanging from a hangable right now - engaged by
    /// <see cref="Physics.PhysicsSystem"/> when Up is held while <see cref="IsTouchingHangable"/>, held until
    /// the body leaves the hangable surface or explicitly lets go (a second Down press from the
    /// fully-stretched pose - see <see cref="IsClambering"/> and the hang stance sequence in
    /// <see cref="Physics.PhysicsSystem.Step"/>).
    /// </summary>
    bool IsHanging { get; set; }

    /// <summary>
    /// Whether the body is hanging in the compact "clamber" pose (both hands and feet on the
    /// hangable, sprite reduced in vertical extent to fit through narrow spaces alongside it) rather
    /// than the regular fully-stretched hang. Only meaningful while <see cref="IsHanging"/>.
    /// Deliberately the inverse of the floor Walk/Crawl sense of up/down: while suspended, Up
    /// pulls into this compact pose (mirroring Crawl on the ground) and Down extends back out to
    /// the fully-stretched hang (mirroring Walk) - a further Down from there lets go entirely
    /// instead of crouching further, since fully stretched is already the least-attached pose.
    /// See the hang stance sequence in <see cref="Physics.PhysicsSystem.Step"/> for the full
    /// transition logic. Derived from the body's stance rather than stored separately, so the
    /// hanging and standing compact/stretched choices can never disagree.
    /// </summary>
    bool IsClambering { get; }
}
