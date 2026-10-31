using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>
/// Gameplay consequences of hazard contacts and collectable overlaps detected by
/// <see cref="CollisionSystem"/>: kills, damage, lives, score, rings, keys, checkpoints,
/// level-end and death transitions, plus their sounds and effects. <see cref="CollisionSystem"/>
/// keeps the detection (solver surface contacts for solid hazards, overlap for passable hazards
/// and collectables) and the hazard-contact rising-edge history; this class decides only what
/// each interaction does.
/// </summary>
public static class InteractionHandler
{
    /// <summary>
    /// A killer body landed on a killable hazard: the hazard is queued for removal and its own
    /// effect (if configured) persists as a permanent husk when EffectPersists is set.
    /// </summary>
    public static void OnHazardKilled(World2D world, Body2D hazard, IKillableBody killable)
    {
        SpawnEffectIfConfigured(hazard, world, killable.EffectPersists);
        world.PlaySound(killable.KillSoundName);
        world.QueueRemoval(hazard);
    }

    /// <summary>
    /// First frame of a fresh, ordinary (non-killing) hazard contact: the moving body's own
    /// effect plays and, if the moving body is the player, the hazard's damage is deducted. If
    /// that deduction is what brings health to exactly 0 (a fresh death, not an already-dead
    /// player standing in a hazard), a Death transition starts: it costs a life, and GameLoop
    /// respawns (or ends the game) once the death effect has played.
    /// </summary>
    public static void OnHazardHit(World2D world, Body2D body, Body2D hazard)
    {
        if (body is Player2D { Health: > 0 })
        {
            world.PlaySound(hazard.SoundName);
        }

        if (body is Player2D { HazardHitEffectClipName: { } hazardHitClipName })
        {
            SpawnEffect(body, hazardHitClipName, world);
        }
        else
        {
            SpawnEffectIfConfigured(body, world);
        }

        if (body is Player2D player && player.Health > 0)
        {
            player.Health = Math.Max(0, player.Health - ((IHazardBody)hazard).Damage);
            if (player.Health <= 0)
            {
                player.Lives = Math.Max(0, player.Lives - 1);
                world.PendingTransition = PendingTransition.Death;
                if (player.DiedEffectClipName is { } diedEffectClipName)
                {
                    SpawnEffect(player, diedEffectClipName, world);
                }
            }
        }
    }

    /// <summary>
    /// A collector body overlaps a collectable. Only the collectable's own effect fires here
    /// (e.g. a ring's pickup fade) - the collector side is deliberately left silent so a
    /// collector's own EffectClipName (e.g. a player's hazard-hit spark) never fires on an
    /// unrelated pickup.
    /// </summary>
    public static void OnCollected(World2D world, IPhysicsBody movingBody, Body2D collectable)
    {
        if (collectable is not Collectable2D typedCollectable)
        {
            world.PlaySound(collectable.SoundName);
            SpawnEffectIfConfigured(collectable, world);
            world.QueueRemoval(collectable);
            return;
        }

        if (typedCollectable.Type is not null)
        {
            world.PlaySound(typedCollectable.SoundName);
        }

        // Every variant below is removed on pickup and spawns its own effect (if
        // configured), honoring EffectPersists the same way a killed hazard's
        // effect can persist as a husk in the hazard's place - so any collectable
        // variant can be turned into a permanent "used" marker via its placement's own
        // EffectPersists key, not just Checkpoint/LevelEnd.
        switch (typedCollectable.Type)
        {
            case CollectableType.Diamond:
                SpawnEffectIfConfigured(collectable, world, typedCollectable.EffectPersists);
                world.QueueRemoval(collectable);
                if (movingBody is Player2D diamondPlayer)
                {
                    diamondPlayer.Score += GameDefaults.DiamondScore;
                }
                break;

            case CollectableType.Ring:
                SpawnEffectIfConfigured(collectable, world, typedCollectable.EffectPersists);
                world.QueueRemoval(collectable);
                if (movingBody is Player2D ringPlayer)
                {
                    ringPlayer.Score += GameDefaults.RingScore;
                    ringPlayer.Rings++;
                    if (ringPlayer.Rings >= world.RingsPerLife)
                    {
                        ringPlayer.Rings = 0;
                        ringPlayer.Lives++;
                    }
                }
                break;

            case CollectableType.Checkpoint:
                // Same as Diamond/Ring, but records the respawn point instead of
                // adjusting a player stat, and - if the collector is the player - also
                // plays the player's own checkpoint-reached effect clip (e.g. "checkpointreached")
                // as a second, independent effect.
                SpawnEffectIfConfigured(collectable, world, typedCollectable.EffectPersists);
                world.QueueRemoval(collectable);
                if (movingBody is Player2D checkpointPlayer)
                {
                    world.RespawnPoint = collectable.Position;
                    if (checkpointPlayer.CheckpointReachedEffectClipName is { } checkpointReachedEffectClipName)
                    {
                        SpawnEffect(checkpointPlayer, checkpointReachedEffectClipName, world);
                    }
                }
                break;

            case CollectableType.LevelEnd:
                // Same as Diamond/Ring, but starts a LevelEnd transition instead of
                // adjusting a player stat: GameLoop lets the player's level-end effect and
                // the sound play before completing the level.
                SpawnEffectIfConfigured(collectable, world, typedCollectable.EffectPersists);
                world.QueueRemoval(collectable);
                if (movingBody is Player2D levelEndPlayer)
                {
                    if (levelEndPlayer.LevelEndReachedEffectClipName is { } levelEndReachedEffectClipName)
                    {
                        SpawnEffect(levelEndPlayer, levelEndReachedEffectClipName, world);
                    }

                    world.PendingTransition = PendingTransition.LevelEnd;
                }
                break;

            case CollectableType.Key:
                // Only the player can carry a key, and a key needs its Lock name (see
                // Key2D.LockName) - without either it is left in place, inert.
                if (collectable is not Key2D { LockName: { Length: > 0 } keyLockName } || movingBody is not Player2D keyPlayer)
                {
                    break;
                }

                keyPlayer.Keys.Add(keyLockName);
                SpawnEffectIfConfigured(collectable, world, typedCollectable.EffectPersists);
                world.QueueRemoval(collectable);
                break;

            case null:
                // This placement's Type key was omitted/unrecognized in {World}_objects.ini -
                // deliberately inert (no effect, not removed, no player state changed) so a
                // missing Type is visibly "broken" during testing rather than silently
                // behaving like some default variant.
                break;
        }
    }

    /// <summary>
    /// Spawns a cosmetic <see cref="EffectInstance2D"/> at <paramref name="body"/>'s current
    /// position if it implements <see cref="IEffectTrigger"/> with a non-null clip name
    /// configured; no-ops otherwise. <paramref name="persists"/> controls whether the spawned
    /// effect remains as a permanent decorative body after its clip finishes playing (used for a
    /// killed hazard's husk) instead of self-removing like an ordinary effect.
    /// </summary>
    private static void SpawnEffectIfConfigured(Body2D body, World2D world, bool persists = false)
    {
        if (body is IEffectTrigger { EffectClipName: { } clipName })
        {
            SpawnEffect(body, clipName, world, persists);
        }
    }

    /// <summary>
    /// Spawns a cosmetic <see cref="EffectInstance2D"/> playing <paramref name="clipName"/> (on
    /// <paramref name="body"/>'s own <see cref="Body2D.Sprite"/>) at <paramref name="body"/>'s
    /// current position - for an explicit, call-site-chosen clip rather than a body's own single
    /// configured <see cref="IEffectTrigger.EffectClipName"/>. Lets one body (e.g. the player)
    /// play different effects for different situations as independent, possibly-overlapping
    /// <see cref="EffectInstance2D"/> instances.
    /// </summary>
    private static void SpawnEffect(Body2D body, string clipName, World2D world, bool persists = false)
    {
        var effect = new EffectInstance2D();
        effect.Spawn(body.Sprite, clipName, body.Position, persists);
        world.Objects.Add(effect);
    }
}
