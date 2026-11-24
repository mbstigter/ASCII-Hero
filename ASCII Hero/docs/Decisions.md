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
  direct velocity assignment. The player's locomotion "motor" force
  (`IMotorBody`/`UpdateMotorForce`) and a hazard's patrol force
  (`IPatrolBody`) both plug into the same accumulator as gravity. Climbing/
  hanging are likewise continuous convergence forces; ground, climb, hang and
  wall push-offs are instantaneous impulses, a simplified short-duration
  approximation rather than sustained locomotion.
- **Push-offs share a bounded relative-speed impulse.** `ApplyPushOff` computes
  the impulse needed to reach the configured take-off speed relative to the
  support using both inverse masses, capped by player effort, and applies the
  exact opposite impulse to a movable support. It never brakes a player already
  moving faster away. Climb and hang push-offs are also limited by the shared
  grip-force model (combined friction times mass times `GripStrengthPerMass`)
  over `GripJumpDurationSeconds`; this is an effective duration, not a simulated
  limb stroke. Wall jumps require a fresh Jump press: a still-overlapping side
  contact takes priority, otherwise the nearest solid collision-rectangle side
  within `WallJumpReachCells` is eligible, even while moving away. This small
  spatial forgiveness approximates reach, without changing collision geometry or
  buffering input. Their upward impulse is capped by combined friction times the
  outward impulse. A vertical ground jump is normal to the floor, so it does not
  depend on friction. Ground retains held-Jump repeat; grip actions take priority
  over wall jumping. World edges are not contacts.
- **Collision response is a genuine impulse-based normal force plus real
  Coulomb friction** (`CollisionSystem.ResolveContact`/`ApplyCoulombFriction`),
  generalized so an immovable solid acts as an infinite-mass second body -
  rather than a flat "multiply by `1 - friction`" approximation.
- **`ResolveContact`'s along-normal velocity impulse (and its dependent
  friction response) only applies while the contacting pair is still
  actually approaching along the contact normal** (`normalRelativeSpeed < 0`)
  - never to a pair already separating or exactly grazing. Position
  correction (push-apart) is unconditional and always runs regardless, so a
  body launching away from a still-overlapping solid gets no extra kick.
- **A body's `Density`/`Friction`/`Restitution` are resolved from a
  named material** (`MaterialLibrary`, merging Global+World-local ini,
  mirroring `ColorPalette`), with per-placement overrides; `Mass` is always
  computed as `Density * width * height` of the spawn footprint, fixed for the
  body's lifetime (a pose change alters collision shape, never mass), and has
  no separate override - a
  creature needing a distinct mass should get its own dedicated material
  instead (every creature has one, named after it, rather than a shared
  generic material with per-placement density). A `Material`/`DefaultMaterial`
  name not defined in the library is a world load error, never a silent
  fallback. Two contacting
  bodies' friction combines as the geometric mean `sqrt(a*b)` (not capped at
  1) and restitution as a softness-weighted average (each side weighted by
  `1 / Hardness`, so the softer, more-deforming body dominates the bounce;
  `CollisionSystem.CombineFriction`/`CombineRestitution`). Impacts slower
  than `PhysicsConstants.RestingBounceSpeedThreshold` never bounce, so
  resting bodies settle instead of micro-bouncing.
- **The solids/movers narrow phase re-runs several times per step**
  (`SolverIterations = 8`) so simultaneous contacts (a corner, a body
  sandwiched between two others, a crate stack with the player on top)
  reconcile within one step instead of fighting each other. A final solids-only
  pass afterwards guarantees no body ends the step overlapping immovable terrain
  after a mover-vs-mover correction. Iterations stop early once one converges:
  no response moved a body out of an overlap deeper than the slop or changed a
  velocity by more than `SolverVelocityTolerance`.
- **Resting contacts keep a small allowed overlap (`ContactSlopCells`).** Overlap
  up to the slop is not pushed apart (the contact is still recorded); deeper
  overlap is corrected back to half the slop. Without this, gravity's per-step
  sink into a support was fully corrected every step, so stacks never settled
  and every solver iteration always ran.
- **Resting groups of bodies sleep (`SleepSystem`).** Contact slop alone cannot
  stop a stack's solver work: sequential impulses keep passing gravity's
  per-step gain up and down the stack in every iteration. Passive bodies
  (`Body2D.CanSleep`; never the player, a patrolling body or a kinematic one)
  are grouped into islands through surface contacts each step; an island
  sleeps only as a whole, once every member has stayed within the contact slop
  of where it came to rest, below `SleepVelocityThreshold`, for
  `TimeToSleepSeconds`, and none touches a moving platform or conveyor. Rest is
  judged by actual displacement, because the solver's residual velocity in a
  stack does not decay. A sleeping body is not integrated, keeps its last
  contacts (still grounded) and is skipped against solids and other sleepers.
  It wakes - together with its whole island - when an awake body or a moving
  solid overlaps it, a reaction force (walk, jump, grip) is applied to it, a
  body it touches is removed, or its own animation changes its collision
  shape. Any gate or switch state change wakes every sleeper: such changes are
  rare, and waking all is simpler and safer than finding exactly which bodies
  an opened gate or a switched flow affects.
- **Overhanging bodies tip off ledges (`TipSystem`).** Support depends on balance,
  not on any overlap, so a body cannot rest on a sliver of an edge. Bodies never
  rotate, so tipping is a sideways pull instead:
  grounded body whose center of mass (mean of its visible cells) lies outside
  the span it stands on (union of its bottom overlaps with everything below it,
  so bridging two supports is balanced) gets `Body2D.TipAccelerationX` toward the
  open side, scaled by how far past the edge (`GameDefaults.MinTipFraction` of
  gravity up to full gravity at half its width). While tipping its ground
  contact applies no friction, the player's motor drops to air control, and the
  body never sleeps. Center of mass rather than a fixed minimum overlap, so it
  fits every body size. The player alone gets
  `GameDefaults.PlayerLedgeToleranceCells` (via `Body2D.LedgeToleranceCells`) for
  platformer-style footing on an edge. Visible air between bodies whose glyph is
  smaller than its cell is separate (collision is per cell) and not addressed.
- **The narrow phase does not allocate per pair.** A pair whose Position/Size
  boxes don't overlap is rejected before any rectangle work; world-space
  collision rectangles are cached per body until its position or shape changes;
  each body's contact lists hold only the current step's contacts and are
  cleared and reused at the start of every step rather than reallocated (no
  previous-step contact history is kept).
- **Broad phase uses a spatial grid** (`SpatialGrid<T>`, one for solids and
  one for moving bodies) so collision cost scales with nearby objects, not
  the world's total object count. Narrow phase additionally requires actual
  rendered-character overlap (`HasCharacterOverlap`), not just merged
  collision-rectangle overlap.
- **`CollisionShapeBuilder` derives collision rectangles from two
  complementary run-length-merge passes (row-run and column-run, the exact
  transpose of each other), unioned together with duplicates removed**, so a
  curved/notched silhouette offers both shallow-vertical and
  shallow-horizontal rectangles along its flanks and the correct contact
  normal wins wherever contact happens. Duplicates are removed so the solver
  does not test the same rectangle pair twice.
- **Two bodies' compound shapes are resolved as one contact per body pair per
  solver iteration, not one contact per overlapping rectangle.**
  `ResolveAgainstOtherBody` checks every overlapping rectangle pair (on both
  sides, measured against the same uncorrected positions) and, for each of
  the four push directions, takes the largest push needed to separate every
  pair in that direction. The smallest of those four worst-case pushes is the
  contact's normal and depth. One correction therefore clears every
  overlapping rectangle along that direction (no body slowly sinking into a
  compound shape, no deep overlap hidden behind a grazing one, and the result
  does not depend on which body is treated as the mover), while only one
  velocity/friction response and one contact recording happens per pair per
  iteration (an impulse per rectangle would count one physical contact several
  times and inflate e.g. jump height). Remaining overlap along another axis is
  left for a later solver iteration.
- **A kinematic body** (`KinematicObject2D`) is static-for-collision-response
  (never pushed) but moves at a prescribed velocity, and a resting body
  matches that velocity in the platform's reference frame. This alone - no
  extra carry/re-seat mechanism - makes a resting rider follow a moving
  platform on both axes, because the player's own movement is force-based
  rather than overwritten from input. Patrolling kinematic platforms brake
  smoothly to rest at each route end and speed up again, with a global
  acceleration limit (`PhysicsConstants.KinematicPlatformAcceleration`, not
  per world), so riders experience a finite acceleration held by ordinary
  friction instead of an instant velocity reversal.
- **Locomotion is traction-limited and has a reaction.** The grounded
  walk/crawl motor force is clamped to the real grip - combined friction times
  the body's weight, with no extra calibration factor - so grip is what limits
  how quickly the player speeds up, stops and turns. The player's own grip
  (the `Human` material's friction) is the lever for that feel, not a hidden
  multiplier. The equal and opposite force is applied to a finite-mass
  support, so a crate is only shoved backward if its own grip on whatever it
  rests on is weaker. A ground jump aims for a relative vertical take-off speed
  and pushes a finite-mass support down with an equal and opposite impulse (no sideways
  kick: the jump itself doesn't change the player's horizontal speed). The
  motor force *is* the foot friction, so contact friction is not applied again
  to a walking player's ground contact (climbing/hanging/swimming excluded).
- **Friction takes back the distance it cancels (static friction).** Bodies move
  before collision runs, so friction alone would remove velocity but leave that
  step's slide, letting any push creep a well-gripped body. When friction removes
  horizontal velocity, the matching displacement from this step's integration is
  undone too (`Body2D.UndoFrictionDisplacement`, capped by what was actually
  travelled), so a body held by sufficient grip stays exactly put.
- **Fast bodies are substepped.** Collision is discrete, so each fixed step is split
  into equal substeps when the fastest moving body would travel more than
  `PhysicsConstants.MaxStepTravelCells` (0.4 cells) in it, up to `MaxSubsteps` (8).
  Ordinary walking/jumping stays one step; only fast falls/launches pay extra.
  Support reference velocity and friction undo use the actual (sub)step length.
- **A resting body's support is judged by its actual movement, not its
  `Velocity`, unless the support is static.** A finite-mass support (e.g. a
  patrolling `DynamicObject2D`) has its own `Velocity` cancelled every step by
  contact friction with whatever stands on it, so reading `Velocity` would make
  riders target ~0 and slip off. `Body2D` records its horizontal displacement
  over the last physics step, measured from its position at the end of the
  previous collision pass so collision pushes are not mistaken for motion
  (`LastStepDisplacementX`, `ReferenceVelocityX`); distance that friction takes
  back (`UndoFrictionDisplacement`) comes off it too, so a stalled driven body
  (e.g. an overloaded patroller) reports no phantom speed to its riders,
  and both `GetSurfaceVelocityX` (the rider's motor/patrol reference) and
  `CollisionSystem.ResolveContact`'s tangential friction use it for the
  supporting body (identified by the contact normal, so pair resolution order
  doesn't matter). Static/kinematic supports keep using their prescribed
  `Velocity` unchanged.
- **Contacts are tracked as explicit `ContactType` flags** per body
  (`Body2D.AddContact`/`HasContact`), cleared once per physics step (except on sleeping bodies);
  `IPhysicsBody.IsGrounded` is derived from this (`HasContact(SurfaceBottom)`),
  not a separately stored, ordering-bug-prone flag.
- **Facing/patrol targets are resolved relative to the surface the body
  rests on** (`Body2D.GetSurfaceVelocityX`), not raw absolute velocity - this
  prevents a platform's own speed from visually flipping a rider's facing,
  or making a patrolling hazard slide/drift on a fast-moving platform.
- **Jumping keeps a reduced (not zero, not full) horizontal "air control"
  force**, so momentum at the moment of jump-off (including any platform
  carry) decays/steers gradually across the arc instead of snapping
  instantly to a target speed.
- **Medium drag/buoyancy is a distinct concept from surface friction, implemented as
  continuous force-accumulator terms** (`PhysicsSystem.StepMovingBodyWithForces`/
  `ResolveCurrentMedium`): a body's ambient medium (`Body2D.CurrentMedium`, defaulting to
  `Air`) is resolved once per physics step from whichever static, passable `Body2D`
  overlaps it - the highest-`Density` overlapping volume wins on a tie, deterministic
  regardless of placement order. Buoyancy is Archimedes' principle (medium density times
  the body's own volume, opposing gravity); drag is a quadratic (velocity-squared)
  velocity-opposing force scaled by the medium's `Viscosity` and the immersed body's own
  frontal area (`Size.Y` for horizontal drag, `Size.X` for vertical drag - the 2D analog
  of cross-sectional area, so a bigger body feels proportionally more drag than a smaller
  one of the same material, not just more buoyancy from its larger volume) - the
  physically accurate model for fluid drag at ordinary speeds, and one that sheds a fast
  impact's momentum far more aggressively than a slow drift's, which keeps a body from
  significantly overshooting/bouncing back out after a hard water entry. Both forces are
  additionally scaled by the body's own **overlap fraction** - the fraction (0 to 1) of
  the body's own collision area actually overlapped by the winning medium volume (both
  axes), computed by summing the overlapped area of every qualifying same-material volume
  (which are assumed not to overlap each other - a world design rule, not enforced in
  code). A body only grazing a medium's edge therefore receives proportionally less
  buoyancy/drag than one fully immersed (fraction 1.0), which is what stops a
  surface-floating body from visibly bouncing as it crosses the binary overlap boundary
  each frame. When no placed medium volume overlaps at all, the body resolves to the
  default `Air` fallback at fraction 1.0 (not 0.0) - `Air` is the ambient substance
  filling all otherwise-unoccupied space, not a bounded world-authored volume with edges
  to be partially submerged past, so its own low but nonzero `Viscosity` drag keeps
  applying continuously everywhere outside a placed volume. A placed volume of the same
  density as `Air` also displaces the fallback (only a strictly less dense one is
  skipped), so an `Air`-material volume can be a medium in its own right (e.g. a wind
  tunnel). Only bodies implementing `IMediumAffected` (mirroring `IGravityAffected`)
  receive these forces; `CurrentMedium` itself is still resolved/exposed for every body
  regardless, for future systems (e.g. a swim pose) to read.
- **A flowing medium is a `FlowZone2D`, not a separate force source.** A `FlowZone`
  placement is a `TwoStateObject2D` and `ILinkTarget` (like `Gate2D`) that is also a
  medium volume (`Medium = true`), with a `FlowDirection` (Left/Right/Up/Down) and a
  `FlowSpeed` (the speed the medium moves at). While on (switched
  through `Link`) `ResolveCurrentMedium`
  that volume's own overlapped share of the body, and the drag terms use the body's velocity
  *relative to the flow* - so flow acts only through the medium's viscosity (a fan's air is
  gentle, a river's water strong), a body moving with the flow feels less drag than one against
  it, and bodies that are not `IMediumAffected` ignore it. Overlapping flow zones are avoided
  by design rule, not summed or enforced.
- **`ResolveCurrentMedium`'s scan only accepts placements that opt in**
  (`PhysicsSystem.IsMediumVolume`): static, passable terrain with `Body2D.IsMediumCandidate`
  set, which a world designer does per placement with `Medium = true` (`WaterSurface`/
  `BodyOfWater`) to represent an occupiable ambient space. It is an opt-in flag rather
  than an exclusion list of body types, so no other passable body (hazard, collectable,
  effect, open gate, switch, ladder) can leak its own material into the medium
  resolution and cause unintended buoyancy or swimming. `IsClimbable`/`IsHangable`
  terrain is still never a medium.
- **Running surfaces (conveyors, running ropes) are not a body type**: a moving surface on
  a body that stays put is the per-placement `Body2D.ConveyorVelocity`
  (`ConveyorDirection`/`ConveyorSpeed`), orthogonal to `Kind` like
  `IsClimbable`/`IsHangable`. `Body2D.GetCarryVelocity()` (own `Velocity` +
  `ConveyorVelocity`) is the single carry speed, so moving climbables/hangables (kinematic
  patrol) and running surfaces (horizontal and vertical) share one path: grips use it as
  their reference frame, and resting contacts use it as the solid's collision velocity
  (friction drags riders along) and as the support speed for walking and jumping
  (`GetSurfaceVelocityX`). Named `Conveyor*` rather than `Flow*` because it prescribes a
  carry velocity, not a push force inside a medium. Only static (including kinematic)
  bodies may have one; the loader rejects the keys on movable bodies, whose surface
  already moves with the body and whose motion the collision response, not a prescribed
  speed, decides.
- **Climbing/hanging are grip-based**: holding a climbable/hangable records a grip
  contact (`ContactType.Climbable`/`Hangable`); gravity stays on and the grip motor
  (`UpdateMotorForce`) carries the weight. Climb/hang targets, grip-speed checks and
  jump-offs are relative to the gripped body's `GetCarryVelocity()`. The grip motor and
  jump use the same limit: combined friction x mass x `GripStrengthPerMass`, independent
  of gravity. Gravity remains a load that grip must overcome. Hanging uses the motor to
  catch and align the hands, with no direct position or velocity snap; an existing hang
  can reach a limited distance below its current surface while the motor responds, without
  reaching an unrelated surface. A grip too weak to hold slips out of reach. Collision
  currently discovers static/kinematic climbables and hangables; the motor's reaction also
  supports finite-mass grips if supplied. Bodies passing a passable climbable/hangable
  without gripping get no friction from it.
- **Climbable and hangable are behaviours, not object types**: the
  per-placement `Climbable`/`Hangable` flags decide how the player holds on,
  independent of what the object looks like. A ladder, pipe or rope sprite is
  just a representation - typically climbable when upright and hangable when
  lying flat (a rope can be either). Code and docs talk about climbables and
  hangables, not ladders and pipes.
- **Climb style is visual only**: an optional per-object `ClimbStyle` (`Climb` default,
  `Shimmy`) picks the player's climbing pose (`Climb` or `Shimmy`); physics, grip and
  constants are identical. The style of the surface grabbed first is kept for the whole
  climb, and a Player sprite without a `Shimmy` pose falls back to `Climb` (`Shimmy` is
  deliberately not in `Player2D.RequiredPoses`). `Shimmy` clips live in the `Climb` clip folder
  as `climb_shimmy_*`.
- **Gates and switches share one two-state mechanism** (`TwoStateObject2D`): a
  `Gate2D` is a door/hatch whose `IsPassable` follows its state; a `Switch2D` is a
  passable button/lever; a `Device2D` is a machine-like object that swaps clips (a
  fan, lamp) and applies its conveyor velocity (e.g. a rope) only while on - or always,
  when unlinked, like a `KinematicObject2D`. A switch drives every `ILinkTarget` sharing its `Link` name (one switch to
  many targets, validated at load); `Latching` (per placement, default false)
  decides whether it toggles or only turns on. A gate is key-operated (`Lock`, stays
  open) or switch-operated (`Link`), never both. Hazards operating switches are not
  implemented.
- **Timed on/off is a capability of `TwoStateObject2D`**, not a separate system: optional
  `OnSeconds` (and `OffSeconds`, defaulting to `OnSeconds`) make any two-state object
  alternate by itself, ticked by `AnimationSystem`. A hazard that blinks is a
  `TimedHazard2D` (hurts only while on, via `IHazardBody.IsHarmful`); a hazard is static,
  moving, or timed - a moving blinking hazard is not supported. The off clip is optional
  for it: no `Clip` means nothing is drawn while off.
- **Touching a hazard affects only the player** (plus stomps and thrown stones on killable
  hazards): other moving bodies, such as patrolling enemies, are not hurt or shown an effect.
- **A body's own force/impulse (motor force, jump-off) is never scaled by the ambient
  medium.** A medium resists self-propelled motion only through its passive buoyancy and
  quadratic drag above, which already grow with speed and frontal area. Force gains
  (`MotorForceMultiplier`, `PatrolForceMultiplier`) and jump-off speeds are calibrated
  for that model directly.
- **World edges are a symmetric, contact-free safety net, not world geometry.** All four
  edges clamp a body back inside and reflect only the velocity heading into the edge, using
  the body's own restitution (with the resting-bounce threshold). They have no material,
  apply no friction and record no contacts, so a body touching only an edge is not grounded.
  Worlds must provide their own floors, walls and ceilings.
- **Added mass is not modelled.** A body accelerating through a fluid does not also
  accelerate the surrounding fluid, so bouncy bodies keep bouncing slightly longer
  underwater than they would in reality.
- **Physics/Collision run on a fixed timestep accumulator, not a variable/capped
  per-frame step** (`GameLoop`'s `_physicsAccumulatorSeconds`). Each frame's elapsed
  time is added to the accumulator, which is drained in whole steps of exactly
  `PhysicsConstants.FixedPhysicsStepSeconds`; any remainder carries over to the next
  frame. Identically sized steps keep collision and pose resolution deterministic
  regardless of frame-rate jitter (a variable step shifts the exact instant contact is
  gained or lost, causing grounded/airborne pose flicker). Physics, Collision and
  `World2D.ApplyPendingRemovals` run for every step (and every fast-body substep), so a
  large catch-up delta never lets a body integrate several times before being
  collision-checked. Animation/camera/render run once per real frame. The accumulator
  is reset when a new world finishes loading, so loading time is not burned through as
  extra steps on the first frame.

## Assets & Materials

- **A world object can be "materials-only"**: naming `Material`+`Width`+
  `Height` instead of `Asset`+`Clip` synthesizes an in-memory `SpriteAsset`
  (`SyntheticSpriteFactory`) filled with the material's `Character`, reusing
  the exact same body/collision/render pipeline as any sprite-backed object.
  `Span`/`TileAxis` don't apply to these (no smaller authored unit to tile).
- **A sprite can take its glyph from its material**: cells drawn with the
  sprite's `[Layout] MaterialChar` placeholder render as the body's material's
  `Character` (swapped by `WorldRenderer`; physics is unaffected, placeholder
  cells are solid). Used by the shared Door/Hatch so one drawing serves every
  material, picked per placement with `Material`. A material without a
  `Character` used with such a sprite is a load error. A closed door is as
  thick as a wall and a closed hatch as a floor; their single one-shot movement clip swings
  right/down on opening and plays backwards on closing. Passability changes immediately;
  closing collision uses the first, closed frame throughout the visual animation. Key and switch
  operations refuse to close a gate over a solid moving body; a blocked linked gate prevents
  the whole switch operation so its targets remain synchronized.
- **Tileable placements set their length with `Span` (cells), not a repeat
  count.** The authored unit may be any number of cells long; the unit is
  repeated to fill the span exactly, cutting the last unit short when needed.
- **The color palette is 36 codes** (`0`-`9`, `A`-`Z`).
- **Variants of one kind of sprite may be grouped in a shared subfolder**
  (`Asset = Group/Variant` reads `Sprites/Group/Variant/`). Each variant stays
  a complete, independent asset with its own settings and clips, and its files
  keep the plain variant name as prefix - grouping only organizes folders, it
  adds no shared settings or inheritance between variants.
- **World building kits are styles, expressed as clips**: each architectural
  component is one sprite family (`Floor`, `Wall`, `Column`, `ColumnCapital`,
  `ColumnBase`, `AnchorVertical`, `AnchorHorizontal`, `Ornament`) with one clip
  per style (`ancient`, ...), plus one `{Style}Stone`-like material per style
  for physics and default colors. A world names its placements
  `{Style}{Component}`; swapping style means changing clip and material, never
  geometry. Footprints are shared across styles so a world can be restyled
  without re-laying it. Column shafts use half blocks at their sides so
  capital and base read wider at the same width; walls match a door's width
  so a door fits flush under a short wall. Solid vs. background use is
  chosen per placement via
  `Passable`. Shared pieces (ladders, pipes, doors, hatches) are recolored per
  style through placement `ForegroundColor`/`Material`. Each style has a
  `{Style}TestWorld` showcase world.
- **Posed bodies** (`[Poses]` section) resolve
  their active clip - and facing, including a vertical axis for `Climb` - from
  each clip name's own suffix
- **Multi-frame animation is supported per clip** (`[Animation.{clip}]`
  timing overrides, `DefaultFrame`, `Reverse` for initial playback direction, and
  `AnimationMode.Off` as an explicit opt-out for multi-frame-but-non-animating assets), including subtle idle
  animation (e.g. blinking) using the same mechanism as static shape
  variants.
- **Animation that depicts movement is travel-driven, not time-driven**: a
  clip with `CellsPerFrame` steps one frame per that many cells travelled
  (`Body2D.AdvanceAnimation`), so the visual always matches the actual speed -
  including acceleration, being blocked, and riding moving surfaces - with no
  per-speed retuning. Travel is measured along the clip's own axis and
  relative to what the body pushes against (`GetAnimationTravelVelocity`):
  ground (incl. belts/platforms) while grounded, the gripped climbable/hangable
  (incl. its running speed) for the player, the medium's flow otherwise; a
  static body uses its own `ConveyorVelocity`, a `FlowZone` its `Flow`. So a
  player hanging still on a running rope shows the idle clip with the hands
  locked on the moving strand. `FrameDurationSeconds` stays the fallback when
  there is no travel source; idle, jump, flying/swimming creature and fan
  clips stay time-driven.
  - Player moving clips share a 2.4-cell stride; swim clips use a longer
    5.4-cell stroke.
  - Flowing water surfaces use `CellsPerFrame = 3`: a body only takes up part of
    a flow (by overlap, and gradually), so waves at the full flow speed looked
    far faster than anything drifting in it.
  - Running surfaces (rope, conveyor) use `CellsPerFrame = 1`
    with a 3-cell repeating unit that shifts one cell per frame, so the
    pattern moves at exactly the surface speed and its direction is
    unambiguous (a 2-cell unit shifted by one cell reads either way).
- **`Kind` is mandatory in `_objects.ini`** and its values match concrete
  class names minus the `2D` suffix (`StaticObject`, `DynamicObject`,
  `KinematicObject`, `StaticHazard`, `DynamicHazard`, `KinematicHazard`,
  `Collectable`, `Gate`, `Switch`, `Device`, `FlowZone`, `Player`) - no
  implicit fallback.
- **"Hazard" is the single harmful-body concept**: each motion model
  `*Object` and a harmful `*Hazard` variant, and being killable by a stomp is
  a per-instance flag (`IKillableBody.IsKillable`), not a separate type.

## Sound

- **Sounds are synthesized (Web Audio) and defined in `SoundLibrary.ini`**,
  not audio files; game logic only queues sound names on `World2D`, and
  `ISoundPlayer`/`audio.js` are the only audio code.
- **No hard-coded fallback sounds**: an undefined sound is silent, so
  designers can make silent worlds.
- **Player movement sounds are per pose** (`[Sounds]` in the sprite
  settings), played on animation frame steps, kept subtle.
- **Placement sounds name the object's own event**: `CollectSound`, `UseSound`,
  `HitSound` and `OperateSound` describe collection, use, throwable impact and
  switch-driven operation. `PortalEnteredSound` and `PortalArrivedSound` belong to
  the player's placement. A throwable's `HitSound` plays on physical impacts
  (including while loose) at the resting-bounce threshold, not on passable overlaps;
  its damage threshold is separate. `HurtSound` and `KillSound` belong to the damaged
  player or hazard; surviving damage and fatal damage play only the corresponding
  sound. Hard landings use the player's damage sounds too. The old `Sound`,
  `PickupSound` and `TurnSound` keys are rejected.
- **Each sound is a single pitch slide**; multi-note sounds are not
  supported (kept simple for now).
- **The world-select menu plays subtle `MenuMove`/`MenuSelect` sounds** when
  the selection moves / a world is confirmed. `GameLoop` detects both (so
  `WorldSelectScreen` stays audio-free) and loads the global-only sound
  library at startup, since no world library exists yet in the menu.

## World Objects & Capability Model

- **Object keys that belong together default each other instead of requiring each other.**
  Setting any key of a group switches the feature on and the missing keys fall back to
  defaults: a conveyor or flow direction falls back to the clip-name suffix
  (`_left`/`_right`/`_up`/`_down`), conveyor/flow/kinematic patrol speeds to
  `GameDefaults`, and a patrol bound switches patrol on with its missing opposite bound at
  the world edge (`Patrol = false` overrides). Loading fails only when a value cannot be
  read or the configuration is broken (no direction at all, Gate with both Lock and Link,
  unmatched switch links). `World2D.ObjectSection` reads every key this way.

- **`World2D` holds one generic `List<Body2D> Objects`**, not separate
  per-category collections; `PhysicsSystem`/`CollisionSystem`/`WorldRenderer`
  all iterate it once and filter by capability interface
  (`IPhysicsBody`, `IGravityAffected`, `IHazardBody`, `ICollectableBody`,
  `ICollectorBody`, `IKillerBody`, `IKillableBody`, `IPosedBody`,
  `IPatrolBody`, `IMotorBody`, `IClimberBody`, `IHangerBody`,
  `ISwimmerBody`) rather than
  concrete type or maintaining separate lists - adding a new object category
  never requires touching every system's iteration logic.
- **Object removal is always deferred to end-of-frame**
  (`World2D.QueueRemoval`/`ApplyPendingRemovals`), generic over any `Body2D`,
  so no system mutates `Objects` mid-iteration.
- **Each `*Hazard2D` is its `*Object2D` plus hazard capabilities**
  (`IHazardBody`, `IEffectTrigger`, `IKillableBody`), implemented as a
  subclass (`StaticHazard2D : StaticObject2D`, `KinematicHazard2D :
  KinematicObject2D`, `DynamicHazard2D : DynamicObject2D`). Patrol and pose
  behavior live on the object, so a harmless body can patrol too.
- **`DynamicObject2D` (and so `DynamicHazard2D`) patrols via its own mass-scaled force**
  (`IPatrolBody`), independently on the X and/or Y axis. Gravity is an ordinary force with
  no patrol special case; each axis has its own gain (`PatrolForceMultiplierX`/`Y`),
  cruise speed and force cap (`PatrolMaxForceX`, default unlimited, and `PatrolMaxForceY`,
  default 0 = no vertical thrust). Caps only limit force, never switch an axis on or off.
  An axis with no range holds its spawn position via a spring target speed; fliers stay
  gravity-affected and get lift from `PatrolMaxForceY`. Facing intent is the sign of the X
  target velocity (dead-zoned), so a body settled on its hold position shows its idle
  pose. A blocked patroller turns early by a per-axis timer rather than on first contact:
  it runs while speed toward the heading is below a fraction of cruise speed (reset by
  progress, not by losing contact, so bouncing off still counts) and flips the heading
  after `PatrolBlockedTurnTime` (default 1 s) if a contact sits on the heading side. So
  hazards still push into the player, strong bodies shove light obstacles aside, and walls
  or heavy obstacles turn it back. `KinematicObject2D` patrols differently:
  acceleration-limited prescribed velocity, no forces.
- **Player state is four counters: Lives, Health, Rings and Score.** Health is a
  0-1 bar for the current life (HUD bar); Lives, Rings and Score are shown in
  the HUD text. Ordinary (non-fatal) hazard contact removes the hazard's
  `Damage` (placement key, default 1 = whole bar; e.g. 0.2 toxic plant, 0.5
  flying insects), once per fresh contact (rising edge on
  `_activeHazardContacts`), clamped at 0. When the bar empties,
  `CollisionSystem` removes one life and starts a `PendingTransition.Death`
  (see the pending-transition bullet below)
  respawn that follows. At 0 lives the death leads to a "Game Over!" screen
  instead of a respawn.
- **Fall damage is a landing speed, not a height.** A step's highest impact
  speed on the player's bottom contacts with non-hazards (relative closing speed,
  so moving platforms and water drag are accounted for naturally) reaching
  `FallDamageImpactSpeed` (default: a 10-cell drop at default gravity) removes
  `FallDamage` (default 0.2) through the same health/death path as a hazard hit.
  Landing on a hazard is a squash or hazard hit, never fall damage. There is
  no grace period after a spawn or respawn: worlds place the player and
  checkpoints near the floor instead. World edges record no
  contacts and so never cause fall damage.
- **A survived hazard hit starts a safe window.** For
  `GameDefaults.DefaultPlayerRecoverySeconds` (1 second; the player's
  `RecoverySeconds` key overrides it) after a non-fatal hazard hit, hazards can't
  hurt the player; hazard contacts are not recorded during it, so one still touching
  when it ends hurts immediately. It is a timer on the player (ticked per physics
  step), not a per-hazard cooldown, so several hazards cannot stack hits either. Hard
  landings neither start nor respect it: a hard fall always hurts. Respawn
  clears it.
- **Hurt and died effects are mutually exclusive.** Damage is applied before
  choosing the effect: a hit the player survives plays `HurtEffectClip`, a fatal
  hit plays only `DiedEffectClip`. An already-dead player (during the death
  delay) gets no further hazard sounds or effects.
- **How a hazard is touched follows how the solver treats it.** A solid hazard
  (any moving hazard, or a static one that is not passable) is pushed apart,
  never left overlapping, so it is touched only through a surface contact
  resolved this frame (`Body2D.HasSurfaceContactWith`); a passable hazard is
  touched by overlap. Climb/hang grip contacts are not surface contacts and
  never count. A killable hazard is squashed only when the impact speed recorded
  for the killer's bottom contact with it (`Body2D.GetContactImpactSpeed(SurfaceBottom, hazard)`,
  measured before the response and stored per contact side, so a hard side hit
  is never mistaken for a landing) reaches its `KillImpactSpeed` (placement key,
  default `GameDefaults.DefaultKillImpactSpeed`); anything else is an ordinary
  hit. Any bounce off a squash is just the normal restitution response, except
  that a squashed hazard that survives bounces the squasher up
  (`GameDefaults.StompBounceSpeed`).
- **Killable hazards have health that mirrors the player's.** `Health` (default 1),
  a post-hit safe window (`GameDefaults.DefaultHazardRecoverySeconds`, 0.5 s) with a following
  `HurtEffectClip` and `HurtSound`, and death at 0. There is no stun: a hurt hazard
  keeps moving and can still hurt the player, unless its `Damage` is 0: then it is harmless to touch (no sound, no effect) yet still killable. Death is permanent (removed; the
  `EffectPersists` husk stays; a moving `DynamicHazard2D` leaves it as a decorative `CorpseObject2D` physics body that falls and rests on the floor and that all other bodies pass through, other hazards a static `EffectInstance2D`). A squash takes the killer's `StompDamage` (default 1),
  so the defaults keep the one-squash kill.
- **Throwables are ordinary dynamic objects** (`ThrowableObject2D : DynamicObject2D`).
  They bounce and settle by material; there is no faked rolling. A thrown one hurts
  a killable hazard once per fresh contact at or above its `HitSpeed` (solid hazard:
  the highest surface impact speed; passable: relative speed) and is never hurt
  itself. It lands when asleep or grounded and slower than
  `GameDefaults.ThrowableRestSpeed`. A `Reusable = false` object is used up on its
  first hazard hit or on landing.
- **A loose throwable and a moving hazard ignore each other physically**
  (`CollisionSystem.IgnoresMoverCollision`, checked in the mover-pair loop before any
  push or wake), so a patrolling hazard never shoves a lying stone along. A thrown one
  stays solid against hazards so its hits still collide. This is a fixed rule for all
  throwables, not an ini setting; hazards still push ordinary dynamic objects.
- **Gear slots are game-wide, listed in `Global/Settings.ini` `[Gear]`**
  (`Name = SpriteAsset`), not per world. Gear always uses the asset's `icon` clip,
  a one-row drawing up to three cells wide; `^` in either color layer means the
  active interface foreground, while an empty foreground cell uses the
  interface background. A `Gear2D` is an inventory slot, not a body; it keeps
  picked-up bodies themselves so a throw puts the same object (with its tuning)
  back. A throwable whose gear isn't listed cannot be picked up. Every listed
  slot is always shown; counts remain dimmed, with no "discovered" state.
  `Player2D.SelectedGear` is the slot chosen in the gear ring, or the first slot
  until one is chosen; it does not jump to another slot when it runs empty (the
  action key then does nothing with it). Health packs are stored in the
  `HealthPack` slot.
- **Keys are gear like any other**, carried in the `Key` slot (`Key2D.GearName`;
  not listed = keys can't be picked up), not in a separate key list or the HUD.
  The key slot must be selected to unlock a gate; the gate then still checks that
  the slot holds a key for its lock. This keeps "the action key uses the selected
  gear" true for every gear. The active settings list only the Key, Rock and
  HealthPack slots; unused placeholder slots are not shown.
- **Gear is shown in its own gear box, chosen in the gear ring.** The HUD box
  itself never shows gear; a fixed gear box right of it (`GameLoop._hudGearBox`)
  always shows the selected gear's monochrome sprite icon and dimmed count as
  `x3` (the same `x` form as the ring). Both surfaces render the same loaded
  `IconFrame` through `UIRenderer.AddMonochromeIcon`. Holding
  `Tab` (`InputState.IsGearRingKeyDown`, browser focus move suppressed in
  game-interop.js) opens the gear ring (`GameMode.GearRing`, game paused like the
  exit prompt): one small dark box per slot around a circle, snapped to whole
  cells, in `[Gear]` order clockwise from the top, with the highlighted one's name
  (normal interface color) and `x` count (dark) in the middle, like the gear box. It grows out of the gear box and shrinks back into it
  (`GearRingScreen.OpenSeconds`/`CloseSeconds`). Left/Right moves the highlight
  (wrapping); releasing `Tab` takes the highlighted gear and `Esc` cancels - the
  action key does nothing in the ring. Keys still held when it closes are not read
  as fresh presses in play, so a cancelling `Esc` does not open the exit prompt.
  Grid-snapped boxes were chosen over a drawn circle because the font has no
  glyphs for smooth curves.
- **Every selection box looks the same** (`UIRenderer.SelectionColor`): lit the
  moment it moves, then glowing light -> normal -> dark -> normal while it stands
  still (`UIRenderer.GlowColor`, also the end screens' settled glow). Used by the
  world-select thumbnail box (`WorldSelectScreen.TitleScrollSeconds`) and the gear
  ring's highlighted slot (`GearRingScreen.StillSeconds`, counting only once the
  ring is fully open, so it stays lit while the ring moves). The difference is
  only that world select draws no box around unselected worlds, while the ring
  draws every slot in a dark box.
- **The action key does one thing per press, in priority order:** operate a switch,
  open/close a key gate in reach, use a selected health pack when health is below
  full, or throw from the selected gear. A health pack is used whole and consumed
  only on successful healing; selecting it or pressing the action key at full
  health leaves it in inventory. A thrown item appears just in front of the player,
  on the side the player last faced, at head height, flying off at the object's
  `ThrowSpeed`/`ThrowAngle` plus the player's velocity, and is refused (keeping
  the item) when that spot is blocked.
- **Gravity is a non-negative magnitude that always pulls down.** Inverted
  gravity is not a concept; negative or non-finite values are rejected at load.
  Zero means weightless; weight-scaled patrol caps then give no force, except
  an unlimited cap, which stays unlimited.
- **A world load seeds key-edge history from the keys currently held**
  (`PhysicsSystem.Reset(InputState)`, `_wasActionKeyDown`), so a key held
  through loading is never read as a fresh press.
- **`GameLoop.Stop()` is a flag checked after every await** in startup, world
  loading and frame processing; nothing new reaches the browser adapters once
  the page has begun disposing them. No cancellation tokens are used.
- **Rings** (`CollectableType.Ring`) add one to `Player2D.Rings` and 5 to Score
  (`GameDefaults.RingScore`); they do not heal. Every `RingsPerLife` rings (`[World]
  RingsPerLife`, default 25) the count resets to 0 and a life is added. Rings survive
  respawn.
- **A world's starting lives are `[World] Lives`** (default
  `GameDefaults.PlayerStartingLives`), applied to `Player2D.Lives` when the
  player object is spawned - mirroring how `[World] Title` and `[Physics]
  Gravity` are already world-level settings rather than per-placement keys.
- **Collectable variants are driven by a required `Type` key on the
  placement** (`Diamond`/`Ring`/
  `Checkpoint`/`WorldEnd`/`Key`/`HealthPack`), not
  asset-level `_settings.ini` key - matching how
  `Killable`/`EffectPersists`/`EffectClip` are already placement-level keys,
  and keeping the same sprite asset (e.g. `Ring`) reusable across multiple
  collectable variants. `Collectable2D.Type` is nullable with no default
  variant: an omitted/unrecognized `Type` is left `null`, which
  `CollisionSystem` treats as inert (no effect, not removed, no player state
  changed) - deliberately, so a placement that forgets `Type` fails loudly
  during testing instead of silently behaving like some other variant.
  `Diamond`/`Ring` are removed on pickup and increment
  `Player2D.Score` (and `Rings` for `Ring`);
  `HealthPack` is removed on pickup and stored in the `HealthPack` gear slot;
  using it consumes one whole pack and restores `Player2D.Health` to full only
  when the player is hurt, playing its optional `UseSound`; `CollectSound` remains
  the pickup sound;
  `Key` is removed on pickup and goes into the `Key` gear slot (inert without a
  `Lock` name or that slot; the lock name lives on `Key2D`, not on
  `Body2D`); a `Gate2D` with a matching `Lock` is opened by the action key
  (`E`/`Enter`) when the player is within one cell of it with the Key gear selected;
  the same press closes it again (refused while a solid moving body is in the gate).
  Keys are permanent (never used up) and carry across worlds, so lock names must be
  unique across a world and its sub-worlds. Key gates stay key-only (no `Link`).
  `Checkpoint`/`WorldEnd` are also removed on
  pickup like every other collectable; a collectable's own
  `Collectable2D.EffectPersists` (mirroring `IKillableBody.EffectPersists`
  on hazards) controls whether its pickup effect remains in its
  place afterward
  away - used by `Checkpoint`/`WorldEnd` in practice, but available to any
  collectable variant.
  `Checkpoint` records `World2D.RespawnPoint`, consumed by `World2D.Respawn()`;
  `WorldEnd` starts a `PendingTransition.WorldEnd`, consumed by `GameLoop`
  (see below).
- **Player effects follow the player**: every effect the player plays stays
  attached to the player at its clip's `OffsetX`/`OffsetY`, and may tint the
  player via `TintColor`/`TintAlternateColor`/optional `TintThirdColor`/`TintBlinkSeconds`
  in the same `[Animation.{clip}]` section (no separate tint structure). The tint is
  temporary and wins over the normal foreground color chain. A hazard's hurt
  effect follows (and may tint) the hazard the same way; other non-player
  effects stay where they were spawned. A clip with a tint and no drawing files
  is a glow-only effect (one blank cell); every enemy's `hurt` clip is one, the
  same short red/orange blink (`L`/`M`) the player glows when hurt, without the
  player's sparks. While a world end or death is pending,
  the player is shown standing still in its default pose's idle clip (solved in
  code, not a setting, since physics is frozen then too).
- **World end and death share
  `CollisionSystem` sets `World2D.PendingTransition` (`WorldEnd` or `Death`)
  and spawns the player's own effect clip (`WorldEndReachedEffectClip` /
  `DiedEffectClip`); the collectable's/hazard's sound plays as usual. While a
  transition is pending `GameLoop` skips physics/collision (effects keep
  animating) and, after `PendingTransitionSeconds`, carries it out: `WorldEnd`
  -> `GameMode.WorldComplete`; `Death` with lives left -> `World2D.Respawn()`
  and play resumes; `Death` with no lives left -> `GameMode.GameOver`. Both end
  screens are the same minimal mode (`OnEndScreenFrameAsync`): the last frame
  with a centered "World Complete!"/"Game Over!" `UILabel` (solid black
  background, inside a `UIFrame` box that ripples inward from a compact
  larger size; green for World Complete, red for Game Over), dismissed by a
  confirm (action or jump) key press (only after that key has been released
  once, so a held key doesn't dismiss it instantly), then back to
  `GameMode.WorldSelecting` - the same reset path as a confirmed exit.

## Player Movement & Input

- **`Up` (directional) and `Jump` (action) are always distinct inputs**, even
  for the ordinary ground jump.
- **Two full, independent key sets** are supported for local co-op/preference
  - "Player 1" (arrows + `Space` + `Enter` as action) and "Player 2" (`WASD` + `Left Ctrl`
    + `E` as action).
- **"Stance" is the stored choice, "pose" is the displayed result.** The
  player's `Stance` (`Walk` = upright/stretched, `Crawl` = compact/clamber) is
  the only stored state (`IsClambering` is derived from it while hanging).
  Crawl only exists on the ground (or as Clamber while hanging): the Crawl<->Walk
  toggle is ground-only, and the stance is forced to `Walk` whenever airborne
  without a grip, so there is no invisible mid-air crawl that would block a
  climb grab or land the player crawling. Pose (Walk, Crawl, Jump,
  Climb, Hang, Clamber, Swim) is derived each frame from stance plus situation.
- **Ground and hang each have their own structured "stance sequence"**
  (Crawl<->Walk on the ground; Clamber<->Hang while hanging, inverted to
  match arm position rather than screen direction), and each locomotion mode
  has its own jump-off speed (`GroundJumpSpeed`/`ClimbJumpSpeed`/`HangJumpSpeed`),
  graded by how much of the body's own leverage backs the push-off:
  `GroundJumpSpeed` (both feet on solid ground) > `ClimbJumpSpeed` (legs pushing off
  a climbable) > `HangJumpSpeed` (arms only).
- **Grabbing a climbable or hangable is always deliberate**, never automatic on touch,
  so the player can jump or fall past either. A climbable needs Up or Down held
  while touching it, and an upright stance (a crawling player stands up
  first). A hangable needs Up held (not Down, which means "let go" while
  hanging) while touching it at hand level, rising or falling; a crawling
  player grabs straight into Clamber. Once hanging, the grip motor aligns the hands
  with the surface, and may slip if the grip is too weak. While climbing with no
  Left/Right held, the climb motor gently pulls the player toward the
  climbable's horizontal centre (`GameDefaults.ClimbCenteringRate`, capped at
  the side-step speed) - motor-driven rather than a hard snap, so it stays
  smooth and never fights a deliberate side-step.
- **A short re-grab block** (private to `PhysicsSystem`, one for climbing and
  one for hanging, same rules) stops a still-held grab key from re-grabbing
  the moment a jump/swing-off or let-go begins. It only blocks a *held* key:
  it is released as soon as the grab keys (Up/Down for climbing, Up for
  hanging) are let go, so a fresh press re-grabs mid-air - even on the same
  climbable/hangable - without having to land first. It is also released
  once no surface of that kind is touched any more, or on landing.
- **Swim detection is medium-based, not reach-based** (`ISwimmerBody`,
  `PhysicsSystem.IsSwimmableMedium`) - unlike `IClimberBody`/`IHangerBody`,
  which key off a specific `Body2D.IsClimbable`/`IsHangable` surface via a
  `CollisionSystem`-set touching flag, swim instead reads the player's own
  already-resolved `Body2D.CurrentMedium` directly every frame and compares
  it against `PhysicsConstants.SwimMediumMinDensity`/`SwimMediumMinViscosity`
  (a medium qualifies if it clears *either* threshold, since density and
  viscosity are independent physical properties).
  Engaging mirrors climbing's and hanging's deliberate-press model (a directional key
  must actually be held while submerged), since
  merely drifting through a passable fluid volume shouldn't force the pose
  any more than brushing past a climbable auto-climbs. Grounded/climbing/hanging
  all take priority and preempt swim entirely (`PhysicsSystem.Step`'s swim
  engage condition requires none of them apply) - a solid floor stays solid
  underwater (walking/crawling keeps working exactly as on land), and a
  climbable/hangable's own capability check is entirely independent of medium, so
  submerging one doesn't need special-casing. Up/down while swimming are
  vertical thrust (depth control, via `GameDefaults.SwimVerticalSpeed`) using
  the same continuous motor-force model as climbing's vertical axis, not a
  distinct pose/mechanic - left/right use their own dedicated
  `SwimHorizontalSpeed`. There is deliberately no swim jump-off impulse: Jump
  does nothing while `IsSwimming` (no capability/interface reserves one),
  since there is no solid surface to push off against underwater.

## Rendering & Camera

- **Interface colors live in `Global/InterfaceColors.ini`, separate from the
  world/sprite `ColorPalette.ini`.** Named tint sections (green/amber/red/blue/white,
  each Dark/Normal/Light) plus `[Defaults]` are the only source of HUD, menu,
  overlay and message-box colors. `[Defaults] Foreground` selects which tint
  section is the interface color, so the whole interface switches family at
  once; the semantic end-screen boxes keep fixed families (Green/Amber/Red).
  The interface normal tint is also the last fallback for uncolored world
  content (`GlyphBuilder.DefaultForeColor`). The canvas is the world's
  `WorldBackdrop`, else `[Defaults] Background`. The HUD and message-box text
  have no panel unless the world sets `[Colors] InterfaceBackdrop` (a code, or
  `WorldBackdrop`); `BackdropColor` was renamed `WorldBackdrop`.
- **Only two hardcoded colors**: green `#00ff00` and black `#000000`
  (`RenderConstants.DefaultForegroundColor`/`DefaultBackgroundColor`), used
  for anything missing from InterfaceColors.ini, or before/without it, so the
  game is then green on black. `game-interop.js` holds no colors; the browser
  styling (loading message, page behind the canvas) repeats the same pair in
  CSS. A drawn cell without
  a background color has no fill (null), not a default color. An absent
  background code means "next tier decides"; `BackgroundColor = None`
  (placement, sprite `DefaultBackgroundColor`, material) means "no fill" and
  stops the chain, so partial-block sprites can be see-through while keeping
  their physical material. Per-cell codes still beat placement overrides.
  `BackgroundColor = WorldBackdrop` stays for the opposite case: looks empty
  but masks what's behind it.
  Foreground has no `None`: a glyph always needs a color. Callers
  pick tints explicitly (e.g. a label's `HighlightColor`); nothing derives a
  darker/lighter tint automatically. End-screen ripples use the light tint
  for the leading box and the dark tint for the trail; once settled, the box
  (not the text) keeps glowing in steps through dark, normal and light.
- **The menu opens with a logo-only intro**: "ASCII" slides in from the left
  and "Hero" from the right (ease-out), shaded with a vertical-only gradient;
  the rest of the menu appears once it finishes (menu input is ignored until
  then, with no skip).
- **Cell/canvas pixel size is a configured, final on-screen value.**
  requires `FontWidthPixels`/`FontHeightPixels` as the final cell size
  already at whatever zoom is wanted (`GameLoop.StartAsync` throws if either
  is missing); `GameLoop`/`CanvasBridge` then size the canvas element and the
  gameplay viewport as `ViewportColumns`/`ViewportRows` (character counts)
  times that cell size (see docs/AssetFormat.md §4.4). Switching fonts or
  zoom level never requires touching game code - just updating the ini
  values. The size is not auto-detected because `measureText` does not
  reliably report a bitmap font's real native size.
- **The bundled font is WebPlus IBM VGA 8x16, shown at 2x (16x32 px cells).**
  Cells are therefore exactly twice as tall as wide, which sprites are drawn
  for (e.g. a round ball is twice as many columns as rows, counting a
  half-block as half a row). Glyphs outside the font silently fall back to
  another font, so assets stick to characters the font contains.
- **`WorldRenderer` only builds glyphs for what's inside the camera's current
  viewport** - physics, collision, and animation are unaffected and keep
  simulating every body regardless of visibility.
- **The camera follows its target with a dead zone** (only scrolls once the
  target nears the viewport edge) and is always clamped to the world's own
  bounds. On an axis where the world is smaller than the viewport the camera cannot
  scroll, so the world is centered on that axis instead (negative view origin).
- **An FPS overlay is a dev/testing toggle, off by default** (`GameLoop._showFpsOverlay`,
  `InputState.IsFpsToggleKeyDown` bound to `F`), showing a smoothed
  frames-per-second reading computed from each real frame's own raw,
  unclamped elapsed time (not the fixed Physics/Collision step size), since
  the point of the overlay is to surface real frame-pacing hitches that the
  physics fixed-timestep otherwise hides from gameplay. Displayed as the
  number leading (e.g. "144 FPS"), right-aligned so the label's column is
  recomputed each frame from its own rendered length and stays flush with
  the top-right corner regardless of digit count.
- **A second dev/testing overlay shows the world's live object count**
  (`GameLoop._showObjectCounterOverlay`, `InputState.IsObjectCounterToggleKeyDown`
  bound to `O`), stacked directly under the FPS overlay (same row-1 vs.
  row-0 top-right corner), displaying `World2D.Objects.Count` (e.g. "42 Obj")
  so a world author can watch the count drop live as collectables
  are picked up.
  right-aligned-by-rendered-length approach as the FPS overlay.
- **`World2D.Respawn()` resets the player's position, velocity, and health**
  (position to `RespawnPoint` if a `Checkpoint` has been reached, else
  `PlayerSpawnPosition` - the original `Player` placement position recorded
  once at load; velocity to `Vector2D.Zero` so no pre-death momentum
  carries over; health back to a full bar). Called by `GameLoop` once the
  `Death` pending transition has played out and lives remain (the bar
  emptying is guarded by the fresh-contact check in `CollisionSystem`, so an
  already-dead player can't retrigger it). There is no debug respawn key.
- **Background/foreground layer colors are precomputed once at world load**
  (`World2D.BackgroundForeColors`/`BackgroundBackColors`/`ForegroundForeColors`/
  `ForegroundBackColors`), since these layers are static world data;
  `WorldRenderer` just reads the precomputed color directly.

## World/Game Flow

- **Sub-worlds are extra object grids, reached through `Kind = Portal`.** A sub-world
  (`{World}/{n}/{World}_{n}_objects.*`) has no art layers and inherits the main world's
  settings, palette, materials, sounds and sprites. A portal is a static passable `Body2D`
  (`Portal2D`) rather than a `Collectable`, because it must stay in place and be reusable;
  it fires on a fresh overlap and re-arms after the player leaves (`CollisionSystem`),
  and arrival re-arms it so the player is not bounced straight back. The caller picks the
  arrival spot (`TargetEntry`, a named `Kind = Player` placement); the game does not
  remember which portal was used. Visited worlds are cached for the play session so state
  persists; progress (score, lives, health, rings, keys) is carried over.
- **Portals have no sound or effect of their own; the player owns them.** The player's
  `PortalEnteredEffectClip`/`PortalEnteredSound` play on stepping in and
  `PortalArrivedEffectClip`/`PortalArrivedSound` on arrival, mirroring the other player
  effect keys. Play is frozen briefly
  (`GameLoop.PortalTransitionSeconds`) only when the player has the matching effect clip.
  A world smaller than the window is centred by the camera instead of left-aligned.
- **World selection is a strict `GameMode` state machine** backed by a
  `Global/Worlds.ini` catalog (world metadata/thumbnails loaded without
  loading the full playable world).
- **`Esc` asks before leaving a world.** During play it freezes the game behind an
  "Exit Game?" prompt: any confirm key (`InputState.IsConfirmKeyDown`: Enter, E, Space or
  Left Control) confirms and returns to the world-select screen, a second `Esc` cancels.
  Menu and end-screen confirmations accept the same keys.
