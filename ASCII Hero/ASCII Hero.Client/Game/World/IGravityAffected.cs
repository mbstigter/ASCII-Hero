namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A physics body that can optionally opt out of gravity via <see cref="UseGravity"/>, letting
/// <see cref="Physics.PhysicsSystem"/> apply gravity generically to any such body instead of
/// special-casing which concrete types have the flag. Kinematic bodies deliberately do not
/// implement this - they move along a predefined path and are never affected by gravity at all,
/// so a boolean toggle would be a meaningless member for them to carry.
/// </summary>
public interface IGravityAffected : IPhysicsBody
{
    bool UseGravity { get; }
}
