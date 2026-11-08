namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A physics body that can optionally opt out of ambient-medium buoyancy/drag via
/// <see cref="MediumAffected"/>, mirroring <see cref="IGravityAffected"/> - lets
/// <see cref="Physics.PhysicsSystem"/> apply buoyancy/drag generically to any such body instead of
/// special-casing which concrete types have the capability. (<see cref="Body2D.CurrentMedium"/>
/// itself is resolved for every force-integrated body; this only decides whether the medium
/// pushes on it.) Kinematic bodies deliberately do not implement this, for the same reason they
/// do not implement <see cref="IGravityAffected"/> - they move along a predefined path, never
/// affected by any force.
/// </summary>
public interface IMediumAffected : IPhysicsBody
{
    /// <summary>Whether buoyancy and drag from the surrounding medium act on this body.</summary>
    bool MediumAffected { get; }
}
