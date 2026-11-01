# Program Structure

This document describes how AsciiHero is put together: its components, their
responsibilities, and the key flows that tie them together at runtime. It is a
companion to [Architecture.md](Architecture.md) (architectural rules and
rationale) and [AssetFormat.md](AssetFormat.md) (file format reference) - this
document instead focuses on "what exists and how it fits together."

## Solution Layout

The solution has three projects:

- **ASCII Hero** - the Blazor Web App host. Serves the page shell, handles
  server-side prerendering, and hosts the WebAssembly runtime. Contains almost
  no game logic itself.
- **ASCII Hero.Client** - the Blazor WebAssembly client project. Contains all
  game code, static assets (sprites, worlds, fonts), and the small amount of
  JavaScript interop the game needs. The entire game runs client-side inside
  this project once the page loads.
- **ASCII Hero.Tests** - MSTest unit tests for browser-independent game code
  in `ASCII Hero.Client`. Not part of the shipped game; test folders mirror
  the `Game/` subsystem folders.

All gameplay code lives under `ASCII Hero.Client/Game/`, organized into one
folder per subsystem, described below in roughly the order data flows through
them at startup and each frame.

### Deployment

The `ASCII Hero` host project is used for local development only (F5
debugging, server-side prerendering, the Edge app-mode launch helper in its
`Program.cs`). For public distribution, `ASCII Hero.Client` is published
standalone as a pure static Blazor WebAssembly site - no server, no
prerendering - using its own `wwwroot/index.html` (separate from the host's
`App.razor`, with a `base href` matching the GitHub Pages sub-path) and a
`RootComponents` registration added to the client's `Program.cs`. A GitHub
Actions workflow (`.github/workflows/deploy-gh-pages.yml`) publishes this
standalone build and deploys it to GitHub Pages on every push to `master`.

## Subsystems

### Assets

Everything to do with reading the plain-text asset format (sprites, worlds,
palettes, materials) from disk/HTTP into in-memory game objects.

- **`IAssetFileProvider`** - fetches raw file text by relative path, returning
  null for a missing file (404); any other failed request throws an error
  naming the file, so a failed download never silently falls back to a global
  default. Implemented by `HttpAssetFileProvider`, which
  reads static files served from `wwwroot/Assets` over HTTP, since Blazor
  WebAssembly has no direct filesystem access. Kept behind this interface so
  no other asset-loading code depends directly on `HttpClient`.
- **`IniDocument`** - a minimal hand-rolled parser for the ".ini"-style files
  used throughout the asset format (`[Section]` headers, `Key = Value` lines,
  `;` comments, quoted values). Not a general-purpose INI parser, only what
  the asset format actually needs.
- **`AssetTextReader`** - the low-level parser for grid-shaped layer files
  (`_characters.txt`, `_foregroundcolors.txt`, etc.): splits multi-frame
  content on `//end` separators, infers frame dimensions from content, and
  pads missing/short rows and columns with the asset's empty-char marker.
  Knows nothing about folders or the Global/World fallback rule.
- **`AssetPathResolver`** - resolves which folder (`Worlds/{WorldName}/Sprites/{Asset}`
  or `Global/Sprites/{Asset}`) a sprite's files should be read from, applying
  the "world-local folder overrides global" rule: presence of a world-local
  settings file is itself the override signal, no explicit flag needed. An
  asset name may include a group folder (`Group/Variant`); its files are then
  prefixed with the last segment only (`GetFileStem`). This
  resolution is per asset name, so a world overriding one sprite (e.g. an
  ice world's own winter-clothed `Player` reskin) still falls back to
  `Global/Sprites/` for every other sprite it uses.
- **`ColorPalette`** - the resolved single-character color code -> CSS color
  lookup, merging a world's own optional `ColorPalette.ini` over `Global/ColorPalette.ini`.
- **`InterfaceColors`** - the interface colors from `Global/InterfaceColors.ini`:
  `ColorTints` (Dark/Normal/Light) for Green/Amber/Red/Blue/White, the active
  `Interface` tints chosen by `[Defaults] Foreground`, and `Background`.
  Loaded once in `GameLoop.StartAsync` (which also sets
  `GlyphBuilder.DefaultForeColor`) and passed to the HUD/overlay widgets,
  `WorldSelectRenderer` and `WorldLoadingRenderer`; `GameLoop` picks the
  end-screen tints itself. Separate from `ColorPalette`, which is for
  world/sprite assets only. Missing values fall back to green on black
  (`InterfaceColors.Fallback`).
- **`MaterialLibrary`** - the resolved material-name -> `Material` (`Density`,
  `Friction`, `Restitution`, optional `ForegroundColor`/`BackgroundColor`/
  `Character`) lookup, merging a world's own optional `MaterialLibrary.ini` over
  `Global/MaterialLibrary.ini`. A body with no material at all (a sprite with
  no `DefaultMaterial`) resolves to a zeroed `Material.Undefined` fallback; a
  material name that is set but not defined is rejected by `World2D.LoadAsync`
  as a load error (checked via `MaterialLibrary.Contains`).
- **`IniOverrideLoader`** - the shared Global-then-World ini load/merge
  routine used by both `ColorPalette` and `MaterialLibrary` (and any future
  asset needing the same fallback rule): reads `Global/{file}`, then merges an
  optional `Worlds/{WorldName}/{file}` over it via a caller-supplied
  projection, so both classes differ only in how they turn a parsed
  `IniDocument` into their own dictionary entries.
- **`IniValueParser`** - shared parsing helpers (`ParseEmptyChar`,
  `ParseColorCode`, `ParseDouble`/`TryParseDouble`, `TryParseInt`) for the
  small ini value shapes repeated across `SpriteLoader`, `WorldCatalog`, and
  `World2D`'s settings.ini/objects.ini parsing, keeping a single
  culture-invariant, consistently-named implementation of each.
- **`SpriteLoader`** - loads one sprite asset's settings and requested clips
  into a `SpriteAsset`, combining `AssetPathResolver` (folder resolution) and
  `AssetTextReader` (grid parsing) into the one loading path reused by every
  sprite-backed object (player, platforms, hazards, collectables alike). An
  asset's optional `[ClipFolders]` section (see docs/AssetFormat.md §2.7)
  lets a busy multi-stance asset (e.g. `Player`) group its clips' files into
  per-stance subfolders instead of one flat folder - purely a file-layout
  convenience, invisible to every other caller/consumer of a loaded clip.
- **`SpriteAsset` / `SpriteClip` / `SpriteFrame`** - the in-memory result of
  loading, and the game's one representation of "what a game object looks
  like": a `SpriteAsset` is one named asset (e.g. `Player`, `Ball`) holding
  every clip it defines (e.g. `walk_idle`, `walk_left`) plus asset-wide
  metadata (its own empty-char, default colors, optional
  stance/facing map). A `SpriteClip` is one named clip, holding one or more
  `SpriteFrame`s in sequence plus resolved animation timing (duration, loop
  mode, default start frame) and its tile axis (from `[Layout.{clip}]`,
  falling back to `[Layout]`). A `SpriteFrame` is the actual drawable/
  collidable unit - parallel `Chars`/`Fore`/`Back`/`Materials` grids, one
  entry per cell - consumed directly by `WorldRenderer` (for its glyphs) and
  `CollisionSystem`/`Body2D` (for its silhouette/material lookup). A `Body2D`
  holds a reference to its `SpriteAsset` plus its currently-active clip/frame
  index rather than copying frame data, so many placements of the same asset
  (e.g. many identical platform tiles) share the one loaded `SpriteAsset`.
- **`SpriteFrameTiler`** - repeats a tileable frame's authored unit along its
  declared axis (horizontal or vertical) to fill a placement's `Span` (cells)
  exactly, cutting the last unit short when needed, at spawn time.
- **`SyntheticSpriteFactory`** - builds a one-clip, one-frame `SpriteAsset` in
  memory from a material name/glyph/width/height, for "materials-only" world
  objects (see docs/AssetFormat.md §3.4) that name a `Material`+`Width`+
  `Height` instead of an `Asset`+`Clip`. Lets `World2D.LoadAsync` feed such a
  placement into the exact same `Body2D`/`CollisionShapeBuilder`/
  `WorldRenderer` path as any sprite-backed object, with no sprite files
  authored on disk.
- **`WorldSummary` / `WorldCatalog`** - lightweight, non-gameplay metadata for
  the world-selection screen: a world's `Title` and its fixed 16x8, optionally
  animated thumbnail art (see docs/AssetFormat.md §3.1/§3.2), loaded without
  loading the rest of that world's playable `World2D`. `WorldCatalog` lists
  every playable world from `Global/Worlds.ini` (§4.6) - Blazor WebAssembly
  has no way to list `wwwroot`'s directory contents at runtime, so (like
  `[Stances]`) this is an authored list rather than one inferred from the
  filesystem.

### World

The live game state and the entity types that make it up.

- **`World2D`** (runtime state in `World2D.cs`; `LoadAsync`, placement
  spawning and switch-link resolution in the partial `World2D.Loading.cs`)
  - holds everything that makes up the current game state: the
  player, every other body in one generic `Objects` list, the background
  layer, the resolved color palette, the resolved material library
  (`Materials`), gravity, world dimensions, and which body the camera should
  follow. `World2D.LoadAsync` is the world loader: it reads a world's
  settings, background, and object-placement files, resolves each
  placement's sprite and concrete body type, resolves each spawned body's
  `Density`/`Friction`/`Restitution` from its material (a placement's ini
  section may override the resolved material name via `Material`, or just
  the resulting `Restitution` via `Restitution`), computes `Mass` from
  the resolved `Density` and the body's spawn footprint (fixed afterwards,
  independent of pose changes), and assembles the
  finished world. Its object stage is split into helpers: `CollectClipNamesByAsset`
  (pre-scan of the union of clips per sprite asset), `ResolveSprite` (asset or
  materials-only sprite), `SpawnPlacement`/`SpawnPlayer` (body per `Kind`),
  `ApplyMaterial`, `ReadConveyorVelocity` and `ReadPatrolRoute` with
  `ConfigureDynamicPatrol`/`ConfigureKinematicPatrol`. Every key is read through the
  private `ObjectSection` reader, which applies the shared default/error rules.
  Also owns deferred removal (`QueueRemoval`/`ApplyPendingRemovals`),
  so nothing mutates the `Objects` list mid-iteration.
- **`Body2D`** (per-frame contact sets and step-displacement/friction-undo
  tracking are grouped at the end of `Body2D.cs`) - the base class for anything living in the world at a
  floating-point position, backed by a loaded sprite frame. Derives its size
  and collision shape once from the active frame's actual (non-empty) glyph
  data, handles animation frame advancement and stance/facing pose switching,
  and exposes the per-instance `IsPassable`/`IsClimbable`/`IsHangable` flags
  that let any static placement opt into those behaviors without a dedicated
  subclass, plus `ConveyorVelocity` (a moving surface on a body that stays
  put, e.g. conveyor/rope) and `GetCarryVelocity()` (own `Velocity` +
  `ConveyorVelocity`, the speed something held onto or resting on the body is carried at;
  also used by `GetSurfaceVelocityX` and as a solid's collision velocity).
- **Concrete body types**, all deriving from `Body2D`:
  - `Player2D` - the player-controlled character; implements the mover,
	gravity, collector, and killer capabilities (below), plus the climbing
	(`IClimberBody`) and hanging (`IHangerBody`) capabilities (see Physics
	below), plus `IPosedBody` (its `UpdatePose()` resolves stance+facing from
	its own state, called explicitly by `PhysicsSystem.Step` rather than
	through the generic per-frame dispatch below) and `IWalkForceBody` (its
	horizontal "motor" force, computed each frame by `PhysicsSystem.
	UpdateWalkForce` - see Physics below). Nothing here is player-specific -
	all of these are ordinary capability interfaces any future body (e.g. a
	climbing/hanging hazard) could implement the same way.
  - `StaticObject2D` - solid, static terrain (platforms, walls, decoration).
  - `TwoStateObject2D` - a `StaticObject2D` with an off/on state that swaps between
    two clips of its sprite (`Clip` / `OnClip`) via `SetState`; subclasses react in
    `OnStateChanged`. Four subclasses:
    `Gate2D` (door/hatch: solid while closed, passable while open; opened by a key
    `Lock` via `World2D.TryOpenGate`, or driven by a switch through `Link`),
    `Switch2D` (button/lever: toggles, or only turns on if `Latching`, and pushes the
    state to all its `Targets`; operated via `World2D.TryOperateSwitch`),
    `Device2D` (a machine-like object - fan, lamp - that only swaps its off/on clip) and
    `FlowZone2D` (a passable medium volume - water, air - whose medium flows in a
    `FlowDirection` at a `FlowSpeed` while on).
  - `ILinkTarget` - capability of an object a switch can drive (`LinkName`,
    `SetState`), implemented by `Gate2D`, `Device2D`, `FlowZone2D` and `KinematicObject2D` (which stops while
    off). `World2D.ResolveLinks` enforces one-switch-to-many-targets `Link` matching at load time.
  - `Key2D` - a `Collectable2D` that carries the key's `LockName`.
  - `StaticHazard2D` - a non-moving hazard (e.g. spikes) that can optionally
	be "killable" and can trigger a cosmetic effect on contact.
  - `KinematicHazard2D` - a `KinematicObject2D` that is also a hazard (e.g.
    a saw blade or laser); reuses all kinematic motion/patrol behavior and adds
    the same optional killable/effect capabilities as `StaticHazard2D`.
  - `DynamicObject2D` - a non-player object driven by velocity and,
	optionally, gravity, bouncing off world bounds/platforms according to its
	restitution (e.g. the bouncing ball).
  - `KinematicObject2D` - a static-for-collision-response (`IsStatic`) body
	(e.g. a moving platform) that still implements `IPhysicsBody` with a
	real, self-driven `Velocity` - a "kinematic body" in the usual
	physics-engine sense: it drives its own prescribed motion every frame via
	`Move(deltaSeconds)` (never gravity/force integration), and other bodies
	collide against/are carried by it, but it is never itself pushed,
	bounced, or halted by anything it touches (see `CollisionSystem` and
	`Body2D.IsStatic`). Supports independent optional per-axis back-and-forth
	patrol (`PatrolMinX`/`PatrolMaxX`/`PatrolSpeedX`,
	`PatrolMinY`/`PatrolMaxY`/`PatrolSpeedY` - either, both, or neither axis
	may be configured) at a prescribed velocity that eases to rest at each
	route end (`PhysicsConstants.KinematicPlatformAcceleration`) - a distinct scheme
	from `IPatrolBody`'s force-based, gravity-integrated patrol used by
	`DynamicHazard2D`.
  - `DynamicHazard2D` - an AI-controlled hazard that moves and collides exactly
	like a `DynamicObject2D`; optionally patrols back and forth independently
	on the X and/or Y axis via `IPatrolBody` (see below - a body configured
	with both axes patrols diagonally), and implements `IPosedBody` to face
	(and animate through) its sprite's directional clips (e.g.
	`move_left`/`move_right`) from
	behavior is not yet implemented.
  - `Collectable2D` - a static, non-solid item removed from the world when a
	collector body touches it (e.g. a coin or power-up), regardless of its
	`Type` (`CollectableType`: `Ring`/`Diamond`/`Checkpoint`/`LevelEnd`/`Key`,
	set from the placement's optional `Type` ini key; absent = inert) - see
	`InteractionHandler.OnCollected` below for the actual
	per-type pickup behavior. `Checkpoint`/`LevelEnd` typically set
	`EffectPersists = true` so their pickup effect remains in the removed
	collectable's place as a permanent "already used" marker, the same way a
	killed hazard's effect can persist as a husk.
  - `EffectInstance2D` - a purely cosmetic, non-collidable body that plays a
	short visual effect clip (a pickup fade, a kill "crumble") and then
	either self-removes or persists as a permanent decorative husk.
  - **Capability interfaces** - behavior is selected by interface, never by concrete type; a
    system that needs to act on "any body that can X" checks one of these
  instead. Some are bare markers (no state, just identity); others carry
  real per-instance state:
  - `IPhysicsBody` - participates in physics integration and collision
	(position, velocity, size, grounded state, collision shape).
  - `IGravityAffected` - can optionally opt out of gravity via a
	`GravityAffected` flag.
  - `IMediumAffected` - can optionally opt out of ambient-medium buoyancy/
	drag via a `MediumAffected` flag, mirroring `IGravityAffected`. Every
	`Body2D` still has its `CurrentMedium` resolved and exposed each frame
	regardless (see `PhysicsSystem` below) - only the force application
	itself is gated on this capability.
  - `IPatrolBody` - patrols back and forth independently on the X and/or Y
	axis between `PatrolMinX`/`PatrolMaxX` and/or `PatrolMinY`/`PatrolMaxY`
	under its own mass-scaled force, recomputed each frame via
	`UpdatePatrolDirection(gravity)` and summed into `PhysicsSystem`'s force
	accumulator alongside gravity (see `DynamicHazard2D`'s `Patrol` placement key,
	docs/AssetFormat.md §3.4). A body configured with both axes patrols
	diagonally - each axis bounces between its own bounds on its own schedule.
	Patrol turns around once a bound is reached or passed, so a bumped body
	heads back. Vertical patrol has no gravity special case: gravity is an
	ordinary force in the net sum, and `PatrolMaxForceX` (default unlimited) /
	`PatrolMaxForceY` (default 0; the "wing power")
	(in multiples of g) cap the patrol force per axis -
	climbing saturates the Y cap, descending needs little. Gains are per axis
	(`PatrolForceMultiplierX`/`Y`). An axis with no range holds its spawn
	position (column for X, height for Y) via a clamped spring target speed;
	caps only limit force and never toggle an axis. With `PatrolStayOnPlatform`
	the X range is refreshed each frame from the static/kinematic solid in the
	body's `SurfaceBottom` contacts (last range kept while airborne).
  - `IPosedBody` - resolves and applies its own sprite pose (stance +
	facing) once per frame via `UpdatePose()`, called by `PhysicsSystem` after
	velocity/position are integrated for the frame (mirroring how a body owns
	`IPatrolBody.UpdatePatrolDirection()` while `PhysicsSystem` only calls it
	at the right moment). `Body2D.ResolveHorizontalFacing(velocityX)` is the
	one shared rule every horizontally-facing implementer (`Player2D`,
	`DynamicHazard2D`) derives `Facing.Left`/`Right`/`Idle` from.
  - `IHazardBody` - damages a body on contact (marker only; the actual damage
	is applied by `InteractionHandler.OnHazardHit`, which
	deducts the hazard's `Body2D.Damage` (default 1 = a full bar) from
	`Player2D.Health` on the first frame of a fresh, non-fatal
	contact - clamped at 0; when the bar empties it removes one `Player2D.Lives`
	and sets `World2D.PendingTransition` to `Death`).
  - `ICollectableBody` - removed from the world on contact with a collector
	(marker only); see `Collectable2D`'s own entry above for how its
	`EffectPersists` can leave its pickup effect behind as a permanent marker
	(used by `Checkpoint`/`LevelEnd` in practice).
  - `ICollectorBody` - can pick up collectables on contact (e.g. the player).
  - `IKillerBody` - can "kill" a killable hazard by landing on top of it
	(e.g. the player); kept independent of `ICollectorBody` since the two
	capabilities are conceptually unrelated even though the player has both.
  - `IKillableBody` - can be killed by a qualifying contact; carries real
	state (`IsKillable`, `EffectPersists`, `KillImpactSpeed`) since not every instance of an
	otherwise-killable type should be killable in every placement.
  - `IEffectTrigger` - can optionally name a cosmetic effect clip
	(`EffectClipName`) to play on contact, reusing a clip already defined on
	that same body's own sprite asset. `CollisionSystem`'s private
	`SpawnEffect` helper can also spawn an explicit, call-site-chosen clip
	independent of `EffectClipName` - used so the player can have more than
	one distinct effect for different situations (e.g. `Player2D`'s
	"hurt" on a hazard hit and its "checkpointreached" clip;
	`Player2D` is deliberately not an `IEffectTrigger`) as
	independent, possibly-overlapping `EffectInstance2D`s, rather than being
	limited to one fixed clip per body.
  - `IClimberBody` - can climb an `IsClimbable` surface (e.g. a ladder);
	carries `IsTouchingClimbable` (mere overlap, recomputed every frame) and
	`IsClimbing` (actually engaged, driven by input). Independent of
	`IHangerBody` - a body can implement either, both, or neither.
  - `IHangerBody` - can hang and shimmy laterally from an `IsHangable`
	surface (e.g. a pipe/rope); carries `IsTouchingHangable`, `IsHanging`,
	`IsClambering` (derived from the body's stance, not stored: hanging while in
	the compact stance - regular fully-stretched hang vs. a compact clamber/
	shimmy pose that fits through narrower spaces), and
	`SuppressHangUntilClear` (debounce set by `PhysicsSystem` the instant the
	player jumps/swings off or lets go, consulted by `CollisionSystem` so it
	doesn't immediately re-snap the body while still overlapping the same
	surface).
  - `ISwimmerBody` - can swim (directional thrust, left/right/up/down) while
	`Body2D.CurrentMedium` is dense/viscous enough to swim in (see
	`PhysicsSystem.IsSwimmableMedium`); carries only `IsSwimming` (actually
	engaged, driven by a deliberate directional press while submerged) - no
	separate "touching" flag like `IClimberBody`/`IHangerBody`, since
	`CurrentMedium` is already resolved once per physics step for every moving body
	regardless, so it alone is the medium-based detection signal swim needs (the
	player movement logic at the start of `PhysicsSystem.Step` reads the value
	resolved during the previous step).
- **`Vector2D`** - the 2D vector struct used for all world-space
  positions and velocities. World coordinates are continuous cells, not
  pixels or integer grid indices - see Coordinate System below.

### Physics

- **`PhysicsSystem`** - resolves player jump/stance/pose input, applies
  gravity to any `IGravityAffected` body, computes the player's own
  horizontal walk/crawl "motor" force (`UpdateWalkForce`, via
  `IWalkForceBody`), and integrates position from velocity for every
  `IPhysicsBody` each frame, all via one shared per-frame mass-scaled force
  accumulator (`StepMovingBodyWithForces` - gravity as
  `mass * world.Gravity`, plus, for any `IPatrolBody`, its own patrol force,
  and for any `IWalkForceBody`, its own walk force, converted to acceleration
  via `a = F / mass`, then integrated into velocity), which is numerically
  identical to a direct gravity-velocity add for a gravity-only body but is
  the extension point for any further force source. Each frame it also
  resolves every body's `CurrentMedium` (`ResolveCurrentMedium` - `Air` by
  default, overridden to the highest-density overlapping `Medium = true`
  volume's material, e.g. a `Water` body, or an equal-density volume such as an
  `Air` wind tunnel) and, for any `IMediumAffected`
  body, adds a buoyancy force (Archimedes' principle - the medium's density
  times the body's own resolved volume, opposing gravity) and a quadratic
  (velocity-squared) drag force (the medium's `Viscosity` times the body's
  velocity relative to the medium's flow (a `FlowZone2D`'s current flow, else zero)
  times its own magnitude, per axis, further scaled by the body's own frontal area
  facing that axis - `Size.Y` for horizontal drag, `Size.X` for vertical drag,
  the 2D analog of cross-sectional area - so a bigger body feels
  proportionally more drag than a smaller one of the same material) into the
  same accumulator. The player's
  sustained locomotion - walk/crawl
  (including its vertical motion), and hanging/shimmying from a pipe/rope
  (including holding position against gravity, which stays on - the grip
  carries the weight) - is one such force
  source, all driven the same way: `Player2D` implements `IWalkForceBody`,
  and each frame `PhysicsSystem.UpdateWalkForce` recomputes its `WalkForce`
  as a proportional "motor" force (both axes) converging `Velocity` toward
  whichever walk/crawl/climb/hang target velocity the current pose/input
  calls for - mirroring `IPatrolBody.PatrolForce`'s role for a patrolling
  hazard, just proportional to the remaining speed gap rather than a fixed
  direction, so the player still reaches (and then holds) the target speed
  promptly without overshooting or oscillating. Neither `WalkForce` nor
  `PatrolForce` is scaled by the current medium; a viscous medium (e.g.
  `Water`) resists them only through the passive buoyancy/drag terms above.
  Jump-off
  impulses (a standing jump, or letting go of a ladder/pipe with an upward
  launch) are the one exception to the continuous-force model, and
  deliberately so: each is a genuine instantaneous velocity kick applied
  once on the frame it triggers, not a sustained, ongoing motion to converge
  toward, so they remain direct velocity assignments on purpose - discrete
  state-machine transitions, not the continuous locomotion `WalkForce`
  drives. Also resolves
  the player's stance - the stored, deliberately chosen upright/stretched
  (`Walk`) vs. compact (`Crawl`, shown as `Clamber` while hanging) choice,
  `Player2D.Stance`, toggled by input and reset to `Walk` on landing after
  being airborne - and its pose, the displayed result (`Player2D.ResolvedPose`,
  derived each frame by `UpdatePose` from the stance plus the situation:
  Climb, Hang/Clamber, Swim, or a visual-only "Jump" pose while airborne).
  It engages climbing/hanging (via
  `IClimberBody`/`IHangerBody`, generically) from the previous frame's
  `IsTouchingClimbable`/`IsTouchingHangable` (set by `CollisionSystem`):
  climbing requires a deliberate up/down key press while touching a climbable
  surface (grabbable from any side, including mid-air) and while already
  standing (`Stance == "Walk"` - a crawling player must explicitly stand up
  first, as a separate step), and yields to landing on solid ground; hanging
  engages automatically as soon as a hangable surface is touched from
  underneath, picking the compact "clamber" pose if the stance was already
  `Crawl` at the moment of grabbing on. Stance changes on the ground and
  while hanging are driven by one shared, structured up/down "stance ladder":
  on the ground, Up always means "more upright" (Crawl -> Walk) and Down
  always means "more compact" (Walk -> Crawl); while hanging, the same keys
  are deliberately inverted to match arm position rather than screen
  direction - Up pulls into the compact `Clamber` pose (mirroring Crawl) and
  Down extends back out to the fully-stretched `Hang` pose (mirroring Walk),
  with a further Down from already-`Hang` meaning an explicit "let go" and
  dropping. `Up` (directional) and `Jump` (action) are always distinct
  inputs, never equivalent anywhere including the ground - see
  `InputState.IsUpKeyDown`/`IsJumpKeyDown` below - so a Jump press instead of
  Up/Down from `Hang` swings/jumps off with an upward-plus-current-lateral-
  velocity impulse (not available from `Clamber`), and a Jump press while
  climbing likewise lets go of the ladder and launches upward. All three
  launch impulses (ground jump, climb jump-off, hang jump-off) use their own
  progressively weaker speed constant, reflecting how much "grip"/momentum
  each stance has behind it. While climbing, vertical movement is locked to a
  fixed climb speed but horizontal input still applies at a slower dedicated
  speed so the player can step off sideways onto a floor or another ladder;
  while hanging, the player is held in place vertically (still laterally
  movable, at its own dedicated - and slower still while `Clambering` -
  speed) until it lets go via the stance ladder above. Neither suspends
  gravity: the grip motor carries the weight, is limited by combined
  friction x weight (`GripTractionMultiplier`), pushes a finite-mass gripped
  body the other way, and measures every target, snap check and jump-off
  relative to the gripped body's `GetCarryVelocity()` (recorded as a
  `ContactType.Climbable`/`Hangable` contact by `CollisionSystem`), so moving
  ladders/pipes and conveyors/ropes carry the player. Both resolve their own dedicated
  poses instead of the "Jump" swap - `Climb` (whose `Facing` is resolved
  along the vertical axis instead of horizontal, since it's a
  vertically-moving stance - see `SpriteAsset.Facing`/`StanceDefinition` - so
  its `Idle`/`Up`/`Down` clips distinguish idle head-sway from actually
  climbing purely from whether up/down is currently held, the same role
  `Left`/`Right` play for every horizontally-moving stance) and
  `Hang`/`Clamber`.
- **`CollisionSystem`** - resolves axis-aligned bounding box collisions:
  moving bodies against solid static terrain, moving bodies against each
  other, moving bodies against the world's own bounds (which act as a
  generic physical surface, bouncing dynamic bodies per their restitution),
  hazard/collectable contact (a solid hazard - any moving hazard, or a
  static one that is not passable - is touched only through a solver
  surface contact; a passable hazard or collectable by overlap; climb/hang
  grip contacts never count; squash = the impact speed recorded for the
  body's bottom contact with the hazard reaching its `KillImpactSpeed`,
  impact speeds being stored per contact side so a side hit never reads as
  a landing; detection
  history only; what each interaction does - kills, damage, lives, score,
  rings, keys, checkpoints, level-end/death transitions, their sounds and
  effects - is applied by **`InteractionHandler`**), and any `IClimberBody`/`IHangerBody`'s
  `IsTouchingClimbable`/`IsTouchingHangable` overlap against
  `IsClimbable`/`IsHangable` terrain. Restitution/friction used in any given
  collision response are the combined values (friction: geometric mean,
  restitution: softness-weighted average using `Hardness`, see
  below `RestingBounceSpeedThreshold`)
  of both contacting bodies' own resolved material properties (see
  `Body2D.Restitution`/`Friction` below), not a single one-sided or
  type-based value. Solid-collision response (`ResolveAgainstSolid`/
  `ResolveAgainstOtherBody`) treats the solid's own velocity (real for a
  kinematic body like `KinematicObject2D`, zero for ordinary stationary
  terrain) as the collision's reference frame - the normal-relative velocity
  response and friction both run on the other body's velocity relative to
  the solid, then the solid's velocity is added back - so a moving platform
  naturally drags a resting rider along via friction (more so for a grippy
  material, less for a slick one) using the exact same formulas as
  stationary terrain. The normal-relative response is a genuine
  impulse-based reflection (`ResolveContact`'s mass-weighted impulse,
  generalized to an infinite-mass second body for solids), and the
  tangential (along-surface) component is damped via real Coulomb friction
  (`ApplyCoulombFriction`) - capped by the magnitude of the normal impulse
  just applied at that same contact (`|f| <= mu * N`, expressed in velocity
  terms since impulses here are resolved per-frame rather than as a
  continuous force). Combined with `PhysicsSystem` integrating that velocity
  into `Position` before `Resolve` runs each frame, this carries a resting
  body - the player included, since its own horizontal velocity is
  force/mass-driven (see `IWalkForceBody` above) rather than overwritten
  from input every frame - along a moving platform on both axes with no
  special-casing: the platform-carry gap (a platform
  displacing farther in one frame than a body's own velocity-matched motion
  keeps up with) is closed simply by the ordinary landing-snap correction
  re-running against the platform's current (already-moved) position every
  frame, so no separate carry or re-seat mechanism is needed on either axis (see
  docs/Decisions.md).
  Moving-body-vs-moving-body resolution
  (`ResolveAgainstMover`, sharing its math with the solid case via the same
  `ResolveAgainstOtherBody`/`ResolveContact`) splits position correction by
  relative mass and resolves the along-normal velocity response via a
  standard 1D mass-weighted impulse, rather than each body independently
  reflecting its own velocity. When a body's own collision shape has several
  overlapping rectangles against the same other body/solid in one iteration
  (e.g. a head rect and a torso rect both touching a platform at once),
  `ResolveAgainstOtherBody` resolves exactly one physical contact that
  iteration, not one per overlapping rectangle: the most-deeply-overlapping
  rectangle's own axis becomes the contact normal, every other overlapping
  rectangle is re-measured along that same normal, and the worst of those
  depths is what actually gets corrected/responded to via one call to
  `ResolveContact`. Climbing and hanging engagement
  share one generic snap-speed gate, measured relative to the surface's
  `GetCarryVelocity()` (a body moving too fast does not snap on,
  matching a jump arc's peak still needing to finish naturally rather than
  instantly catching on a passing platform/pipe/ladder); hanging additionally
  requires approaching the surface from underneath, checked geometrically.
  Contacts are tracked per body as explicit `ContactType`
  flags (`SurfaceTop`
  `Climbable`/`Hangable`, recorded by `CollisionSystem` for the ladder/pipe
  currently gripped and read back by `PhysicsSystem` as the grip's reference
  frame and reaction target; the `IClimberBody.IsTouchingClimbable`/
  `IHangerBody.IsTouchingHangable` flags are kept alongside for engage/release),
  via `Body2D.AddContact`/`HasContact`/`GetContactingBodies`.
  `Resolve` clears every awake body's contacts (via
  `Body2D.ClearContactsForNewStep`) at the very start of each step (sleeping
  bodies keep theirs), and
  records each body's horizontal displacement since the end of the previous
  collision pass (`Body2D.MarkResolvedPosition`), excluding collision pushes
  (`Body2D.LastStepDisplacementX`/`ReferenceVelocityX`) - the real speed of a
  finite-mass support, which riders (`GetSurfaceVelocityX`) and
  `ResolveContact`'s friction are judged against (static supports use their
  prescribed `Velocity`). World edges (`ResolveWorldBounds`) are a
  contact-free safety net: all four sides clamp and reflect identically, but
  record no contact, so a body touching only an edge is not grounded.
  `ResolveContact` then records the reciprocal contact
  pair on both sides as it resolves a landing (e.g. the rider gets
  `SurfaceBottom` against the solid, the solid gets `SurfaceTop` against the
  rider). `IPhysicsBody.IsGrounded` is a derived, read-only property
  (`HasContact(SurfaceBottom)`); `PhysicsSystem` calls
  `RemoveContact(ContactType.SurfaceBottom)` the instant a jump/climb/hang
  begins, so the derived state reflects "airborne" for that same frame
  rather than waiting for `Resolve`'s next contact pass. Broad-phase
  uses a per-frame `SpatialGrid<T>` (one instance each for solids and moving
  bodies - `_solidsGrid`/`_movingBodiesGrid`, bucketed by `GridCellSize`)
  rather than testing every solid/mover pair unconditionally, so collision
  cost scales with nearby objects rather than the level's total object
  count - this bucket-and-candidate-lookup logic is the broad phase in its
  entirety; it only ever reasons about whole AABBs and grid cells, never
  about a body's actual collision rectangles or rendered characters.
  Everything downstream of a broad-phase candidate list - resolving a body
  against a specific solid or another specific moving body - is the narrow
  phase, and always performs a fine-grained character check:
  `TryFindContact` only accepts a rectangle-pair overlap if at least
  one world cell within it has a non-empty character on both bodies' sprite
  frames (see `HasCharacterOverlap`), rather than trusting the merged
  collision rectangles alone - a refinement that only matters for a shape
  whose true silhouette doesn't exactly fill its own merged rectangle (e.g.
  diagonal/notched sprites). All of this is resolved generically against
  capability interfaces, never by checking concrete types - the player is
  just a moving body whose restitution happens to be zero.
- **`CollisionShapeBuilder`** - derives a small set of collision rectangles
  from a sprite frame's actual non-empty glyph shape, so collision follows a
  sprite's real silhouette instead of its full bounding box. Runs two
  complementary run-length-merge passes and unions their results: a row-run
  pass (merging horizontal runs of non-empty cells, then merging
  vertically-identical runs across rows) and a column-run pass (its exact
  transpose - merging vertical runs, then merging horizontally-identical runs
  across columns), deduplicating any rectangle the two passes produce
  identically (the common case for a blocky rectangular sprite - a platform,
  a wall). Deduplication keeps the solver from testing the same rectangle
  pair twice. `CollisionSystem` does not resolve rectangles one by one: per
  solver iteration it resolves one contact per body pair, choosing the
  direction whose worst-case push separates every overlapping rectangle pair
  with the least correction (see `ResolveAgainstOtherBody`). For
  a curved/notched silhouette (e.g. a round `Ball` sprite), the row-run pass
  alone produces very short, wide rectangles along the shape's flanks, whose
  shallow vertical overlaps can make a vertical push look cheapest even where
  the true overlap is deep horizontally. The column-run pass's tall, narrow
  rectangles along those same flanks give the shape an accurate horizontal
  extent there too, so the correct horizontal contact normal wins.
- **`Rect`** - a simple axis-aligned rectangle in world cells, used to
  describe a piece of a body's collision shape and test overlap.
- **`SpatialGrid<T>`** - a small reusable broad-phase spatial hash grid:
  buckets arbitrary items by the world cells their AABB overlaps, then
  answers "what's near this AABB" without testing every item. Used by
  `CollisionSystem` for both solids and moving bodies; contains no
  collision-specific logic of its own.
- **`SleepSystem`** - run by `CollisionSystem` at the end of each step: groups
  awake and sleeping moving bodies into islands through their surface
  contacts and puts an island to sleep (`Body2D.Sleep`) once every member has
  rested long enough, or wakes it (`Body2D.Wake`) when any member was
  disturbed. Sleeping bodies are skipped by `PhysicsSystem` integration and by
  the solver except where an awake body or moving solid reaches them;
  `World2D.ApplyPendingRemovals` wakes sleepers that touched a removed body,
  `World2D.TryOpenGate`/`TryOperateSwitch` wake all sleepers after a state
  change (an opened gate or switched flow), and `Body2D` wakes itself when an
  animation frame changes its collision shape.



- **`Camera`** - follows a target's bounding box using a "dead zone": it
  only scrolls once the target nears the edge of the current view, and never
  scrolls past the world's own bounds. `SnapTo` immediately centers on a
  target with no smoothing (used once at world load); `Follow` smoothly
  catches up each frame afterward.
- **`WorldRenderer`** - turns the visible part of the world into a flat
  list of positioned glyphs
  frame), resolving each cell's color codes through the palette and
  converting world positions to pixel positions via the camera's current
  view. A game object's color-resolution chain is: per-cell code > its own
  `ForeColorOverride`/`BackColorOverride` > its sprite's own default color >
  its resolved material's own `ForegroundColor`/`BackgroundColor` (see
  `MaterialLibrary`) > the world's own default color > a hardcoded engine
  fallback (see `GlyphBuilder.ResolveColor`). The background/foreground
  layers are purely static level data, so their colors are instead resolved
  once at world load time into `World2D.BackgroundForeColors`/
  `BackgroundBackColors`/`ForegroundForeColors`/`ForegroundBackColors`
  (see `World2D.LoadAsync`'s `PrecomputeLayerColors`), and `WorldRenderer`
  just reads the precomputed color for each visible cell every frame instead
  of re-resolving it. The world itself is never
  restricted to a grid; this mapping exists
  purely for the visual output. `BuildFrame` only builds glyphs for the
  background rows/columns and objects that actually intersect the camera's
  current viewport rect, so a world larger than the viewport doesn't do
  per-cell work for the off-screen portion every frame; physics, collision,
  and animation are unaffected by this and keep simulating every body
  regardless of visibility. Game objects are drawn in two passes - every
  static body first, then every non-static body - rather than in
  `world.Objects`' own order (just the placement grid's row-by-row scan
  order), so a static body (e.g. a passable body of water) can never paint
  over a mover (a ball, the player, a hazard) that happens to be placed
  earlier in the grid than it; `Passable` only ever affects collision, never
  draw order. An optional per-world foreground layer (`World2D.ForegroundChars`/
  `ForegroundFore`/`ForegroundBack`, mirroring the background layer but empty
  by default) is then drawn last, on top of every object regardless of
  static/mover - purely visual decoration (prison bars, foliage overhang, ...)
  meant to always read as in front of everything, without a full per-object
  z-order system.
- **`Glyph`** - the final, fully-resolved output of the rendering pipeline: a
  single ASCII character plus its already-resolved foreground/background CSS
  colors, already positioned in pixel space (not world/cell coordinates) for
  one specific frame. Built by `GlyphBuilder`/`WorldRenderer`/`UIRenderer`/
  `WorldSelectRenderer`/`WorldLoadingRenderer`, then
  `CanvasBridge` for drawing - it carries no reference back to the world cell,
  sprite, or object it came from, so it is purely a draw instruction, never
  retained or reused across frames.
- **`GlyphBuilder`** - shared color-resolution (a code's first match in a
  caller-supplied precedence list, tried against the palette), cell-to-pixel
  glyph construction, and box-drawing-border helpers used by `WorldRenderer`,
  `WorldSelectRenderer`, and `UIRenderer` alike, since all three need the
  same few small operations just with different precedence chains/coordinate
  spaces/placements. `GlyphBuilder.DefaultForeColor` (the interface Normal
  tint once loaded, `RenderConstants.DefaultForegroundColor` before that) is
  the fallback for world content and any glyph built without a foreground
  color; a glyph without a background color has no fill. The canvas is filled
  with the world's `BackdropColor`, else `InterfaceColors.Background`.
- **`UIFrame`** / **`UILabel`** / **`UIBar`** - the independent screen-space drawable
  primitives, laid out directly in viewport cell coordinates, unaffected by
  the camera: a box-drawing border, one or more lines of text, and a gauge.
  Deliberately separate types rather than one bundled "panel" object, since
  neither requires the other (a plain decorative frame needs no text; a
  centered message needs no box) - any screen (world HUD, world-select, or a
  future screen) can draw just one, or several side by side. Each has its own
  optional foreground/background color (callers pass `InterfaceColors` values),
  falling back to `GlyphBuilder.DefaultForeColor` when unset; `UILabel`'s width/line-count
  caps are fixed rather than auto-sized to its current content, so a
  reserved screen area keeps a stable footprint frame to frame even as the
  text updates (e.g. a live score) - an overly long line or an overflowing
  line count is simply truncated.
- **`UIRenderer`** - turns a `UIFrame`, `UILabel` or `UIBar` into glyphs, reusing
  `GlyphBuilder.AddBox`/`BuildGlyph`; used by `GameLoop`'s HUD overlay and
  available to any future screen-space overlay. A label with a `HighlightColor`
  draws text wrapped in `{}` in that color (braces not drawn, no space taken;
  `GetVisibleLength` measures such a line); used for the HUD numbers and the
  menu's control keys, both in the interface Dark tint.
- **`WorldSelectRenderer`** - builds the world-selection screen's glyph list
  (thumbnails, titles, selector box). Unlike `WorldRenderer` it has no camera
  and no `World2D` - it lays out directly in viewport cell coordinates, since
  the selection screen exists before any world is loaded. Titles wider than a
  thumbnail are truncated, except the selected one, which slides back and
  forth (equal pause at each end) driven by `WorldSelectScreen.TitleScrollSeconds`.
  The logo is drawn per glyph with a vertical gradient of the interface tints (light top two
  rows, normal middle two, dark bottom row), and the scroll chevrons likewise
  (light top row, normal middle, dark bottom row). The selector box is drawn
  in the interface Dark tint for a moment after the selection moves, so it appears to fade in.
  While `WorldSelectScreen.IsIntroComplete` is false only the logo is drawn,
  with "ASCII" sliding in from the left and "Hero" from the right
  (`WorldSelectScreen.IntroSeconds`; menu input is ignored until it finishes).
- **`WorldLoadingRenderer`** - creates the "Loading..." `UIBar`
  (`CreateLoadingBar`) and draws the frozen selection layout plus that bar
  while a world loads (`BuildLoadingFrame`).
- **`EndScreenRenderer`** - draws the centered end-screen message box
  (ripple, glow and label) for a given text, tint set and elapsed time. It
  only builds glyphs; `GameLoop` decides the mode, text, colors and timing.

- **`AnimationSystem`** - advances every body's animation timer once per
  frame (bodies with no animation configured, or a single frame, no-op
  internally) and ticks the lifetime of any `EffectInstance2D`, queuing it
  for removal once its effect finishes playing (unless configured to
  persist).

### Browser

- **`CanvasBridge`** - the sole interop boundary between C# and the browser's
  Canvas/keyboard APIs (via `game-interop.js`). Initializes the canvas with
  the configured font family, cell size, and viewport columns/rows (see
  `Global/Settings.ini`'s `[Render]` section - `FontWidthPixels`/
  `FontHeightPixels` are the final, already-scaled on-screen cell size, not
  measured by the browser), resizes the canvas element to
  `columns/rows * cellSize`, and draws a frame's glyphs. No other game code
  talks to JavaScript directly.
- **`InputState`** - tracks which keyboard keys are currently held down and
  exposes them as game-oriented queries (`IsLeftKeyDown`, `IsUpKeyDown`,
  etc.), so gameplay code never depends on raw DOM key codes. Two full,
  independent key sets are supported for local co-op/preference - "Player 1"
  (arrow keys + `Space`, action `Enter`) and "Player 2" (`WASD` + `Left Ctrl`, action `E`) - rather than
  one shared movement set plus one shared jump key. `IsUpKeyDown` and
  `IsJumpKeyDown` are deliberately never combined into one query anywhere,
  even for the ordinary ground jump - see `PhysicsSystem` above for why
  (`Hang`/`Climb` both need to tell "pull in"/"climb" apart from "let go and
  launch").

### Audio

- **`SoundLibrary`** (Assets) - loads `SoundLibrary.ini` (global, merged
  with world overrides) into `SoundDefinition`s.
- **`ISoundPlayer`** / **`WebAudioSoundPlayer`** - game-facing audio boundary
  and its JS interop implementation (`wwwroot/js/audio.js`, Web Audio
  synthesis). `GameLoop` calls `PlayQueuedSounds()` after physics/collision
  and animation each frame, forwarding names queued on `World2D`
  (`PlaySound`/`DrainPendingSounds`) by `CollisionSystem` and
  `AnimationSystem`. At startup `GameLoop` registers the global-only
  `SoundLibrary` so the world-select menu can play `MenuMove` when the
  selection changes and `MenuSelect` when a world is confirmed; each loaded
  world then replaces it with its own merged library.

### Menu

- **`WorldSelectScreen`**
  world-selection screen (see docs/AssetFormat.md §3.2): which world is
  currently selected, how many thumbnail slots are visible, and the scrolled
  window (`ScrollOffset`) that keeps the selection centered in the middle
  slot once there are enough worlds on both sides. Edge-triggers Left/Right
  (so holding the key doesn't repeat every frame) and exposes `Confirmed`
  once the action key is pressed. Also keeps `TitleScrollSeconds`, the time
  since the current selection was made (reset on every selection change).

### Game Loop

- **`GameLoop`** - ties every subsystem above together and drives them once
  per animation frame. Owns one instance of each system (input, physics,
  collision, camera, renderer, animation) and, once past the world-selection
  screen, one loaded `World2D`. Driven entirely by JavaScript's
  `requestAnimationFrame` calling back into C# - never by Blazor's
  `StateHasChanged`.
  - Internally a strict `GameMode` state machine (`WorldSelecting` ->
    `LoadingWorld` -> `Playing` -> `LevelComplete` or `GameOver` -> back to
    `WorldSelecting`, plus `ConfirmExit` while the "Exit game?" prompt is
    open during play) - never more than one is active, and
    `OnFrame` switches on it to decide whether to drive
    `WorldSelectScreen`/`WorldSelectRenderer`, redraw the frozen selection
    layout plus a filling "Loading..." `UIBar` (a world's `World2D.LoadAsync`
    is in flight), run the normal gameplay tick, or freeze gameplay and show
    a "Level Complete!"/"Game Over!" message (see below) before looping back to
    world selection.
  - `OnFrame` is invoked by JS in a fire-and-forget fashion - the next
    `requestAnimationFrame` call is scheduled without waiting for the
    previous call's `Task` to finish (see game-interop.js) - so a frame that
    spans a genuine async gap (in particular, `World2D.LoadAsync`'s real
    HTTP fetches while confirming a world) could otherwise be re-entered by
    an overlapping `OnFrame` call before it finishes, which would
    manifest as the
    selection screen and gameplay both appearing to render at once. Two
    guards prevent this: a blanket `_isProcessingFrame` flag drops any
    `OnFrame` call that overlaps one still in progress, and `GameMode` is
    flipped to `LoadingWorld` synchronously - before the `await
    World2D.LoadAsync(...)` gap - so the transition itself can never be
    entered twice even if that blanket guard were ever loosened.

## Key Flows

### Startup

1. The Blazor host (`ASCII Hero` project's `Program.cs`) configures the web
   app, registers an `HttpClient` for prerendering, and maps the root
   component with WebAssembly interactive rendering.
2. Once the WebAssembly runtime takes over in the browser, the client
   project's `Program.cs` registers its own `HttpClient` (used by
   `HttpAssetFileProvider` to fetch asset files as static web content).
3. The hosting page creates a `GameLoop` and calls `StartAsync`, which:
   - Loads every world's lightweight `WorldSummary` up front via
	 `WorldCatalog.LoadAllAsync` (reading `Global/Worlds.ini` for the world
	 list, then each world's title + thumbnail only, not a full `World2D`).
   - Initializes `CanvasBridge`, which sets up the canvas at the configured
	 `[Render]` cell size and returns it as `CellMetrics`.
   - Builds the initial `WorldSelectScreen` (how many thumbnail slots fit the
	 viewport width, defaulting
	 and puts `GameLoop` into its `WorldSelecting` `GameMode`.
4. From here, `OnFrame` drives the world-selection screen (see below) until
   the player confirms a world, at which point `GameLoop` switches to its
   `LoadingWorld` `GameMode`, creates a `UIBar` sized to
   `World2D.LoadStepCount` (`WorldLoadingRenderer.CreateLoadingBar`), and
   loads that world's `World2D` via `World2D.LoadAsync` (reading settings,
   palette/materials, background, object definitions, and every object
   placement, resolving each placement's sprite through `SpriteLoader`) - so
   gameplay never stalls mid-frame waiting on a network fetch. `LoadAsync`
   reports its progress after each of those logical steps via an
   `IProgress<double>` callback, which fills in the bar's `CurrentValue`; every
   `OnFrame` tick that lands while still in `LoadingWorld` redraws the
   (`WorldLoadingRenderer.BuildLoadingFrame`)
   instead of leaving the canvas showing a stale frame. Once loading
   completes, `GameLoop` snaps the camera immediately onto the world's
   designated camera target (the player by default, or another body if a
   world opts one in), and finally switches to its `Playing` `GameMode`,
   running the normal per-frame gameplay tick from the next frame on.

### Per-Frame Tick

Driven by the browser's `requestAnimationFrame` calling `GameLoop.OnFrame`
once per frame, with the elapsed time since the last frame (clamped to avoid
large jumps after e.g. a tab switch):

1. **Physics + Collision (fixed timestep)** - a single real frame's elapsed
   time varies frame to frame, both from ordinary timing jitter and
   occasionally far more than ordinary after a real hitch (a GC pause, a
   slow JS interop round-trip, a dropped/re-entrant frame). Integrating a
   variable amount directly through `PhysicsSystem.Step`/`CollisionSystem.Resolve`
   is unsafe two ways: an oversized single step risks a body moving far
   enough to jitter erratically or tunnel through a wall before collision
   ever reacts (neither uses continuous/swept detection), and even an
   ordinary but inconsistent step size can, on its own, visibly perturb
   collision/pose resolution right at a grounded/airborne boundary (the
   exact instant contact is gained/lost shifts with the immediately
   preceding step's size). `GameLoop.OnPlayingFrameAsync` therefore drives
   Physics/Collision with a fixed-timestep accumulator (the standard "fix
   your timestep" pattern, via `_physicsAccumulatorSeconds`): each frame's
   elapsed time is added to the accumulator, which is then drained in
   however many whole steps of exactly `PhysicsConstants.FixedPhysicsStepSeconds`
   are currently available, leaving any remainder (always strictly less
   than one fixed step) for next frame rather than folding it into an
   odd-sized partial step this frame - so every single step is identically
   sized, deterministically, regardless of how the frame rate fluctuates.
   When the fastest moving body would travel more than
   `PhysicsConstants.MaxStepTravelCells` in one step, that fixed step is split
   into equal substeps (`GameLoop.CountSubsteps`, at most `MaxSubsteps`), each
   running the full Physics/Collision/removals sequence below, so fast bodies
   cannot tunnel through thin walls.
   Each step runs `PhysicsSystem.Step` (resolves player jump/stance/pose
   input, feeding the player's own force-based ground/climb/hang locomotion,
   see `IWalkForceBody` above; applies gravity to affected bodies; and
   integrates every moving body's position from its accumulated forces) and
   then `CollisionSystem.Resolve` (resolves overlaps: moving bodies against
   solid terrain and world bounds, moving bodies against each other,
   hazard/collectable contact - including collector pickups and
   killer/killable kill contacts, each of which may spawn a cosmetic
   `EffectInstance2D` - and any `IClimberBody`/`IHangerBody`'s
   `IsTouchingClimbable`/`IsTouchingHangable` overlap against
   `IsClimbable`/`IsHangable` terrain, both speed-gated, with hanging
   additionally requiring approaching the surface from underneath) once per
   fixed step - not once for the whole frame - so a large catch-up delta
   can't let a body integrate several times before ever being
   collision-checked. Collectable pickup itself branches on `Collectable2D.Type`
   (`CollectableType`): all variants are removed on pickup. `Diamond`
   adds 1 to `Player2D.Score`; `Ring` increments `Rings`, adds 5 to `Score` and, every
   `World2D.RingsPerLife` rings, resets `Rings` and adds a life; `Checkpoint`
   records `World2D.RespawnPoint`; `LevelEnd` sets `World2D.PendingTransition`
   to `LevelEnd` for `GameLoop` to act on. Collectables (and hazards) with an
   effect clip (e.g. a
   collectable with `EffectPersists = true`) spawn their pickup effect as a
   permanent husk in their place, the same way a killed hazard's effect can
   persist. `World2D.ApplyPendingRemovals`
   (removes anything queued for removal - a picked-up collectable, a killed hazard, an expired
   effect - deferred from the systems above so nothing mutates the object
   list mid-iteration) likewise runs after every fixed step, not just once
   at the end, so a later step's collision pass never sees a body that
   should already be gone. `_physicsAccumulatorSeconds` is reset to `0` when
   a new world finishes loading, so no leftover time from a previous world
   (or time spent loading) is burned through as extra steps on the new
   world's very first frame. At the same moment the pending-transition timer
   is cleared and `PhysicsSystem`, `CollisionSystem` and `AnimationSystem`
   each `Reset()` the state they carry between frames (held-key edges,
   active hazard contacts, previous airborne state), so nothing from the
   previous world leaks into the new one.
2. **Animation** - `AnimationSystem.Update` advances every body's animation
   frame timer and ticks down any active effect's remaining lifetime. Runs
   once per real frame (using the frame's full elapsed time, not the fixed
   fraction any one Physics/Collision step used), not once per fixed step,
   since it is purely presentational and already tolerant of a larger delta.
3. **Camera** - `Camera.Follow` smoothly scrolls toward the current camera
   target's position, respecting its dead zone and the world's bounds. Also
   runs once per real frame, for the same reason as Animation above.
4. **Render** - `WorldRenderer.BuildFrame` converts the current world and
   camera view into a flat glyph list - culled to the camera's current
   viewport rect (see Rendering above) - in background, static-object,
   non-static-object, foreground order. `GameLoop.OnFrame` then appends the
   HUD's own glyphs (`UIRenderer.AddFrame`/`AddLabel`/`AddBar` for `_hudBox`/
   `_hudText`/`_hudBar`) to that same list, so the HUD is always drawn last -
   on top of the foreground layer and everything else - before
   `CanvasBridge.DrawFrameAsync` sends the complete list to JavaScript to
   paint onto the canvas (`drawFrame` first fills the whole canvas with the world's
   `BackdropColor`, if set, then fills backgrounds as cell rectangles and copies
   each character from a cache of pre-rendered images, one per character/color
   combination, cleared when the font or cell size changes). `_hudText` is refreshed every `Playing` frame from
   `World2D.Player.Lives`/`.Rings`/`.Score` on its first row; its second row is a
   "Health:" caption followed by the `_hudBar` gauge (`Player2D.Health`), and a key icon
   followed by a superscript count (`¹`-`⁹`, `ⁿ` for more than 9) appears while a key is held.
   A dev/testing FPS overlay
   (`InputState.IsFpsToggleKeyDown`, bound to `F`) can be toggled on top
   of the HUD, showing a smoothed frames-per-second reading computed each
   frame from that frame's own raw, unclamped elapsed time (not the fixed
   Physics/Collision step size), right-aligned using the rendered text's own
   length (leading with the number, e.g. "144 FPS") so it stays pinned to the
   top-right corner regardless of digit count;
   off by default so it never appears for an ordinary player. A second
   dev/testing overlay (`InputState.IsObjectCounterToggleKeyDown`, bound
   to `O`) stacks directly under the FPS overlay, showing `World2D.Objects`'s
   live count (e.g. "42 Obj") - same right-aligned-by-rendered-length
   approach, also off by default.

   When `CollisionSystem` sets `World2D.PendingTransition` (`LevelEnd` on a
   `CollectableType.LevelEnd` pickup, `Death` when hazard damage empties
   `Player2D.Health` - see above), the Playing tick skips Physics/Collision
   (Animation, Camera and Render keep running, so the player's own
   `LevelEndReachedEffectClip`/`DiedEffectClip` plays) until
   `PendingTransitionSeconds` have passed. `GameLoop.AdvancePendingTransition` then
   carries it out: `LevelEnd` switches to `GameMode.LevelComplete`; `Death` calls
   `World2D.Respawn()` and play continues if `Player2D.Lives` is above 0,
   otherwise it switches to `GameMode.GameOver`. In both end modes gameplay is
   frozen, the last rendered world frame is redrawn with a centered "Level
   Complete!"/"Game Over!" message on top (`GameLoop.AddEndScreenMessage` picks
   the text and tint, `EndScreenRenderer.AddMessage` draws it: a
   short trail of `UIFrame` boxes shrinks in a ripple onto the final box, then
   the `UILabel` appears inside it on a solid black background; green for
   Level Complete, red for Game Over (amber for the "Exit game?" prompt, which
   uses the same method); once settled the box border keeps glowing in steps
   through the dark/normal/light tints while the text stays put; the animation is driven by an elapsed
   timer reset on entering the mode), and a confirm-key press (only after
   those keys were released once) returns `GameLoop` to `GameMode.WorldSelecting`,
   the same reset path used by a confirmed exit.

### Asset Loading (Global vs. World Fallback)

Sprites, colors, and materials are resolved with a consistent "world
overrides global" rule (see [AssetFormat.md](AssetFormat.md) §1.1 for the
full reference):

- A sprite is loaded from a world-local `Sprites/{AssetName}/` folder if one
  exists there; otherwise from the shared `Global/Sprites/{AssetName}/`
  folder. The mere presence of the world-local folder is the override
  signal - no explicit flag is needed.
- A world's own optional `ColorPalette.ini`/`MaterialLibrary.ini` is merged over the
  global one, with world entries taking precedence for same-named
  codes/sections, while anything only defined globally still applies.

Resolution happens per asset/code/section, not per world, so overriding one
thing never hides the rest of the globals - e.g. an ice-themed world could add
a world-local `Sprites/Player/` reskin, an icy `ColorPalette.ini` recolor, and
a low-friction `Ice` entry in `MaterialLibrary.ini`, while every other sprite,
color, and material it uses still resolves to the global ones.

### Coordinate System

Game entities use floating-point world coordinates ("cells"), not integer
grid indices - the ASCII character grid is a rendering concept applied only
at draw time (via the camera transform), so movement and physics stay smooth
regardless of the grid-based visual language. See
[Architecture.md](Architecture.md#coordinate-system) for the full rationale.
