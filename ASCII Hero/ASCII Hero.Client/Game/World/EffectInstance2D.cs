using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A purely cosmetic, non-collidable, non-physics body spawned to play a short visual effect clip
/// (e.g. a collectable's pickup fade, a killed enemy's "crumble" clip) and then either self-remove
/// or persist as a permanent decorative body. Implements none of <see cref="Physics.IPhysicsBody"/>,
/// <see cref="IHazardBody"/>, <see cref="ICollectableBody"/>, or <see cref="ICollectorBody"/>, which
/// is what makes it automatically invisible to every positive-capability-filtered loop in
/// <see cref="Physics.CollisionSystem"/> (moving bodies, hazards, collectables). It also always
/// sets <see cref="Body2D.IsPassable"/> = true in its constructor rather than exposing it as an
/// overridable placement key like every other static body's <c>Passable</c> - this type is never
/// authored in <c>_objects.ini</c> (it is only ever spawned directly by code), so there is no level
/// data to override it from, and a spawned effect must never block movement regardless.
/// </summary>
public class EffectInstance2D : Body2D
{
    /// <summary>Fallback lifetime, in seconds, used when the spawned clip has no configured frame duration (i.e. isn't animated).</summary>
    private const double DefaultLifetimeSeconds = 0.5;

    private double _remainingSeconds;

    /// <summary>
    /// Whether this instance keeps existing/rendering (holding its clip's last frame) once its
    /// lifetime timer reaches zero, instead of being removed from the world - used for a killed
    /// enemy's permanent husk. Defaults to false (an ordinary effect that plays once and vanishes).
    /// </summary>
    public bool PersistsAfterPlayback { get; set; }

    /// <summary>
    /// Whether this instance's lifetime has elapsed and, per <see cref="PersistsAfterPlayback"/>,
    /// it should now be removed from the world.
    /// </summary>
    public bool IsExpiredAndShouldBeRemoved { get; private set; }

    public EffectInstance2D()
    {
        IsStatic = true;
        IsPassable = true;
    }

    /// <summary>
    /// Assigns the loaded sprite asset/clip and world position for this effect, and computes its
    /// lifetime from the clip's own frame count/duration.
    /// </summary>
    public void Spawn(SpriteAsset sprite, string clipName, Vector2D position, bool persistsAfterPlayback = false)
    {
        SetFrame(sprite, clipName);
        Position = position;
        PersistsAfterPlayback = persistsAfterPlayback;

        var clip = sprite.GetClip(clipName);
        _remainingSeconds = clip.FrameDurationSeconds is { } frameDuration
            ? clip.Frames.Count * frameDuration
            : DefaultLifetimeSeconds;
    }

    /// <summary>
    /// Decrements this effect's remaining lifetime. When it reaches zero: if
    /// <see cref="PersistsAfterPlayback"/> is false, flags this instance for removal (see
    /// <see cref="IsExpiredAndShouldBeRemoved"/>); if true, just stops decrementing and leaves the
    /// body holding whatever frame its own animation last landed on. Does not duplicate
    /// <see cref="Body2D.AdvanceAnimation"/>'s frame-cycling, which keeps running unchanged via
    /// <see cref="Animation.AnimationSystem.Update"/>.
    /// </summary>
    public void Tick(double deltaSeconds)
    {
        if (_remainingSeconds <= 0)
        {
            return;
        }

        _remainingSeconds -= deltaSeconds;
        if (_remainingSeconds <= 0)
        {
            _remainingSeconds = 0;
            if (!PersistsAfterPlayback)
            {
                IsExpiredAndShouldBeRemoved = true;
            }
        }
    }
}
