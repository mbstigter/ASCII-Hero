namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A hazard (e.g. a toxic leaf or a patrolling snake) that moves and collides exactly like a
/// <see cref="DynamicObject2D"/> - including its optional patrol and directional pose - and
/// damages the player (or any moving body) on contact. Only the hazard/killable/effect
/// capabilities are added here, mirroring <see cref="StaticHazard2D"/> and
/// <see cref="KinematicHazard2D"/>.
/// </summary>
public class DynamicHazard2D : DynamicObject2D, IHazardBody, IEffectTrigger, IKillableBody
{
    /// <summary>
    /// Optional clip name (on this instance's own <see cref="Body2D.Sprite"/>) to play as a
    /// cosmetic effect on contact (e.g. a "crumble" clip when killed). Null (the default) means
    /// no effect.
    /// </summary>
    public string? EffectClipName { get; set; }

    /// <summary>
    /// Whether this instance can be "killed" (removed from the world) by a qualifying contact
    /// (landed on top of). Defaults to false, so existing levels that don't opt in are unaffected.
    /// </summary>
    public bool IsKillable { get; set; }

    /// <summary>Whether this instance's effect (if configured) persists as a permanent husk after a kill contact.</summary>
    public bool EffectPersists { get; set; }
}
