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
- **Medium drag/buoyancy is a distinct concept from surface friction, implemented as
  continuous force-accumulator terms** (`PhysicsSystem.StepMovingBodyWithForces`/
  `ResolveCurrentMedium`): a body's ambient medium (`Body2D.CurrentMedium`, defaulting
  to `Air`) is resolved fresh every frame from whichever static, passable `Body2D`
  overlaps it - the highest-`Density` overlapping volume wins on a tie, deterministic
  regardless of placement order. Buoyancy is Archimedes' principle (medium density
  times the body's own volume, opposing gravity); drag is a quadratic
  (velocity-squared) velocity-opposing force scaled by the medium's `Viscosity` and
  the immersed body's own frontal area (`Size.Y` for horizontal drag, `Size.X` for
  vertical drag - the 2D analog of cross-sectional area, so a bigger body feels
  proportionally more drag than a smaller one of the same material, not just more
  buoyancy from its larger volume) - the physically accurate model for fluid drag
  at ordinary speeds, and one that sheds a fast impact's momentum far more
  aggressively than a slow drift's, which keeps a body from significantly
  overshooting/bouncing back out after a hard water entry. Only bodies implementing
  `IMediumAffected` (mirroring `IGravityAffected`) receive these forces; `CurrentMedium`
  itself is still resolved/exposed for every body regardless, for future systems (e.g.
  a swim pose) to read.
- **Physics/Collision run on a fixed timestep accumulator, not a variable/capped
  per-frame step** (`GameLoop.OnPlayingFrameAsync`'s `_physicsAccumulatorSeconds`,
  `PhysicsSystem.FixedPhysicsStepSeconds`): an earlier "cap the step size and
  sub-step to cover the frame" approach still let each step's size vary with
  ordinary frame-rate jitter, which alone (independent of any oversized-single-step
  tunneling concern) was enough to visibly perturb collision/pose resolution right
  at a grounded/airborne boundary, since the exact instant contact is gained/lost
  shifts with the immediately preceding step's size - observed as pose flicker
  (rapid grounded/airborne alternation) specifically in the ~25-40 FPS range, where
  frame deltas straddled the old cap unpredictably. Each frame's elapsed time is
  now added to an accumulator, which is drained in however many whole steps of
  exactly `FixedPhysicsStepSeconds` are currently available - every step
  identically sized, deterministically, regardless of how the frame rate
  fluctuates - with any remainder left for next frame's accumulator rather than
  folded into an odd-sized partial step this frame. `CollisionSystem.Resolve` and
  `World2D.ApplyPendingRemovals` both re-run after every fixed step (not once for
  the whole frame), so a large catch-up delta still can't let a body integrate
  several times before ever being collision-checked (neither Physics nor Collision
  uses continuous/swept detection). Animation/camera/render remain once per real
  frame, since both are purely presentational and already delta-tolerant. The
  accumulator is reset to `0` when a new world finishes loading, so no leftover
  time from a previous world (or time spent loading) is burned through as extra
  steps on the new world's first frame.

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
- **An FPS overlay is a dev/testing toggle, off by default** (`GameLoop._showFpsOverlay`,
  `InputState.IsFpsToggleKeyPressed` bound to `F`), showing a smoothed
  frames-per-second reading computed from each real frame's own raw,
  unclamped elapsed time (not the fixed Physics/Collision step size), since
  the point of the overlay is to surface real frame-pacing hitches that the
  physics fixed-timestep otherwise hides from gameplay. Displayed as the
  number leading (e.g. "144 FPS"), right-aligned so the label's column is
  recomputed each frame from its own rendered length and stays flush with
  the top-right corner regardless of digit count.
- **Background/foreground layer colors are precomputed once at world load**
  (`World2D.BackgroundForeColors`/`BackgroundBackColors`/`ForegroundForeColors`/
  `ForegroundBackColors`), instead of resolving each visible cell's color code
  against the palette every frame in `WorldRenderer`. These layers are purely
  static level data, so the palette lookup only ever needs to happen once per
  cell, not once per cell per frame; `WorldRenderer` now just reads the
  precomputed color directly.

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
- Batching consecutive same-row/same-color/adjacent glyphs in
  `game-interop.js`'s `drawFrame` into one `fillRect`/`fillText` call pair
  per run (measured no FPS improvement, slightly negative, likely because
  game-object glyphs interleave with background/foreground glyphs in the
  array, keeping runs short - reverted to one draw call pair per glyph).
