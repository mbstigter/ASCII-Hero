namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A passable ambient-medium volume (water, air and the like) whose medium moves in a fixed
/// direction - a river, a wind tunnel. It is a two-state object: while on, bodies immersed in it
/// are dragged along by the flow (see <see cref="Flow"/>); while off it behaves like a still medium.
/// A <see cref="Switch2D"/> sharing its <see cref="LinkName"/> can turn it on and off.
/// </summary>
public class FlowZone2D : TwoStateObject2D, ILinkTarget
{
    /// <summary>Name of the switch that drives this zone, from the placement's <c>Link</c> key. Null if it is not switch-operated.</summary>
    public string? LinkName { get; set; }

    /// <summary>Unit vector the medium flows along, from the placement's <c>FlowDirection</c> key.</summary>
    public Vector2D FlowDirection { get; set; }

    /// <summary>Speed the medium flows at in cells/second, from the placement's <c>FlowSpeed</c> key.</summary>
    public double FlowSpeed { get; set; }

    /// <summary>The velocity of the medium: <see cref="FlowDirection"/> times <see cref="FlowSpeed"/> while on, zero while off.</summary>
    public Vector2D Flow => IsOn
        ? new Vector2D(FlowDirection.X * FlowSpeed, FlowDirection.Y * FlowSpeed)
        : new Vector2D(0, 0);
}
