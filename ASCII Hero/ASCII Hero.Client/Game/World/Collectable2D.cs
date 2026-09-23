using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// An item the player can gather, backed by a loaded sprite asset. Marked <see cref="IsStatic"/>
/// like a platform (it doesn't move), but unlike a platform it is not solid: it is excluded from
/// platform-collision blocking in <see cref="Physics.CollisionSystem"/> and instead only
/// participates in the overlap pass that removes it from the world on contact.
/// </summary>
public class Collectable2D : Body2D, ICollectableBody, IEffectTrigger
{
    /// <summary>
    /// Optional clip name (on this instance's own <see cref="Body2D.Sprite"/>) to play as a
    /// cosmetic effect when this collectable is picked up. Null (the default) means no effect.
    /// </summary>
    public string? EffectClipName { get; set; }

    /// <summary>
    /// Whether this collectable's pickup effect (see <see cref="EffectClipName"/>) persists as a
    /// permanent decorative body once its clip finishes playing, instead of self-removing -
    /// mirrors <see cref="IKillableBody.EffectPersists"/> on hazards/enemies. Used by
    /// <see cref="CollectableType.Checkpoint"/> so its "reached" effect remains visible as a
    /// marker rather than fading away, unlike an ordinary pickup (e.g. a ring/star) whose effect
    /// is expected to vanish.
    /// </summary>
    public bool EffectPersists { get; set; }

    /// <summary>
    /// What picking this up actually does - see <see cref="CollectableType"/> and the branching in
    /// <see cref="Physics.CollisionSystem.ResolveHazardsAndCollectables"/>. Null means this
    /// placement's <c>Type</c> key was omitted or unrecognized in <c>{World}_objects.ini</c> -
    /// picking it up then does nothing (see <see cref="Physics.CollisionSystem"/>), rather than
    /// silently falling back to some default variant.
    /// </summary>
    public CollectableType? Type { get; set; }

    public Collectable2D()
    {
        IsStatic = true;
    }

    /// <summary>Assigns the loaded sprite asset/clip/frame, world position, and type for this instance.</summary>
    public void Spawn(SpriteAsset sprite, string clipName, int frameIndex, Vector2D position, CollectableType? type = null, int repeatCount = 1)
    {
        SetFrame(sprite, clipName, frameIndex, repeatCount);
        Position = position;
        Type = type;
    }
}
