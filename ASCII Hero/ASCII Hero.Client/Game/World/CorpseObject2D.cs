namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// The body a defeated <see cref="DynamicHazard2D"/> leaves behind. It is a loose
/// <see cref="DynamicObject2D"/> - it falls under gravity, lands on solid terrain and comes to rest
/// there, so an airborne hazard's corpse drops to the floor instead of hanging in mid-air - that plays its
/// <c>EffectClip</c> once and keeps its last frame. It is purely decorative: moving bodies (the
/// player, hazards, throwables) pass through it, and it takes no part in hazard or collectable
/// contacts (see <see cref="Physics.CollisionSystem"/>). Static and kinematic hazards leave a plain
/// persistent <see cref="EffectInstance2D"/> instead, as they cannot fall.
/// </summary>
public sealed class CorpseObject2D : DynamicObject2D
{
    /// <summary>A corpse never changes pose: it keeps playing the clip it was spawned with.</summary>
    public override void UpdatePose()
    {
    }
}
