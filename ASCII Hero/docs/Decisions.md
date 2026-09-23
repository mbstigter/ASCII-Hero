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
- **`ResolveContact`'s along-normal velocity impulse (and its dependent
  friction response) only applies while the contacting pair is still
  actually approaching along the contact normal** (`normalRelativeSpeed < 0`)
  - never to a pair already separating or exactly grazing. Position
  correction (push-apart) is unconditional and always runs regardless.
  Without this, a rectangle pair that's still reported overlapping the
  instant after a body has already launched away from it (e.g. a leftover
  sliver of overlap against a solid's corner right as the player jumps
  beside a slightly higher, tightly adjacent platform) got treated as a
  fresh landing and impulse-response, injecting an extra unwanted velocity
  kick (an unearned second jump boost) on top of the body's own existing
  motion.
- **A body's `Density`/`Friction`/`Restitution` are resolved from a
  named material** (`MaterialLibrary`, merging Global+World-local ini,
  mirroring `ColorPalette`), with per-placement overrides; `Mass` is always
  computed as `Density * width * height` and has no separate override - a
  creature needing a distinct mass should get its own dedicated material
  instead. Two contacting
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
- **`CollisionShapeBuilder` derives collision rectangles from two
  complementary run-length-merge passes (row-run and column-run, the exact
  transpose of each other), unioned together with duplicates removed** - not
  row-run alone. A row-only decomposition of a curved/notched silhouette
  (e.g. the round `Ball` sprite) produces very short, wide rectangles along
  its flanks, whose shallow vertical overlap always wins the
  minimum-translation-vector contact-direction rule in `CollisionSystem`,
  even where the true overlap is deep horizontally - letting a body slide
  sideways through the shape's middle instead of being blocked. The
  column-run pass's tall, narrow rectangles along the same flanks give the
  resolver a shallow-horizontal option there too, so the correct contact
  normal wins regardless of where along a curved outline contact happens.
  Deduplication (rather than just unioning both lists) is required, not
  optional: for a blocky rectangular sprite both passes produce
  byte-identical rectangles, and `CollisionSystem` resolves each of a body's
  own rectangles independently per solver iteration - an undetected
  duplicate silently doubles that contact's position correction and
  velocity impulse every iteration, invisible at rest but capable of adding
  real extra velocity (e.g. an unwanted jump-height boost) to a fast-moving
  contact.
- **Of a body's own overlapping collision rectangles against one other body/solid,
  `ResolveAgainstOtherBody` resolves exactly one physical contact per solver iteration, not one
  contact per overlapping rectangle.** The rectangle overlapping most deeply (by its own natural
  axis) defines that contact's normal; every other overlapping rectangle is then re-measured
  along that *same* normal axis, and the worst (largest) of those depths is what is actually
  corrected/responded to, via one call to `ResolveContact`. Two earlier variants were each wrong
  in an opposite direction, and both were tried and reverted here: (1) resolving every
  overlapping rectangle fully and independently applied the along-normal velocity impulse more
  than once for what was really one physical contact - an unearned extra velocity kick (visible
  as inflated jump height) whenever a body's own shape has two rectangles overlapping the same
  solid along different axes at once (e.g. `CollisionShapeBuilder`'s column-run pass giving the
  player's jump pose both a 3-wide top rectangle and a narrower, one-row-taller rectangle through
  the same column); (2) resolving only the single deepest rectangle and leaving every other
  overlapping rectangle completely untouched that iteration instead regressed plain wall
  collision to feel mushy - a rectangle that never happened to be the single deepest one across
  4 solver iterations could end up never corrected at all, letting a body visibly sink slightly
  into an ordinary wall while walking into it. Re-measuring every overlapping rectangle along the
  primary rectangle's own axis (rather than independently letting each rectangle pick its own
  axis, and rather than ignoring non-primary rectangles outright) fixes both: every rectangle's
  overlap along the established contact direction is still guaranteed to be corrected in the same
  pass, while only one velocity/friction response and one contact recording happens per solid per
  iteration. Any leftover overlap along a genuinely different axis (a separate physical contact)
  is left for a later solver iteration, which re-detects fully fresh contacts against the body's
  just-corrected position rather than permanently ignoring it.
- **A kinematic body** (`KinematicObject2D`) is static-for-collision-response

  reference frame. This alone - no extra carry/re-seat mechanism - makes a


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
  overshooting/bouncing back out after a hard water entry. Both forces are additionally
  scaled by the body's own **submerged fraction** - the fraction (0 to 1) of the body's
  own vertical extent actually overlapped by the winning medium volume, computed by
  merging every qualifying overlap into a covered-interval union so several
  stacked/adjacent placements of the same medium don't double-count. A body only
  grazing a medium's surface therefore receives proportionally less buoyancy/drag than
  one fully submerged (fraction 1.0, identical to the previous full-strength
  behavior), which is what stops a surface-floating body from visibly bouncing as it
  crosses the binary overlap boundary each frame. When no placed medium volume
  overlaps at all, the body resolves to the default `Air` fallback at fraction 1.0
  (not 0.0) - `Air` is the ambient substance filling all otherwise-unoccupied space,
  not a bounded level-authored volume with edges to be partially submerged past, so
  its own low but nonzero `Viscosity` drag keeps applying continuously everywhere
  outside a denser placed volume, exactly as before submerged-fraction scaling was
  introduced. Only bodies implementing
  `IMediumAffected` (mirroring `IGravityAffected`) receive these forces; `CurrentMedium`
  itself is still resolved/exposed for every body regardless, for future systems (e.g.
  a swim pose) to read.
- **`ResolveCurrentMedium`'s scan explicitly excludes `EffectInstance2D` and any
  `IsClimbable`/`IsHangable` terrain**, even though both satisfy the same
  `IsStatic && IsPassable` check as a genuine level-authored medium volume
  (`WaterSurface`/`BodyOfWater`). The distinction is intent: a medium volume is
  deliberately placed by a level designer to represent an occupiable ambient space,
  whereas `EffectInstance2D.IsPassable` is unconditionally true purely so a cosmetic
  effect never blocks movement (e.g. a killed `ToxicPlant`'s "crumble" husk), and a
  ladder/pipe/bar is structural terrain to grip/hang from, not a substance to be
  immersed in, regardless of whatever material it happens to be given (e.g. for its
  render color). Without these exclusions, either could leak its own (often fairly
  dense) material into the medium resolution the moment the player merely overlapped
  its footprint, causing unintended buoyancy (e.g. a suspiciously high jump) that has
  nothing to do with its actual role.
- **A body's own actively-generated force/impulse (motor force, jump-off) is separately
  dampened by ambient medium viscosity, distinct from the passive buoyancy/drag above**
  (`PhysicsSystem.ResolveMediumForceScale`): maps `Material.Viscosity` alone (never
  `Density`, which already fully does its own job via buoyancy - reusing it here too
  would double-count the same property for two unrelated effects) to a multiplier in
  `[MinMediumForceScale, 1.0]`, applied to `Player2D`'s `UpdateWalkForce` motor force and
  all three jump-off impulses (`WalkJumpSpeed`/`ClimbJumpSpeed`/`HangJumpSpeed`), and to
  `MovingEnemy2D`'s `PatrolForce` (any `IMediumAffected` body that is also
  `IWalkForceBody`/`IPatrolBody`). Uses each medium's raw `Viscosity` directly (not
  relative to any other medium's value) for pure, literal physical accuracy - even
  `Air`'s own small authored baseline (0.02, `Global/MaterialLibrary.ini`) applies a
  slight, deliberate penalty on land, same as it already does for the existing passive
  drag term, rather than treating whichever medium happens to be ambient as an
  artificial zero-point. A denser fluid's higher `Viscosity` (e.g. `Water`'s 0.15 vs.
  `Air`'s 0.02) falls off reciprocally toward the floor, so a
  push-off/stride genuinely struggles more the more viscous the medium - fixing an
  issue where a water jump, once buoyancy had already cancelled most of gravity, still
  launched off a fixed ground-level impulse and so leapt unrealistically high. The floor
  keeps a body from ever being fully unable to move under its own power, however viscous
  the medium. `StepMovingBodyWithForces` resolves `CurrentMedium` up front (before
  patrol/walk force summation) so `PatrolForce` scaling uses the current frame's medium;
  `UpdateWalkForce`/the jump-off sites read `Player2D.CurrentMedium` as of the *previous*
  frame's resolution (the only value available at that point in `Step`) - an accepted
  one-frame lag, inconsequential since medium rarely changes frame-to-frame.
- **Physics/Collision run on a fixed timestep accumulator, not a variable/capped
  per-frame step** (`GameLoop.OnPlayingFrameAsync`'s `_physicsAccumulatorSeconds`,
  `PhysicsConstants.FixedPhysicsStepSeconds`): an earlier "cap the step size and
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
- **A moving (non-idle) clip's `FrameDurationSeconds` is deliberately
  correlated with that pose's own move speed**, not a single fixed value
  shared by every pose: `FrameDurationSeconds = 2.4 / speed`, anchored on
  `walk_left`/`walk_right` (`WalkSpeed` = 12, `FrameDurationSeconds` = 0.2,
  so 12 * 0.2 = 2.4). See the `Player_settings.ini` `[Animation]` section
  comment for the full worked table, and each `GameDefaults` speed
  constant's own doc comment for the reminder to recompute its paired
  clip(s) if that speed is tweaked.
- **`Kind` is mandatory in `_objects.ini`** and its values match concrete
  class names 1:1 (`Static`, `Dynamic`, `Kinematic`, `MovingEnemy`,
  `StaticEnemy`, `Collectable`, `PlayerSpawn`) - no implicit fallback.

## World Objects & Capability Model

- **`World2D` holds one generic `List<Body2D> Objects`**, not separate
  per-category collections; `PhysicsSystem`/`CollisionSystem`/`WorldRenderer`
  all iterate it once and filter by capability interface
  (`IPhysicsBody`, `IGravityAffected`, `IHazardBody`, `ICollectableBody`,
  `ICollectorBody`, `IKillerBody`, `IKillableBody`, `IPosedBody`,
  `IPatrolBody`, `IWalkForceBody`, `IClimberBody`, `IHangerBody`,
  `ISwimmerBody`) rather than
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
  has its own jump-off speed (`WalkJumpSpeed`/`ClimbJumpSpeed`/`HangJumpSpeed`),
  graded by how much of the body's own leverage/grip backs the push-off -
  currently `WalkJumpSpeed` highest, with `ClimbJumpSpeed`/`HangJumpSpeed` tuned
  independently and not required to stay in strict order relative to each other.
- **A short debounce** (`IHangerBody.SuppressHangUntilClear`, and the
  climbing equivalent) prevents an immediate re-grab of the same
  ladder/pipe the same frame a jump/swing-off begins.
- **Swim detection is medium-based, not reach-based** (`ISwimmerBody`,
  `PhysicsSystem.IsSwimmableMedium`) - unlike `IClimberBody`/`IHangerBody`,
  which key off a specific `Body2D.IsClimbable`/`IsHangable` surface via a
  `CollisionSystem`-set touching flag, swim instead reads the player's own
  already-resolved `Body2D.CurrentMedium` directly every frame and compares
  it against `PhysicsConstants.SwimMediumMinDensity`/`SwimMediumMinViscosity`
  (a medium qualifies if it clears *either* threshold, not both - density and
  viscosity are independent physical properties, and requiring both would
  wrongly exclude a hypothetical dense-but-thin or thin-but-viscous fluid).
  Engaging still mirrors climbing's deliberate-press model (a directional key
  must actually be held while submerged), not hanging's automatic grab, since
  merely drifting through a passable fluid volume shouldn't force the pose
  any more than brushing past a ladder auto-climbs. Grounded/climbing/hanging
  all take priority and preempt swim entirely (`PhysicsSystem.Step`'s swim
  engage condition requires none of them apply) - a solid floor stays solid
  underwater (walking/crawling keeps working exactly as on land), and a
  ladder/pipe's own capability check is entirely independent of medium, so
  submerging one doesn't need special-casing. Up/down while swimming are
  vertical thrust (depth control, via `GameDefaults.SwimVerticalSpeed`) using
  the same continuous motor-force model as climbing's vertical axis, not a
  distinct pose/mechanic - left/right use their own dedicated
  `SwimHorizontalSpeed`. There is deliberately no swim jump-off impulse: Jump
  does nothing while `IsSwimming` (no capability/interface reserves one),
  since there is no solid surface to push off against underwater.

## Rendering & Camera

- **Cell/canvas pixel size is a configured, final on-screen value - not
  measured by the browser, not a fixed pixel constant, and not a separate
  base-size-plus-scale pair.** `Global/Settings.ini`'s `[Render]` section
  requires `FontWidthPixels`/`FontHeightPixels` as the final cell size
  already at whatever zoom is wanted (`GameLoop.StartAsync` throws if either
  is missing); `GameLoop`/`CanvasBridge` then size the canvas element and the
  gameplay viewport as `ViewportColumns`/`ViewportRows` (character counts)
  times that cell size (see docs/AssetFormat.md §4.3). Switching fonts or
  zoom level never requires touching game code - just updating the ini
  values - superseding both an earlier approach that hardcoded a 16x28
  target cell size and a fixed 1280x700 canvas, and a later attempt at
  auto-detecting the native size via the browser's `measureText` plus a
  separate integer `FontScale` multiplier (dropped because `measureText`
  doesn't reliably report a true bitmap font's real native size - the
  bundled 8x14 font measured back as 9.143x16 at a 16px probe size - and the
  extra scale key was redundant once the final size is just configured
  directly).
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
