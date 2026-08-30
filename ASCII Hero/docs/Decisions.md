# Decisions

Log of significant architecture/design decisions. Newest first.

## `Kind` made mandatory, `Static` key removed, `Player` via `Kind`, `Kind` values match class names

- **`Kind` is now a required key on every `_objects.ini` placement section;
  there is no default category anymore.** The prior scheme (see Milestone 3b
  below) let `Kind` be omitted and fall back to a `Static`/`GravityAffected`
  boolean-based guess (`Static` unless `Static = false`). This silently
  produced the wrong concrete class whenever a section forgot to set `Kind`
  — caught in practice when a `ToxicPlant` placement in `LevelBallTest`
  omitted `Kind` and spawned as a plain `StaticObject2D` instead of
  `StaticEnemy2D`, making it walkable instead of hazardous. `World2D.LoadAsync`
  now throws a `FormatException` naming the offending section if `Kind`
  (or `Asset`) is missing, rather than silently guessing or skipping.
- **The `Static` key is removed entirely** — it only ever existed to drive the
  now-removed fallback guess and is redundant with `Kind = StaticObject`.
- **The player is now `Kind = Player` instead of `Kind = PlayerSpawn` matched
  by special-cased section name.** Previously `World2D.LoadAsync`
  special-cased any section literally named `PlayerSpawn`; this was
  inconsistent with every other object category being explicitly selected via
  `Kind`, and meant the player's category was implicit in a naming convention
  rather than declared data. `PlayerSpawn` itself was then judged an
  inconsistent `Kind` name too — every other `Kind` names *what the object is*
  (`Static`, `Dynamic`, `MovingEnemy`, ...), not an event like "spawn," even
  though every kind's placement position is, in the same sense, a spawn/start
  location. Renamed to plain `Player` to match. All player placements
  (`Level1`, `LevelBallTest`) were updated accordingly.
- **Every `Kind` value now corresponds exactly to a concrete body class name
  with the `2D` suffix dropped** (`Player` → `Player2D`, `StaticObject` →
  `StaticObject2D`, `StaticEnemy` → `StaticEnemy2D`, etc.), so the
  value-to-class mapping is discoverable directly from the codebase instead
  of needing separate memorization. This required renaming `Static` →
  `StaticObject`, `Dynamic` → `DynamicObject`, and `Kinematic` →
  `KinematicObject` (previously inconsistent with their `...Object2D` class
  names), in addition to `PlayerSpawn` → `Player`. `MovingEnemy`/
  `StaticEnemy`/`Collectable` already matched their class names and are
  unchanged. Multi-player (`Player1`/`Player2`, a `Players` collection) was
  considered but rejected as a larger, separate feature — `World2D` currently
  has a single `Player` field, so `Kind = Player` still means "the one
  player" under the current single-player model.
- Rationale: predictability and fail-fast behavior were prioritized over
  backward-compatible fallbacks now that the schema is small and fully
  controlled by this project — a silently-wrong placement is worse than a
  level failing to load with a clear error.


## `AnimationMode.Off`: explicit opt-out for multi-frame, non-animating assets

- **Added `Mode = Off` to `[Animation]`'s `AnimationMode` enum.** Previously the
  only way to keep a multi-frame clip static was to omit `[Animation]`/
  `FrameDurationSeconds` entirely; there was no way for an asset that *does*
  want `FrameDurationSeconds`/`DefaultFrame` configured (e.g. sharing one
  settings file/art layout with an animated sibling) to explicitly disable
  playback. `Body2D.AdvanceAnimation` now returns immediately whenever
  `Sprite.AnimationMode == AnimationMode.Off`, holding forever on whatever
  frame `DefaultFrame` selected at spawn.
- **Motivating use case: dead vs. alive variants of the same visual family**
  (e.g. a wilted `ToxicPlant` that should render a fixed pose while a living
  one animates). Note this still requires two separate assets/settings.ini
  files (`[Animation]` is asset-wide, not per-instance) — `Mode = Off` solves
  "this asset should never animate despite having multiple frames and
  animation settings," not "this specific placed instance should stop
  animating." A true per-instance animation toggle would need further design
  (e.g. a `Body2D`-level override) if that need arises later.

## `DefaultFrame` in `[Animation]`, removal of placement-level `Frame`

- **Added `DefaultFrame` to the `[Animation]` section** in `{Asset}_settings.ini`,
  giving an asset an explicit starting frame index (default `0` when omitted).
  This lets a Left/Center/Right clip (e.g. `ToxicPlant_idle`) declare `Center` as
  its natural starting point instead of always starting at frame `0`
  (`Left`). `SpriteAsset.DefaultFrame` (nullable `int`) is parsed in
  `SpriteLoader` the same way as `FrameDurationSeconds`/`Mode`.
- **Verified `PingPong` mode bouncing from a middle starting frame requires no
  changes to the bounce mechanics themselves** — `Body2D.AdvanceAnimation`'s
  existing direction-reversal logic (reverse at index `0` and `Count - 1`)
  naturally produces `Center, Right, Center, Left, Center, Right, ...` when
  `_animationFrameIndex` simply *starts* at `DefaultFrame` instead of `0`, with
  no other change needed to the stepping algorithm.
- **Removed the placement-level `Frame` key from `Level1_objects.ini` entirely**,
  along with its parsing in `World2D.LoadAsync`. It was only ever used by
  `ToxicPlant` (`Frame = 1`, now superseded by `ToxicPlant_settings.ini`'s
  `[Animation] DefaultFrame = 1`) and the user judged the per-instance
  override use case (e.g. staggering multiple placements of the same animated
  asset to different starting frames) unlikely to ever be needed. Starting
  frame is now purely an asset-level concern (`DefaultFrame`), computed once as
  `sprite.DefaultFrame ?? 0` and applied uniformly to every placement of that
  asset (including `Player2D.Spawn`, which previously always started its
  `idle` clip at frame `0` unconditionally).
- **This simplifies the frame-animation model from the prior milestone**: the
  previous decision's "placement-time `Frame` selects the *starting* frame an
  animated instance begins from" concept was short-lived — `DefaultFrame`
  proved to be the simpler, sufficient mechanism, and per-placement frame
  overrides added complexity without a concrete use case.

## Frame Animation for Multi-Frame Clips (Idle Animation)

- **Multi-frame clips can now animate over time via an opt-in `[Animation]`
  section in `{Asset}_settings.ini`.** Previously, `Body2D.SetFrame` picked one
  fixed `frameIndex` at spawn and never changed it again — multi-frame clips
  existed structurally (per `AssetFormat.md` §2.1, `//end`-separated frames)
  but were only used for static shape variants (e.g., `ToxicPlant`'s
  left/middle/right-facing frames selected once at placement time via `Frame`
  in the level's objects.ini). Now, when an asset declares
  `FrameDurationSeconds` in an `[Animation]` section, `Body2D.AdvanceAnimation`
  (called once per frame via the new `AnimationSystem`) cycles through frames
  at the specified rate, updating `Frame`, `Size`, and collision rects each
  advance so physics stays correct even if animated frames differ in shape.
- **Two playback modes: `Loop` (0,1,2,0,1,2,...) and `PingPong`
  (0,1,2,1,0,1,...),** selectable per asset via `[Animation] Mode` (defaults
  to `Loop`). This design was chosen to satisfy the recollection of "maybe
  back and forth or continuous" from the old `ConsoleGame2D` codebase without
  having access to the specific old idle-animation implementation.
- **Animation timing is per-instance, not shared per-clip.** Each `Body2D`
  owns its own `_animationElapsedSeconds`, `_animationFrameIndex`, and
  `_animationDirection` fields, so multiple instances of the same animated
  asset can be out of sync (e.g., staggered via different starting `Frame`
  values in objects.ini). This maximizes flexibility and matches the
  per-instance nature of all other `Body2D` state.
- **No hardcoded global default frame duration.** Per user instruction, if a
  clip has multiple frames but no `[Animation]` section (or the section omits
  a parseable `FrameDurationSeconds`), the asset simply keeps rendering
  whichever frame it was spawned/set to, unchanged — exactly like today's
  behavior, preserving backward compatibility with existing single-frame clips
  and static shape-variant usage.
- **`ToxicPlant`'s `idle` clip now animates** (when `[Animation]` is present
  in its settings.ini), changing the documented example in `AssetFormat.md`
  from "pure shape variant, not animation" to "animates left/middle/right
  frames." Placement-time `Frame` (via objects.ini) now selects the *starting*
  frame a given instance's animation begins from, rather than locking it to
  one permanently fixed shape.
- **Animation state managed entirely in `Body2D`, not in a separate
  component/system object attached to each body.** This keeps the existing
  simple object model unchanged (a single `Body2D` subclass per object
  category, no component dictionary/attachment API), matching the project's
  established anti-abstraction stance and the precedent set by
  `IPhysicsBody.Velocity`/`IsGrounded` living directly on the body rather than
  in a detached "VelocityComponent."

## Milestone 4b: interface naming cleanup, player gravity unification

- **`IMovingBody` was renamed to `IPhysicsBody`.** The old name suggested a
  body actively in motion, but the interface is implemented by anything that
  participates in physics integration and collision (including bodies at
  rest); the new name matches its actual role as a capability marker rather
  than a runtime state description. `IHazard`/`ICollectable` were similarly
  renamed to `IHazardBody`/`ICollectableBody` for consistent `*Body` naming
  across all body capability interfaces.
- **`Player2D` now implements `IGravityAffected` (fixed `UseGravity => true`)
  instead of `PhysicsSystem` special-casing player gravity in a separate code
  block.** Cross-checking the old `ConsoleGame2D` project's documentation
  (`docs/console-game-2d-documentation.md`) confirmed the player is not
  conceptually exempt from gravity — it is affected by all physics forces,
  same as dynamic objects, and is only special in that its horizontal
  velocity and jumps are driven by input rather than by an initial
  velocity/restitution. `KinematicObject2D` remains the only mover that does
  *not* implement `IGravityAffected` (per the same old-engine docs, kinematic
  objects travel a predefined path and are never affected by physics forces
  at all). `PhysicsSystem.Step` now applies input directly to `player.Velocity`
  and lets the single generic `foreach (var body in world.Objects)` loop
  integrate gravity and position for the player exactly like any other
  `IGravityAffected` body, removing the previous player-only early-return and
  duplicate integration code.

## Milestone 4: unified generic object model, capability-based categories

- **`World2D` exposes one generic `List<Body2D> Objects` instead of separate
  `Platforms`/`DynamicObjects` collections.** `PhysicsSystem`, `CollisionSystem`,
  and `AsciiRenderer` previously each had their own multi-loop structure
  (platforms, dynamic objects, player, handled separately), even though the
  loop bodies were already generic per-object logic once filtered by
  capability interface. All three now iterate `Objects` once, filtering with
  `OfType<IPhysicsBody>()`/`IsStatic` checks/etc. instead of visiting three
  separate lists. `Player` is retained as a convenience reference into the
  same list (it is also present in `Objects`), not a separate silo, matching
  the existing precedent set by `CameraTarget` ("pick one object generically").
- **`Body2D`/`GameObject2D`'s two-layer abstract split was collapsed into one
  `Body2D` class.** Nothing in the codebase ever constructed a `Body2D` that
  wasn't sprite-backed, and no non-visual body (e.g. an invisible trigger
  volume) was planned; the split was unused indirection. `GameObject2D`'s
  members (`Sprite`/`Clip`/`Frame`/`SetFrame`/collision-rect derivation) were
  merged directly into `Body2D`, and `Player2D`/`StaticObject2D`/
  `DynamicObject2D` now derive from it directly. Rejected: keeping the split
  "for future flexibility" — introducing a sprite-less body type can be done
  later if an actual need arises, and speculative layering with zero current
  consumers contradicts the project's existing anti-abstraction stance.
- **New composable capability markers (`IHazardBody`, `ICollectableBody`,
  `IGravityAffected`) extend the existing `IPhysicsBody` pattern instead of
  introducing per-noun subclass hierarchies.** `IHazardBody`/`ICollectableBody` are
  empty marker interfaces detected generically in `CollisionSystem` (any
  `IPhysicsBody` overlapping any `IHazardBody`/`ICollectableBody`), with no
  concrete-type checks on either side. `IGravityAffected` (exposing
  `GravityAffected`) lets `PhysicsSystem` apply gravity to any qualifying moving
  body generically, replacing the earlier pattern where only
  `DynamicObject2D` had a gravity flag read via a type-specific loop.
  Only one genuinely new concrete class was introduced per category with a
  fundamentally different update path (`KinematicObject2D`, whose per-frame
  motion is constant-velocity integration with no force/gravity, unlike every
  other moving body) — `MovingEnemy2D`/`StaticEnemy2D`/`Collectable2D` reuse
  the existing sprite-backed `Body2D` shape entirely, adding only the marker
  interface(s) relevant to their category.
- **Collectable pickup is restricted to a new `ICollectorBody` marker interface,
  implemented by `Player2D`, rather than any moving body.** Without this, the
  bouncing ball (or any future dynamic object/enemy) would also consume
  collectables purely by physically overlapping them, which was confirmed as
  an actual bug while testing `LevelBallTest`. `ICollectorBody` is deliberately
  a capability interface rather than a `Player2D` type-check, so a future
  second player (multiplayer) automatically qualifies without
  `CollisionSystem` needing to know how many player types or instances exist —
  consistent with every other capability check in this class.
- **Object removal from the world is deferred to end-of-frame via
  `World2D.QueueRemoval`/`ApplyPendingRemovals`, generic over any `Body2D`,**
  not collectable-specific. `CollisionSystem.Resolve` queues a collectable
  for removal when any moving body overlaps it; `GameLoop.OnFrame` applies
  all queued removals once, after collision resolution, so no system ever
  mutates `Objects` mid-iteration. This mechanism is intentionally reusable
  for future removal needs (e.g. an enemy dying) beyond just collectables.
- **Hazard contact detection exists but does not yet apply any effect.** No
  health/damage system exists in the game yet, so `ResolveHazardsAndCollectables`
  detects `IPhysicsBody`-vs-`IHazardBody` overlap generically but leaves the actual
  effect as a documented no-op, to be wired once damage/health exists —
  detection, not the effect, was the part relevant to this milestone's goal
  of generic categorization.
- **The level `_objects.ini` schema gained an explicit `Kind` key**
  (`PlayerSpawn`/`Static`/`Dynamic`/`Kinematic`/`MovingEnemy`/`StaticEnemy`/
  `Collectable`, see `AssetFormat.md` §3.2) selecting which concrete class a
  placement spawns as. `Kind` was later made mandatory with no fallback (see
  the newer decision above).

## Milestone 3b: unified moving-body collision

- **Platform collision and moving-body-vs-moving-body collision are both
  resolved generically against `IPhysicsBody`, with no type-specific
  methods.** `CollisionSystem` previously had two near-duplicate methods —
  `ResolveAgainstPlatform(Player2D, ...)` (stops dead) and
  `ResolveDynamicObjectAgainstPlatform(DynamicObject2D, ...)` (bounces) —
  that differed only by a restitution value baked into each copy. These are
  now one `ResolveAgainstPlatform(IPhysicsBody, restitution, ...)`, with the
  player passing `restitution: 0.0` (which naturally zeroes velocity on the
  affected axis, reproducing the old "stop dead" behavior as a special case
  of the same bounce formula, not a separate code path).
- **The player and every dynamic object are collected into one list of
  moving bodies each frame and checked pairwise against each other**, fixing
  the gap where the player could completely overlap the ball with no
  collision at all (they were never in the same collision loop). Resolution
  splits position correction evenly between both bodies (neither is
  immovable, unlike a platform) and reflects each body's velocity using its
  own restitution — consistent with how that same body already bounces off
  platforms and world bounds. Rejected: giving player-ball collision special
  physics (mass, momentum transfer) — no other collision in the game models
  mass, so introducing it for just this one pairing would be inconsistent;
  the existing restitution-per-body model was reused instead.
- `IPhysicsBody` now also exposes `CollisionRects`, promoting a member
  `Body2D` already provided generically, so the shared collision code can
  operate purely against the interface without a downcast.

## Milestone 3: world-bounds physics, culture-safe parsing, configurable camera

- **World bounds are a generic physical surface, not player-specific.**
  `CollisionSystem.ResolveWorldBounds` treats the level's edges the same way
  for any `IPhysicsBody`: dynamic objects bounce off them via `Restitution`
  (already true), and any body resting against the floor is marked
  `IsGrounded`, whether that's the player or (in principle) a dynamic object.
  `IsGrounded` lives on the shared `IPhysicsBody` interface, not just
  `Player2D`. Rejected: a `body is Player2D` type-check inside
  `ResolveWorldBounds` to special-case the player's jump-at-edge behavior —
  this was the initial fix and works, but special-cases by concrete type
  instead of treating the world's floor as a generic supporting surface like
  a platform's top.
- **Sprite glyphs are clipped to the world's cell grid at render time.**
  `AsciiRenderer.AddGameObjectGlyphs` now skips any glyph whose world cell
  falls outside `[0, WidthCells) x [0, HeightCells)`. This matters because a
  sprite's anchor (`Position`) doesn't have to be its top-left-most solid
  cell (e.g. a plant sprite whose handle is at its own top-left but whose
  leaves extend left of that), so a sprite placed near an edge can otherwise
  have cells that render past the world boundary.
- **All numeric ini parsing uses `CultureInfo.InvariantCulture` explicitly.**
  `World2D`'s raw `double.TryParse`/`int.TryParse` calls used the current
  culture, which silently misparsed decimal-point values like
  `Restitution = 1.0` as `10.0` under locales (e.g. `nl-NL`) where `.` is the
  thousands separator rather than the decimal point — sending the bouncing
  ball's velocity into exponential blow-up to `Infinity` within a handful of
  bounces (visually indistinguishable from "stuck in a corner", since
  `ResolveWorldBounds` then clamps an infinite position to the exact
  boundary). Fixed via private `TryParseDouble`/`TryParseInt` helpers that
  always pass `CultureInfo.InvariantCulture`.
- **The camera follows an explicit, level-configurable target, not a
  hardcoded reference to the player.** `World2D.CameraTarget` (an
  `IPhysicsBody`) defaults to the player, but any placement in a level's
  `_objects.ini` can claim it via `CameraTarget = true` on its section (e.g.
  the ball in `LevelBallTest`). Rejected: leaving the camera hardwired to
  `world.Player` — this made it impossible to build isolated test levels
  (like the ball-bounce reproduction level) where the interesting motion
  belongs to a non-player object.
- **Camera scrolling uses a "dead zone" plus world-bounds clamping, not
  constant re-centering.** `Camera2D.Follow` only moves the camera once the
  followed body's bounding box crosses within `EdgeMarginCells` of the
  current view's edge, and the camera's position is always clamped to
  `[0, worldSize - viewportSize]` per axis. Net effect: minor movement near
  the middle of the screen doesn't scroll the camera at all, and a body near
  a world edge (or in a world no larger than the viewport) can walk right up
  to the true edge of the screen instead of the camera trying to keep it
  centered. Rejected: the original "always lerp toward centering the
  target" approach — it scrolled on every frame of motion and had no
  awareness of world bounds, so the camera could imply there was more world
  to reveal than there actually was.

## Milestone 2: collision derivation & asset frame model

- **Collision shape is derived per-body from actual sprite grid data**, not a
  single hand-typed bounding box offset. `CollisionShapeBuilder.DeriveRectangles`
  merges non-empty grid cells into a small set of axis-aligned rectangles
  (row-run + vertical merge), reused identically by any body backed by a
  sprite. `CollisionSystem` resolves collisions per rectangle-pair, not per
  whole-body box. Rejected: keeping the earlier row/column trim hack
  (`CollisionOffset`/`CollisionSize`) - it only worked at row granularity and
  incorrectly excluded non-empty cells sharing a row with empty ones.
- **A clip's `//end`-separated frames serve two purposes, using one
  mechanism.** Frames can be true animation (played back over time, e.g. an
  idle blink) or non-animating shape variants of one static object, selected
  once at spawn time via `Frame` in `Level1_objects.ini`. Rejected: giving
  `ToxicPlant` three separate clips (`left`/`middle`/`right`) for what is
  really one `idle` clip with three shape variants - this misused the
  clip/frame distinction and would have forced the loader to special-case
  "static objects with multiple shapes" instead of reusing the existing frame
  concept.

## Milestone 1: game loop & rendering approach

- **Game loop is driven by JS `requestAnimationFrame`**, calling back into a
  single C# `[JSInvokable]` method per frame. Rejected: using
  `StateHasChanged`/Blazor re-render as the loop (too slow, couples game
  timing to UI framework).
- **Rendering surface is HTML5 Canvas**, not DOM elements or Razor
  components. The C# `AsciiRenderer` produces glyphs (character + pixel
  position); JS only clears the canvas and calls `fillText`.
- **World coordinates are floating-point "cells"**, not integer grid
  coordinates. The ASCII grid is a visual concept applied only at render
  time, so movement, gravity and camera scrolling stay smooth.
- **JS interop is isolated behind `CanvasBridge`** (init/drawFrame/dispose)
  and one JS module (`game-interop.js`) containing only canvas setup,
  keyboard forwarding, and the animation loop — no game logic in JS.
- **No game engine or extra NuGet packages.** Game logic (world, entities,
  physics, collision, camera, rendering) is plain C# under `Game/`.
- **Game code lives in `ASCII Hero.Client`** (the WebAssembly project),
  since that's where it executes at runtime.
