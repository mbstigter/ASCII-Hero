# Game Architecture

## Platform

- .NET 10, C#
- Blazor Web App with WebAssembly interactivity
- HTML5 Canvas as the rendering surface
- The game runs entirely client-side in WebAssembly.

## Architecture Boundary

Blazor provides the application host and surrounding UI (navigation, layout,
pages). The game itself is an independent C# game system that does not
depend on Blazor's component rendering model.

## Coordinate System

- Game entities use floating-point world coordinates ("cells"), not integer
  grid cells.
- The ASCII character grid is a rendering concept only — physics and
  movement must never be quantized to it.
- World coordinates are converted to screen/pixel coordinates during
  rendering via the camera transform, enabling smooth sub-cell movement.

## Game Loop

The game loop is independent of Blazor component rendering and is
responsible for: reading input, updating game state/physics, updating the
camera, and rendering the current frame.

`StateHasChanged` is never used as the real-time game loop. Instead, the
loop is driven by JavaScript's `requestAnimationFrame`, which calls back
into C# once per frame.

## Rendering

- HTML5 Canvas is the only rendering surface — no DOM elements or Razor
  components are used to render the game world.
- The renderer converts game state into ASCII glyphs and draws them on
  Canvas at pixel positions derived from the camera transform, so glyphs can
  move smoothly even though the visual language is grid-based.
- The game renders using a single bundled bitmap CP437 font, scaled to a
  fixed pixel cell size that the browser measures and reports back to C#
  (`CellMetrics`), so the world grid stays consistent.

## JavaScript Interop

- Interop is limited to browser capabilities that are impractical in C#
  (Canvas 2D context, keyboard events, `requestAnimationFrame`).
- Interop is isolated behind a small C# abstraction (`CanvasBridge`); game
  logic must never depend directly on JavaScript APIs.

## Input

Keyboard input is captured at the browser boundary and exposed to the C#
game as game-oriented input state. Gameplay code never depends directly on
DOM keyboard events.

## Physics

The physics system has one core (force integration, collision detection and
resolution, ambient medium) shared by every body regardless of category, and
a thin layer of per-category behavior (player input, enemy patrol AI, 
kinematic platform scripting) that only ever plugs into that core by
implementing a capability interface — it never reimplements or bypasses the
core's own force/collision/medium math.

- Physics operates on continuous world coordinates.
- Collision detection operates on game-world geometry, not rendered ASCII
  characters.
- Every body in the world — static or moving, player or otherwise — lives in
  one generic `World2D.Objects` list. `PhysicsSystem`, `CollisionSystem`, and
  `WorldRenderer` iterate this single list and filter by capability interface
  (`IPhysicsBody`, `IGravityAffected`, `IHazardBody`, `ICollectableBody`,
  `ICollectorBody`) rather than by concrete type or by maintaining separate
  per-category collections — adding a new object category (e.g. a moving
  enemy) does not require touching every system's iteration logic.
- The world's own edges (bounds) act as a generic physical surface, handled
  uniformly for any moving body (player or dynamic object) rather than
  special-cased per type: dynamic objects bounce off them according to their
  `Restitution`, and any body resting against the floor is considered
  grounded (`IPhysicsBody.IsGrounded`), exactly as if it were resting on a
  platform.
- Every body's physical properties — `Density`, `Friction`, `Restitution` —
  are resolved once at spawn time from a named material (`Body2D.MaterialName`,
  derived from the dominant non-empty material of the active sprite frame's
  per-cell `_materials.txt`/`DefaultMaterial` layer) looked up in
  `World2D.Materials` (a `MaterialLibrary` that merges
  `Global/MaterialLibrary.ini` with an optional level-local override,
  mirroring `ColorPalette`'s Global+Level pattern); `Mass` is always
  computed from the resolved `Density` and the body's current footprint
  (`Density * Size.X * Size.Y`) rather than being independently overridable.
  A level placement's ini section can override the resolved material name via
  `Material`, or just the resulting `Restitution` via `Restitution`, without
  needing a distinct sprite asset. `CollisionSystem` combines two contacting
  bodies' `Restitution`/`Friction` via a simple average (`Combine`, see
  docs/Decisions.md) rather than one side dominating outright.
- Every moving body — the player included — integrates via a per-frame,
  mass-scaled force accumulator (`PhysicsSystem.StepMovingBodyWithForces`):
  forces (gravity as `mass * world.Gravity`, plus a body's own
  `IPatrolBody.PatrolForce`/`IWalkForceBody.WalkForce`, e.g. the player's
  proportional-to-speed-gap horizontal "motor" force computed by
  `PhysicsSystem.UpdateWalkForce`) are summed into a net force, converted to
  acceleration via `a = F / mass`, and integrated into velocity — for a
  gravity-only body this is numerically identical to a direct `velocity.Y +=
  gravity * dt`, but it is the extension point for any non-gravity force
  source. Climb/hang vertical velocity and jump-off impulses remain direct
  velocity assignments on purpose — those are discrete state-machine
  locomotion modes (like a scripted teleport), not continuous ground-level
  walk/crawl locomotion. There is no separate constraint-solver "normal
  force" term; a resting body's velocity along the contact normal is instead
  resolved by `CollisionSystem`'s own impulse-based response each frame it
  remains in contact (see below).
- Every body lives in an ambient medium (`Body2D.CurrentMedium`), resolved
  fresh each frame by `PhysicsSystem` — `Air` by default, overridden to
  whichever overlapping static/passable `Body2D` (e.g. a `Water` volume) has
  the highest material `Density`. Any `IMediumAffected` body then receives
  two further terms in the same force accumulator: buoyancy (Archimedes'
  principle — the medium's density times the body's own resolved volume,
  opposing gravity) and drag (a quadratic, velocity-squared force scaled by
  the medium's `Viscosity`, physically correct for fluid drag at ordinary
  speeds, and by the body's own frontal area, so a bigger body of the same
  material feels proportionally more resistance than a smaller one). Both
  terms are further scaled by the body's own submerged fraction (how much
  of its vertical extent actually overlaps the resolved medium volume, 0
  to 1), so a body only grazing a medium's surface receives proportionally
  less force than one fully submerged — this is what keeps a
  surface-floating body from visibly bouncing at the waterline. This is
  deliberately distinct from `Friction`/`Restitution`, which only act at
  solid-contact time — buoyancy/drag act continuously while immersed,
  independent of any contact.
- A body's own actively-generated force/impulse (walk/patrol motor force,
  jump-off velocity kicks) is separately damped by the current medium's raw
  `Viscosity` (`PhysicsSystem.ResolveMediumForceScale`), distinct from the
  passive buoyancy/drag above — a stride or push-off through a viscous
  medium is inherently less effective than the same effort on land.
- Collision resolution between two finite-mass moving bodies
  (`CollisionSystem.ResolveAgainstMover`, sharing its math with solid-vs-mover
  resolution via `ResolveAgainstOtherBody`/`ResolveContact`) splits position
  correction by relative mass (a heavier body yields less ground than a
  lighter one) and resolves the along-normal velocity response via a
  standard 1D mass-weighted impulse (`j = -(1+e) * relativeVelocity / (1/mA
  + 1/mB)`), rather than each body independently reflecting its own
  velocity. The tangential (along-surface) component is then damped via real
  Coulomb friction (`ApplyCoulombFriction`), capped by the normal impulse
  magnitude just resolved at that same contact.
- Platform collision and moving-body-vs-moving-body collision (e.g. the
  player and a dynamic object) are both resolved against `IPhysicsBody`
  generically — there is no per-concrete-type collision method. The player
  differs from other moving bodies only by the restitution value passed in
  (0, so it stops dead instead of bouncing), not by a separate code path.
- A kinematic body (`KinematicObject2D`, e.g. a moving platform) is
  `IsStatic` (immune to collision response — nothing corrects its own
  position/velocity) while also implementing `IPhysicsBody` with a real,
  self-driven `Velocity`. `CollisionSystem`'s solid-collision math
  (`ResolveAgainstOtherBody`/`ResolveContact`) treats that solid's own
  velocity as the collision's reference frame — computing the normal-impulse
  response and Coulomb friction on the other body's velocity *relative to*
  the solid, then adding the solid's velocity back — rather than assuming
  every solid is stationary. For ordinary terrain (velocity zero) this
  reduces to exactly the prior math; for a moving platform, friction
  naturally drags a resting rider along toward matching the platform's own
  velocity (a grippy material carries the rider more than a slick one).
- That velocity-matching, combined with `PhysicsSystem` integrating
  velocity into `Position` each frame *before* `CollisionSystem.Resolve`
  runs, carries a resting body — the player included, since its own
  horizontal velocity is now force/mass-driven rather than overwritten from
  input — along a moving platform on both axes with no special-casing: the
  ordinary landing-snap correction re-running against the platform's
  current (already-moved) position every frame closes the platform-carry
  gap on its own (a platform displacing farther in one frame than a body's
  own velocity-matched motion keeps up with). Two earlier workarounds for
  this gap — `CollisionSystem._groundedSolids` and a player-only horizontal
  platform-carry hack, plus a since-removed `MaintainVerticalGroundedContact`
  re-seat step — were provably made redundant by this and deleted outright
  (see docs/Decisions.md).
- Hazard contact is resolved generically: any `IPhysicsBody` overlapping any
  `IHazardBody` in `World2D.Objects` is detected, with no concrete-type checks
  on either side. Hazard contact detection exists but does not yet apply any
  effect, since no health/damage system exists in the game yet.
- Collectable pickup is resolved similarly, but narrower: only a body
  implementing `ICollectorBody` (e.g. the player) overlapping an
  `ICollectableBody` triggers a pickup — a non-collector moving body (the
  bouncing ball, an enemy) can physically collide with a collectable without
  consuming it. A picked-up collectable is queued for removal via
  `World2D.QueueRemoval` and actually removed from `Objects` once per frame via
  `World2D.ApplyPendingRemovals` — removal is always deferred to end-of-frame
  so no system mutates `Objects` while iterating it.
- Numeric values parsed from level/asset `.ini` files always use
  `CultureInfo.InvariantCulture`, never the current culture — otherwise a
  decimal point in a value like `1.0` can be silently misread as a thousands
  separator under some locales, corrupting the value.

## Camera

- The camera follows an explicit target (`World2D.CameraTarget`), not a
  hardcoded reference to the player. A level's object placement data can
  designate a different body (e.g. a dynamic object) as the camera target;
  see [AssetFormat.md](AssetFormat.md).
- Camera scrolling uses a "dead zone": the camera only moves once its target
  approaches within a margin of the current viewport's edge, rather than
  re-centering on every frame of target movement.
- The camera's position is always clamped to the world's own bounds, so it
  never reveals space beyond the level and a target near a world edge can
  reach the true edge of the screen instead of the camera trying (and
  failing) to keep it centered.

## Dependencies

Prefer built-in .NET and Blazor platform APIs. Do not add game engines,
frameworks, or NuGet packages unless there is a concrete, explicitly
justified architectural reason.

