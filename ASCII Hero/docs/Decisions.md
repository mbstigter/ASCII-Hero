# Decisions

Log of significant architecture/design decisions. Newest first.

## `MovingEnemy`'s ini parser now accepts `PatrolInitialDirectionY`, reserved for future vertical patrol

- **`MovingEnemy2D` gained a `PatrolInitialDirectionDown` property and a
  matching `SetPatrol` parameter, and `World2D.LoadAsync` now parses
  `PatrolInitialDirectionY` (`Up`/`Down`) for `Kind = MovingEnemy` placements,
  same as it already did for `Kind = KinematicObject`.** `MovingEnemy2D` has
  no vertical patrol behavior yet (it only has `PatrolMinX`/`PatrolMaxX`/
  `UpdatePatrolDirection`, all X-axis only), so this value is stored but not
  yet read/applied anywhere - purely groundwork so level authors can already
  set the key in their `.ini` files (see `Enemies_objects.ini`'s `SnakeOne`
  section for a commented-out example) without it being silently dropped,
  and so a later vertical-patrol implementation is a drop-in rather than
  requiring another ini-parsing change. docs/AssetFormat.md was updated to
  note the key is accepted but currently a no-op.

## `PatrolInitialDirectionX`/`Y` values changed from `Min`/`Max` to `Left`/`Right` and `Up`/`Down`; `MovingEnemy` key renamed for consistency

- **`KinematicObject`'s `PatrolInitialDirectionX`/`PatrolInitialDirectionY`
  ini values changed from the axis-agnostic `Min`/`Max` to the intuitive
  `Left`/`Right` (X) and `Up`/`Down` (Y):** `Min`/`Max` was originally chosen
  because it was a single shared vocabulary that (thanks to the world's
  top-left-origin, Y-increases-downward coordinate system - see
  docs/Architecture.md's Coordinate System) correctly meant both
  `Left`/`Right` on X and `Up`/`Down` on Y at once. In practice that
  genericness bought nothing, since each key already names its own axis
  (`...X` vs `...Y`) - forcing the *value* to stay abstract just made it
  read less intuitively than `MovingEnemy`'s own `Left`/`Right` wording, and
  would only get more awkward once a vertically-patrolling enemy needs the
  same Y-axis key. `World2D.LoadAsync`'s parsing now compares
  `PatrolInitialDirectionX` against `"Right"` and `PatrolInitialDirectionY`
  against `"Down"` (both still resolving to the same `KinematicObject2D.
  SetPatrol` `initialDirectionTowardMaxX`/`Y` booleans as before - only the
  ini-facing spelling changed, not the underlying semantics).
- **`MovingEnemy`'s own patrol-direction key renamed from
  `PatrolInitialDirection` to `PatrolInitialDirectionX`**, matching
  `KinematicObject`'s per-axis naming exactly (still `Left`/`Right` valued,
  unchanged) - `MovingEnemy2D` has no vertical patrol today, but naming its
  key `...X` now avoids a second rename later if/when one is added, and
  makes the two `Kind`s' patrol-direction keys consistent in the meantime.
  All authored worlds (`Enemies_objects.ini`, `MovingPlatforms_objects.ini`)
  and docs/AssetFormat.md were updated to match both changes.

## Facing-with-intent and platform-relative patrol convergence (two of the three deferred grip/facing fixes)

- **New shared `Body2D.GetSurfaceVelocityX()`**, generalizing what used to
  be `PhysicsSystem`'s private, player-only `GetGroundVelocityX`: the
  horizontal velocity of whichever solid this body currently rests on top
  of (`ContactType.SurfaceBottom`), or 0 if not grounded or resting on
  stationary terrain. Moved onto `Body2D` (rather than staying
  player-specific) so `MovingEnemy2D` can use the exact same reference-frame
  concept `PhysicsSystem.UpdateWalkForce`'s target speed already relied on
  for the player.
- **Fixes "facing direction flipping with platform direction":** both
  `Player2D.UpdatePose` and `MovingEnemy2D.UpdatePose` now resolve
  horizontal facing from `Velocity.X - GetSurfaceVelocityX()` instead of
  raw absolute `Velocity.X`. A body standing/patrolling with zero
  horizontal intent while carried along by a fast-moving platform previously
  had that platform's own speed baked directly into its absolute velocity,
  so `ResolveHorizontalFacing` would flip it to face (and, for the player,
  visually imply walking) in the platform's direction of travel even though
  its own intent hadn't changed; resolving against the platform-relative
  velocity isolates the body's own motion from the ride.
- **Fixes "enemy sliding on fast horizontal platforms":** `MovingEnemy2D.
  UpdatePatrolDirection`'s target velocity is now `GetSurfaceVelocityX() +-
  PatrolCruiseSpeed` rather than an absolute `+-PatrolCruiseSpeed` - mirroring
  `PhysicsSystem.Step`'s existing `groundVelocityX` fix for the player's own
  `WalkForce`. Previously the patrol force converged toward an absolute
  world-frame speed, so a fast platform's own carry meant part of the
  enemy's "muscle power" was spent fighting (or being fought by) the
  platform's own motion every frame instead of purely walking across its
  surface, letting the enemy visibly slide relative to the platform instead
  of holding a steady cruise speed over it.
- **Third deferred item (a broader structural review of `CollisionSystem`'s
  broad-phase code) intentionally not bundled into this entry:** it's a
  code-quality/structure pass, not a behavioral fix like the two above, and
  deserves its own focused review rather than being folded in here.

## Enemy patrol cruise-speed model; overridable player/body mass, friction, and walk tuning

- **`MovingEnemy2D` patrol motion converted from constant-thrust force to a
  cruise-speed convergence model, matching how the player's own walk force
  already worked.** Previously `PatrolForce` (a fixed-direction, mass-scaled
  force) was applied every frame with no target speed, so it behaved as
  uncapped thrust and enemies would accelerate indefinitely - this became
  clearly visible as a regression once other physics changes made frame
  timing/force integration more consistent. `MovingEnemy2D` now has a
  `PatrolCruiseSpeed` property (default `DefaultPatrolCruiseSpeed = 6.0`
  cells/second) as the actual steady-state target speed, and
  `PatrolForceMultiplier` (still defaulting to `60`, still authored via the
  `PatrolForce` ini key) is reinterpreted as "muscle power" - a force *gain*
  proportional to the remaining gap between current and target velocity
  (`(targetVelocityX - Velocity.X) * mass * PatrolForceMultiplier`), the
  exact same shape as `PhysicsSystem.UpdateWalkForce`'s player-side motor
  force. This is a closer, more realistic parallel between player and enemy
  locomotion, and fixes the runaway-acceleration bug as a side effect of
  giving patrol motion an actual target speed to converge on.
- **`Body2D.Mass` made independently overridable**, rather than always being
  computed as `Density * Size.X * Size.Y`. That computed value remains the
  default (a reasonable 2D-volume proxy per the entry below), but an object
  type's ini section may now set an explicit `Mass` key when the default
  would be unrealistic for a body's actual shape/weight (e.g. a small but
  very heavy or very light prop). Applies uniformly to the player and to any
  other spawned body.
- **Per-object-type `Friction` override added**, independent of `Material`/
  `Restitution` (which were already independently overridable). Lets one
  object type deviate from its resolved material's default grip (e.g. an icy
  patch of an otherwise-ordinary platform) without needing a dedicated
  near-duplicate material.
- **Per-object-type `Density` override added**, for the same reason and in
  the same style as `Friction`/`Restitution` above - independent of
  `Material`, and feeding into `Mass`'s density-times-footprint default the
  same way the resolved material's own density normally would (unless `Mass`
  is itself overridden too). Motivating case: a body whose effective density
  legitimately differs from its shared material's, or changes per-instance/
  at runtime (e.g. water that's been heated) - not yet used by any shipped
  world, but added alongside the other three physical overrides for
  consistency/completeness now rather than as a later one-off addition.
- **Player-only "muscle power"/cruising-speed overrides added**, mirroring
  the enemy-side concepts above: `Player2D.WalkForceMultiplier` (default
  `PhysicsSystem.DefaultWalkForceMultiplier = 40.0`) is the player's own force
  gain toward its target speed (previously a private, non-overridable
  constant on `PhysicsSystem`), and `Player2D.WalkSpeed`/`CrawlSpeed`
  (defaults `PhysicsSystem.DefaultWalkSpeed = 12.0`/`DefaultCrawlSpeed = 6.0`)
  are the target ground speeds for standing/walking vs. crouched/crawling
  poses, authored via the same-named optional keys in the `Player` ini
  section. Deliberately named `WalkForceMultiplier` rather than the earlier
  `WalkForceGain`, to be the exact same name (modulo the `Walk`/`Patrol`
  prefix) as `MovingEnemy2D.PatrolForceMultiplier`, since both represent
  identically-shaped "muscle power" concepts - a mass-scaled force gain
  converging velocity toward a target speed. Only one such multiplier exists
  for the player (there is no separate `CrawlForceMultiplier`) because
  Climb/Hang are direct velocity-assignment locomotion modes, not
  force-based, so `WalkForceMultiplier` is shared by both Walk and Crawl (the
  only two force-driven poses) with nothing left over for a second
  multiplier to apply to.
  Considered but rejected: leaving these as engine-wide constants only -
  per-level/per-character tuning (e.g. a heavier or weaker player variant)
  is a natural extension of the existing per-object-type `Material`/
  `Restitution`/`Mass`/`Friction` override pattern, so exposing them the same
  way was the more consistent choice.

## Pose terminology consistency; MultiRect/broad-phase-toggle removal; landing jitter accepted as-is; grip/facing fixes planned

- **`Stance` renamed to `Pose` everywhere it meant the named visual/body
  state** (walk/crawl/jump/climb/hang/etc.), matching the already-existing
  `Player2D.UpdatePose()`/`IPosedBody.UpdatePose()` naming that had been
  introduced without the rest of the codebase following suit: `SpriteAsset.
  Stances`/`DefaultStance`/`StanceDefinition` became `Poses`/`DefaultPose`/
  `PoseDefinition`, `SpriteLoader.ParseStances` became `ParsePoses`,
  `Body2D.SetPose`'s `stance` parameter/docs, `Player2D.Stance`,
  `MovingEnemy2D.Stance`, the `[Stances]` asset-file section (now
  `[Poses]`), and all doc/docs/AssetFormat.md references were updated to
  match. Purely a naming pass - no behavior changed.
- **Legacy `MultiRect` narrow phase and the `N`/`B` debug toggles removed**
  now that `CharacterGrid` narrow phase (actual rendered-character overlap,
  not just merged collision-rect overlap) and the spatial-grid broad phase
  have both proven out as the correct, permanent behavior (see the entry
  below and the "Contact-tracking..." entry further down): `NarrowPhaseMode`
  (the enum and `CollisionSystem.NarrowPhaseMode` property) is gone -
  `TryFindDeepestOverlap` now always applies the character-grid check
  when both owning bodies are supplied, unconditionally. `CollisionSystem.
  BroadPhaseEnabled` is gone - `GetCandidateSolids`/`GetCandidateMovingBodies`
  always consult the spatial grid; the never-filtered brute-force fallback
  lists were dead code once the toggle was removed. `InputState.
  IsToggleNarrowPhasePressed`/`IsToggleBroadPhasePressed`, `GameLoop`'s
  matching edge-trigger fields, and the top-right `R`/`C` + `1`/`0` HUD
  debug label are all removed as a result - they existed solely to compare
  the two approaches live, which is no longer needed now that only one
  approach exists for each phase.
- **Player landing jitter (brief visual settle when landing) accepted as a
  known, low-priority cosmetic quirk, not a bug to fix now:** may be
  revisited later, or simply designed around at the level-design stage
  (e.g. avoiding hard, high-speed vertical landings right at a scripted
  camera/timing beat). No code change tracks this; this note is the only
  record of the decision to leave it alone for now.
- **Enemy `PatrolSpeedX`/`PatrolSpeedY` intentionally not added for
  `MovingEnemy2D`:** since `MovingEnemy2D` is force-based (like the
  player's own `WalkForce`), the correct authoring control for patrol
  speed is `PatrolForce` in the object's settings file, not a direct speed
  key - adding a speed key would reintroduce the same direct-velocity-
  assignment pattern the force/mass rewrite deliberately replaced.
- **Enemy sliding on fast horizontal platforms, and facing direction
  flipping with platform direction, identified as real (if low-severity)
  defects with proposed fixes** - see the follow-up plan messages for the
  concrete approach for each; not yet implemented as of this entry.

## Player force/mass movement rewrite + full impulse-based normal force (both axes); temporary narrow/broad-phase debug toggles

- **Completes the deferred "Player force/mass movement rewrite" from the
  entry below:** the player no longer moves via direct velocity
  assignment. `Player2D` implements a new `IWalkForceBody` (mirrors
  `IPatrolBody` for a patrolling enemy) exposing `WalkForce`, a
  horizontal-only mass-scaled "motor" force computed each frame by
  `PhysicsSystem.UpdateWalkForce` as proportional to the remaining gap
  between the player's current `Velocity.X` and whichever target
  walk/crawl/climb-side/hang-side speed this frame's input calls for.
  `PhysicsSystem.Step` now dispatches the player through the same
  `StepMovingBodyWithForces` every other body already used (patrol force,
  gravity), rather than a separate `StepMovingBody` direct-assignment
  path - that older method and the player-only switch case were removed
  entirely. Climb/hang vertical velocity and the jump-off velocity itself
  remain direct assignments on purpose: those are discrete state-machine
  locomotion modes (like a scripted teleport), not the continuous
  ground-level walk/crawl locomotion `WalkForce` drives.
- **Full impulse-based normal-force response, both axes, solids included:**
  `ResolveRectAgainstSolid`'s velocity response for landing-on-top,
  underside, and left/right-edge collisions was rewritten to properly
  reflect the body's velocity relative to an infinite-mass solid along the
  contact normal (consistent with `ResolveNormalImpulse`'s existing
  mover-vs-mover math, generalized to an infinite-mass second body), then
  apply real Coulomb friction (`ApplyCoulombFriction`) to the tangential
  component - capped by the normal impulse magnitude just resolved at that
  same contact, rather than the old flat "multiply by `1 - friction`"
  approximation (`ApplyFriction`, now solid/solid-vs-solid removed but
  still used by `ResolveBodyPair`'s remaining flat friction case). This
  made the last two grounding hacks provably redundant and they were
  deleted outright: `CollisionSystem._groundedSolids`, the player-only
  horizontal platform-carry block at the top of `Resolve`, and
  `MaintainVerticalGroundedContact` (the vertical re-seat hack) are gone -
  a resting body (including the player) now stays glued to a moving
  platform on both axes purely through the same velocity-matching +
  landing-snap response every other body already relied on, with no
  special-casing. `CollisionSystem.Resolve` no longer needs a
  `deltaSeconds` parameter as a result.
- **Temporary debug toggles for comparing collision modes live:** `N`
  cycles `CollisionSystem.NarrowPhaseMode` between `MultiRect` and
  `CharacterGrid`; `B` toggles `CollisionSystem.BroadPhaseEnabled`, which
  (when false) makes `GetCandidateSolids`/`GetCandidateMovingBodies` fall
  back to the full unfiltered solids/movers lists instead of consulting
  the spatial grid, rather than removing the grid machinery itself. Both
  are edge-triggered in `GameLoop.OnPlayingFrameAsync` (mirroring
  `PhysicsSystem`'s own stance-key edge-triggering) and reflected by a
  single top-right HUD label showing `R`/`C` (narrow-phase mode) followed
  by `1`/`0` (broad-phase on/off) - explicitly called out as temporary in
  code comments, to be removed once the two approaches are no longer
  being actively compared.

## Contact-tracking + impulse-based normal force redesign planned; supersedes "no separate normal force" decision

- **Supersedes the earlier "No separate constraint-solver normal force
  term" decision below** (kept in place further down for history): that
  decision was made when `IsGrounded` was a simple, pragmatic contact
  signal and grounded-response worked well enough for stationary/slowly
  moving terrain. Debugging the moving-platform carry/grounding bugs (head
  hanging on a platform's underside, side-collision jitter, vertical carry
  gaps) repeatedly traced back to the same root cause: resolution axis
  (top/bottom vs. left/right) was inferred from *current-frame overlap
  depth alone*, with no memory of which side a body actually approached
  from - workable for the common case, ambiguous whenever a thin/fast
  solid or multi-rect body (e.g. player head+torso) produces a shallow
  overlap on the "wrong" axis for one frame.
- **New direction:** replace the ad hoc, per-bug patches
  (`MaintainVerticalGroundedContact`, the temporary player-only horizontal
  carry hack) with a persisted, explicit per-body `ContactType` model
  (SurfaceTop/Bottom/Left/Right/Ladder/Bar - ported in spirit from the
  older `ConsoleGame2D.CollisionSystem2D`/`Body2D` contact tracking) plus
  genuine impulse/constraint-based normal-force resolution: a resting
  body's velocity component along the contact normal (relative to the
  contacted surface's own velocity) is clamped/reflected directly each
  frame, rather than inferring "was I grounded" from geometry after the
  fact. Explicitly **rejected**: a penalty/spring-style normal force
  (force proportional to penetration depth) - this is the classic source
  of the stiffness-tuning/oscillation problems experienced previously
  before this codebase existed, and is unnecessary when the impulse/
  constraint form is unconditionally stable (no spring constant to
  calibrate).
- **`IsGrounded` becomes a derived, read-only query, not stored state:**
  rather than a mutable field set/reset by scattered code (the exact
  pattern that caused ordering bugs this session), `IsGrounded` is
  recomputed fresh every frame from that frame's resolved `ContactType`
  set (`HasContact(ContactType.SurfaceBottom)`), evaluated once per frame
  after normal-force resolution runs. Any future hysteresis/game-feel
  state (e.g. coyote-time jump forgiveness) must be its own separately
  named, clearly-labeled field - never reusing or conflating with
  whatever replaces `IsGrounded`.
- **One-way (jump-through-from-below) platforms fall out for free:** a
  `Body2D.IsOneWayPlatform` flag can gate whether an underside
  (`SurfaceBottom`) contact is even created, using the previous frame's
  contact snapshot to disambiguate "was resting on top" from "approaching
  from below" - not otherwise reliably possible under the old
  overlap-depth-only model.
- **Character-level narrow phase added as a toggle, not a replacement:**
  the existing multi-rect AABB decomposition (chosen originally so oddly
  shaped sprites, e.g. a rounded ball or a narrower head above wider
  shoulders, only collide on their non-empty cells) is itself the source
  of the platform-through-head bug, since a short per-rect height can
  make a sub-rect's overlap look like "resting on top" at a shallower
  penetration than the whole body's true silhouette would. A ported
  character-grid narrow phase (test actual non-space sprite characters in
  the overlap region, also from `ConsoleGame2D`) sidesteps this by testing
  the true silhouette directly, but is added behind a
  `CollisionSystem.NarrowPhaseMode` toggle (default `MultiRect`) rather
  than replacing the existing path outright, until it's proven correct
  and fast enough with many objects.
- **Broad phase via spatial grid, not plain pairwise AABB:** since levels
  are expected to have many objects, candidate-pair gathering is bucketed
  by world cell so per-frame collision checks scale with nearby objects
  only, not the full object count.
- **Player force/mass movement rewrite deliberately deferred, sequenced
  after this redesign:** the temporary horizontal carry hack being
  deleted here exists only because Player currently overwrites
  `Velocity.X` directly from input each frame (see "Force-based movement,
  non-player only" below); once this contact/normal-force redesign lands,
  `MovingEnemy2D` (already force-driven) serves as the proof that
  platform-riding/grounding/one-way-platforms work correctly *before*
  Player's own movement model changes, keeping the two rewrites isolated
  rather than debugged together.

## Medium (air/liquid) drag and buoyancy: deferred, distinguished from surface friction

- **Prompted by user idea:** every character cell can already be assigned a
  material with properties; the user proposed treating empty cells as an
  implicit "air" material (low density, low friction/drag) as the natural
  default, then adding explicit liquid regions (e.g. water) as ordinary
  objects with their own material, so a steel ball sinks (slower through
  water than air) and a low-density hollow ball floats - envisioned as a
  variant of the `TestPhysics` world.
- **Friction, drag, and buoyancy are three distinct mechanisms, not one:**
  - **Friction** (`Body2D.Friction`, already implemented) is a
    contact/surface phenomenon - it only exists between two bodies
    actually touching via a resolved normal-force contact, and damps only
    the *tangential* (parallel-to-surface) velocity component.
  - **Drag** is a volumetric/medium phenomenon - it opposes a body's
    velocity in whatever direction it's actually moving, requires no
    contact/touching at all, only "is this body's position currently
    inside a medium's volume," and scales with the medium's density (and
    typically the body's cross-section/shape).
  - **Buoyancy** is a separate force again - proportional to displaced
    medium volume times the medium's density, opposing gravity; whether a
    body sinks or floats falls out of comparing the body's own `Density`
    (already an existing property, used today for mass) against the
    medium's density, with drag only affecting *how fast* it sinks/rises,
    not *whether* it does.
  - These are easy to conflate (all "resistance") but need separate
    properties/forces: reusing `Friction` for medium drag would be
    physically wrong (friction requires a surface contact; drag doesn't)
    and would block correctly modeling a body moving through open air/water
    with no surface contact at all.
- **"Empty cells = air" is an elegant default, not a special case:**
  treating the background/empty-cell state as an always-present, very
  low-density, very low-drag medium means there is no special-casing
  needed for "not inside anything" - it is simply the ambient medium,
  with explicit water/liquid regions being ordinary denser/higher-drag
  media placed as objects on top of that default.
- **Deferred, not implemented now:** this is additive to, not entangled
  with, the in-progress contact-tracking/impulse-based-normal-force
  collision redesign (see below) - media/drag/buoyancy are forces applied
  during integration alongside gravity, and don't touch contact
  resolution, `ContactType`, or normal force at all. Sequencing it after
  that redesign lands means medium overlap detection can reuse the new
  broad-phase spatial grid instead of duplicating overlap-query code.
  It's also a good, Player-independent milestone (falling/floating balls)
  to exercise the force/mass integration loop before the Player force/mass
  movement rewrite (also deferred, see below) takes on user input as just
  another force source.

## Vertical grounded-carry gap fixed permanently; horizontal fix explicitly flagged as a temporary hack

- **Prompted by user follow-up:** after the horizontal-only restriction
  below fixed enemy sliding, the user pointed out the *original* ask - the
  player floating momentarily above a downward-moving platform - was still
  entirely unfixed (the horizontal-only change left the vertical axis
  exactly as it started), and separately asked that the still-necessary
  horizontal carry be clearly flagged as temporary scaffolding rather than
  a permanent design, since it exists only to work around the player's
  input-driven `Velocity.X` overwrite and should be deleted once player
  movement becomes force/mass-based (see the deferred TODO on
  `PhysicsSystem.Step`) - at which point ordinary friction-based
  velocity-matching (already used by every other body) will carry the
  player horizontally for free, same as it already does vertically.
- **Horizontal carry flagged, not removed:** `_groundedSolids` and the
  block in `CollisionSystem.Resolve` that reads it are now explicitly
  labeled `TEMPORARY HACK`, with the doc comments explaining exactly what
  future change (player force/mass movement) makes them redundant and
  should trigger their deletion, so this isn't mistaken for a permanent
  design decision later.
- **Vertical fix: a genuinely permanent, additive-free re-seat.** Unlike
  horizontal, gravity affects the player exactly like every other body, so
  there's no input-overwrite problem to work around here - the vertical
  gap is purely the discrete-collision timing issue described below, with
  no reason a proper fix couldn't be permanent and apply to every body.
  Added `CollisionSystem.MaintainVerticalGroundedContact`: for a body that
  was already confirmed grounded as of last frame (`IsGrounded` still true)
  and remembered to be standing on a *moving* solid (`_groundedSolids`),
  if it still horizontally overlaps that solid's collision rect(s) this
  frame, its `Position.Y` is snapped directly onto the solid's *current*
  top surface - an absolute, direct assignment, not an additive
  velocity/position delta. This runs immediately, before this frame's
  ordinary overlap-based landing check, closing the same gap the earlier
  (reverted) vertical delta attempt intended to close - a platform
  displacing downward farther in one frame than the body's own
  velocity-matched fall keeps up with, which otherwise briefly loses all
  rect overlap and free-falls under gravity alone until it "catches up".
- **Why this can't double-count, unlike the earlier attempt:** the earlier
  vertical fix failed because it *added* `solidVelocity.Y * deltaSeconds`
  to whatever position the body already had - on top of the pre-existing
  velocity-matching + landing-snap mechanism that was *also* moving the
  body vertically, compounding both. This fix instead *sets* `Position.Y`
  outright, purely from the solid's current rect - the same operation the
  ordinary landing-snap in `ResolveRectAgainstSolid` already performs, just
  run one step earlier so the body's rect never actually loses overlap
  with the solid in the first place. The subsequent ordinary collision
  pass then finds the exact same overlap it would already have produced
  and simply reconfirms it - a no-op, not a second correction.
- **Guarded against misuse in two ways:** (1) gated on `IsGrounded` still
  being true as of the start of this `Resolve` call, since `PhysicsSystem`
  already clears it the instant a jump/climb/hang begins - without this, a
  player who just jumped off a platform this same frame would be wrongly
  snapped straight back down for still horizontally overlapping it; and
  (2) gated on the remembered solid's own `Velocity` being non-zero, since
  ordinary stationary terrain never has this gap to begin with, and a
  bouncing `DynamicObject2D` is still momentarily `IsGrounded` the very
  frame it bounces upward off stationary ground - re-seating it back down
  every such frame would wrongly cancel every bounce.
- **Applies to every grounded `IPhysicsBody`, not just the player** - both
  the enemy-sliding regression and the original platform-floating bug were
  really the same class of problem (an axis not being carried correctly),
  and this fix, being a correct/permanent one, is written generically from
  the start rather than needing the same player-only carve-out the
  horizontal hack requires.

## Grounded-carry fix restricted to the horizontal axis only, fixing jitter it introduced vertically

- **Prompted by user testing:** after the grounded-carry fix below shipped,
  the user reported it did *not* fix the original downward-platform lag,
  and additionally introduced new jitter - the player rapidly alternating
  stand/airborne pose while riding a platform moving *upward*, and the same
  jitter on an enemy riding an upward-moving platform. The *horizontal*
  carry (walking on a sideways-patrolling platform) did work correctly,
  however.
- **Root cause: double-counting vertical carry.** Vertical motion was
  already being carried by a second, independent mechanism that the
  original fix's design didn't account for: `ResolveRectAgainstSolid`
  already nudges a landed body's `Velocity.Y` toward the solid's own
  velocity (see the reference-frame collision math below), and
  `PhysicsSystem.Step` integrates that velocity into `Position` *before*
  `CollisionSystem.Resolve` runs each frame - so a resting body's vertical
  position was already approximately tracking the platform frame to frame,
  with the following frame's ordinary landing-snap correction (onto the
  solid's current, already-moved top surface) papering over the small
  remaining gap. Applying the new `solidVelocity * deltaSeconds` position
  shift on *top* of that (both before *and* independently of the existing
  velocity-integration + snap) shifted the body too far, and the very next
  frame's landing-snap then corrected it back - a feedback loop visible as
  jitter, worse against gravity (moving up) than with it (moving down).
  Horizontal was unaffected by this double-counting because
  `PhysicsSystem.Step` overwrites the player's `Velocity.X` directly from
  input every frame, so the friction-based horizontal velocity-matching
  never had a chance to apply in the first place - the new shift was the
  *only* thing carrying it, which is exactly why horizontal carry worked
  immediately while vertical did not.
- **Fix: the grounded-carry shift now applies to `Position.X` only.**
  `Position.Y` is left entirely to the pre-existing velocity-matching +
  landing-snap combination, which remains responsible for all vertical
  platform-riding behavior (including whatever residual lag it still has
  at the very top of a vertical patrol's reversal point - not eliminated
  by this follow-up, since doing so without reintroducing the
  double-counting would require deeper work, e.g. continuous rather than
  discrete collision detection, and the jitter was the more urgent
  regression to fix first).

## `CollisionSystem` carries grounded bodies along a moving solid's own displacement, closing the downward-platform lag

- **Prompted by user request:** after validating (via `MovingEnemy2D`) that a
  body resting on a moving `KinematicObject2D` platform is properly dragged
  along horizontally by the existing friction/reference-frame collision
  math, the user noticed a platform moving *downward* still left a resting
  rider visibly lagging behind/floating above it until gravity closed the
  gap - most noticeable at the top of a vertical platform's patrol cycle,
  right as it reverses and starts descending. Asked to fix this at the
  engine level (favoring the most realistic simulation short of a full
  player force/mass-based rewrite), rather than special-casing player
  movement.
- **Root cause: discrete per-frame overlap detection, not a friction/mass
  problem.** `ResolveAgainstSolid`/`ResolveRectAgainstSolid` already treat a
  solid's own `Velocity` as the collision reference frame, so once two
  rects still overlap, friction correctly drags a resting body's velocity
  toward the solid's velocity. But if the platform displaces *farther* in
  one frame than the resting body's own (near-zero, since it was just
  resting) velocity carries it, the body's collision rect can stop
  overlapping the solid's new position entirely for a frame or more - the
  landing check then finds nothing to resolve, and the body free-falls
  under gravity alone until it "catches up". This is purely a consequence
  of resolving collision once per frame from stale positions; it has
  nothing to do with either body's mass.
- **Fix: remember what each grounded body was standing on, and carry it
  forward.** `CollisionSystem` now keeps a `Dictionary<IPhysicsBody, Body2D>`
  (`_groundedSolids`) recording, for each currently-grounded moving body,
  which solid it landed on. At the *start* of the next `Resolve` call -
  before `IsGrounded` is reset and before the ordinary overlap-based
  landing check runs - if that remembered solid is itself an
  `IPhysicsBody` with a non-zero `Velocity`, the grounded body's `Position`
  is shifted by `solidVelocity * deltaSeconds` first. This re-establishes
  overlap for the *same* frame the platform moves, so the ordinary
  collision pass immediately re-detects/re-confirms the landing instead of
  the body ever visibly separating from the platform. For ordinary
  stationary terrain (the overwhelming common case) the remembered
  solid's `Velocity` is zero, so this is a no-op and behavior is unchanged.
- **Scoped to `IPhysicsBody`, not `Body2D` in general:** only bodies
  subject to the physics/collision pass (i.e. affected by gravity and
  other forces) can ever be "grounded" on something in the first place, so
  tracking is keyed by the mover, not by every `Body2D` in the world.
  `ResolveAgainstSolid`/`ResolveRectAgainstSolid` were changed to return
  whether the body landed on top of that particular solid, so `Resolve`
  can record (or clear, if no longer grounded) the association per body
  per frame without any extra overlap re-checking.
- **`CollisionSystem.Resolve` gains a `deltaSeconds` parameter** (now
  `Resolve(World2D world, double deltaSeconds)`), threaded in from
  `GameLoop.OnPlayingFrameAsync`'s existing per-frame `deltaSeconds`, used
  solely for this carry displacement - every other collision response in
  the class remains purely positional/velocity-based.
- **Stale entries are pruned each frame** against the current frame's
  `_movingBodies` set, so a body removed from the world (e.g. a killed
  enemy) can never leak its dictionary entry.

## `KinematicObject2D.SetPatrol` gains explicit per-axis initial-direction overrides

- **Prompted by user request:** a follow-up to the `KinematicObject2D` work
  below asked whether the initial patrol direction is stored anywhere at
  runtime, and if so, whether a level author could pin it explicitly via
  `_objects.ini` instead of only relying on the automatic
  farther-bound inference, for more precise level design.
- **Yes - it was already stored, just not exposed as an override:**
  `KinematicObject2D` already holds `_patrolMovingTowardMaxX`/
  `_patrolMovingTowardMaxY` fields, computed once in `SetPatrol` and then
  read every frame by `Move`. `MovingEnemy2D.SetPatrol` already had exactly
  this kind of explicit override (`initialDirectionRight`, surfaced via the
  `PatrolInitialDirection` ini key), so `KinematicObject2D.SetPatrol` was
  extended to match: two new optional `bool?` parameters,
  `initialDirectionTowardMaxX`/`initialDirectionTowardMaxY`, each either
  overriding that axis's starting heading or (if left `null`, the default)
  falling back to the existing farther-bound inference exactly as before.
- **New ini keys `PatrolInitialDirectionX`/`PatrolInitialDirectionY`**
  (`World2D.LoadAsync`), taking `Min`/`Max` (case-insensitive) rather than
  `MovingEnemy`'s `Left`/`Right`, since a `KinematicObject`'s Y-axis patrol
  has no "left"/"right" reading - `Min`/`Max` reads correctly for either
  axis. Documented in docs/AssetFormat.md alongside the existing
  `KinematicObject` patrol keys, and demonstrated in the `MovingPlatforms`
  demo world's `SteelHorizontal` placement (pinned to start heading toward
  `PatrolMinX`, so it visibly departs from the brick platform before
  returning).

## `KinematicObject2D` reimagined as a real kinematic body (static + `IPhysicsBody`), with per-axis patrol and reference-frame collision, plus a `MovingPlatforms` demo world

- **Prompted by user request:** move on from `MovingEnemy2D` to
  `KinematicObject2D` for moving platforms, referencing the old
  `ConsoleGame2D` prototype's equivalent behavior, and build both the
  plumbing and a demo world with a static brick platform flanked by a
  horizontally- and a vertically-patrolling steel platform, tested by
  walking the player onto them.
- **`IsStatic` clarified to mean collision-response immunity, not
  immobility:** `Body2D.IsStatic`'s doc comment now explicitly describes the
  "kinematic body" case - a body that is `IsStatic` (never itself corrected
  by `CollisionSystem`) yet still implements `IPhysicsBody` with a real,
  non-zero `Velocity` it drives every frame under its own prescribed motion,
  as distinct from ordinary stationary terrain where `IsStatic` and "never
  moves" happen to coincide. This was needed because a moving platform must
  push/carry other bodies (so it needs a real velocity other systems can
  react to) while never being pushed/bounced/halted by anything it touches
  (so it stays `IsStatic`).
- **`KinematicObject2D` rewritten accordingly:** `IsStatic = true` (was
  `false`), keeps `IPhysicsBody.Velocity`, and gains independent, optional
  per-axis patrol state (`PatrolMinX`/`PatrolMaxX`/`PatrolSpeedX`,
  `PatrolMinY`/`PatrolMaxY`/`PatrolSpeedY`, configured via `SetPatrol`) plus
  its own `Move(deltaSeconds)` that recomputes each configured axis's
  velocity toward its current target bound (reversing near it, mirroring
  `MovingEnemy2D`'s turn-around approach) and integrates position - never
  gravity/force integration. Per-axis (rather than a single linear
  direction) was chosen specifically so a future non-linear patrol path
  (e.g. a rectangular circuit) can be added without redesigning the body,
  even though only pure-horizontal or pure-vertical patrol is used today.
  `PhysicsSystem.Step`'s `KinematicObject2D` case now just calls
  `Move(deltaSeconds)` instead of inlining the integration.
- **`CollisionSystem`'s `_movingBodies` list now excludes any `IsStatic`
  body:** previously any `IPhysicsBody` (including a would-be kinematic
  body) was added, which would have made `KinematicObject2D` incorrectly
  participate as something to be *itself* resolved/corrected. Static bodies
  are only ever encountered from the *solids* side of collision resolution
  now, matching how ordinary terrain already worked.
- **Solid-collision math (`ResolveAgainstSolid`/`ResolveRectAgainstSolid`)
  generalized to use the solid's own velocity as the collision's reference
  frame**, rather than assuming every solid is stationary: bounce/friction
  response is computed on the other body's velocity *relative to* the
  solid's velocity, then the solid's velocity is added back to the result.
  For ordinary static terrain (velocity always zero) this is numerically
  identical to the prior math. For a moving `KinematicObject2D`, this means
  friction naturally drags a resting/colliding rider's velocity toward
  matching the platform's own velocity instead of toward zero (more so for a
  grippy material, less for a slick one, using the exact same
  `ApplyFriction` formula already used against stationary terrain), and
  vertical carry (riding a platform up/down) falls out for free from the
  existing per-frame snap-onto-top-surface correction, which already runs
  against wherever the platform currently is. This was chosen deliberately
  over a bespoke "carry the rider" mechanism, so a moving platform reuses
  exactly the same collision code path as ordinary terrain.
- **`World2D.LoadAsync` extended to parse the new per-axis `KinematicObject`
  patrol ini keys** (`PatrolMinY`/`PatrolMaxY`/`PatrolSpeedX`/`PatrolSpeedY`,
  alongside the existing X-axis keys and the shared `Patrol` flag) and call
  `SetPatrol` with whichever axes are configured.
- **New `MovingPlatforms` demo world** (registered in `Global/Worlds.ini`):
  a static `BrickPlatform` in the middle where the player naturally lands
  after falling from spawn, flanked by two `SteelPlatform` kinematic
  objects - one patrolling purely horizontally, one purely vertically -
  reusing the already-existing `BrickPlatform`/`SteelPlatform` sprite assets
  rather than adding new ones, to validate the plumbing above by walking the
  player onto each platform.

## New `IPosedBody` capability interface + shared `Body2D.ResolveHorizontalFacing`, prepping `Player2D` for its own eventual force-based conversion

- **Prompted by user request:** a follow-up review of the `MovingEnemy2D`
  facing/animation work (previous entry below) asked whether its pose logic
  was consistent with `Player2D`'s, and - once the plan to eventually convert
  `Player2D` to the same force-based movement model was factored in - what to
  do about it now rather than let the two diverge further first.
- **Two inconsistencies identified and fixed:** (1) `MovingEnemy2D` decided
  its own pose internally (inside `UpdatePatrolDirection()`), while
  `Player2D`'s pose was decided externally, by `PhysicsSystem.Step`; and (2)
  `MovingEnemy2D` derived facing from patrol *intent* (`_patrolMovingRight`,
  decided before this frame's force is integrated), while `Player2D` derived
  facing from actual resolved `Velocity.X` (read after integration) - meaning
  the two could briefly disagree for a frame right after a patrol turn-around.
- **New `IPosedBody : IPhysicsBody` interface, mirroring `IPatrolBody`'s own
  shape:** a body owns its own `UpdatePose()` decision (matching how a body
  already owns `IPatrolBody.UpdatePatrolDirection()`), and
  `PhysicsSystem.StepMovingBodyWithForces` merely calls it once per frame,
  after velocity/position are finalized - not before, and not by taking over
  the decision itself. `MovingEnemy2D.UpdatePose()` now reads its own
  (post-integration) `Velocity.X`, resolving the frame-of-lag inconsistency
  above.
- **`Player2D` also implements `IPosedBody` now, even though it isn't called
  by `PhysicsSystem`'s generic force-based path yet** (the player still moves
  via direct velocity assignment - see the next decision entry below and the
  `TODO` in `PhysicsSystem.Step`). Its previous inline pose-resolution block
  moved essentially verbatim into `Player2D.UpdatePose()`, called explicitly
  by `PhysicsSystem.Step` for now. Climb facing (no horizontal facing at all
  while climbing) is resolved from `Velocity.Y` via a new
  `Body2D.ResolveVerticalFacing`, the vertical counterpart to
  `ResolveHorizontalFacing` - **not** read directly from input as first
  implemented: `PhysicsSystem` already writes up/down input straight into
  `Velocity.Y` every frame while climbing (`velocity.Y -= ClimbVerticalSpeed`/
  `+= ClimbVerticalSpeed`, both today's direct-assignment code and its future
  force-based replacement), so `Velocity.Y`'s sign is already exactly as
  reliable a direction proxy as `Velocity.X`'s sign is for every other
  stance - an extra input-driven field would have added state to work around
  a problem that doesn't exist, and would still have needed removing once
  the player's movement converts. When the player's movement is eventually
  converted, `PhysicsSystem.Step`'s explicit `player.UpdatePose()` call
  becomes a one-line deletion, and the player falls into the same generic
  `is IPosedBody` dispatch every other body already uses.
- **`Body2D.ResolveHorizontalFacing(velocityX)`/`ResolveVerticalFacing(velocityY)`**
  factor out the `< 0 ? Left/Up : > 0 ? Right/Down : Idle` rules every
  velocity-driven stance needs, so there is exactly one definition of each
  for the whole game rather than copies that could quietly drift apart
  again.

## `MovingEnemy2D` patrol facing/animation via `SetPose`, plus per-placement `SnakeTwo` patrol range

- **Prompted by user request:** the `Enemies` world's `Snake` placements
  didn't visually animate or turn to face the direction they were currently
  patrolling, even though the `Snake` asset already had `left`/`right`
  clips authored (tracked as a follow-up in the prior decision entry below).
  The user also added a second placement, `SnakeTwo`, and asked that it be
  confined to the platform it starts on rather than patrolling the full
  world like `SnakeOne`.
- **Facing/animation implemented via the existing stance/pose mechanism,
  not a bespoke one:** rather than inventing new per-enemy facing state,
  `Snake` gained a `[Stances]` section (`Snake_settings.ini`) with one
  `Move` stance mapping `Facing.Idle/Left/Right` to `move_idle`/`move_left`/
  `move_right` (the asset's existing clip files, renamed with a `move_`
  prefix to fit the stance-clip naming convention described in
  [AssetFormat.md \u00a72.6](AssetFormat.md)). Pose resolution itself was
  revised again shortly after - see the newer decision entry above.
- **`SnakeTwo`'s patrol range set via the already-existing `PatrolMinX`/
  `PatrolMaxX` ini keys**, not a new mechanism - those keys were added in
  the original patrol decision below specifically so a placement could be
  narrowed to less than the full world width. `SnakeTwo`'s placement now
  sets them to the column span of the wood platform it spawns on
  (`Enemies_objects.txt`), while `SnakeOne` is left without an override and
  continues to default to the full world width.

## `MovingEnemy2D` linear patrol via a new `IPatrolBody` force source, not direct velocity assignment

- **Prompted by user request:** implement `MovingEnemy2D`'s previously-stubbed
  patrol behavior so the `Enemies` world's `Snake` placement can actually
  move, having first confirmed (in conversation, not yet written down until
  now) that `MovingEnemy2D`/`DynamicObject2D` already move via
  `PhysicsSystem.StepMovingBodyWithForces`'s mass-scaled force accumulator
  (today just gravity) rather than the player's direct velocity-assignment
  model, and that the player's own conversion to force-based movement is
  deliberately deferred until after this non-player groundwork is proven out.
- **Design chosen over reviving the old `ConsoleGame2D` `IMoveable`/
  `MovementForce` model wholesale:** the old reference project's
  `MovingEnemy2D`/`Player2D` both drove movement through one shared
  `IMoveable` interface with a settable `MovementForce`, braking factor, and
  max-force clamp, applied every frame via `ApplyObjectSpecificForces`. This
  round intentionally does *not* resurrect that as a shared player/enemy
  interface (the player is explicitly out of scope for now) - instead, a new,
  narrower `IPatrolBody : IPhysicsBody` capability interface (mirroring how
  `IGravityAffected` lets `PhysicsSystem` apply gravity generically to any
  opted-in body) exposes `IsPatrolling`, `PatrolMinX`/`PatrolMaxX`, a
  `PatrolForce` the body recomputes itself, and an `UpdatePatrolDirection()`
  call `PhysicsSystem` invokes once per frame before reading `PatrolForce`.
  `StepMovingBodyWithForces` was already carrying a comment describing
  exactly this kind of extension point ("a future force source... would sum
  its contribution into netForce here") - this fills that in rather than
  adding a separate movement code path.
- **Patrol direction/turn-around logic ported from the old project's
  `UpdateLinearPatrol`,** adapted from velocity-assignment to force
  contribution: `MovingEnemy2D.UpdatePatrolDirection()` compares its current
  position to whichever bound (`PatrolMinX`/`PatrolMaxX`) it's currently
  heading toward, flips direction once within a small `PatrolTurnThreshold`
  (0.25 cells) of that bound (avoiding oscillation exactly on the boundary),
  then sets `PatrolForce` to a fixed-magnitude (`PatrolForceMultiplier = 30`),
  mass-scaled horizontal force in the current direction - mirroring how
  gravity itself is `mass * world.Gravity`, so patrol speed doesn't
  implicitly depend on a body's resolved mass the way an un-scaled force
  would.
- **`Patrol`/`PatrolMinX`/`PatrolMaxX` ini keys (`Kind = MovingEnemy`
  placements only), with the range defaulting to the entire world width**
  (`0` to `WidthCells - Size.X`) when `Patrol = true` is set without an
  explicit range - chosen so "make this enemy patrol the level" (the
  `Snake` placement's actual ask) needs zero further authoring, while
  `PatrolMinX`/`PatrolMaxX` remain available to narrow a specific enemy to a
  shorter stretch later. `MovingEnemy2D.SetPatrol(minX, maxX)` also picks an
  initial direction toward whichever bound is farther from the spawn
  position, so an enemy spawned near one end still sweeps the full range
  immediately instead of hitting the near bound and turning back within the
  first frame or two.
- **Deliberately out of scope for this round:** sprite facing (`Snake` has
  unused `left`/`right` clips already authored - see
  [Design.md](Design.md)'s Planned/Future Work; implemented in the decision
  entry above), chase-the-player behavior, and any change to player movement
  itself - tracked as follow-ups rather than bundled in here.

## Esc-to-world-select dev/testing shortcut

- **Prompted by user request:** an easy way to abandon the current world and
  return to the world-select screen while testing, without a full
  level-complete/death flow existing yet.
- **Implementation:** `InputState.IsEscapePressed` (a plain `Escape` key

  `GameLoop.OnPlayingFrameAsync` checks it first thing each frame and, if
  pressed, calls `WorldSelectScreen.ResetConfirmation()` (already existed for
  the failed-load recovery path) and sets `_mode = GameMode.WorldSelecting`
  directly - reusing the exact same `GameMode` transition a later
  level-complete/death flow will need, rather than a one-off code path.
- **Scope:** deliberately a raw dev shortcut, not itself the intended
  finishing-a-level/dying flow - that will likely route through a dedicated
  intermediate `GameMode` (e.g. showing a result message) before returning to
  `WorldSelecting`, rather than jumping back instantly the way Esc does.

## Level-selection screen: strict `GameMode` state machine and `Global/Levels.ini` catalog

- **Prompted by user report:** after confirming a level on the new
  level-selection screen, the game sometimes appeared to load/redraw
  multiple times or even switch levels mid-load.
- **Root cause:** `GameLoop.OnFrame` is invoked by JS in a fire-and-forget
  fashion - `requestAnimationFrame` schedules the next call without waiting
  for the previous call's `Task` to complete (see game-interop.js). Ordinary
  gameplay frames never do genuine async I/O after their first `await`
  (canvas draw calls resolve fast enough in practice not to matter), but
  confirming a level starts `World2D.LoadAsync`, which does real,
  multi-file HTTP fetches spanning many milliseconds - a large enough window
  for an overlapping `OnFrame` call to land while the first was still
  awaiting, re-enter the level-selection branch (the boolean flag guarding
  it hadn't flipped yet), see the confirm state still set, and kick off a
  second, concurrent `World2D.LoadAsync` for a possibly-different selected
  level.
- **Fix:** replaced the single `_isSelectingLevel` boolean with an explicit
  three-state `GameMode` enum (`LevelSelecting` -> `LoadingLevel` ->
  `Playing`), flipped to `LoadingLevel` **synchronously**, before the
  `await World2D.LoadAsync(...)` gap, so no re-entrant call can ever observe
  `LevelSelecting` again during the load. Added a second, blanket
  `_isProcessingFrame` guard around all of `OnFrame` (drops any literally-
  overlapping call outright) as defense in depth, independent of which
  `GameMode` is active.
- **Also while touching this area:** `LevelCatalog`'s level list moved from
  a hardcoded C# array to an authored `Global/Levels.ini` manifest (`[Levels]
  Order = ...`), and level thumbnails gained real frame-based animation
  (`//end`-separated frames + an `[Animation]` section in the level's own
  `settings.ini`, reusing `SpriteLoader`'s existing animation-timing parsing)
  instead of only ever rendering their first frame - see docs/AssetFormat.md
  §3.1/§4.4.

## Material-driven collision response (Density/Friction/Restitution/Mass) and force-based movement for non-player bodies

- **Prompted by user request:** significantly enhance the physics system
  with material-dependent collisions, gravity/normal-force concepts, and
  force-based movement - explicitly scoped down to exclude the player from
  the force-based movement change for this round, and to add a `TestPhysics`
  validation level.
- **Material resolution:** a new `MaterialLibrary` (mirroring `ColorPalette`'s
  Global+Level merge pattern) parses `Global/Materials.ini` plus an optional
  level-local `Materials.ini`, exposing a `Material` record
  (`Density`/`Friction`/`Restitution`) per name, with a zeroed
  `Material.Undefined` fallback for an unknown/absent name. `Body2D` gained
  `MaterialName` (resolved from the dominant non-empty material of the
  active sprite frame's per-cell material layer/`DefaultMaterial` - see
  `SpriteLoader`, which already existed), plus `Density`/`Friction`/
  `Restitution` and a computed `Mass = Density * Size.X * Size.Y`. These are
  all resolved once, centrally, in `World2D.LoadAsync` right after a body is
  spawned, rather than scattered across each concrete body type - removing
  the ad hoc per-class `Restitution` field that `DynamicObject2D`/
  `MovingEnemy2D` used to each own individually.
- **Per-placement overrides, deliberately supported (not deferred):** a
  level placement's ini section can override the resolved material name via
  a new `Material` key (e.g. re-using the one `Ball` sprite asset with a
  different material per placement in `TestPhysics`, rather than needing a
  distinct, otherwise-identical sprite asset per material), or override just
  the resulting `Restitution` via the existing `Restitution` key (e.g.
  `TestMovement`'s intentionally "weightless, perfectly elastic" ball).
  This was implemented rather than deferred because `TestPhysics` requires
  it to reuse a single ball shape across four material combinations.
- **Restitution/friction combination rule:** `CollisionSystem.Combine(a, b)`
  is a plain average of both contacting bodies' own values, not
  `Math.Min`/`Math.Max` or a geometric mean - chosen so either side can
  meaningfully pull the result toward its own value (a rubber ball on
  concrete bounces less than rubber-on-rubber but more than
  concrete-on-concrete) rather than one material always dominating
  outright. Applied uniformly for both moving-vs-static
  (`ResolveAgainstSolid`/`ResolveRectAgainstSolid`) and moving-vs-moving
  (`ResolveBodyPair`) collision response, replacing the old
  `CollisionSystem.GetRestitution` type-switch entirely.
- **Mass-weighted moving-body-pair resolution:** `ResolveBodyPair` now
  splits position correction by relative mass (a heavier body yields less
  ground than a lighter one) instead of always 50/50, and resolves the
  along-normal velocity response via a standard 1D mass-weighted impulse
  (`j = -(1+e) * relativeVelocity / (1/mA + 1/mB)`, then
  `v' = v +/- j/mass`) instead of each body independently reflecting its own
  velocity via its own restitution. A body with no resolved mass (0) is
  treated as mass 1 purely to keep the impulse formula well-defined; this
  does not affect that body's own position-correction share, which already
  falls back to an even split independently when total mass is 0.
- **Friction as a flat per-frame damping factor**, not a continuous
  force/impulse model: `ApplyFriction(velocityComponent, friction)` damps
  the tangential (along-surface) velocity component by the pair's combined
  friction each frame contact is resolved - simple and enough to make a
  grippy material (e.g. `Concrete`) visibly settle sliding motion faster
  than a slick one, without needing normal-force-dependent friction-force
  integration. Rejected: a true friction force scaled by the normal force
  and integrated like gravity - unnecessary complexity for a 2D ASCII
  platformer with no meaningful sliding gameplay yet.
- **No separate constraint-solver "normal force" term:** rather than
  computing and applying an opposing normal force every frame a body rests
  on something, the existing grounded-contact response (a resting body's
  downward velocity is zeroed/reduced by its resolved restitution right at
  the point of contact) already achieves the same net effect pragmatically.
  `IPhysicsBody.IsGrounded` remains just that same contact signal surfaced
  for other systems (animation, jump gating) to read.
- **Force-based movement, non-player only:** every non-player
  `IGravityAffected`/`IPhysicsBody` in `World2D.Objects` now integrates via
  `PhysicsSystem.StepMovingBodyWithForces` - a per-frame net-force
  accumulator (today, just gravity as `mass * world.Gravity`) converted to
  acceleration via `a = F / mass` and integrated into velocity, rather than
  a direct `velocity.Y += gravity * dt`. For a gravity-only body this is
  numerically identical (mass cancels out of `F / mass`), but the
  accumulator is the deliberate extension point for any future non-gravity
  force source (e.g. a hypothetical `IThrustBody`). A body with no resolved
  mass (0) is treated as mass 1 for this conversion, same rationale as the
  collision-impulse math above.
- **Player explicitly excluded from force-based movement this round** - per
  explicit user scoping ("limit that to all objects except Player for
  now"). `Player2D` is matched first in `PhysicsSystem.Step`'s per-body
  dispatch switch and continues to use the original direct
  velocity-assignment `StepMovingBody` path, ahead of the generic
  `IGravityAffected`/`IPhysicsBody` cases every other body now falls into.
  Converting player movement to the same force model is left for a future,
  separate round (see the TODO comment above the player movement block in
  `PhysicsSystem.Step`).
- **New `TestPhysics` level** added purely to visually validate this
  material system end-to-end: two static floor segments side by side
  (`Concrete` and `Wood`, both reusing the `SteelPlatform` sprite shape via
  the new `Material` override), each with a `Rubber` and a `Plastic` ball
  (both reusing the one `Ball` sprite asset, likewise via `Material`)
  dropped from rest at identical height - so any difference in
  bounce-height/settle-time visible between the four drop zones can only be
  coming from each ball/floor material pairing's own combined
  restitution/friction, not from any other varying factor. Two new global
  materials, `Plastic` and `Concrete`, were added to `Global/Materials.ini`
  to support this.

## Hang jump/swing-off debounce promoted to `IHangerBody.SuppressHangUntilClear`, and overlap detection split from the snap/stop itself

- **Prompted by user follow-up (regression #1):** after the hang snap
  accuracy fixes above, the user reported "jumping/swing from pipe/rope has
  disappeared" - pressing Jump while hanging appeared to do nothing.
- **Root cause:** `PhysicsSystem`'s hang stance ladder clears `IsHanging`
  and sets an upward `velocity.Y` the instant Jump is pressed from the
  fully-stretched hang, but the player's collision rect is still
  overlapping the pipe/rope that same frame (nothing has moved yet).
  `CollisionSystem.ResolveClimbingAndHanging` ran unconditionally and,
  seeing that same overlap plus a qualifying approach direction, zeroed
  `hanger.Velocity.Y` and re-snapped the body right back - the same frame
  the jump-off velocity was set, cancelling it before it was ever visible.
  The existing `_suppressHangUntilClear` debounce lived only in
  `PhysicsSystem` and was invisible to `CollisionSystem`, so it had no
  effect on the re-catch.
- **Fix:** `_suppressHangUntilClear` was promoted from a private
  `PhysicsSystem` field to a shared `IHangerBody.SuppressHangUntilClear`
  auto-property (implemented on `Player2D`), so `CollisionSystem` can also
  observe it. `ResolveClimbingAndHanging` now skips the actual
  snap/velocity-zero step while `SuppressHangUntilClear` is set.
- **Prompted by user follow-up (regression #2):** after the above fix, the
  user reported the jump/swing was visible but far too short - "player
  doesn't get high enough before getting snapped again" - and asked
  whether the ladder's jump-off (which already worked well) used a
  different mechanism worth mirroring.
- **Root cause:** the first fix's implementation gated the *entire* overlap
  loop (including `hanger.IsTouchingHangable = true`) on
  `!hanger.SuppressHangUntilClear`, so while suppressed,
  `IsTouchingHangable` was forced straight to `false` on the very first
  frame after jumping - even though the body was still genuinely
  overlapping the pipe. `PhysicsSystem` releases `SuppressHangUntilClear`
  as soon as `!IsTouchingHangable`, so the debounce cleared almost
  instantly (one frame) instead of lasting the whole jump arc, letting the
  very next frame re-catch and re-snap the player. This is exactly what
  ladders get right: `IClimberBody.IsTouchingClimbable` is always computed
  from genuine overlap regardless of `_suppressClimbUntilClear` - that
  debounce only blocks `PhysicsSystem` from re-engaging `IsClimbing`, it
  never hides the overlap fact from itself - so `_suppressClimbUntilClear`
  naturally stays held for the ladder jump's whole arc, until the player
  truly clears the ladder or lands.
- **Fix:** `IsTouchingHangable` is now always set from genuine overlap
  (mirroring `IsTouchingClimbable`), regardless of
  `SuppressHangUntilClear`; only the velocity-zero + `SnapOntoHangable`
  step itself is skipped while suppressed. The debounce now stays engaged
  for the player's whole jump/swing arc, matching ladder behavior.
- **Known remaining limitation (tracked in
  [Design.md](Design.md#planned--future-work)):** because the debounce is
  now only released once the body fully clears the hangable surface's
  overlap (same as ladders), a modest jump - e.g. swinging up to a pipe one
  character above, or sideways onto an adjacent platform/wall - can still
  reach a new hangable/solid surface mid-arc before the debounce clears,
  so it can't yet snap onto that new surface as readily as intended. A
  ladder-style "release on landing too" doesn't directly translate to hang,
  since landing isn't the relevant condition for re-grabbing a hangable
  surface - a different release condition still needs designing.

## Hang snap now corrects position exactly on overlap, instead of a fudge-tolerance edge check - `EdgeTolerance` removed

- **Prompted by user follow-up:** after an initial attempt just shrank
  `EdgeTolerance` (0.05 -> 0.01), the user correctly pointed out two
  things: (1) shrinking a symmetric tolerance doesn't make the check
  directional, it just narrows the same early-snap window on both the
  reach-up-from-below and fall-through-from-above cases; and (2) solid
  terrain doesn't use a tolerance at all - `ResolveRectAgainstSolid`
  detects genuine rectangle overlap, then corrects the body's position
  exactly onto the solid's surface (e.g. `newRectBottom =
  bestSolidRect.Top`). Hanging should work the same way: overlap is
  overlap, and gets corrected, rather than merely tested against a
  fractional-cell slack window.
- **Root cause (unchanged from the previous entry):** `WouldSnapFromBelow`
  compared the body's top edge to the hangable surface's top edge with a
  tolerance slack, so the grab could fire while the body's top was still a
  fraction of a cell away from true alignment in either direction - this
  read as the hang engaging early/late even though the ASCII row shown
  looked correct, since world positions and rendering are both continuous.
- **Fix:** `ResolveClimbingAndHanging` now snaps a hanger's position
  (`SnapOntoHangable`) so its own overall topmost collision edge lands
  exactly on the hangable surface's bottom edge (hanging just underneath
  it, not overlapping into it) the instant genuine overlap plus the
  qualifying approach direction (`WouldSnapFromBelow`, now a plain
  `bodyTop >= otherRect.Top` check with no tolerance) plus the existing
  snap-speed gate are all satisfied - mirroring `ResolveRectAgainstSolid`'s
  "detect overlap, correct exactly" pattern. An initial version of this
  snapped to the surface's *top* edge instead, which visibly overlapped
  the player into the hangable surface rather than hanging underneath it;
  corrected to the surface's bottom edge, the one that actually sits just
  below the player's own top row once grabbed. Only the vertical axis is
  corrected; hanging still shimmies laterally under ordinary player input
  afterward. `EdgeTolerance` itself has been removed from `CollisionSystem`
  entirely, since nothing needs it anymore.
- **Follow-up fix (snap-then-immediately-drop):** landing the hanger's top
  edge exactly flush with the hangable surface's bottom edge (zero actual
  overlap) meant `Rect2D.Overlaps`'s strict inequalities (`Bottom >
  other.Top`) returned false again the very next frame, so
  `IsTouchingHangable` flipped back to false and the player dropped right
  after the grab became visible - not a gravity-suspension timing issue
  (`Player2D.GravityAffected` already excludes `IsHanging` the same frame
  it's set). Fixed by adding a tiny `HangOverlapEpsilon` (0.01 cells) that
  `SnapOntoHangable` pulls the hanger's top edge past the surface's bottom
  edge by, guaranteeing genuine (if visually imperceptible) overlap
  persists so the grab holds.
- **Second follow-up fix (jump upward motion cut off dead on first
  touch) - reverted, wrong direction:** the user reported that jumping up
  into a hangable surface from below now stopped the upward motion
  immediately on first contact instead of continuing to rise. The initial
  attempt deferred `SnapOntoHangable` until `hanger.IsHanging` was already
  true (only set the *following* frame by `PhysicsSystem`, since physics
  runs before collision each tick), reasoning the snap fired a frame too
  early. This made things worse: a thin, one-cell-tall pipe often only
  overlaps for a single frame, so by the time `IsHanging` finally engaged
  next frame the body had already moved fully clear of it uncorrected,
  and the overlap was gone again before the deferred catch could ever
  apply - reported by the user as the pipe becoming "no longer passable"
  (hitting it like a solid ceiling from below with no snap) and falling
  straight through with no snap from above either.
- **Third follow-up fix (restored synchronous catch, matching solid
  terrain):** the user correctly pointed out that solid-surface landing
  (`ResolveRectAgainstSolid`) never waits on a flag set on a previous
  frame (e.g. `IsGrounded`) before stopping/snapping a body - it reacts
  the instant it detects overlap, in the same `CollisionSystem.Resolve`
  call, and that reliability should be mirrored here rather than
  reinvented. Reverted the frame-deferral: `ResolveClimbingAndHanging` now
  zeroes the hanger's vertical velocity and calls `SnapOntoHangable`
  synchronously the instant genuine overlap plus the qualifying approach
  direction plus the snap-speed gate are satisfied - the same frame
  `IsTouchingHangable` first becomes true, exactly mirroring how a solid
  landing stops/snaps a body immediately rather than waiting a frame.
  This still lets an ordinary jump arc rise most of the way up first (the
  velocity/position are untouched every frame the body isn't yet
  overlapping the surface), but once the collision rects do overlap the
  grab now catches immediately and reliably instead of racing a one-frame
  overlap window against a flag from the previous tick.
- **Fourth follow-up fix (actual root cause: `HangOverlapEpsilon` had the
  wrong sign) - fixes both remaining symptoms in one line:** even after
  restoring the synchronous catch, the user reported the grab still
  didn't hold - falling through a pipe snapped only for an instant then
  immediately let go again, and jumping up into a pipe stopped dead
  ("hit its head") without ever actually catching, as if the pipe were
  suddenly solid instead of passable. Root cause: `SnapOntoHangable`
  computed `otherRect.Bottom - topOffset + HangOverlapEpsilon` - adding
  the epsilon pushes the hanger's resulting top edge *below* (Y increases
  downward) the surface's bottom edge, i.e. `bodyTop > otherRect.Bottom`,
  which is the opposite of overlap under `Rect2D.Overlaps`'s strict
  `bodyRect.Top < otherRect.Bottom` check. So the very "correction"
  meant to guarantee persistent overlap was instead placing the body just
  clear of the surface: `IsTouchingHangable` immediately evaluated false
  on the following frame regardless of how the snap/velocity-zero was
  sequenced, which is why every previous timing-focused fix (deferred
  catch, synchronous catch) still failed to hold - the snap position
  itself was never actually inside the pipe to begin with. Fixed by
  flipping the sign to `otherRect.Bottom - topOffset - HangOverlapEpsilon`,
  which pulls the hanger's top edge slightly *above* the surface's bottom
  edge (into genuine overlap) instead of past it.
- **Deferred:** a similar exact-alignment correction for climbing
  (snapping the player horizontally onto a ladder's own horizontal
  center/rect once grabbed) was raised as a related idea, but ladders can
  vary in width unlike a hangable surface's single top edge, making the
  "correct to what" question less obvious - left for a later, separate
  pass focused specifically on climbing alignment.

## Climbing's jump-off moved into the same stance-ladder chain as the other transitions, and gains a debounce fixing a real re-grab bug

- **Prompted by user follow-up asking for a "trivial" cleanup for
  consistency:** the climbing jump-off (letting go of a ladder via a Jump
  press) lived inline inside the vertical-movement block, structurally
  different from the ground stance toggle and the `Hang` stance ladder,
  which both live in one shared, ordered `if`/`else if` chain earlier in
  `Step`. Moved climbing's jump-off into that same chain (as its own `else
  if (player.IsClimbing)` branch) purely for consistency - the vertical
  movement block itself is now a plain speed calculation with no branching
  on `jumpPressedThisFrame` left in it.
- **This surfaced (and fixed) a genuine, previously-unnoticed bug the user
  suspected from testing:** unlike `Hang`'s "let go", climbing's jump-off had
  no debounce - and `ClimbJumpSpeed` (15) is comfortably under
  `CollisionSystem.MaxSnapSpeed` (24), so a player jumping off a ladder was
  still both geometrically overlapping it and (very likely) still holding
  Up/Down for a frame or two afterward. Without a debounce, the very next
  frame's climb-engage check would immediately re-grab the same ladder
  before the jump was ever visible - making the jump-off silently do
  nothing from the player's perspective, exactly the "released too early to
  jump?" symptom reported. Fixed by adding `_suppressClimbUntilClear`,
  mirroring the existing `_suppressHangUntilClear`: set the instant the
  player jumps off, cleared once `IsTouchingClimbable` goes false again.

## `Up` becoming a jump trigger again is reverted - it's not intuitive; back to `Up`/`Jump` always fully separate (supersedes the entry below)

- **Prompted by user follow-up, immediately after trying the change below:**
  even though the priority-based aliasing was logically sound (verified via
  walkthroughs of the climbing jump-off guard and the `Hang`->`Clamber`
  transition), it didn't feel intuitive in practice - a single input
  (`IsJumpPressed`) silently meaning different things depending on context
  and priority rules was more confusing to reason about day to day than
  having Up and Jump be two inputs that are simply always distinct.
- **Fix: reverted `InputState.IsJumpPressed` to no longer include
  `IsUpPressed`**, back to only the dedicated jump keys
  (`Space`/`ControlLeft`). The climbing jump-off check's `!upKeyDown &&
  !downKeyDown` guard - only needed to stop Up/Down from being misread as a
  jump-off once Up aliased into Jump - is no longer necessary and was
  removed at the same time, restoring the climbing branch to a plain
  `if (jumpPressedThisFrame)` check. All associated comments/docs describing
  the aliasing were reverted alongside it.

## `Up` becomes a jump trigger again everywhere `Jump` applies, superseding the earlier full separation - but only where Up has no directional meaning to prioritize instead (reverted by the entry above)

- **Prompted by user follow-up, reversing part of the "always fully
  separate" decision below:** the earlier full separation was originally
  motivated by "Up can no longer jump, or `Clamber` becomes unreachable from
  `Hang`". Revisiting that: the actual conflict was never Up-vs-Jump in
  general - it only ever existed for the single case of Up pressed while
  already `Hang`ing, where Up's directional meaning ("pull into `Clamber`")
  and a hypothetical jump meaning would collide on the same key press. Every
  other context (ground, `Climb`) has no such collision, since a directional
  Up press there already has an unambiguous, higher-priority meaning
  ("stand up"/"keep climbing") that a jump/let-go action should never
  override anyway - so Up can safely also be a jump trigger there and simply
  never actually acts as one while that directional meaning is available.
- **Fix (at the time): `InputState.IsJumpPressed` included `IsUpPressed`**
  (in addition to each key set's own dedicated jump key), restoring the more
  familiar "Up/W also jumps" convention. To prevent this from reintroducing
  the original conflict, every `PhysicsSystem` call site that branches on
  both gave Up's directional/posture meaning priority whenever one applied
  for the current stance: the `Hang` stance ladder already checked
  Up-to-`Clamber` before Jump-to-swing-off (no change needed there), and the
  climbing jump-off check was tightened to require a jump-key press with
  neither Up nor Down held (i.e. only the dedicated Space/`ControlLeft`
  keys) so holding Up to climb a ladder couldn't be misread as "let go and
  jump". A plain ground jump (`Stance == "Walk"`, no directional meaning of
  Up applies there beyond the already-separate Crawl->Walk stance toggle,
  which only fires from `Crawl`) was unaffected by this priority rule and
  simply jumped from either key, same as before the original separation.
- **Confirmed an assumption the user checked while requesting this:** jumping
  was still not reachable directly from `Crawl` or `Clamber` - both remained
  reachable only via their own stance-ladder step (standing up first, or
  un-clambering back to `Hang` first), exactly as before. This entry only
  changed which keys could trigger a jump, not from which stances a jump was
  possible - but the entry above reverts the whole change regardless.

## `HangCrawl`/`HangCrawling` renamed to `Clamber`/`Clambering`; hanging gains its own dedicated lateral speed constants

- **Prompted by user request:** `HangCrawl` read as a mash-up of unrelated
  verbs (hanging + crawling) rather than naming the pose itself. Renamed
  throughout code, assets, and docs to `Clamber`/`Clambering` - the compact,
  both-hands-and-feet-on-the-rope grip used to squeeze through narrow gaps
  alongside a hangable surface. This covers the stance name (`Clamber`),
  the `IHangerBody`/`Player2D` property (`IsClambering`, was
  `IsHangingCrawled`), the clip-folder and clip names (`Sprites/Player/Clamber/`,
  `clamber_idle`/`clamber_left`/`clamber_right`, was `HangCrawl`/`hangcrawl_*`),
  and every code comment/doc reference to the old name. Purely a rename -
  no behavior change.
- **Hanging's lateral movement also gained its own dedicated speed
  constants instead of reusing a ground speed:** `HangSpeed` (8, shimmying
  sideways along a pipe/rope while fully stretched) and `ClamberSpeed` (5,
  slower still while gripping tight in the compact `Clamber` pose) - both
  slower than `WalkSpeed`/`CrawlSpeed` since arm-only lateral movement is
  weaker than a full stride, and `ClamberSpeed` slower than `HangSpeed` for
  the same reason `CrawlSpeed` is slower than `WalkSpeed` (a more
  compact/braced pose trades speed for stability/fit).

## `Up`/`Jump` are always fully separate inputs (never equivalent, even on the ground); a "Player 2" key set is added; jump-off is added for climbing too, and speed constants gain a three-tier naming scheme

- **Prompted by user follow-up after the previous two entries below:** the
  first pass kept `IsUpPressed || IsJumpPressed` as the ground jump
  condition (treating Up and Jump as equivalent there, distinct only for
  `Hang`). The user asked to never conflate them anywhere, including the
  ground - jump is always its own dedicated action. That immediately raises
  a keyboard-ergonomics problem: the arrow-key player already has `Space`
  right next to their movement keys, but the WASD player has no equivalent
  key next to WASD once Up (`KeyW`) no longer also jumps.
- **Fix: `InputState` now documents and treats the keyboard as two full,
  independent key sets** - "Player 1" (arrow keys + `Space`) and "Player 2"
  (`WASD` + `ControlLeft`) - rather than one shared set of "movement keys"
  plus one shared "jump key". `IsJumpPressed` now checks `Space` (Player 1)
  or `ControlLeft` (Player 2, positioned next to WASD the same way `Space`
  sits next to the arrow keys), and every call site (ground, `Climb`,
  `Hang`) uses only `IsJumpPressed` for jumping/letting-go - `IsUpPressed`
  never contributes to any jump/let-go decision anywhere anymore.
- **Climbing gained the jump-off it was previously missing entirely:** a
  `Jump` press while `IsClimbing` now also lets go and launches upward
  (mirroring `Hang`'s jump-off, added below), rather than climbing being the
  only suspended/attached stance with no jump-off exit at all.
- **Speed constants renamed again to a `{Stance}JumpSpeed` scheme for the
  three launch impulses, ordered by attachment strength:** `WalkJumpSpeed`
  (18, standing jump, legs planted on solid ground - strongest),
  `ClimbJumpSpeed` (15, letting go of a ladder rung - a bit weaker, less
  overall grip/momentum than solid footing), `HangJumpSpeed` (12, swinging
  off a pipe/rope - weakest, arms alone are weaker than legs). The
  horizontal climb constant is renamed `ClimbHorizontalSpeed` (was
  `ClimbLateralSpeed`) and bumped from 6 to 8 - still slower than
  `WalkSpeed` (12, full leg-driven stride) but now faster than `CrawlSpeed`
  (6, a deliberately slow compact stance), rather than matching crawl
  exactly. `ClimbVerticalSpeed` (10) is unchanged.

## `Up` and `Jump` (Space) become distinct inputs for `Hang`; ground jump still treated them as equivalent (superseded by the entry above)

- **Prompted by the jump-off-hang feature below breaking `HangCrawl`
  reachability:** `InputState.IsUpPressed` previously ORed `Space` in
  alongside `ArrowUp`/`KeyW`, so once Up became "swing off" for `Hang`, there
  was no remaining way to press "just Up" to pull into `HangCrawl` - Space
  and Up were indistinguishable at every call site.
- **Fix (at the time): `InputState` gained separate `IsUpPressed`
  (`ArrowUp`/`KeyW`) and `IsJumpPressed` (`Space`) properties**, with the
  ground jump condition deliberately kept as `IsUpPressed || IsJumpPressed`
  to preserve old ground behavior. The very next entry above replaced this
  ground-equivalence choice with full separation everywhere instead.

## Jump/swing-off from `Hang` returns, but only from the fully-stretched pose, not `HangCrawl`

- **Prompted by user feedback after the earlier hang stance ladder cleanup**
  removed jumping off a hang entirely, leaving only the explicit "second Down
  to let go" exit. Requested back specifically as a swing-off-a-pipe-or-rope
  action, deliberately restricted to the fully-stretched `Hang` pose - the
  compact `HangCrawl` grip (knees pulled up, holding tight) doesn't represent
  the same "swinging and about to let go" body position, so it keeps its
  existing behavior (Down un-crawls back to `Hang`; no direct jump-off).
- **Fix (at the time): an Up press while in plain `Hang` cleared `IsHanging`
  and gave velocity an upward jump impulse** (originally the same magnitude
  as the standing jump; later split into its own, weaker `HangJumpSpeed` -
  see the entry above), reusing the same `_suppressHangUntilClear` debounce
  as the existing let-go-and-drop exit so the player can't instantly re-grab
  the surface they just launched off. Horizontal velocity for a diagonal
  swing (Up held together with Left/Right) falls out of the existing,
  unchanged horizontal-movement block that runs later in the same `Step` -
  no separate diagonal-specific code needed, since by the time it runs the
  player is simply airborne and un-hung like any other jump. The resolved
  pose naturally becomes `Jump` afterward too, with no special-casing: with
  `IsHanging` now false and `IsGrounded` already false (set false throughout
  every hang), the existing `poseStance`/`facing` resolution at the bottom of
  `Step` falls through to the ordinary airborne case.

## `[Stances]` facing is resolved from each clip name's own suffix, not a fixed slot position/flag

- **Prompted by a follow-up review of the `Up`/`Down` facing axis added just
  before this:** that design encoded which axis (`Left`/`Right` vs `Up`/`Down`)
  a stance used via clip *position* in its `[Stances]` line plus a bolted-on
  literal `Vertical` marker in a fourth slot when the axis wasn't the default
  horizontal one. Flagged as clunky and, worse, genuinely insufficient: an
  upcoming `Swim` stance needs *all four* directions (plus idle) at once,
  which a single "pick one axis" flag can't express at all.
- **Fix: a stance's clips are no longer positional.** Each clip name's own
  trailing suffix - `_idle`/`_left`/`_right`/`_up`/`_down` - says which
  `Facing` it's for (a name with none of these suffixes is treated as
  `Idle`), parsed by a small `ResolveFacingFromClipName` in
  `SpriteLoader.ParseStances`. A `[Stances]` line is therefore just an
  unordered list of clip names, each self-describing its own facing; a stance
  declares only the facings it actually has art for - two (`_left`/`_right`
  for Walk), two different ones (`_up`/`_down` for Climb), or all four at
  once (`_left`/`_right`/`_up`/`_down` for a future Swim) - with no special
  marker needed for any of these cases, including the four-direction one the
  previous slot-based design had no room for.
- **`StanceDefinition` changed shape to match:** `LeftClip`/`RightClip`/
  `UpClip`/`DownClip` (four fixed optional properties) became a single
  `DirectionalClips` dictionary keyed by `Facing` (never containing `Idle`,
  which stays its own required `IdleClip` property) - open-ended over however
  many directional facings a stance actually declares, rather than a fixed
  set of slots. `GetClipName(Facing)` still falls back to `IdleClip` for any
  facing the stance didn't declare, unchanged in spirit from before.
- **No change to `PhysicsSystem` beyond what the previous entry already
  introduced** - it still resolves `Facing` from the climb input directly
  while `IsClimbing` and from `velocity.X` otherwise; only how `[Stances]` is
  authored and parsed changed. `Climb`'s ini line is now
  `climb_idle, climb_up, climb_down` (order no longer matters), and the
  player's climb-moving art was split into two real files/clips
  (`climb_up`/`climb_down`, currently identical content) instead of one
  `climb_moving` clip reused via a flag - a pure asset rename with headroom
  for genuinely distinct up/down art later.

## `Facing` gains a vertical axis (`Up`/`Down`); `Climb`/`ClimbMoving` collapse into one `Climb` stance

- **Prompted by a design review of the `ClimbMoving` stance added earlier:**
  every other multi-pose stance (`Walk`, `Crawl`, `Jump`, `Hang`, `HangCrawl`)
  is a single stance whose `Idle`/`Left`/`Right` clips are selected by
  `Facing`, resolved from horizontal input/velocity. `Climb`, uniquely, was
  split into two separate top-level stances (`Climb` for idle head-sway,
  `ClimbMoving` for actually climbing) instead of being one stance with a
  facing-style choice - an inconsistency, not a deliberate design difference.
- **On inspection, there is no real conceptual difference to justify the
  special case.** Walking's `Idle` means "facing the viewer, not moving
  sideways"; its `Left`/`Right` mean "moving sideways in that direction,"
  selected from `velocity.X`. Climbing's `Idle` means "facing the ladder, not
  moving vertically" (already the case - `climb_idle` is literally described
  as its head-sway-while-holding-still clip); a hypothetical `Up`/`Down` would
  mean "moving vertically in that direction," selected from the climb input
  directly. Same shape, different axis. The only actual wart was that
  `Facing`/`StanceDefinition` had no vertical axis to select through - not
  that climbing has some fundamentally different relationship between pose
  and movement.
- **Fix: `Facing` gained `Up`/`Down` cases alongside `Left`/`Right`.** (The
  original follow-up implementation encoded which axis a stance used via a
  positional `Vertical` flag in `[Stances]` - superseded by the very next,
  newer entry above, which replaced that with per-clip-name suffix parsing;
  `Facing` itself and `PhysicsSystem`'s axis-resolution rule below are
  unchanged.) `PhysicsSystem.Step` resolves `Facing` from the climb input
  directly (not `velocity.Y`, since climbing itself directly sets `velocity.Y`
  from that same input) only while `IsClimbing`, and from `velocity.X`
  otherwise - the same "resolve along whichever axis this stance actually
  moves on" rule applied consistently instead of `Climb` being special-cased.
- **This removes `ClimbMoving` as a stance entirely** - `Climb` is now one
  stance with three clips (`Idle`/`Up`/`Down`), symmetric with every other
  stance, and `IsClimbing` alone still gates all engage/disengage logic (pose
  selection is the only thing that reads whether up/down is currently held,
  same as before).

## Hanging gains a structured, symmetric up/down stance ladder; snap-from-above uses the body's overall top edge


- **Prompted by two reported quirks after the hang feature and its visuals
  were otherwise finished:** falling onto a pipe/rope from above snapped far
  too early (the player could end up hanging from mid-body or even feet
  level instead of from its actual top row), and toggling `IsHangingCrawled`
  via the same crawl key used on the ground felt arbitrary/unstructured, with
  no consistent relationship to how Up/Down already work while standing.
- **Snap-from-above fix:** `CollisionSystem.WouldSnapFromBelow` previously
  compared the *deepest-overlapping rect pair's* top edge (from
  `TryFindDeepestOverlap`) against the hangable surface's top edge. For a
  multi-rect body like the player (separate head/body/leg rects), the
  deepest-overlapping pair while falling through a thin pipe is initially a
  lower rect (e.g. a leg), whose own top edge is nowhere near the player's
  actual topmost row - so the check passed while the player was still deep
  under the surface. Fixed by comparing the body's overall top edge (the
  minimum `Top` across *all* of its collision rects) instead, so falling
  through from above and climbing/jumping up from below both resolve to the
  same geometric moment: the body's true top row is (just) below the
  surface's own top edge.
- **Hanging stance ladder:** rather than an isolated crawl-key toggle while
  hanging, Up/Down while suspended now mirror the existing ground Walk/Crawl
  toggle as its structural inverse - a single mental model instead of two
  unrelated ones. On the ground, Up always means "more upright" (Crawl ->
  Walk) and Down always means "more compact" (Walk -> Crawl). While hanging,
  Up pulls into the compact `HangCrawl` pose (mirroring Crawl) and Down
  extends to the fully-stretched `Hang` pose (mirroring Walk); since fully
  stretched is already the least-attached pose, a further Down from there
  means an explicit "let go" and drop, replacing the old (and arguably
  backwards) "Up jumps off a hang" behavior. Both the ground and hanging
  toggles are edge-triggered off dedicated `_wasUpKeyDown`/`_wasDownKeyDown`
  latches in `PhysicsSystem` (previously only Down had one, shared and reused
  for both ground crouch and the old hang-crawl toggle).
- **Debounce on re-grab after letting go:** the instant a player lets go from
  `Hang`, they are still geometrically touching/underneath the very same
  surface for that frame (nothing has moved yet), which would otherwise
  re-engage `IsHanging` immediately and make "let go" unreachable. A small
  `_suppressHangUntilClear` latch in `PhysicsSystem` blocks re-engagement
  until `IsTouchingHangable` actually goes false (i.e. the player has fallen
  clear), then clears itself for a normal future grab.

## `AsciiRenderer.BuildFrame` culls glyph generation to the camera's viewport

- **Prompted by a performance review of the render path:** `BuildFrame`
  previously iterated the entire world's background grid, and every game
  object's full sprite frame, unconditionally every frame - relying on the
  HTML canvas to clip pixels that ended up off-screen. For a world larger
  than the viewport this did per-cell work (allocating a `Glyph`, resolving
  colors, sending it through JS interop) proportional to total world size
  instead of visible viewport size.
- **Fix: `BuildFrame` now takes the camera's current viewport size (in
  cells) and derives a visible rect from `camera.Position` +
  viewport size.** `AddBackgroundGlyphs` only loops the row/column range
  that intersects this rect (clamped to the world's own bounds), and
  `BuildFrame` skips calling `AddGameObjectGlyphs` entirely for any body
  whose bounding box (`Position`/`Size`) doesn't intersect it, before ever
  touching that body's sprite frame grid.
- **Scope is render-only; simulation is untouched.** Physics
  (`PhysicsSystem.Step`) and collision (`CollisionSystem.Resolve`) still run
  against every body in `world.Objects` regardless of camera position -
  off-screen bodies keep moving, colliding, and animating exactly as
  before. Rejected (for now): also culling physics/animation updates for
  bodies far outside the camera, since that would change simulation
  behavior (e.g. an off-screen moving platform "freezing" instead of
  continuing to cycle) rather than being a pure rendering optimization.

## `CollisionSystem` resolves solid collisions per body collision rect, not per globally-deepest-overlapping pair


- **Prompted by two related bugs surfaced by testing the new mid-height
  `MiddleWall` placements and ladder/pipe overlap in `TestMovement`:** the
  player could sometimes walk through a solid wall, and jumping into a wall
  could leave the player visibly stuck "inside" its lower layers while only
  the top layer behaved as standable.
- **Root cause: a multi-rectangle body's collision shape (e.g. the player's
  narrower "head" rect above its wider "torso" rect, from
  `CollisionShapeBuilder`) was resolved against a solid by picking a single
  globally-deepest-overlapping (body rect, solid rect) pair across every
  combination, then correcting only that one pair.** A shallow/different-axis
  overlap on one rect (the head clipping a wall's top corner) could "win"
  over a deeper overlap on another rect (the torso still embedded in the
  wall's side), so only the head got pushed out while the torso stayed stuck
  in the solid - explaining both the walk-through and stuck-inside symptoms.
- **Fix: `ResolveAgainstSolid` now loops over each of the body's own
  collision rects and resolves each one individually against the solid**
  (`ResolveRectAgainstSolid`), rather than reducing the whole body to one
  "best" pair. `TryFindDeepestOverlap`/the push-out math themselves are
  unchanged and still reused per rect.
- **Each iteration re-reads `body.CollisionRects` fresh by index rather than
  enumerating one rect list captured before the loop started.** Resolving one
  rect can move the body's `Position`; since `CollisionRects` is computed
  live from `Position`, the next rect must be checked against the
  already-corrected position, not a stale pre-frame one - otherwise two
  rects' corrections can fight each other frame to frame, which manifested
  as the player's pose jittering between standing and jumping while resting
  still on solid ground.

## `Passable`/`Climbable`/`Hangable` are plain per-instance bools, not new classes or interfaces

- **Prompted by planning ladders/pipes/ropes and by `EffectInstance2D` being
  accidentally treated as solid terrain.** The old `ConsoleGame2D` reference
  project modeled this as `IsClimbable`/`IsHangable`/`IsPassable` plain bool
  properties directly on its body base class, with collision resolution
  gating on `!IsPassable` rather than excluding specific types. That pattern
  was ported, rather than introducing dedicated `Ladder2D`/`Pipe2D` classes,
  because the actual requirement is purely per-instance/data-driven (any
  static placement - a wall, a hazard, a plain platform used as a secret
  passage - can independently be made passable/climbable/hangable), with
  nothing about it varying by concrete type.
- **`Body2D` gained `IsPassable`, `IsClimbable`, `IsHangable` bool properties**
  (default `false`), settable per-placement from `_objects.ini` via new
  `Passable`/`Climbable`/`Hangable` keys in `World2D.LoadAsync`, with
  `Passable` defaulting to `true` for `StaticEnemy`/`Collectable` kinds to
  preserve their prior (previously hardcoded) non-blocking behavior.
- **`CollisionSystem.Resolve`'s `solids` filter was simplified from an
  exclusion list (`is not ICollectableBody and not IHazardBody and not
  EffectInstance2D`) to a single positive check: `body.IsStatic &&
  !body.IsPassable`.** This directly fixes the `EffectInstance2D`-as-terrain
  bug at its root (that type now just always sets `IsPassable = true` in its
  own constructor, since it is only ever spawned by code and never authored
  in level data) rather than special-casing it in `CollisionSystem`, and
  means any future static category never needs another `is not` entry added
  to that filter - it opts out via data, not by the filter knowing about it.
- **Consistency rule for bool property vs. marker interface, generalized from
  existing code (`IHazardBody`/`ICollectableBody`/`ICollectorBody` as bare
  markers vs. `IsKillable`/`EffectPersists`/`GravityAffected`/`Restitution` as
  plain properties on concrete classes):** use a marker interface when a
  capability is restricted to certain *types* and the restriction itself is
  meaningful (only some classes should ever be eligible to opt in at all -
  e.g. only the player should ever be able to stomp-kill via `IKillerBody`,
  only some bodies are hazards at all via `IHazardBody`); use a plain bool
  property directly on `Body2D` when a capability could apply to *any* body
  and only the per-instance value differs, with nothing gated by type
  (`IsPassable`/`IsClimbable`/`IsHangable` fall here, same as `IsStatic`
  itself). `IKillableBody` remains the one interface that also carries state
  (`IsKillable`, `EffectPersists`) because the interface's presence is itself
  meaningful there too - not every body should be eligible to be killed at
  all (e.g. the player), so it isn't a case of "any body, per-instance value
  differs."

## Jump stance is a visual-only pose swap, not a real `Stance`

- **`Player_settings.ini` gained a `Jump = jump_idle, jump_left, jump_right`
  stance entry**, backed by three new `Player_jump_*_characters.txt` art
  files (plus matching `_foregroundcolors.txt`, generated to mirror the
  existing walk/crawl coloring: `F` head/limbs, `R` torso, `B` legs).
- **`PhysicsSystem.Step` resolves the pose's stance separately from
  `Player.Stance`.** `Player.Stance` continues to only ever hold `"Walk"` or
  `"Crawl"` — the actual toggle the player controls via the crawl key — and
  is unaffected by being airborne. When resolving which clip to show each
  frame, `PhysicsSystem` swaps in the `"Jump"` stance in place of whatever
  `Player.Stance` currently is whenever `!player.IsGrounded`, so crawling off
  a ledge assumes the jump pose mid-air just like walking off one does. This
  keeps "what stance can the player be in" (a persistent, input-driven
  state) distinct from "what pose looks right to render this instant" (a
  transient, physics-derived one), matching the `Stance`/`Pose` split
  established earlier — see `SetPose`'s naming, which was reverted from a
  bad interim rename to `SetStance` for the same reason.

## Per-clip `[Animation]` overrides, `Stand` renamed to `Walk`

- **Animation timing (`FrameDurationSeconds`, `Mode`, `DefaultFrame`) moved from
  asset-wide to per-clip.** Previously all three lived on `SpriteAsset` and
  applied identically to every clip of an asset; this broke down once a single
  asset had clips with very different frame counts/pacing (e.g. `Player`'s
  3-frame idle head-turn vs. its 2-frame walk cycle) — a `DefaultFrame` tuned
  for one clip could start another clip already pinned at its last frame,
  wasting its first bounce tick and making walking left/right look
  unanimated. `SpriteClip` now owns these three properties directly, resolved
  once at load time in `SpriteLoader`, and `Body2D.AdvanceAnimation`/`SetPose`
  read them from `Clip` instead of `Sprite`.
- **Resolution uses an optional dot-qualified `[Animation.{clipName}]`
  section** falling back to the asset-wide `[Animation]` section per-key. This
  keeps the common case (every clip shares one asset's timing) a single
  section, while letting any one clip (e.g. `walk_left`) override just the
  keys it needs (e.g. a much shorter `FrameDurationSeconds` for a fast walk
  loop) without repeating the other keys.
- **`Player`'s "Stand" stance/vocabulary renamed to "Walk"** throughout code,
  docs, and assets (`Player2D.Stance` default, `PhysicsSystem`'s stance
  checks, `Player_settings.ini`'s `[Stances]` section, and
  `AssetFormat.md` §2.6's example). The player's non-crawling stance is not
  literally "standing still" (it's the stance used while walking, running,
  jumping, and idling alike), so "Walk" was judged the clearer, more
  consistent name — matching the also-renamed `walk_idle`/`walk_left`/
  `walk_right` clip names (previously just `idle`). `Player_idle_*.txt` was
  renamed to `Player_walk_idle_*.txt` to match, and both levels'
  `[Player] Clip = idle` placements updated to `Clip = walk_idle`.

## `Kind` made mandatory, `Static` key removed, `Player` via `Kind`, `Kind` values match class names

- **`Kind` is now a required key on every `_objects.ini` placement section;
  there is no default category anymore.** The prior scheme (see Milestone 3b
  below) let `Kind` be omitted and fall back to a `Static`/`GravityAffected`
  boolean-based guess (`Static` unless `Static = false`). This silently
  produced the wrong concrete class whenever a section forgot to set `Kind`
  — caught in practice when a `ToxicPlant` placement in `TestMovement`
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
  (`Level1`, `TestMovement`) were updated accordingly.
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
  an actual bug while testing `TestMovement`. `ICollectorBody` is deliberately
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
  the ball in `TestMovement`). Rejected: leaving the camera hardwired to
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

## Climbing/hanging is a generic "snap" mechanism via small capability interfaces

- **Superseding the previous "plain state on `Player2D`" approach** after
  real gameplay testing surfaced further design flaws beyond the original
  marker-interface concern: the player couldn't step off a ladder sideways
  (only jumping off worked), and hanging engaged the instant the player's
  feet merely brushed a pipe's underside while landing, rather than only
  when actually reaching up into it. Revisiting both at once produced a more
  generic mechanism, described here, that also directly answers "why doesn't
  `IsGrounded` have a touching/engaged pair like climbing/hanging do" (see
  the last bullet).
- **Two small, focused capability interfaces - `IClimberBody` and
  `IHangerBody` (both extending `IPhysicsBody`)** - not one combined
  interface and not plain `Player2D`-only properties. The former "plain
  properties" decision assumed only `Player2D` would ever need this and a
  shared interface bought no polymorphism; that's revisited now because the
  user confirmed enemies (and later, swimming) are a real future direction,
  matching the existing convention of small single-purpose capability
  interfaces (`IGravityAffected`, `ICollectorBody`, `IKillerBody`) rather than
  reintroducing the previously-rejected single `IClimberBody`-does-everything
  marker. Splitting climbing and hanging into two interfaces (rather than
  one covering both) lets a body implement just one - a fish that swims and
  climbs out onto ladders but never hangs from a rope, for instance.
- **Each mechanic still keeps its touching/engaged pair** -
  `IsTouchingClimbable`/`IsClimbing` and `IsTouchingHangable`/`IsHanging` -
  recomputed fresh every frame by `CollisionSystem` (touching) and
  engaged/disengaged by `PhysicsSystem` (climbing/hanging), unchanged from
  the previous design and still the fix for the "walking through a passable
  ladder froze movement" bug (overlap alone must never lock movement, only a
  deliberate press while touching does).
- **A single generic snap-speed gate (`CollisionSystem.MaxSnapSpeed`),
  applied to both mechanics, replaces bespoke "is this fast enough to be a
  problem" reasoning per case.** Before setting `IsTouchingClimbable` or
  `IsTouchingHangable`, the body's overall speed is checked against one
  shared threshold - a body moving too fast (jumping past a ladder at speed,
  or genuinely freefalling through a thin pipe in one frame) does not snap
  on; a body moving at ordinary walk/fall/jump speeds does. This directly
  answers the user's "some sort of velocity threshold" request in the most
  reusable way: one gate, one constant, shared by every snap-capable
  mechanic, rather than a separate ad hoc speed check per surface type.
- **"From underneath" for hanging is still a direct top-edge comparison
  (renamed `WouldSnapFromBelow`, was `IsApproachingFromBelow`)** - unchanged
  reasoning from the previous decision (a penetration-depth/axis heuristic,
  as used for kill contacts, breaks down once a fast or passable body already
  deeply engulfs a thin hangable object) - now additionally backed by the
  snap-speed gate above, so a body plunging through a pipe too fast to
  plausibly grab on doesn't hang even if it is momentarily geometrically
  "underneath" for one frame.
- **Ladders can be grabbed from any side, including mid-jump** -
  `IsTouchingClimbable` has no directional restriction (unlike hanging),
  matching the user's request that a ladder be grabbable while airborne, not
  just while walking into it at ground level. Climbing also now yields to
  solid ground: if the player is simultaneously grounded on real (non-ladder)
  terrain, `IsClimbing` is forced off, so landing on a floor at a ladder's
  base always wins over continuing to "climb" in place.
- **Ladders can now be exited sideways, not just by jumping off** - while
  climbing, horizontal input still applies (at a new, slower `ClimbSideSpeed`
  rather than the ordinary walk speed, since stepping off a rung is more
  deliberate than free walking), letting the player step onto an adjacent
  floor or another ladder. This directly fixes the reported "can't leave the
  ladder other than by jumping" bug.
- **Crawling cannot climb directly - the player must explicitly stand up
  first, as a separate deliberate step, before a later up/down press can
  grab a ladder.** Engaging `IsClimbing` requires `Stance == "Walk"` already;
  there is no combined "stand up and grab on" shortcut collapsing that into
  one input, even though `Player2D.Stance` itself has no notion of a
  "Climb"/"Hang" stance being incompatible with crawling by construction (a
  plain string) - the two-step requirement is a deliberate gameplay/feel
  choice enforced by `PhysicsSystem`, the same place all other stance
  transitions (Walk<->Crawl) already live, not a technical necessity.
- **Hanging gained a second pose, `IsHangingCrawled`, on `IHangerBody`** -
  the regular fully-stretched hang (hands only) versus a compact
  hang-and-shimmy pose (hands and feet both on the rope, vertically shorter
  to fit through narrow spaces), directly matching the user's requested two
  hanging flavors. Which pose is used is decided once, at the moment of
  grabbing on (compact if the player was already crawling, regular
  otherwise, mirroring whichever silhouette the player already had a moment
  before), and can be toggled afterward via the same crawl key used for the
  ordinary ground Walk/Crawl toggle - reusing one input's meaning
  ("go compact") across both grounded and hanging contexts instead of
  inventing a separate hang-specific key.
- **Three new dedicated stances - `Climb`, `Hang`, `HangCrawl` - replace the
  previous "no dedicated art yet, keep showing Walk/Crawl" placeholder.**
  Each has its own `Idle`/`Left`/`Right` clip set, mirroring `Walk`/`Crawl`.
  `Climb`'s idle is a 2-frame `Loop` clip alternating arm-over-arm position;
  `Hang`/`HangCrawl`'s idle clips are 3-frame `PingPong`, matching the
  `Walk`/`Crawl` idle head-turn convention (only the face moves); their
  `Left`/`Right` clips are 2-frame `Loop`, matching the `Walk`/`Crawl`
  left/right convention. Old `ConsoleGame2D`
  was checked for reusable art/an approach to marking specific sprite parts
  (e.g. hands) as hangable, but confirmed to have never actually implemented
  climb/hang sprites at all (`MovementState.Hanging`/`Climbing` existed as
  enum values and contact-driven transitions only) - the new stances'
  character art is original to this implementation, following the same
  glyph vocabulary as the existing Walk/Crawl clips, not reused old art.
- **Why `IsGrounded` doesn't get its own touching/engaged pair, and that's
  fine**: climbing/hanging need the split because there is a genuine
  *choice* to make while touching (press up/down to climb; anything else and
  you just walk past) - the touching flag exists specifically to gate that
  choice one frame later without blocking movement in the meantime. Standing
  on solid ground has no equivalent alternate action for a body to opt out
  of - a body resting on a platform's top surface has no "touching but not
  grounded" state to represent (unlike passable ladders, ordinary solid
  terrain always blocks/supports on contact), so `IsGrounded` staying a
  single flag, computed by physical collision push-out rather than a
  velocity-gated snap check, is a deliberate asymmetry rather than an
  oversight. The one conceptual parallel worth calling out: a very fast body
  moving through a platform in a single frame (tunneling) is a separate,
  pre-existing physics-fidelity concern for `ResolveAgainstSolid`, unrelated
  to this snap mechanism and out of scope for this change.
- **Optional per-stance clip subfolders (`[ClipFolders]`) are declared in the
  asset's own `settings.ini`, not per-level `_objects.ini`.** `Player`'s
  sprite folder had grown busy enough (six stances' worth of clips, each with
  several facing/layer files) to be worth organizing into subfolders. Where
  that mapping lives had two options: alongside the asset (`settings.ini`) or
  alongside each placement (`_objects.ini`). `settings.ini` won because the
  subfolder layout is a property of *how this one asset's files happen to be
  organized on disk*, identical for every level that ever places a `Player`
  object - putting it in `_objects.ini` would mean repeating (and keeping in
  sync) the same mapping in every level file that uses the asset, for a
  concern that has nothing to do with a specific placement. A simple asset
  like `Pipe` just omits `[ClipFolders]` entirely and keeps its flat
  single-folder layout, with zero extra ceremony - the loader falls back to
  reading straight from the asset's root folder for any clip with no
  matching entry, so adopting subfolders is fully opt-in per asset (and even
  per clip within an asset, via prefix matching) rather than a structural
  change forced onto every sprite.
- **Climbing gained a second, visual-only pose split - `Climb` (idle) vs.
  `ClimbMoving` (actually climbing) - the same way `Jump` already is a
  visual-only variant layered on top of `Walk`/`Crawl`, not a new engagement
  state.** The original single `climb_idle` clip animated its arm-over-arm
  loop even while the player held still on a ladder (not pressing up/down),
  which reads wrong - the old clip is kept, renamed to `climb_moving`, and
  used only while up/down is actually held; a new `climb_idle` clip (3-frame
  head-sway `PingPong`, matching the `walk_idle`/`hang_idle` idle-head-turn
  convention) is shown the rest of the time while still `IsClimbing`.
  `PhysicsSystem` computes this purely as a local `isActivelyClimbing`
  bool (`IsClimbing && (IsUpPressed || IsDownPressed)`) feeding pose
  selection alongside the existing `Jump`/`Hang`/`HangCrawl` pose logic -
  `IsClimbing` itself, and every engage/disengage/gravity-suspension rule
  gated on it, is entirely unchanged; nothing about *whether* the player is
  climbing depends on whether up/down happens to be held on a given frame,
  only *which clip* is shown. This was considered and rejected as "similar to
  `walk_left`/`walk_right`, just up/down" (i.e. as two more facings on the
  same `Climb` stance) - facings are a lateral (`Left`/`Right`)/no-direction
  (`Idle`) concept tied to horizontal movement direction throughout the
  rest of the format (see §2.6), and climbing's up-vs-down movement has no
  visible difference worth separate art anyway; modeling this instead as a
  second whole stance keeps `Facing` meaning one consistent thing everywhere
  and reuses the pre-existing "extra automatic pose over a stance" pattern
  `Jump` already established, rather than overloading `Facing` with a new,
  inconsistent meaning just for this one stance.

