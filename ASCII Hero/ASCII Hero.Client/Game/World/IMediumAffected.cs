namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A physics body that can optionally opt out of ambient-medium buoyancy/drag via
/// <see cref="MediumAffected"/>, mirroring <see cref="IGravityAffected"/> - lets
/// <see cref="Physics.PhysicsSystem"/> resolve <see cref="Body2D.CurrentMedium"/> and apply
/// buoyancy/drag generically to any such body instead of special-casing which concrete types have
/// the capability. Kinematic bodies deliberately do not implement this, for the same reason they
/// do not implement <see cref="IGravityAffected"/> - they move along a predefined path, never
/// affected by any force.
/// </summary>
public interface IMediumAffected : IPhysicsBody
{
    bool MediumAffected { get; }
}
