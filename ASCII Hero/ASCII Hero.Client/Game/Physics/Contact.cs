using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>
/// A single resolved-geometry collision contact between one of a body's own collision rectangles
/// (<see cref="BodyRect"/>) and whichever rectangle it overlaps most deeply on the other side
/// (<see cref="OtherRect"/>) - see <see cref="CollisionSystem.TryFindContact"/>. Derived purely
/// from rectangle overlap geometry (which axis has the shallower overlap, and which side of that
/// axis is closer) - never from either body's velocity. A body simultaneously touching two
/// unrelated surfaces (e.g. resting on a floor while also pressed against a wall, the classic
/// "ball in a corner" case) is represented as two independent <see cref="Contact"/> values, one
/// per surface, each resolved along its own <see cref="Normal"/> - see
/// <see cref="CollisionSystem.ResolveContact"/>.
/// </summary>
/// <param name="Normal">
/// Unit vector pointing along the direction <see cref="BodyRect"/>'s owning body must move to
/// separate from the other side - e.g. a body resting on top of a solid has
/// <c>Normal == (0, -1)</c> (up, since -Y is up in this screen-space convention); a body pressed
/// against a solid's right-hand edge has <c>Normal == (1, 0)</c>. Exactly one component is
/// non-zero, since contacts here are always axis-aligned (rectangles, not arbitrary polygons).
/// </param>
/// <param name="Depth">
/// How far <see cref="BodyRect"/> and <see cref="OtherRect"/> overlap along <see cref="Normal"/>'s
/// axis - i.e. the minimum-translation-vector distance needed along that axis to just separate
/// the two rectangles. Always positive (a <see cref="Depth"/> of zero or overlap-free rectangles
/// mean no contact - see <see cref="CollisionSystem.TryFindContact"/>, which returns false rather
/// than a degenerate <see cref="Contact"/> in that case).
/// </param>
/// <param name="BodyRect">The specific one of the resolved-against body's own collision rectangles this contact applies to.</param>
/// <param name="OtherRect">The specific one of the other side's collision rectangles this contact applies to.</param>
public readonly record struct Contact(Vector2D Normal, double Depth, Rect BodyRect, Rect OtherRect);
