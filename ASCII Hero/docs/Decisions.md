# Decisions

Log of significant architecture/design decisions. Newest first.

## Milestone 3b: unified moving-body collision

- **Platform collision and moving-body-vs-moving-body collision are both
  resolved generically against `IMovingBody`, with no type-specific
  methods.** `CollisionSystem` previously had two near-duplicate methods —
  `ResolveAgainstPlatform(Player2D, ...)` (stops dead) and
  `ResolveDynamicObjectAgainstPlatform(DynamicObject2D, ...)` (bounces) —
  that differed only by a restitution value baked into each copy. These are
  now one `ResolveAgainstPlatform(IMovingBody, restitution, ...)`, with the
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
- `IMovingBody` now also exposes `CollisionRects`, promoting a member
  `Body2D` already provided generically, so the shared collision code can
  operate purely against the interface without a downcast.

## Milestone 3: world-bounds physics, culture-safe parsing, configurable camera

- **World bounds are a generic physical surface, not player-specific.**
  `CollisionSystem.ResolveWorldBounds` treats the level's edges the same way
  for any `IMovingBody`: dynamic objects bounce off them via `Restitution`
  (already true), and any body resting against the floor is marked
  `IsGrounded`, whether that's the player or (in principle) a dynamic object.
  `IsGrounded` lives on the shared `IMovingBody` interface, not just
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
  `IMovingBody`) defaults to the player, but any placement in a level's
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
