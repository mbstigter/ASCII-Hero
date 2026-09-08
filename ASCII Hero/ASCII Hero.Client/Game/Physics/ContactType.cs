namespace ASCII_Hero.Client.Game.Physics;

/// <summary>
/// Describes which side of a body a resolved collision contact was found on this frame, or what
/// kind of special surface it touched (<see cref="Ladder"/>/<see cref="Bar"/>). Recorded on both
/// sides of a resolved contact by <see cref="CollisionSystem"/> - e.g. a body resting on top of a
/// solid gets <see cref="SurfaceBottom"/> recorded on itself (something is below it) while the
/// solid gets <see cref="SurfaceTop"/> recorded on itself (something is above it). Ported in spirit
/// from the older ConsoleGame2D prototype's own <c>ContactType</c>, which tracked contacts the same
/// way rather than re-deriving "which side" from raw overlap geometry every frame - see
/// docs/Decisions.md for why this replaces the old ambiguous overlap-depth-only axis inference.
/// </summary>
[Flags]
public enum ContactType
{
    /// <summary>No resolved contact.</summary>
    None = 0,

    /// <summary>Something is directly above this body (this body's top edge is contacted).</summary>
    SurfaceTop = 1 << 0,

    /// <summary>Something is directly below this body (this body's bottom edge is contacted) - the signal <see cref="IPhysicsBody.IsGrounded"/> is derived from.</summary>
    SurfaceBottom = 1 << 1,

    /// <summary>Something is directly to this body's left (this body's left edge is contacted).</summary>
    SurfaceLeft = 1 << 2,

    /// <summary>Something is directly to this body's right (this body's right edge is contacted).</summary>
    SurfaceRight = 1 << 3,

    /// <summary>Overlapping a climbable surface (see <see cref="World.Body2D.IsClimbable"/>).</summary>
    Ladder = 1 << 4,

    /// <summary>Overlapping a hangable surface (see <see cref="World.Body2D.IsHangable"/>).</summary>
    Bar = 1 << 5,
}
