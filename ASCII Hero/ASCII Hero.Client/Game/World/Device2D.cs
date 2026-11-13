namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A machine-like object (fan, lamp, pump and the like) that is either off or on and shows a
/// different clip of its own sprite asset for each state - an animated "on" clip for a running
/// fan, say. It has no other behavior of its own: it is solid like any other static object, and
/// a <see cref="Switch2D"/> sharing its <see cref="LinkName"/> can turn it on and off. A device with
/// a moving surface (<c>ConveyorDirection</c>/<c>ConveyorSpeed</c>, e.g. a rope) only moves it while on;
/// without a <c>Link</c> it always moves, like an unlinked <see cref="KinematicObject2D"/>.
/// </summary>
public class Device2D : TwoStateObject2D, ILinkTarget
{
    /// <summary>Name of the switch that drives this device, from the placement's <c>Link</c> key. Null if it is not switch-operated.</summary>
    public string? LinkName { get; set; }

    /// <summary>The surface velocity applied while the device is on (zero for a device without a moving surface).</summary>
    public Vector2D RunningConveyorVelocity { get; set; }

    /// <summary>Applies <see cref="RunningConveyorVelocity"/> while on and no surface movement while off.</summary>
    protected override void OnStateChanged() => ConveyorVelocity = IsOn ? RunningConveyorVelocity : default;
}
