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
  (`IWalkForceBody`/`UpdateWalkForce`) and a hazard's patrol force
  (`IPatrolBody`) both plug into the same accumulator as gravity. Climbing/
  hanging are likewise continuous convergence forces; only jump-off (stand,
  ladder, hang-swing) remains a true one-time impulse (instantaneous
  Delta-v), since a real push-off happens in a single instant, unlike
  sustained locomotion.
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
  after a mover-vs-mover correction.
- **Broad phase uses a spatial grid** (`SpatialGrid<T>`, one for solids and
  one for moving bodies) so collision cost scales with nearby objects, not
  the level's total object count. Narrow phase additionally requires actual
  rendered-character overlap (`HasCharacterOverlap`), not just merged
  collision-rectangle overlap.
- **`CollisionShapeBuilder` derives collision rectangles from two
  complementary run-length-merge passes (row-run and column-run, the exact
  transpose of each other), unioned together with duplicates removed**, so a
  curved/notched silhouette offers both shallow-vertical and
  shallow-horizontal rectangles along its flanks and the correct contact
  normal wins wherever contact happens. Duplicates must be removed because
  each rectangle is resolved independently per solver iteration; a
  duplicate would double that contact's correction and impulse.
- **Of a body's own overlapping collision rectangles against one other body/solid,
  `ResolveAgainstOtherBody` resolves exactly one physical contact per solver iteration, not one
  contact per overlapping rectangle.** The rectangle overlapping most deeply (by its own natural
  axis) defines that contact's normal; every other overlapping rectangle is then re-measured
  along that *same* normal axis, and the worst (largest) of those depths is what is actually
  corrected. Every rectangle's overlap along the established contact direction is therefore
  corrected in the same pass (no body slowly sinking into a wall), while only one
  velocity/friction response and one contact recording happens per solid per iteration
  (applying the impulse per rectangle would count one physical contact several times and
  inflate e.g. jump height). Leftover overlap along a genuinely different axis (a separate
  physical contact) is left for a later solver iteration, which re-detects fresh contacts
  against the just-corrected position.
- **A kinematic body** (`KinematicObject2D`) is static-for-collision-response
  (never pushed) but moves at a prescribed velocity, and a resting body
  matches that velocity in the platform's reference frame. This alone - no
  extra carry/re-seat mechanism - makes a resting rider follow a moving
  platform on both axes, because the player's own movement is force-based
  rather than overwritten from input. Patrolling kinematic platforms brake
  smoothly to rest at each route end and speed up again, with a global
  acceleration limit (`PhysicsConstants.KinematicPlatformAcceleration`, not
  per level), so riders experience a finite acceleration held by ordinary
  friction instead of an instant velocity reversal.
- **Locomotion is traction-limited and has a reaction.** The grounded
  walk/crawl motor force is clamped to combined friction times the body's
  weight times `PhysicsConstants.GroundTractionMultiplier` (10, a calibration for the
  stylised gravity/proportions so ordinary floors keep the snappy feel; only
  slippery pairings bind), and the equal and opposite force is applied to a finite-mass
  support. A ground jump is relative to the support's vertical velocity and
  pushes a finite-mass support down with equal and opposite momentum. The
  walk force *is* the foot friction, so contact friction is not applied again
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
  (`LastStepDisplacementX`, `ReferenceVelocityX`),
  and both `GetSurfaceVelocityX` (the rider's motor/patrol reference) and
  `CollisionSystem.ResolveContact`'s tangential friction use it for the
  supporting body (identified by the contact normal, so pair resolution order
  doesn't matter). Static/kinematic supports keep using their prescribed
  `Velocity` unchanged.
- **Contacts are tracked as explicit `ContactType` flags** per body
  (`Body2D.AddContact`/`HasContact`), snapshotted-and-cleared once per physics step;
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
  `ResolveCurrentMedium`): a body's ambient medium (`Body2D.CurrentMedium`, defaulting
  to `Air`) is resolved once per physics step from whichever static, passable `Body2D`
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
  scaled by the body's own **overlap fraction** - the fraction (0 to 1) of the body's
  own collision area actually overlapped by the winning medium volume (both axes), computed by
  summing the overlapped area of every qualifying same-material volume (which are
  assumed not to overlap each other - a level design rule, not enforced in code). A body only
  grazing a medium's edge therefore receives proportionally less buoyancy/drag than
  one fully immersed (fraction 1.0), which is what stops a surface-floating body from visibly bouncing as it
  crosses the binary overlap boundary each frame. When no placed medium volume
  overlaps at all, the body resolves to the default `Air` fallback at fraction 1.0
  (not 0.0) - `Air` is the ambient substance filling all otherwise-unoccupied space,
  not a bounded level-authored volume with edges to be partially submerged past, so
  its own low but nonzero `Viscosity` drag keeps applying continuously everywhere
  outside a placed volume. A placed volume of the same density as `Air` also displaces the
  fallback (only a strictly less dense one is skipped), so an `Air`-material volume
  can be a medium in its own right (e.g. a wind tunnel). Only bodies implementing
  `IMediumAffected` (mirroring `IGravityAffected`) receive these forces; `CurrentMedium`
  itself is still resolved/exposed for every body regardless, for future systems (e.g.
  a swim pose) to read.
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
  set, which a level designer does per placement with `Medium = true` (`WaterSurface`/
  `BodyOfWater`) to represent an occupiable ambient space. It is an opt-in flag rather
  than an exclusion list of body types, so no other passable body (hazard, collectable,
  effect, open gate, switch, ladder) can leak its own material into the medium
  resolution and cause unintended buoyancy or swimming. `IsClimbable`/`IsHangable`
  terrain is still never a medium.
- **Conveyors/ropes are not a body type**: a moving surface on a body that stays put
  is the per-placement `Body2D.ConveyorVelocity` (`ConveyorDirection`/`ConveyorSpeed`),
  orthogonal to `Kind` like `IsClimbable`/`IsHangable`. `Body2D.GetCarryVelocity()`
  (own `Velocity` + `ConveyorVelocity`) is the single carry speed, so moving ladders/pipes
  (kinematic patrol) and conveyors/ropes (horizontal and vertical) share one path: grips use it as
  their reference frame, and resting contacts use it as the solid's collision velocity
  (friction drags riders along) and as the support speed for walking and jumping
  (`GetSurfaceVelocityX`). Named `Conveyor*` rather than
  `Flow*` because it prescribes a carry velocity, not a push force inside a medium.
- **Climbing/hanging are grip-based**: holding a climbable/hangable records a grip
  contact (`ContactType.Climbable`/`Hangable`); gravity stays on and the grip motor
  (`UpdateWalkForce`) carries the weight. Climb/hang targets, snap/grab speed checks,
  the hang snap's vertical velocity and jump-offs are all relative to the gripped
  body's `GetCarryVelocity()`. The whole grip force is limited by combined friction x
  weight x `GripTractionMultiplier`; beyond that the body slides along the surface
  (no separate grip-loss rule). A finite-mass gripped body receives the equal and
  opposite reaction; static/kinematic ones absorb it. Bodies passing a passable
  ladder/pipe without gripping get no friction from it.
- **Gates and switches share one two-state mechanism** (`TwoStateObject2D`): a
  `Gate2D` is a door/hatch whose `IsPassable` follows its state; a `Switch2D` is a
  passable button/lever; a `Device2D` is a machine-like object that only swaps clips (a
  fan, lamp). A switch drives every `ILinkTarget` sharing its `Link` name (one switch to
  many targets, validated at load); `Latching` (per placement, default false)
  decides whether it toggles or only turns on. A gate is key-operated (`Lock`, stays
  open) or switch-operated (`Link`), never both. Enemies operating switches are not
  implemented.
- **A body's own force/impulse (motor force, jump-off) is never scaled by the ambient
  medium.** A medium resists self-propelled motion only through its passive buoyancy and
  quadratic drag above, which already grow with speed and frontal area. Force gains
  (`WalkForceMultiplier`, `PatrolForceMultiplier`) and jump-off speeds are calibrated
  for that model directly.
- **World edges are a symmetric, contact-free safety net, not level geometry.** All four
  edges clamp a body back inside and reflect only the velocity heading into the edge, using
  the body's own restitution (with the resting-bounce threshold). They have no material,
  apply no friction and record no contacts, so a body touching only an edge is not grounded.
  Levels must provide their own floors, walls and ceilings.
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
- **Tileable placements set their length with `Span` (cells), not a repeat
  count.** The authored unit may be any number of cells long; the unit is
  repeated to fill the span exactly, cutting the last unit short when needed.
- **The color palette is 36 codes** (`0`-`9`, `A`-`Z`).
- **Variants of one kind of sprite may be grouped in a shared subfolder**
  (`Asset = Group/Variant` reads `Sprites/Group/Variant/`). Each variant stays
  a complete, independent asset with its own settings and clips, and its files
  keep the plain variant name as prefix - grouping only organizes folders, it
  adds no shared settings or inheritance between variants.
- **Posed bodies** (`[Poses]` section) resolve
  their active clip - and facing, including a vertical axis for `Climb` - from
  each clip name's own suffix
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
  clip(s) if that speed is tweaked. Swimming is the deliberate exception: a
  swimmer glides further per stroke than a walker per step, so swim clips use
  their own, longer anchor (`FrameDurationSeconds = 5.4 / speed`) for fewer,
  slower strokes.
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
  designers can make silent levels.
- **Player movement sounds are per pose** (`[Sounds]` in the sprite
  settings), played on animation frame steps, kept subtle.
- **Object sound keys are `Sound` and `KillSound`, with `Sound` meaning
  "the object's main event"**: for a hazard, the player being hurt by it; for
  a collectable, being picked up. `KillSound` is for a hazard being squashed
  and replaces `Sound` for that contact. The same two keys serve every kind
  of object.
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
  `IPatrolBody`, `IWalkForceBody`, `IClimberBody`, `IHangerBody`,
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
- **`DynamicObject2D` (and so `DynamicHazard2D`) patrols via its own mass-scaled force** (`IPatrolBody`),
  independently on the X and/or Y axis. Gravity is an ordinary force with no
  patrol special case; each axis has its own gain (`PatrolForceMultiplierX`/`Y`),
  cruise speed and force cap (`PatrolMaxForceX`, default unlimited, and
  `PatrolMaxForceY`, default 0 = no vertical thrust). Caps only limit force,
  never switch an axis on or off. An axis with no range holds its spawn
  position via a spring target speed; fliers stay gravity-affected and get
  lift from `PatrolMaxForceY`. Facing intent is the sign of the X target velocity
  (dead-zoned), so a body settled on its hold position shows its idle pose.
  `KinematicObject2D` patrols differently: acceleration-limited prescribed
  velocity, no forces.
- **Player state is four counters: Lives, Health, Rings and Score.** Health is a
  0-1 bar for the current life (HUD bar); Lives, Rings and Score are shown in
  the HUD text. Ordinary (non-fatal) hazard contact removes the hazard's
  `Damage` (placement key, default 1 = whole bar; e.g. 0.2 toxic plant, 0.5
  butterflies), once per fresh contact (rising edge on
  `_activeHazardContacts`), clamped at 0. When the bar empties,
  `CollisionSystem` removes one life and starts a `PendingTransition.Death`
  (see the pending-transition bullet below); the bar is refilled by the
  respawn that follows. At 0 lives the death leads to a "Game Over!" screen
  instead of a respawn.
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
  hit. Any bounce off a squash is just the normal restitution response.
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
- **Rings** (`CollectableType.Ring`) add one to `Player2D.Rings` and 5 to
  Score (`GameDefaults.RingScore`); they do not heal. Every `RingsPerLife` rings (`[World] RingsPerLife`,
  default 25) the count resets to 0 and a life is added. Rings survive respawn.
- **A world's starting lives are `[World] Lives`** (default
  `GameDefaults.PlayerStartingLives`), applied to `Player2D.Lives` when the
  player object is spawned - mirroring how `[World] Title` and `[Physics]
  Gravity` are already world-level settings rather than per-placement keys.
- **Collectable variants are driven by a required `Type` key on the
  placement** (`Diamond`/`Ring`/
  `Checkpoint`/`LevelEnd`/`Key`), not
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
  `Key` is removed on pickup and adds its required `Lock` name to
  `Player2D.Keys` (inert without one; the lock name lives on `Key2D`, not on
  `Body2D`); a `Gate2D` with a matching `Lock` is opened by the action key
  (`E`/`Enter`) when the player is within one cell of it, consuming one key, and
  then stays open. `Checkpoint`/`LevelEnd` are also removed on
  pickup like every other collectable; a collectable's own
  `Collectable2D.EffectPersists` (mirroring `IKillableBody.EffectPersists`
  on hazards) controls whether its pickup effect remains in its
  place afterward
  away - used by `Checkpoint`/`LevelEnd` in practice, but available to any
  collectable variant.
  `Checkpoint` records `World2D.RespawnPoint`, consumed by `World2D.Respawn()`;
  `LevelEnd` starts a `PendingTransition.LevelEnd`, consumed by `GameLoop`
  (see below).
- **Level end and death share one pending-transition mechanism**:
  `CollisionSystem` sets `World2D.PendingTransition` (`LevelEnd` or `Death`)
  and spawns the player's own effect clip (`LevelEndReachedEffectClip` /
  `DiedEffectClip`); the collectable's/hazard's sound plays as usual. While a
  transition is pending `GameLoop` skips physics/collision (effects keep
  animating) and, after `PendingTransitionSeconds`, carries it out: `LevelEnd`
  -> `GameMode.LevelComplete`; `Death` with lives left -> `World2D.Respawn()`
  and play resumes; `Death` with no lives left -> `GameMode.GameOver`. Both end
  screens are the same minimal mode (`OnEndScreenFrameAsync`): the last frame
  with a centered "Level Complete!"/"Game Over!" `UILabel` (solid black
  background, inside a `UIFrame` box that ripples inward from a compact
  larger size; green for Level Complete, red for Game Over), dismissed by a
  confirm (action or jump) key press (only after that key has been released
  once, so a held key doesn't dismiss it instantly), then back to
  `GameMode.WorldSelecting` - the same reset path as a confirmed exit.

## Player Movement & Input

- **`Up` (directional) and `Jump` (action) are always distinct inputs**, even
  for the ordinary ground jump.
- **Two full, independent key sets** are supported for local co-op/preference
  - "Player 1" (arrows + `Space` + `Enter` as action) and "Player 2" (`WASD` + `Left Ctrl` + `E` as action).
- **"Stance" is the stored choice, "pose" is the displayed result.** The
  player's `Stance` (`Walk` = upright/stretched, `Crawl` = compact/clamber) is
  the only stored state (`IsClambering` is derived from it while hanging), and
  resets to `Walk` on landing after being airborne. Pose (Walk, Crawl, Jump,
  Climb, Hang, Clamber, Swim) is derived each frame from stance plus situation.
- **Ground and hang each have their own structured "stance ladder"**
  (Crawl<->Walk on the ground; Clamber<->Hang while hanging, inverted to
  match arm position rather than screen direction), and each locomotion mode
  has its own jump-off speed (`WalkJumpSpeed`/`ClimbJumpSpeed`/`HangJumpSpeed`),
  graded by how much of the body's own leverage backs the push-off:
  `WalkJumpSpeed` (both feet on solid ground) > `ClimbJumpSpeed` (legs pushing off
  a rung) > `HangJumpSpeed` (arms only).
- **A short debounce** (`IHangerBody.SuppressHangUntilClear`, and the
  climbing equivalent) prevents an immediate re-grab of the same
  ladder/pipe the same frame a jump/swing-off begins.
- **Swim detection is medium-based, not reach-based** (`ISwimmerBody`,
  `PhysicsSystem.IsSwimmableMedium`) - unlike `IClimberBody`/`IHangerBody`,
  which key off a specific `Body2D.IsClimbable`/`IsHangable` surface via a
  `CollisionSystem`-set touching flag, swim instead reads the player's own
  already-resolved `Body2D.CurrentMedium` directly every frame and compares
  it against `PhysicsConstants.SwimMediumMinDensity`/`SwimMediumMinViscosity`
  (a medium qualifies if it clears *either* threshold, since density and
  viscosity are independent physical properties).
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

- **Interface colors live in `Global/InterfaceColors.ini`, separate from the
  world/sprite `ColorPalette.ini`.** Named tints (green/amber/red/blue, dark/
  normal/light, plus black) are the only source of HUD, menu, overlay and
  message-box colors; all names are required. `RenderConstants.DefaultForeColor`
  remains the world-content fallback, and `game-interop.js` keeps one green
  bootstrap fallback for the canvas before C# draws its first frame. Callers
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
- **`WorldRenderer` only builds glyphs for what's inside the camera's current
  viewport** - physics, collision, and animation are unaffected and keep
  simulating every body regardless of visibility.
- **The camera follows its target with a dead zone** (only scrolls once the
  target nears the viewport edge) and is always clamped to the world's own
  bounds.
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
  so a level author can watch the count drop live as collectables
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
  `ForegroundBackColors`), since these layers are static level data;
  `WorldRenderer` just reads the precomputed color directly.

## Level/Game Flow

- **Level selection is a strict `GameMode` state machine** backed by a
  `Global/Worlds.ini` catalog (world metadata/thumbnails loaded without
  loading the full playable world).
- **`Esc` asks before leaving a world.** During play it freezes the game behind an
  "Exit game?" prompt: any confirm key (`InputState.IsConfirmKeyDown`: Enter, E, Space or
  Left Control) confirms and returns to the world-select screen, a second `Esc` cancels.
  Menu and end-screen confirmations accept the same keys.
