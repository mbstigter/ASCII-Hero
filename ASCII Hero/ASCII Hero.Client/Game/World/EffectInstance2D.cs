using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Rendering;

namespace ASCII_Hero.Client.Game.World;

/// <summary>
/// A purely cosmetic, non-collidable, non-physics body spawned to play a short visual effect clip
/// (e.g. a collectable's pickup fade, a killed hazard's "crumble" clip) and then either self-remove
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
    private double _remainingSeconds;
    private double _elapsedSeconds;

    /// <summary>
    /// The body this effect stays attached to (e.g. the player), re-positioned every frame by
    /// <see cref="Follow"/> at the clip's <see cref="SpriteClip.EffectOffsetX"/>/<see cref="SpriteClip.EffectOffsetY"/>.
    /// Null for an effect that stays where it was spawned (e.g. a collectable's pickup fade).
    /// </summary>
    public Body2D? FollowTarget { get; private set; }

    /// <summary>
    /// The tint color this effect currently applies to its <see cref="FollowTarget"/>, following
    /// the clip's <see cref="SpriteClip.TintColor"/>/<see cref="SpriteClip.TintAlternateColor"/>/
    /// <see cref="SpriteClip.TintBlinkSeconds"/>. Null when the clip has no tint, or during a
    /// blink phase that shows the body's normal colors.
    /// </summary>
    public char? CurrentTintColor
    {
        get
        {
            if (Clip.TintColor is not { } tint)
            {
                return null;
            }

            return Clip.TintBlinkSeconds is { } blinkSeconds && (int)(_elapsedSeconds / blinkSeconds) % 2 == 1
                ? Clip.TintAlternateColor
                : tint;
        }
    }

    /// <summary>
    /// Whether this instance keeps existing/rendering (holding its clip's last frame) once its
    /// lifetime timer reaches zero, instead of being removed from the world - used for a killed
    /// hazard's permanent husk. Defaults to false (an ordinary effect that plays once and vanishes).
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
    /// lifetime from the clip's own frame count/duration. With a <paramref name="followTarget"/>,
    /// the effect is positioned on that body (plus the clip's offset) instead, and keeps following it.
    /// </summary>
    public void Spawn(SpriteAsset sprite, string clipName, Vector2D position, bool persistsAfterPlayback = false, Body2D? followTarget = null)
    {
        SetFrame(sprite, clipName);
        Position = position;
        PersistsAfterPlayback = persistsAfterPlayback;
        FollowTarget = followTarget;
        Follow();

        var clip = sprite.GetClip(clipName);
        _remainingSeconds = clip.EffectDurationSeconds is { } effectDuration
            ? effectDuration
            : clip.FrameDurationSeconds is { } frameDuration
                ? clip.Frames.Count * frameDuration
                : GameDefaults.EffectLifetimeSeconds;
    }

    /// <summary>
    /// Moves this effect onto its <see cref="FollowTarget"/> (plus the clip's offset). No-op for
    /// an effect without a follow target.
    /// </summary>
    public void Follow()
    {
        if (FollowTarget is { } target)
        {
            Position = new Vector2D(target.Position.X + Clip.EffectOffsetX, target.Position.Y + Clip.EffectOffsetY);
        }
    }

    /// <summary>
    /// Decrements this effect's remaining lifetime (and advances its tint blink timer). When it reaches zero: if
    /// <see cref="PersistsAfterPlayback"/> is false, flags this instance for removal (see
    /// <see cref="IsExpiredAndShouldBeRemoved"/>); if true, just stops decrementing and leaves the
    /// body holding whatever frame its own animation last landed on. Does not duplicate
    /// <see cref="Body2D.AdvanceAnimation"/>'s frame-cycling, which keeps running unchanged via
    /// <see cref="AnimationSystem.Update"/>.
    /// </summary>
    public void Tick(double deltaSeconds)
    {
        _elapsedSeconds += deltaSeconds;
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
