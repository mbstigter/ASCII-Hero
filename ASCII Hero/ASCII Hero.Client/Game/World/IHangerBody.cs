namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A physics body that can hang and shimmy laterally from an <see cref="Body2D.IsHangable"/>
/// surface (e.g. a pipe/rope) - gravity suspended while actually hanging. A small, focused
/// capability interface (see <see cref="IClimberBody"/>'s doc comment for why this is separate
/// rather than one combined interface). Only <see cref="Player2D"/> implements this today, but
/// nothing here is player-specific.
/// </summary>
public interface IHangerBody : IPhysicsBody
{
    /// <summary>
    /// Whether the body is currently overlapping an <see cref="Body2D.IsHangable"/> surface from
    /// a qualifying direction/speed (see <see cref="Physics.CollisionSystem"/>'s generic
    /// edge-approach snapping check), regardless of whether it is actually hanging right now. Set
    /// each frame by <see cref="Physics.CollisionSystem"/>, consumed the following frame by
    /// <see cref="Physics.PhysicsSystem"/> to engage <see cref="IsHanging"/> automatically -
    /// grabbing on is not a separate deliberate input, unlike climbing.
    /// </summary>
    bool IsTouchingHangable { get; set; }

    /// <summary>
    /// Whether the body is actually hanging from a pipe/rope right now - engaged by
    /// <see cref="Physics.PhysicsSystem"/> as soon as <see cref="IsTouchingHangable"/>, held until
    /// the body leaves the hangable surface or explicitly lets go (a second Down press from the
    /// fully-stretched pose - see <see cref="IsClambering"/> and the hang stance ladder in
    /// <see cref="Physics.PhysicsSystem.Step"/>).
    /// </summary>
    bool IsHanging { get; set; }

    /// <summary>
    /// Whether the body is hanging in the compact "clamber" pose (both hands and feet on the
    /// rope, sprite reduced in vertical extent to fit through narrow spaces alongside it) rather
    /// than the regular fully-stretched hang. Only meaningful while <see cref="IsHanging"/>.
    /// Deliberately the inverse of the floor Walk/Crawl sense of up/down: while suspended, Up
    /// pulls into this compact pose (mirroring Crawl on the ground) and Down extends back out to
    /// the fully-stretched hang (mirroring Walk) - a further Down from there lets go entirely
    /// instead of crouching further, since fully stretched is already the least-attached pose.
    /// See the hang stance ladder in <see cref="Physics.PhysicsSystem.Step"/> for the full
    /// transition logic.
    /// </summary>
    bool IsClambering { get; set; }

    /// <summary>
    /// Debounce set by <see cref="Physics.PhysicsSystem"/> the instant the body deliberately
    /// jumps/swings off or lets go of a hangable surface, held until it actually clears the
    /// surface's overlap entirely. Consulted by <see cref="Physics.CollisionSystem"/> so it
    /// doesn't immediately re-catch/re-snap the body the very same frame it let go - overlap with
    /// the pipe/rope typically still exists for a frame or two after the jump/release, since the
    /// body has only just started moving away from it (mirroring the equivalent climb debounce
    /// for ladders, for the same underlying reason).
    /// </summary>
    bool SuppressHangUntilClear { get; set; }
}
