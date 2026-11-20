using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>
/// Gameplay consequences of hazard contacts and collectable overlaps detected by
/// <see cref="CollisionSystem"/>: kills, damage, lives, score, rings, keys, checkpoints,
/// world-end and death transitions, plus their sounds and effects. <see cref="CollisionSystem"/>
/// keeps the detection (solver surface contacts for solid hazards, overlap for passable hazards
/// and collectables) and the hazard-contact rising-edge history; this class decides only what
/// each interaction does.
/// </summary>
public static class InteractionHandler
{
    /// <summary>The player sprite's [Sounds] key played on a hard landing (see <see cref="OnHardLanding"/>).</summary>
    public const string HardLandingSoundKey = "HardLanding";

    /// <summary>
    /// A killable hazard's health ran out: it is queued for removal and its own effect (if
    /// configured) persists as a permanent husk when EffectPersists is set. A moving
    /// <see cref="DynamicHazard2D"/> leaves that husk as a <see cref="CorpseObject2D"/> that falls to
    /// the floor; any other hazard leaves a persistent <see cref="EffectInstance2D"/> where it was.
    /// </summary>
    public static void OnHazardKilled(World2D world, Body2D hazard, IKillableBody killable)
    {
        if (killable.EffectPersists && hazard is DynamicHazard2D { EffectClipName: { } corpseClipName } dynamicHazard)
        {
            SpawnCorpse(world, dynamicHazard, corpseClipName);
        }
        else
        {
            SpawnEffectIfConfigured(hazard, world, killable.EffectPersists);
        }

        world.PlaySound(killable.KillSoundName);
        world.QueueRemoval(hazard);
    }

    /// <summary>
    /// A killer body landed on a killable hazard hard enough: the hazard loses the killer's
    /// <see cref="IKillerBody.StompDamage"/> (see <see cref="DamageHazard"/>). When the hazard
    /// survives, the killer bounces off it (<see cref="GameDefaults.StompBounceSpeed"/>) instead of
    /// staying on top of it.
    /// </summary>
    public static void OnHazardStomped(World2D world, IKillerBody killer, Body2D hazard, IKillableBody killable)
    {
        if (!DamageHazard(world, hazard, killable, killer.StompDamage))
        {
            killer.Velocity = new Vector2D(killer.Velocity.X, Math.Min(killer.Velocity.Y, -GameDefaults.StompBounceSpeed));
        }
    }

    /// <summary>
    /// A thrown object hit a killable hazard hard enough: its <c>Sound</c> plays, the hazard loses
    /// the object's <see cref="ThrowableObject2D.Damage"/> (see <see cref="DamageHazard"/>), and a
    /// single-use object is used up. A reusable one keeps bouncing.
    /// </summary>
    public static void OnHazardStruck(World2D world, ThrowableObject2D thrown, Body2D hazard, IKillableBody killable)
    {
        world.PlaySound(thrown.SoundName);
        DamageHazard(world, hazard, killable, thrown.Damage);
        if (!thrown.Reusable)
        {
            UseUp(world, thrown);
        }
    }

    /// <summary>
    /// Hurts a killable hazard the same way a hazard hurts the player (see <see cref="OnHazardHit"/>):
    /// nothing happens during its safe window; otherwise it loses <paramref name="damage"/>. If that
    /// empties its health it is killed (<see cref="OnHazardKilled"/>, permanently); if it survives, its
    /// hurt sound and effect play and its own safe window
    /// (<see cref="IHurtableBody.Recovery"/>) starts. Returns whether it was killed.
    /// </summary>
    public static bool DamageHazard(World2D world, Body2D hazard, IKillableBody killable, double damage)
    {
        if (killable.Recovery.IsActive || killable.Health <= 0)
        {
            return false;
        }

        killable.Health = Math.Max(0, killable.Health - damage);
        if (killable.Health <= 0)
        {
            OnHazardKilled(world, hazard, killable);
            return true;
        }

        killable.Recovery.Start();
        world.PlaySound(killable.HurtSoundName);
        if (killable.HurtEffectClipName is { } hurtClipName)
        {
            SpawnEffect(hazard, hurtClipName, world, follow: true);
        }

        return false;
    }

    /// <summary>
    /// A thrown object came to rest: a reusable one now lies loose, ready to be picked up again;
    /// a single-use one is used up.
    /// </summary>
    public static void OnThrowableCameToRest(World2D world, ThrowableObject2D thrown)
    {
        thrown.Land();
        if (!thrown.Reusable)
        {
            UseUp(world, thrown);
        }
    }

    /// <summary>
    /// A collector touched a loose throwable. The player takes it into the gear slot matching its
    /// <see cref="ThrowableObject2D.GearName"/>, playing its pickup sound; without such a slot (or for
    /// any other collector) it stays where it is, an ordinary loose object.
    /// </summary>
    public static void OnThrowableTouched(World2D world, IPhysicsBody collector, ThrowableObject2D throwable)
    {
        if (collector is not Player2D player || player.FindGear(throwable.GearName) is not { } gear)
        {
            return;
        }

        gear.Add(throwable);
        world.PlaySound(throwable.PickupSoundName);
        world.QueueRemoval(throwable);
    }

    /// <summary>A single-use throwable is used up: its effect (e.g. a splat) plays where it is and it leaves the world.</summary>
    private static void UseUp(World2D world, ThrowableObject2D thrown)
    {
        SpawnEffectIfConfigured(thrown, world);
        world.QueueRemoval(thrown);
    }

    /// <summary>
    /// First frame of a fresh, ordinary (non-killing) hazard contact: if the moving body is the
    /// player, the hazard's damage is deducted first, and the player's hurt effect only plays if it
    /// survived - a fatal hit shows just the died effect. A survived hit also starts a short safe
    /// window (<see cref="Player2D.Recovery"/>) in which hazards can't hurt it again.
    /// A player already out of health (e.g. touching a hazard during the death delay) gets no sound or effect. If the deduction brings health to
    /// exactly 0, a Death transition starts: it costs a life, and GameLoop respawns (or ends the
    /// game) once the death effect has played. Any other moving body just plays its own effect.
    /// </summary>
    public static void OnHazardHit(World2D world, Body2D body, Body2D hazard)
    {
        if (body is not Player2D player)
        {
            SpawnEffectIfConfigured(body, world);
            return;
        }

        if (player.Health <= 0)
        {
            return;
        }

        world.PlaySound(hazard.SoundName);
        if (!DamagePlayer(world, player, ((IHazardBody)hazard).Damage))
        {
            player.Recovery.Start();
            if (player.HurtEffectClipName is { } hurtClipName)
            {
                SpawnEffect(player, hurtClipName, world);
            }
        }
    }

    /// <summary>
    /// The player landed hard (see <see cref="Player2D.FallDamageImpactSpeed"/>): it plays the
    /// player sprite's "HardLanding" sound and loses <see cref="Player2D.FallDamage"/>, which can
    /// cost a life just like a hazard hit. The hurt effect only plays if the player survived - a
    /// fatal landing shows just the died effect. Unlike a hazard hit there is no other body to
    /// supply the sound, so it comes from the player's own [Sounds] section.
    /// </summary>
    public static void OnHardLanding(World2D world, Player2D player)
    {
        if (player.Health <= 0)
        {
            return;
        }

        if (player.Sprite.PoseSounds.TryGetValue(HardLandingSoundKey, out var hardLandingSound))
        {
            world.PlaySound(hardLandingSound);
        }

        if (!DamagePlayer(world, player, player.FallDamage)
            && player.HurtEffectClipName is { } hurtClipName)
        {
            SpawnEffect(player, hurtClipName, world);
        }
    }

    /// <summary>
    /// Deducts
    /// what empties the bar (a fresh death, not an already-dead player), a Death transition starts:
    /// it costs a life, plays the died effect, and GameLoop respawns (or ends the game) once that
    /// effect has played. Returns whether this hit killed the player, so callers skip the hurt effect.
    /// </summary>
    private static bool DamagePlayer(World2D world, Player2D player, double damage)
    {
        if (player.Health <= 0)
        {
            return false;
        }

        player.Health = Math.Max(0, player.Health - damage);
        if (player.Health > 0)
        {
            return false;
        }

        player.Lives = Math.Max(0, player.Lives - 1);
        world.PendingTransition = PendingTransition.Death;
        if (player.DiedEffectClipName is { } diedEffectClipName)
        {
            SpawnEffect(player, diedEffectClipName, world);
        }

        return true;
    }

    /// <summary>
    /// A collector body overlaps a collectable. Only the collectable's own effect fires here
    /// (e.g. a ring's pickup fade) - the collector side is deliberately left silent so a
    /// collector's own EffectClipName (e.g. a player's hurt spark) never fires on an
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

        // A health pack is only taken by a player who actually needs it; otherwise it stays in
        // place without sound or effect, as if it was never touched.
        if (typedCollectable.Type == CollectableType.HealthPack
            && movingBody is not Player2D { Health: < GameDefaults.PlayerFullHealth })
        {
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
        // EffectPersists key, not just Checkpoint/WorldEnd.
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

            case CollectableType.WorldEnd:
                // Same as Diamond/Ring, but starts a WorldEnd transition instead of
                // adjusting a player stat: GameLoop lets the player's world-end effect and
                // the sound play before completing the world.
                SpawnEffectIfConfigured(collectable, world, typedCollectable.EffectPersists);
                world.QueueRemoval(collectable);
                if (movingBody is Player2D worldEndPlayer)
                {
                    if (worldEndPlayer.WorldEndReachedEffectClipName is { } worldEndReachedEffectClipName)
                    {
                        SpawnEffect(worldEndPlayer, worldEndReachedEffectClipName, world);
                    }

                    world.PendingTransition = PendingTransition.WorldEnd;
                }
                break;

            case CollectableType.Key:
                // Only the player can carry a key, in its Key gear slot, and a key needs its Lock
                // name (see Key2D.LockName) - without any of these it is left in place, inert.
                if (collectable is not Key2D { LockName: { Length: > 0 } } key
                    || movingBody is not Player2D keyPlayer
                    || keyPlayer.FindGear(Key2D.GearName) is not { } keyGear)
                {
                    break;
                }

                keyGear.Add(key);
                SpawnEffectIfConfigured(collectable, world, typedCollectable.EffectPersists);
                world.QueueRemoval(collectable);
                break;

            case CollectableType.HealthPack:
                // Same as Diamond/Ring, but restores the player's health fully and plays the
                // player's own healed effect clip (e.g. a temporary recolor).
                SpawnEffectIfConfigured(collectable, world, typedCollectable.EffectPersists);
                world.QueueRemoval(collectable);
                if (movingBody is Player2D healthPlayer)
                {
                    healthPlayer.Health = GameDefaults.PlayerFullHealth;
                    if (healthPlayer.HealedEffectClipName is { } healedEffectClipName)
                    {
                        SpawnEffect(healthPlayer, healedEffectClipName, world);
                    }
                }
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
    /// A collector body has started overlapping a portal: plays the player's portal sound and effect (if
    /// configured) and queues the move to the portal's target world. The portal itself stays where it is,
    /// ready for the next use.
    /// </summary>
    public static void OnPortalEntered(World2D world, Portal2D portal, IPhysicsBody? collector = null)
    {
        if (collector is Player2D player)
        {
            world.PlaySound(player.PortalEnteredSoundName);
            if (player.PortalEnteredEffectClipName is { } portalEnteredEffectClipName)
            {
                SpawnEffect(player, portalEnteredEffectClipName, world);
            }
        }

        world.PendingPortal = portal;
        world.PendingTransition = PendingTransition.EnterWorld;
    }

    /// <summary>
    /// The player has arrived in a world through a portal: plays the player's arrival sound and effect, and
    /// when there is an effect freezes play briefly (<see cref="PendingTransition.Arrive"/>) so it can show.
    /// </summary>
    public static void OnPortalArrived(World2D world, Player2D player)
    {
        world.PlaySound(player.PortalArrivedSoundName);
        if (player.PortalArrivedEffectClipName is { } portalArrivedEffectClipName)
        {
            SpawnEffect(player, portalArrivedEffectClipName, world);
            world.PendingTransition = PendingTransition.Arrive;
        }
    }

    /// <summary>
    /// Leaves a <see cref="CorpseObject2D"/> playing <paramref name="clipName"/> where
    /// <paramref name="hazard"/> was. It keeps the hazard's vertical speed (so one killed in mid-air
    /// keeps falling) but not its sideways motion, and the hazard's material, so it lands and rests
    /// like it would.
    /// </summary>
    private static void SpawnCorpse(World2D world, DynamicHazard2D hazard, string clipName)
    {
        var corpse = new CorpseObject2D();
        corpse.Spawn(hazard.Sprite, clipName, frameIndex: 0, hazard.Position, new Vector2D(0, hazard.Velocity.Y), gravityAffected: true);
        corpse.MaterialName = hazard.MaterialName;
        corpse.Density = hazard.Density;
        corpse.Friction = hazard.Friction;
        corpse.Restitution = hazard.Restitution;
        corpse.Hardness = hazard.Hardness;
        corpse.Viscosity = hazard.Viscosity;
        world.Objects.Add(corpse);
    }

    /// <summary>
    /// Spawns a cosmetic <see cref="EffectInstance2D"/> at <paramref name="body"/>'s current
    /// position if it implements <see cref="IEffectTrigger"/> with a non-null clip name
    /// configured; no-ops otherwise.
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
    /// <see cref="EffectInstance2D"/> instances. A player's effects follow the player (at the
    /// clip's offset, applying its tint); so does any effect spawned with <paramref name="follow"/>
    /// (a hazard's hurt effect); every other effect stays where it was spawned.
    /// </summary>
    private static void SpawnEffect(Body2D body, string clipName, World2D world, bool persists = false, bool follow = false)
    {
        var effect = new EffectInstance2D();
        effect.Spawn(body.Sprite, clipName, body.Position, persists, followTarget: follow ? body : body as Player2D);
        world.Objects.Add(effect);
    }
}
