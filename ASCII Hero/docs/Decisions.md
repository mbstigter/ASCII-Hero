# Decisions

A compact, current-state reference of significant architecture/design
decisions, grouped by subsystem rather than by date. Each bullet reflects
what is **true today** - this is not a historical log, and entries are
updated/removed in place as decisions change or get superseded, rather than
appended to chronologically. It is not a substitute for
[Architecture.md](Architecture.md) (current rules) or
[Structure.md](Structure.md) (current "what exists") - it exists purely to
capture the "why" behind a decision without needing a lengthy narrative.


## Physics & Collision

- **Every moving body, including the player, moves via a mass-scaled force
  accumulator** (`PhysicsSystem.StepMovingBodyWithForces`), not per-body-type
  direct velocity assignment. The player's horizontal "motor" force
  (`IWalkForceBody`/`UpdateWalkForce`) and an enemy's patrol force
  (`IPatrolBody`) both plug into the same accumulator as gravity. Climbing/
  hanging are likewise continuous convergence forces; only jump-off (stand,
  ladder, hang-swing) remains a true one-time impulse (instantaneous
  Delta-v), since a real push-off happens in a single instant, unlike
  sustained locomotion.
- **Collision response is a genuine impulse-based normal force plus real
  Coulomb friction** (`CollisionSystem.ResolveContact`/`ApplyCoulombFriction`),
  generalized so an immovable solid acts as an infinite-mass second body -
  replacing an earlier flat "multiply by `1 - friction`" approximation.
- **A body's `Density`/`Friction`/`Restitution`/`Mass` are resolved from a
  named material** (`MaterialLibrary`, merging Global+World-local ini,
  mirroring `ColorPalette`), with per-placement overrides. Two contacting
  bodies' restitution/friction combine via a simple average
  (`CollisionSystem.Combine`), not one side dominating.
- **The solids/movers narrow phase re-runs several times per frame**
  (`SolverIterations = 4`) so simultaneous contacts (a corner, a body
  sandwiched between two others) reconcile instead of fighting each other
  frame to frame.
- **Broad phase uses a spatial grid** (`SpatialGrid<T>`, one for solids and
  one for moving bodies) so collision cost scales with nearby objects, not
  the level's total object count. Narrow phase additionally requires actual
  rendered-character overlap (`HasCharacterOverlap`), not just merged
  collision-rectangle overlap.
- **A kinematic body** (`KinematicObject2D`) is static-for-collision-response
  but still has a real `IPhysicsBody.Velocity`, used as the collision's
  reference frame. This alone - no extra carry/re-seat mechanism - makes a
  resting rider follow a moving platform on both axes via ordinary
  friction/velocity-matching plus the everyday landing-snap correction. Two
  earlier stopgap workarounds for this (a horizontal-only "carry" hack keyed
  off `_groundedSolids`, and a separate vertical re-seat step) were both
  deleted once the player's own movement became force-based and made them
  redundant.
- **Contacts are tracked as explicit `ContactType` flags** per body
  (`Body2D.AddContact`/`HasContact`), snapshotted-and-cleared once per frame;
  `IPhysicsBody.IsGrounded` is derived from this (`HasContact(SurfaceBottom)`),
  not a separately stored, ordering-bug-prone flag.
- **A one-way platform** (`Body2D.IsOneWayPlatform`) only blocks a genuine
  downward-moving top-landing; any other approach angle passes through
  untouched.
- **Facing/patrol targets are resolved relative to the surface the body
  rests on** (`Body2D.GetSurfaceVelocityX`), not raw absolute velocity - this
  prevents a platform's own speed from visually flipping a rider's facing,
  or making a patrolling enemy slide/drift on a fast-moving platform.
- **Jumping keeps a reduced (not zero, not full) horizontal "air control"
  force**, so momentum at the moment of jump-off (including any platform
  carry) decays/steers gradually across the arc instead of snapping
  instantly to a target speed.
- **Medium drag/buoyancy (air/liquid) is deliberately deferred and treated as
  a distinct concept from surface friction** - not yet implemented.

## Assets & Materials

- **A world object can be "materials-only"**: naming `Material`+`Width`+
  `Height` instead of `Asset`+`Clip` synthesizes an in-memory `SpriteAsset`
  (`SyntheticSpriteFactory`) filled with the material's `Character`, reusing
  the exact same body/collision/render pipeline as any sprite-backed object.
  `Repeat`/`TileAxis` don't apply to these (no smaller authored unit to tile).
- **The color palette is 36 codes** (`0`-`9`, `A`-`Z`).
- **A sprite's poses** (`[Poses]` section, formerly named `[Stances]`) resolve
  their active clip - and facing, including a vertical axis for `Climb` - from
  each clip name's own suffix, not a fixed slot position/flag.
- **Multi-frame animation is supported per clip** (`[Animation.{clip}]`
  timing overrides, `DefaultFrame`, `AnimationMode.Off` as an explicit
  opt-out for multi-frame-but-non-animating assets), including subtle idle
  animation (e.g. blinking) using the same mechanism as static shape
  variants.
- **`Kind` is mandatory in `_objects.ini`** and its values match concrete
  class names 1:1 (`Static`, `Dynamic`, `Kinematic`, `MovingEnemy`,
  `StaticEnemy`, `Collectable`, `PlayerSpawn`) - no implicit fallback.

## World Objects & Capability Model

- **`World2D` holds one generic `List<Body2D> Objects`**, not separate
  per-category collections; `PhysicsSystem`/`CollisionSystem`/`WorldRenderer`
  all iterate it once and filter by capability interface
  (`IPhysicsBody`, `IGravityAffected`, `IHazardBody`, `ICollectableBody`,
  `ICollectorBody`, `IKillerBody`, `IKillableBody`, `IPosedBody`,
  `IPatrolBody`, `IWalkForceBody`, `IClimberBody`, `IHangerBody`) rather than
  concrete type or maintaining separate lists - adding a new object category
  never requires touching every system's iteration logic.
- **Object removal is always deferred to end-of-frame**
  (`World2D.QueueRemoval`/`ApplyPendingRemovals`), generic over any `Body2D`,
  so no system mutates `Objects` mid-iteration.
- **`MovingEnemy2D` patrols via its own mass-scaled force** (`IPatrolBody`),
  independently on the X and/or Y axis, with vertical patrol
  gravity-asymmetric (cancelled while climbing so the proportional force can
  reach its target, left alone while descending for a controlled glide).
  This is a distinct scheme from `KinematicObject2D`'s own constant-velocity,
  non-force patrol (chosen to leave room for non-linear future paths without
  redesigning the body).
- **Hazard contact detection exists but applies no effect yet** - there is no
  health/damage system in the game yet; this is intentionally a detected,
  no-op stub.

## Player Movement & Input

- **`Up` (directional) and `Jump` (action) are always distinct inputs, never
  equivalent, even for the ordinary ground jump** - an earlier experiment to
  let `Up` also trigger a jump was tried and explicitly reverted as
  unintuitive.
- **Two full, independent key sets** are supported for local co-op/preference
  - "Player 1" (arrows + `Space`) and "Player 2" (`WASD` + `Left Ctrl`).
- **Ground and hang each have their own structured "stance ladder"**
  (Crawl<->Walk on the ground; Clamber<->Hang while hanging, inverted to
  match arm position rather than screen direction), and each locomotion mode
  has its own jump-off speed, graded by how much of the body's own
  leverage/grip backs the push-off (`WalkJumpSpeed` > `ClimbJumpSpeed` >
  `HangJumpSpeed`).
- **A short debounce** (`IHangerBody.SuppressHangUntilClear`, and the
  climbing equivalent) prevents an immediate re-grab of the same
  ladder/pipe the same frame a jump/swing-off begins.

## Rendering & Camera

- **`WorldRenderer` only builds glyphs for what's inside the camera's current
  viewport** - physics, collision, and animation are unaffected and keep
  simulating every body regardless of visibility.
- **The camera follows its target with a dead zone** (only scrolls once the
  target nears the viewport edge) and is always clamped to the world's own
  bounds.

## Level/Game Flow

- **Level selection is a strict `GameMode` state machine** backed by a
  `Global/Worlds.ini` catalog (world metadata/thumbnails loaded without
  loading the full playable world).
- **`Esc` returns to the world-select screen** as a dev/testing shortcut.

## Notably rejected/reverted approaches

A few approaches were deliberately tried and abandoned; if you find old
comments or muscle memory referencing these, they're gone:

- The `MultiRect`/`CharacterGrid` narrow-phase toggle and its `N`/`B` debug
  keys (removed once `CharacterGrid` narrow phase proved out as the only
  correct behavior).
- `CollisionSystem._groundedSolids` and `MaintainVerticalGroundedContact`
  (temporary platform-carry hacks, superseded by the force-based player
  movement rewrite above).
- A fudge-tolerance `EdgeTolerance` check for hang-snap (replaced by an exact
  overlap-based correction).
- `Up` doubling as a jump trigger (tried twice, reverted both times).
- `Min`/`Max` wording for patrol initial direction (renamed to
  `Left`/`Right`/`Up`/`Down` for readability).
