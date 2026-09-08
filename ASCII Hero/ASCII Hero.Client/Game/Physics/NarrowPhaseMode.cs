namespace ASCII_Hero.Client.Game.Physics;

/// <summary>
/// Which narrow-phase test <see cref="CollisionSystem"/> uses to confirm/refine an AABB overlap
/// found during broad phase, once two bodies' bounding boxes are known to intersect.
/// </summary>
public enum NarrowPhaseMode
{
    /// <summary>
    /// The default: resolves collision using each body's derived collision rectangles (see
    /// <see cref="CollisionShapeBuilder"/>) - fast, and accurate for typical blocky ASCII shapes,
    /// but a shape whose actual non-empty characters don't fill the merged rectangle exactly
    /// (e.g. a diagonal or irregular silhouette) can report an overlap slightly before/after the
    /// true visible shapes actually touch.
    /// </summary>
    MultiRect,

    /// <summary>
    /// Additionally requires that at least one world cell within the two bodies' overlapping
    /// rectangles has a non-empty character on both sides (ported from the older ConsoleGame2D
    /// prototype's <c>CheckCharacterCollision</c> - see docs/Decisions.md) before accepting a
    /// rectangle-pair overlap as a real collision. This tests the true rendered silhouette rather
    /// than the merged rectangle approximation, at the cost of an extra per-cell character lookup
    /// for every candidate rectangle pair.
    /// </summary>
    CharacterGrid,
}
