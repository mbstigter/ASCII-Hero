# Decisions

Log of significant architecture/design decisions. Newest first.

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
  `UseGravity`) lets `PhysicsSystem` apply gravity to any qualifying moving
  body generically, replacing the earlier pattern where only
  `DynamicObject2D` had a `UseGravity` flag read via a type-specific loop.
  Only one genuinely new concrete class was introduced per category with a
  fundamentally different update path (`KinematicObject2D`, whose per-frame
  motion is constant-velocity integration with no force/gravity, unlike every
  other moving body) — `MovingEnemy2D`/`StaticEnemy2D`/`Collectable2D` reuse
  the existing sprite-backed `Body2D` shape entirely, adding only the marker
  interface(s) relevant to their category.
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
  (`Static`/`Dynamic`/`Kinematic`/`MovingEnemy`/`StaticEnemy`/`Collectable`,
  see `AssetFormat.md` §3.2) selecting which concrete class a placement
  spawns as. The previous `Static`/`Gravity` boolean-only scheme is preserved
  as the default resolution path when `Kind` is omitted, so existing levels
  (e.g. `Level1`, `LevelBallTest`) needed no edits.

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
