# Special Effects & Killable Enemies — Plan

## Context

This plan was produced through an extended discussion about adding small,
low-complexity cosmetic special effects to the game (e.g. a collectable ring
fading away when picked up, a spark when the player hits a hazard), and a
related idea for enemies that can be "killed" by a specific kind of contact
(e.g. jumping on top of one crumbles it, leaving an inert husk behind). No
code has been written yet — this document is the complete design arrived at,
intended to be handed to a fresh coding agent with no other context.

Read `docs/Architecture.md`, `docs/Design.md`, `docs/Decisions.md`, and
`docs/AssetFormat.md` first; this plan builds directly on the patterns
described there (generic `Body2D`/`World2D.Objects` list, capability-interface
filtering instead of concrete-type checks, the sprite/clip/frame asset
format). Do not restate or duplicate those documents — just follow their
existing conventions.

## Guiding principles (carried over from the wider discussion)

- Effects must be purely cosmetic. They must never be quantized to the
  physics/collision system, and must never feed back into `Body2D.Position`
  or collision shape derivation.
- Reuse existing plumbing wherever possible: the existing sprite/clip/frame
  loading pipeline (`SpriteLoader`, `SpriteAsset`, `AssetTextReader`), the
  existing per-instance ini-key pattern already used for `Restitution`/
  `GravityAffected`/`CameraTarget` in `World2D.LoadAsync`, the existing
  deferred-removal mechanism (`World2D.QueueRemoval`/`ApplyPendingRemovals`),
  and the existing capability-interface filtering idiom (`IPhysicsBody`,
  `IHazardBody`, `ICollectableBody`, `ICollectorBody`).
- No new sprite-file format, no new folder structure, no new parser. An
  effect's visuals are just another clip on an asset that already has a
  `_settings.ini` and layer files, following `docs/AssetFormat.md` §2
  exactly as-is (including the per-clip `[Animation.{clipName}]` override
  section that already exists for this purpose).
- Prefer one small, generic, data-driven mechanism over multiple hardcoded,
  special-cased ones. Avoid adding fields to `Body2D` itself for anything
  that isn't universally needed — new state should live on the narrowest
  class/interface that actually needs it.

## Recommended implementation order

Implement **special effects first, as a complete and independently shippable
slice**. Only after that is built and verified, implement **killable
enemies** as a separate follow-up. Do not attempt both in a single change:

1. Killable enemies' "crumble into a husk" behavior is *built on top of* the
   effects mechanism (it's `EffectInstance2D` plus one persistence flag) —
   it cannot be meaningfully implemented or tested before effects exist.
2. Effects alone are low-risk: no changes to core collision math, only
   additive classes/interfaces/ini keys plus a couple of new lines in
   `CollisionSystem`.
3. Killable enemies require a genuinely new piece of collision-resolution
   logic (directional overlap detection — see below) that is unrelated to
   effects and carries real design/implementation risk. It deserves its own
   focused change so any iteration on that logic doesn't entangle or block
   the simpler, safer effects work.

Suggested finer-grained sequence:
1. Build the generic effect mechanism (`EffectInstance2D`, the effect-trigger
   capability, `CollisionSystem` wiring) and prove it end-to-end with the
   collectable-pickup case (e.g. a ring that fades on pickup).
2. Extend the same mechanism, unchanged, to the hazard-contact case (e.g. a
   spark effect on the player and/or the enemy on hit) — this should require
   no new classes, only using the mechanism from step 1 a second time.
3. Only then take on killable enemies, starting with the directional-overlap
   collision problem (see "Open problem" below) before touching any
   killable-specific interface or ini key.

---

## Part 1: Generic special effects

### 1.1 New class: `EffectInstance2D`

Location: `ASCII Hero.Client/Game/World/EffectInstance2D.cs` (same folder as
`Body2D`, `Collectable2D`, etc.), following the exact structure of the
existing sprite-backed body classes in that folder (see `Collectable2D.cs`,
`StaticEnemy2D.cs` for the pattern to copy).

- Inherits `Body2D`.
- Implements **none** of `IPhysicsBody`, `IHazardBody`, `ICollectableBody`,
  `ICollectorBody`. This is intentional and is what makes it automatically
  invisible to `PhysicsSystem` and to every capability-filtered loop in
  `CollisionSystem` — no new exclusion logic is needed anywhere else.
- `IsStatic = true` in its constructor (it never moves).
- A `Spawn(SpriteAsset sprite, string clipName, Vector2D position)` method
  that calls the existing `SetFrame(sprite, clipName)` (inherited from
  `Body2D`) and sets `Position`, mirroring `Collectable2D.Spawn`.
- One new field: a remaining-lifetime timer (e.g. `_remainingSeconds`,
  `double`), computed once at spawn time from the clip's own data:
  `frameCount * (FrameDurationSeconds ?? someFallback)`. If the clip has no
  `FrameDurationSeconds` (not animated), treat it as a short fixed default
  (pick something reasonable, e.g. 0.5s) rather than never expiring.
- One new bool field, `PersistsAfterPlayback` (default `false`), settable at
  spawn time (extra `Spawn` parameter or set right after spawning). See
  Part 2 for why this exists — for Part 1 alone, every spawned instance uses
  the default `false`.
- A per-frame update method, e.g. `Tick(double deltaSeconds)`, that:
  - Decrements the remaining-lifetime timer.
  - When it reaches zero: if `PersistsAfterPlayback` is `false`, this
    instance should be removed from the world (see 1.4 for how this hook is
    invoked). If `true`, do nothing further — just stop decrementing (clamp
    at zero) and let the body continue existing/rendering as-is, holding on
    whatever frame the clip's own animation last landed on.
  - Do **not** duplicate `AdvanceAnimation`'s frame-cycling logic here —
    that continues to run exactly as it already does for every other body
    in `World2D.Objects`, unchanged, via `AnimationSystem.Update` →
    `Body2D.AdvanceAnimation`.

### 1.2 New capability interface for triggering an effect

Location: `ASCII Hero.Client/Game/World/IEffectTrigger.cs`, following the
existing interface style in that folder (see `IHazardBody.cs`,
`ICollectableBody.cs` for XML-doc-comment style and one-line marker
convention — but note this interface carries data, unlike those two).

```csharp
public interface IEffectTrigger
{
    string? EffectClipName { get; }
}
```

- `null` (the default) means "no effect configured for this instance."
- A non-null value names a clip that must already exist on **this same
  body's own `Sprite`** (the object that triggered/received the effect) —
  i.e. the effect reuses a clip authored on the existing asset, not a
  separate effect-specific asset. (See 1.5 for asset-authoring guidance.)
- Implement this on whichever concrete classes need to optionally trigger an
  effect: at minimum `Collectable2D`, `StaticEnemy2D`, `MovingEnemy2D`, and
  `Player2D`. Each implementing class gets a settable property (e.g. a plain
  auto-property with a `private set` assigned during `Spawn`, or a public
  settable property assigned by `World2D.LoadAsync` after construction —
  follow whichever style matches how `Restitution`/`GravityAffected` are
  currently assigned on `DynamicObject2D`/`MovingEnemy2D`).
- Every implementing class must default `EffectClipName` to `null` so that
  existing levels with no `EffectClip` ini key behave exactly as they do
  today — this is a purely additive, opt-in feature.

### 1.3 Level/asset configuration: `EffectClip` ini key

In `World2D.LoadAsync` (`ASCII Hero.Client/Game/World/World2D.cs`), alongside
the existing per-section reads of `Asset`, `Kind`, `Clip`, `Repeat`,
`GravityAffected`, `Restitution`, `InitialVelocityX/Y`:

- Read an optional `EffectClip` key from the object's `objects.ini` section
  (`objectSection.TryGetValue("EffectClip", out var effectClipName)`).
- After constructing the relevant object (`Collectable2D`, `StaticEnemy2D`,
  `MovingEnemy2D`, or the player), assign the read value (or `null` if
  absent) to that instance's `EffectClipName`.
- This requires `EffectClipName` to be settable from `World2D` — expose it
  as `{ get; set; }` (not `{ get; private set; }`) unless you prefer routing
  it through each class's existing `Spawn` method as an added optional
  parameter (defaulting to `null`), matching how other optional per-instance
  values are threaded through `Spawn` today. Prefer whichever style is more
  consistent with the surrounding code once you're looking at it directly.

### 1.4 Wiring into `CollisionSystem`

In `ASCII Hero.Client/Game/Physics/CollisionSystem.cs`,
`ResolveHazardsAndCollectables` currently:

- Detects any `IPhysicsBody` overlapping any `IHazardBody` (currently a
  no-op with a `// TODO: apply damage...` comment) — **do not touch this
  damage TODO or add a damage system; that is out of scope.**
- Detects any `IPhysicsBody` that is also `ICollectorBody` overlapping any
  `ICollectableBody`, queuing the collectable for removal.

Add a small shared helper, e.g.:

```csharp
private static void SpawnEffectIfConfigured(Body2D body, World2D world)
{
    if (body is IEffectTrigger { EffectClipName: { } clipName })
    {
        var effect = new EffectInstance2D();
        effect.Spawn(body.Sprite, clipName, body.Position);
        world.Objects.Add(effect);
    }
}
```

Call this helper for **both** participants independently at each existing
overlap point:

- In the hazard-overlap loop: call it once for `hazard` and once for
  `movingBody` (checking each for `IEffectTrigger` separately — a hit can
  trigger an effect on the hazard, the moving body, both, or neither,
  depending purely on which side(s) have `EffectClipName` configured; do not
  hardcode which side gets an effect).
- In the collectable-overlap loop: call it once for `collectable` (the
  primary "ring fades away" case) and once for the collector body (e.g.
  `Player2D`), for symmetry/future use, even though most collectable levels
  will likely only configure the collectable side.
- The effect must be spawned using the **triggering body's own position**
  (its position at the moment of the overlap, before any removal), not a
  fixed/arbitrary position.
- For the collectable case specifically: the collectable is still queued for
  removal via `World2D.QueueRemoval` exactly as it is today, with **no
  change to that removal or its timing** — the spawned `EffectInstance2D` is
  what remains visible afterward, not the original collectable body. Do not
  delay `QueueRemoval`.

Also add the per-frame tick for effect lifetimes. The cleanest place is
`AnimationSystem.Update` (`ASCII Hero.Client/Game/Animation/AnimationSystem.cs`),
since it already iterates `world.Objects` once per frame calling
`AdvanceAnimation`:

```csharp
foreach (var body in world.Objects)
{
    body.AdvanceAnimation(deltaSeconds);
    if (body is EffectInstance2D effect)
    {
        effect.Tick(deltaSeconds);
        if (effect.IsExpiredAndShouldBeRemoved) // however you choose to expose this
        {
            world.QueueRemoval(effect);
        }
    }
}
```

Decide the exact shape of the "is this effect done and not persisting"
signal (a public bool property, or `Tick` returning a bool) — either is
fine, just keep it consistent with the rest of `Body2D`'s public surface
style.

### 1.5 Asset authoring guidance (not code, but needed for testing)

To exercise this end-to-end, add a new clip to an existing collectable
asset (e.g. whatever asset the ring/collectable in the current test level
uses) rather than creating a new asset:

- `{AssetName}_fade_characters.txt` / `_foregroundcolors.txt` with, e.g.,
  3 `//end`-separated frames of ever-lower-intensity yellow (reuse existing
  color codes in the relevant `Colors.ini`, or add new lower-intensity
  yellow entries if none exist).
- An `[Animation.fade]` section in that asset's existing `_settings.ini`
  (per `docs/AssetFormat.md` §2.4) with its own `FrameDurationSeconds` and
  `Mode = Loop` (or `Off` if you want it to hold on the last frame — for the
  non-persisting collectable-fade case, `Loop` or a short one-shot-like
  duration is fine since the whole object is removed once the lifetime timer
  expires anyway).
- Add `EffectClip = fade` to that collectable's section in the relevant
  level's `objects.ini`.

### 1.6 Validation for Part 1

- Build the solution (see repository root for build command — check
  `ASCII Hero.sln`/project files if unsure) and fix any compile errors.
- Manually verify (or add a test if the codebase has a test project — check
  for one before assuming; if none exists for game logic, do not introduce a
  new test framework, per repository conventions) that:
  - Picking up a collectable configured with `EffectClip` shows the fade
    clip at the collectable's last position, then that effect object
    disappears after its own clip's duration.
  - A collectable/hazard with **no** `EffectClip` configured behaves exactly
    as before (no crash, no phantom effect).
  - Effects never block movement, are never collidable, and never appear in
    `PhysicsSystem`/`CollisionSystem`'s solid/moving/hazard/collectable
    processing.

---

## Part 2: Killable enemies (implement only after Part 1 is complete)

### 2.1 No new `Killable2D` class

Do **not** create a separate class. Both `StaticEnemy2D` and
`MovingEnemy2D` (`ASCII Hero.Client/Game/World/StaticEnemy2D.cs` /
`MovingEnemy2D.cs`) should gain this capability directly.

### 2.2 New capability interface with a per-instance flag

Important: this must **not** be a bare marker interface. Unlike
`ICollectableBody`/`IHazardBody` (which have no consequence-bearing state —
implementing them unconditionally on a class is safe because either every
instance of that class already behaves that way, or, for hazards, detection
is currently a no-op), being killable has a real, irreversible effect
(removal from the world). If `IKillableBody` were a bare marker implemented
unconditionally by `StaticEnemy2D`/`MovingEnemy2D`, **every** instance of
those classes in every level would become killable with no way to opt out —
that would be wrong.

Instead:

```csharp
public interface IKillableBody
{
    bool IsKillable { get; }
}
```

- `StaticEnemy2D` and `MovingEnemy2D` both implement this interface
  unconditionally (so `CollisionSystem` can keep checking capability
  generically, consistent with every other interface in the codebase), but
  `IsKillable` defaults to `false`.
- `CollisionSystem` must check `body is IKillableBody { IsKillable: true }`
  — checking interface presence alone is not sufficient and would
  incorrectly treat every enemy as killable.
- Populate `IsKillable` per spawned instance from an optional ini key (e.g.
  `Killable = true`) in `World2D.LoadAsync`, read and assigned the same way
  as `Restitution`/`GravityAffected` already are for these same classes.
  Defaults to `false` when the key is absent, so existing levels are
  unaffected.

### 2.3 Effect persistence on kill

Reuse `IEffectTrigger`/`EffectClipName` from Part 1 unchanged for the visual
(the "crumble" clip). The one addition specific to killable enemies:

- Add an `EffectPersists` bool to whatever governs `EffectInstance2D`
  spawning for a killable-triggered effect (e.g. a second optional ini key,
  `EffectPersists = true`, read alongside `EffectClip`, threaded through to
  `EffectInstance2D.Spawn`/`PersistsAfterPlayback` from Part 1).
- When a killable enemy is removed via a qualifying kill contact, spawn its
  `EffectInstance2D` with `PersistsAfterPlayback = true` (if `EffectPersists`
  was configured `true`) so it becomes a permanent decorative body in
  `World2D.Objects` — e.g. a "dead plant" husk — instead of self-removing.
  Author the corresponding clip so its last frame *is* the desired husk
  appearance (e.g. `Mode = Off` or a clip that naturally ends there).
- For a killable enemy without `EffectPersists` set, the effect behaves
  exactly like any other Part 1 effect (self-removes after its lifetime).

### 2.4 Open problem to solve before writing any killable-specific code: directional overlap

This is the hardest and riskiest part of this plan — budget real design time
for it, separately from the rest.

Today, `CollisionSystem.ResolveHazardsAndCollectables` detects hazard
overlap with a simple, non-directional AABB `Overlaps` check (see
`Overlaps(IPhysicsBody a, Body2D b)` in that file) — it has no notion of
*which side* of the hazard was contacted. Directional penetration analysis
only exists today in `ResolveAgainstSolid` (computing `overlapLeft/Right/
Top/Bottom` to determine top-vs-side collision for solid terrain), and
hazards/killables are deliberately excluded from the "solids" list that
method operates on (see the `solids` filter in `CollisionSystem.Resolve`,
which excludes `ICollectableBody` and `IHazardBody`).

To distinguish "landed on top" (should kill/remove a killable enemy) from
"walked into the side / hit from below" (should be treated as an ordinary
hazard contact, not a kill), you need to:

1. Reuse (do not copy/duplicate) the same deepest-penetration-axis
   calculation already used in `ResolveAgainstSolid` — refactor it into a
   shared private static helper if needed so both methods call the same
   logic, rather than maintaining two separate implementations of the same
   math.
2. In `ResolveHazardsAndCollectables`, when the overlapping body is
   `IKillableBody { IsKillable: true }`, compute which axis/side has the
   shallowest penetration for that specific overlap.
3. Only treat it as a "kill" contact if the shallowest-penetration axis is
   vertical **and** the moving body is above the killable body (approaching
   from the top) — mirroring how `ResolveAgainstSolid` distinguishes
   "landing on top" from "hitting the underside." Any other direction
   (from the side, or from below) should fall through to the existing,
   unmodified hazard-contact behavior for that same body (still a no-op
   today per the existing TODO, unless/until a damage system exists —
   which remains explicitly out of scope for this plan).
4. This logic must not alter existing hazard-only behavior for enemies that
   are *not* `IKillableBody { IsKillable: true }` — those must continue to
   use the exact same non-directional overlap check as before.

Do not attempt to guess at velocity-based heuristics beyond what
`ResolveAgainstSolid` already does — reuse its existing, already-working
approach rather than inventing a new one.

### 2.5 Validation for Part 2

- Build and manually verify:
  - An enemy with `Killable = true` and an `EffectClip`/`EffectPersists`
    configured, when landed on top of, is removed and replaced by a
    permanent husk object at its former position.
  - The same enemy, when contacted from the side (not landed on top), is
    **not** removed and behaves exactly as an ordinary hazard does today
    (still a no-op contact, per the existing TODO).
  - An enemy without `Killable = true` is entirely unaffected by any of this
    — it cannot be killed regardless of contact direction, matching its
    current behavior exactly.
  - Non-killable-related hazard contact behavior (e.g. existing levels with
    enemies that don't set `Killable`) is unchanged.

---

## Explicitly out of scope / deferred

- **Player recoloring/tint effects.** An earlier idea considered a
  color-only clip (omitting `_characters.txt`, only `_foregroundcolors.txt`/
  `_backgroundcolors.txt`) to temporarily tint the player in place, so an
  effect doesn't need to match the player's currently-active stance/pose
  shape. This was explicitly parked, not adopted, because:
  - `SpriteLoader.LoadClipAsync` currently treats `_characters.txt` as
    required and throws `FileNotFoundException` if it's missing — allowing
    a characters-less clip would need a deliberate, explicit change to that
    requirement.
  - It is a fundamentally different mechanism from `EffectInstance2D`
    (recoloring the *target's own live frame* in place at render time, not
    spawning a new standalone body), and would need its own transient
    tint-state field on `Body2D` plus changes to
    `AsciiRenderer.AddGameObjectGlyphs`'s per-cell color resolution.
  - If revisited later, treat it as a separate, additional mechanism
    alongside (not a replacement for) `EffectInstance2D`, reserved for cases
    where an effect must track a body's ongoing/changing shape (e.g. the
    player mid-movement) rather than replace it with a fixed lookalike.
- **A general health/damage system.** Hazard contact detection remains
  exactly as no-op as it is today outside of the specific killable-kill-
  contact case described in Part 2. Do not add player health, damage
  amounts, or death/respawn logic as part of this plan.
