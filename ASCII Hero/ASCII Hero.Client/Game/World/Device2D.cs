namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A machine-like object (fan, lamp, pump and the like) that is either off or on and shows a
/// different clip of its own sprite asset for each state - an animated "on" clip for a running
/// fan, say. It has no other behavior of its own: it is solid like any other static object, and
/// a <see cref="Switch2D"/> sharing its <see cref="LinkName"/> can turn it on and off.
/// </summary>
public class Device2D : TwoStateObject2D, ILinkTarget
{
    /// <summary>Name of the switch that drives this device, from the placement's <c>Link</c> key. Null if it is not switch-operated.</summary>
    public string? LinkName { get; set; }
}
