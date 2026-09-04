# Program Structure

This document describes how AsciiHero is put together: its components, their
responsibilities, and the key flows that tie them together at runtime. It is a
companion to [Architecture.md](Architecture.md) (architectural rules and
rationale) and [AssetFormat.md](AssetFormat.md) (file format reference) - this
document instead focuses on "what exists and how it fits together."

## Solution Layout

The solution has two projects:

- **ASCII Hero** - the Blazor Web App host. Serves the page shell, handles
  server-side prerendering, and hosts the WebAssembly runtime. Contains almost
  no game logic itself.
- **ASCII Hero.Client** - the Blazor WebAssembly client project. Contains all
  game code, static assets (sprites, worlds, fonts), and the small amount of
  JavaScript interop the game needs. The entire game runs client-side inside
  this project once the page loads.

All gameplay code lives under `ASCII Hero.Client/Game/`, organized into one
folder per subsystem, described below in roughly the order data flows through
them at startup and each frame.

## Subsystems

### Assets

Everything to do with reading the plain-text asset format (sprites, worlds,
palettes, materials) from disk/HTTP into in-memory game objects.

- **`IAssetFileProvider`** - fetches raw file text by relative path, returning
  null for a missing file (404). Implemented by `HttpAssetFileProvider`, which
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
  settings file is itself the override signal, no explicit flag needed.
- **`ColorPalette`** - the resolved single-character color code -> CSS color
  lookup, merging a world's own optional `Colors.ini` over `Global/Colors.ini`.
- **`MaterialLibrary`** - the resolved material-name -> `Material` (`Density`,
  `Friction`, `Restitution`) lookup, merging a world's own optional
  `Materials.ini` over `Global/Materials.ini`. An unknown/absent material name
  resolves to a zeroed `Material.Undefined` fallback rather than throwing.
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
  sprite-backed object (player, platforms, enemies, collectables alike). An
  asset's optional `[ClipFolders]` section (see docs/AssetFormat.md §2.7)
  lets a busy multi-stance asset (e.g. `Player`) group its clips' files into
  per-stance subfolders instead of one flat folder - purely a file-layout
  convenience, invisible to every other caller/consumer of a loaded clip.
- **`SpriteAsset` / `SpriteClip` / `SpriteFrame`** - the in-memory result of
  loading: an asset holds named clips (e.g. `walk_idle`, `walk_left`), each
  clip holds one or more frames (char/fore/back/material grids), plus
  resolved animation timing (duration, loop mode, default frame) and optional
  stance/facing metadata mapping a stance name to the clip shown for each
  facing.
- **`SpriteFrameTiler`** - repeats a tileable frame's authored unit along its
  declared axis (horizontal or vertical) to build an arbitrary-length
  platform or wall from one small authored unit, at spawn time.
- **`WorldSummary` / `WorldCatalog`** - lightweight, non-gameplay metadata for
  the world-selection screen: a world's `Title` and its fixed 16x8, optionally
  animated thumbnail art (see docs/AssetFormat.md §3.1/§3.2), loaded without
  loading the rest of that world's playable `World2D`.
  `WorldCatalog.LoadWorldNamesAsync` reads the explicit, authored list of
  every playable world from `Global/Worlds.ini` (§4.4) - Blazor WebAssembly
  has no way to list `wwwroot`'s directory contents at runtime, so (like
  `[Stances]`) this is an authored list rather than one inferred from the
  filesystem.

### World

The live game state and the entity types that make it up.

- **`World2D`** - holds everything that makes up the current game state: the
  player, every other body in one generic `Objects` list, the background
  layer, the resolved color palette, the resolved material library
  (`Materials`), gravity, world dimensions, and which body the camera should
  follow. `World2D.LoadAsync` is the world loader: it reads a world's
  settings, background, and object-placement files, resolves each
  placement's sprite and concrete body type, resolves each spawned body's
  `Density`/`Friction`/`Restitution`/`Mass` from its material (a placement's
  ini section may override the resolved material name via `Material`, or
  just the resulting `Restitution` via `Restitution`), and assembles the
  finished world. Also owns deferred removal (`QueueRemoval`/`ApplyPendingRemovals`),
  so nothing mutates the `Objects` list mid-iteration.
- **`Body2D`** - the base class for anything living in the world at a
  floating-point position, backed by a loaded sprite frame. Derives its size
  and collision shape once from the active frame's actual (non-empty) glyph
  data, handles animation frame advancement and stance/facing pose switching,
  and exposes the per-instance `IsPassable`/`IsClimbable`/`IsHangable` flags
  that let any static placement opt into those behaviors without a dedicated
  subclass.
- **Concrete body types**, all deriving from `Body2D`:
  - `Player2D` - the player-controlled character; implements the mover,
	gravity, collector, and killer capabilities (below), plus the climbing
	(`IClimberBody`) and hanging (`IHangerBody`) capabilities (see Physics
	below). Nothing here is player-specific - both are ordinary capability
	interfaces any future body (e.g. a climbing/hanging enemy) could
	implement the same way.
  - `StaticObject2D` - solid, static terrain (platforms, walls, decoration).
  - `StaticEnemy2D` - a non-moving hazard (e.g. spikes) that can optionally
	be "killable" and can trigger a cosmetic effect on contact.
  - `DynamicObject2D` - a non-player object driven by velocity and,
	optionally, gravity, bouncing off world bounds/platforms according to its
	restitution (e.g. the bouncing ball).
  - `KinematicObject2D` - moves at a constant, predefined velocity; never
	affected by gravity. Currently a simple base for future patrol-style
	motion.
  - `MovingEnemy2D` - an AI-controlled hazard that moves and collides exactly
	like a `DynamicObject2D`; patrol/chase behavior itself is not yet
	implemented.
  - `Collectable2D` - a static, non-solid item removed from the world when a
	collector body touches it (e.g. a coin or power-up).
  - `EffectInstance2D` - a purely cosmetic, non-collidable body that plays a
	short visual effect clip (a pickup fade, a kill "crumble") and then
	either self-removes or persists as a permanent decorative husk.
- **Capability interfaces** - rather than checking concrete types, every
  system that needs to act on "any body that can X" checks one of these
  instead. Some are bare markers (no state, just identity); others carry
  real per-instance state:
  - `IPhysicsBody` - participates in physics integration and collision
	(position, velocity, size, grounded state, collision shape).
  - `IGravityAffected` - can optionally opt out of gravity via a
	`GravityAffected` flag.
  - `IHazardBody` - damages a body on contact (marker only; the actual damage
	effect is not yet implemented, as there is no health/damage system yet).
  - `ICollectableBody` - removed from the world on contact with a collector
	(marker only).
  - `ICollectorBody` - can pick up collectables on contact (e.g. the player).
  - `IKillerBody` - can "kill" a killable hazard by landing on top of it
	(e.g. the player); kept independent of `ICollectorBody` since the two
	capabilities are conceptually unrelated even though the player has both.
  - `IKillableBody` - can be killed by a qualifying contact; carries real
	state (`IsKillable`, `EffectPersists`) since not every instance of an
	otherwise-killable type should be killable in every placement.
  - `IEffectTrigger` - can optionally name a cosmetic effect clip
	(`EffectClipName`) to play on contact, reusing a clip already defined on
	that same body's own sprite asset.
  - `IClimberBody` - can climb an `IsClimbable` surface (e.g. a ladder);
	carries `IsTouchingClimbable` (mere overlap, recomputed every frame) and
	`IsClimbing` (actually engaged, driven by input). Independent of
	`IHangerBody` - a body can implement either, both, or neither.
  - `IHangerBody` - can hang and shimmy laterally from an `IsHangable`
	surface (e.g. a pipe/rope); carries `IsTouchingHangable`, `IsHanging`,
	`IsClambering` (regular fully-stretched hang vs. a compact clamber/
	shimmy pose that fits through narrower spaces), and
	`SuppressHangUntilClear` (debounce set by `PhysicsSystem` the instant the
	player jumps/swings off or lets go, consulted by `CollisionSystem` so it
	doesn't immediately re-snap the body while still overlapping the same
	surface).

  positions and velocities. World coordinates are continuous cells, not
  pixels or integer grid indices - see Coordinate System below.

### Physics

- **`PhysicsSystem`** - applies player input to horizontal velocity and
  jumping, applies gravity to any `IGravityAffected` body, and integrates
  position from velocity for every `IPhysicsBody` each frame. The player
  still moves via direct velocity assignment from input; every other moving
  body integrates instead via a per-frame mass-scaled force accumulator
  (`StepMovingBodyWithForces` - gravity as `mass * world.Gravity`, converted
  to acceleration via `a = F / mass`, then integrated into velocity), which
  is numerically identical to a direct gravity-velocity add for a
  gravity-only body but is the extension point for any future non-gravity
  force source. Also resolves
  the player's stance (Walk/Crawl, toggled by input) and pose (which swaps to
  a visual-only "Jump" pose while airborne, independent of the underlying
  stance). Also engages/disengages `IsClimbing`/`IsHanging` (on any
  `IClimberBody`/`IHangerBody`, generically) from the previous frame's
  `IsTouchingClimbable`/`IsTouchingHangable` (set by `CollisionSystem`):
  climbing requires a deliberate up/down key press while touching a climbable
  surface (grabbable from any side, including mid-air) and while already
  standing (`Stance == "Walk"` - a crawling player must explicitly stand up
  first, as a separate step), and yields to landing on solid ground; hanging
  engages automatically as soon as a hangable surface is touched from
  underneath, picking the compact "clamber" pose if the player was already
  crawling at the moment of grabbing on. Stance transitions on the ground and
  while hanging are driven by one shared, structured up/down "stance ladder":
  on the ground, Up always means "more upright" (Crawl -> Walk) and Down
  always means "more compact" (Walk -> Crawl); while hanging, the same keys
  are deliberately inverted to match arm position rather than screen
  direction - Up pulls into the compact `Clamber` pose (mirroring Crawl) and
  Down extends back out to the fully-stretched `Hang` pose (mirroring Walk),
  with a further Down from already-`Hang` meaning an explicit "let go" and
  dropping. `Up` (directional) and `Jump` (action) are always distinct
  inputs, never equivalent anywhere including the ground - see
  `InputState.IsUpPressed`/`IsJumpPressed` below - so a Jump press instead of
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
  speed) until it lets go via the stance ladder above. Both suspend gravity
  via `IGravityAffected.GravityAffected` and resolve their own dedicated
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
  hazard/collectable overlap, and any `IClimberBody`/`IHangerBody`'s
  `IsTouchingClimbable`/`IsTouchingHangable` overlap against
  `IsClimbable`/`IsHangable` terrain. Restitution/friction used in any given
  collision response are the combined (simple-average, see `Combine`) values
  of both contacting bodies' own resolved material properties (see
  `Body2D.Restitution`/`Friction` below), not a single one-sided or
  type-based value. Moving-body-vs-moving-body resolution
  (`ResolveBodyPair`) splits position correction by relative mass and
  resolves the along-normal velocity response via a standard 1D
  mass-weighted impulse, rather than each body independently reflecting its
  own velocity. Both climbing and hanging touch checks
  share one generic snap-speed gate (a body moving too fast does not snap on,
  matching a jump arc's peak still needing to finish naturally rather than
  instantly catching on a passing platform/pipe/ladder); hanging additionally
  requires approaching the surface from underneath, checked geometrically
  (top-edge comparison) rather than by penetration depth so a fast-falling or
  passable body can't be misread as hanging while plunging through. Most of
  this is resolved generically against capability interfaces, never by
  checking concrete types - the player is just a moving body whose
  restitution happens to be zero. Against solid terrain, a multi-rectangle
  body (e.g. the player's narrower "head" rect above its wider "torso" rect)
  is resolved one of its own rects at a time rather than picking a single
  globally-deepest-overlapping pair, and each rect is re-read fresh from the
  body's current position on every iteration - otherwise one rect's push-out
  can leave another rect still stuck in the solid, or two rects' corrections
  can fight each other frame to frame (visible as pose jitter while standing
  still on solid ground).
- **`CollisionShapeBuilder`** - derives a small set of collision rectangles
  from a sprite frame's actual non-empty glyph shape (merging horizontal runs
  of non-empty cells, then merging vertically-identical runs across rows),
  so collision follows a sprite's real silhouette instead of its full
  bounding box.
- **`Rect2D`** - a simple axis-aligned rectangle in world cells, used to
  describe a piece of a body's collision shape and test overlap.

### Camera

- **`Camera2D`** - follows a target's bounding box using a "dead zone": it
  only scrolls once the target nears the edge of the current view, and never
  scrolls past the world's own bounds. `SnapTo` immediately centers on a
  target with no smoothing (used once at world load); `Follow` smoothly
  catches up each frame afterward.

### Rendering

- **`WorldRenderer`** - translates the floating-point game world into a flat
  list of positioned glyphs (background layer plus every object's active
  frame), resolving each cell's color codes through the palette and
  converting world positions to pixel positions via the camera's current
  view. The world itself is never restricted to a grid; this mapping exists
  purely for the visual output. `BuildFrame` only builds glyphs for the
  background rows/columns and objects that actually intersect the camera's
  current viewport rect, so a world larger than the viewport doesn't do
  per-cell work for the off-screen portion every frame; physics, collision,
  and animation are unaffected by this and keep simulating every body
  regardless of visibility.
- **`Glyph`** - a single ASCII character to draw at a pixel position, with
  resolved foreground/background colors.
- **`GlyphBuilder`** - shared color-resolution (a code's first match in a
  caller-supplied precedence list, tried against the palette), cell-to-pixel
  glyph construction, and box-drawing-border helpers used by `WorldRenderer`,
  `WorldSelectRenderer`, and `UIRenderer` alike, since all three need the
  same few small operations just with different precedence chains/coordinate
  spaces/placements. Also defines `DefaultForeColor`, the single app-wide
  fallback color used whenever no more specific color is resolved.
- **`UIBox`** / **`UIText`** - the two independent screen-space drawable
  primitives, laid out directly in viewport cell coordinates, unaffected by
  the camera: a box-drawing border, and one or more lines of text.
  Deliberately separate types rather than one bundled "panel" object, since
  neither requires the other (a plain decorative frame needs no text; a
  centered message needs no box) - any screen (world HUD, world-select, or a
  future screen) can draw just one, or both side by side. Each has its own
  optional foreground/background color, falling back to
  `GlyphBuilder.DefaultForeColor` when unset; `UIText`'s width/line-count
  caps are fixed rather than auto-sized to its current content, so a
  reserved screen area keeps a stable footprint frame to frame even as the
  text updates (e.g. a live score) - an overly long line or an overflowing
  line count is simply truncated.
- **`UIRenderer`** - turns a `UIBox` or `UIText` into glyphs, reusing
  `GlyphBuilder.AddBox`/`BuildGlyph`; used by `GameLoop`'s HUD overlay and
  available to any future screen-space overlay.
- **`WorldSelectRenderer`** - builds the world-selection screen's glyph list
  (thumbnails, titles, selector box). Unlike `WorldRenderer` it has no camera
  and no `World2D` - it lays out directly in viewport cell coordinates, since
  the selection screen exists before any world is loaded.

### Animation

- **`AnimationSystem`** - advances every body's animation timer once per
  frame (bodies with no animation configured, or a single frame, no-op
  internally) and ticks the lifetime of any `EffectInstance2D`, queuing it
  for removal once its effect finishes playing (unless configured to
  persist).

### Browser / Input

- **`CanvasBridge`** - the sole interop boundary between C# and the browser's
  Canvas/keyboard APIs (via `game-interop.js`). Initializes the canvas and
  measures the active font's real pixel cell size (`CellMetrics`), switches
  fonts at runtime, and draws a frame's glyphs. No other game code talks to
  JavaScript directly.
- **`InputState`** - tracks which keyboard keys are currently held down and
  exposes them as game-oriented queries (`IsLeftPressed`, `IsUpPressed`,
  etc.), so gameplay code never depends on raw DOM key codes. Two full,
  independent key sets are supported for local co-op/preference - "Player 1"
  (arrow keys + `Space`) and "Player 2" (`WASD` + `Left Ctrl`) - rather than
  one shared movement set plus one shared jump key. `IsUpPressed` and
  `IsJumpPressed` are deliberately never combined into one query anywhere,
  even for the ordinary ground jump - see `PhysicsSystem` above for why
  (`Hang`/`Climb` both need to tell "pull in"/"climb" apart from "let go and
  launch").

### Menu

- **`WorldSelectScreen`** - state and input handling for the pre-game
  world-selection screen (see docs/AssetFormat.md §3.2): which world is
  currently selected, how many thumbnail slots are visible, and the scrolled
  window (`ScrollOffset`) that keeps the selection centered in the middle
  slot once there are enough worlds on both sides. Edge-triggers Left/Right
  (so holding the key doesn't repeat every frame) and exposes `Confirmed`
  once the jump/action key is pressed.

### Game Loop

- **`GameLoop`** - ties every subsystem above together and drives them once
  per animation frame. Owns one instance of each system (input, physics,
  collision, camera, renderer, animation) and, once past the world-selection
  screen, one loaded `World2D`. Driven entirely by JavaScript's
  `requestAnimationFrame` calling back into C# - never by Blazor's
  `StateHasChanged`.
  - Internally a strict three-state `GameMode` (`WorldSelecting` ->
    `LoadingWorld` -> `Playing`) - never more than one is active, and
    `OnFrame` switches on it to decide whether to drive
    `WorldSelectScreen`/`WorldSelectRenderer`, do nothing (a world's
    `World2D.LoadAsync` is in flight), or run the normal gameplay tick.
  - `OnFrame` is invoked by JS in a fire-and-forget fashion - the next
    `requestAnimationFrame` call is scheduled without waiting for the
    previous call's `Task` to finish (see game-interop.js) - so a frame that
    spans a genuine async gap (in particular, `World2D.LoadAsync`'s real
    HTTP fetches while confirming a world) could otherwise be re-entered by
    an overlapping `OnFrame` call before it finishes, which previously
    manifested as a world's assets being loaded more than once / the
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
   - Initializes `CanvasBridge`, which sets up the canvas and reports back
	 the real measured pixel size of one glyph cell for the active font.
   - Builds the initial `WorldSelectScreen` (how many thumbnail slots fit the
	 measured viewport width, defaulting the selection to the first world)
	 and puts `GameLoop` into its `WorldSelecting` `GameMode`.
4. From here, `OnFrame` drives the world-selection screen (see below) until
   the player confirms a world, at which point `GameLoop` switches to its
   `LoadingWorld` `GameMode` and loads that world's `World2D` via
   `World2D.LoadAsync` (reading settings, background, palette, and every
   object placement, resolving each placement's sprite through
   `SpriteLoader`) - so gameplay never stalls mid-frame waiting on a network
   fetch - snaps the camera immediately onto the world's designated camera
   target (the player by default, or another body if a world opts one in),
   and finally switches to its `Playing` `GameMode`, running the normal
   per-frame gameplay tick from the next frame on.

### Per-Frame Tick

Driven by the browser's `requestAnimationFrame` calling `GameLoop.OnFrame`
once per frame, with the elapsed time since the last frame (clamped to avoid
large jumps after e.g. a tab switch):

1. **Physics** - `PhysicsSystem.Step` applies input to the player's velocity
   and stance/pose, applies gravity to affected bodies, and integrates every
   moving body's position from its velocity.
2. **Collision** - `CollisionSystem.Resolve` resolves overlaps: moving bodies
   against solid terrain and world bounds, moving bodies against each other,
   hazard/collectable contact (including collector pickups and killer/
   killable kill contacts, each of which may spawn a cosmetic
   `EffectInstance2D`), and any `IClimberBody`/`IHangerBody`'s
   `IsTouchingClimbable`/`IsTouchingHangable` overlap against
   `IsClimbable`/`IsHangable` terrain (both speed-gated; hanging additionally
   requires approaching the surface from underneath).
3. **Removal** - `World2D.ApplyPendingRemovals` removes anything queued for
   removal this frame (a picked-up collectable, a killed enemy, an expired
   effect), deferred from the systems above so nothing mutates the object
   list mid-iteration.
4. **Animation** - `AnimationSystem.Update` advances every body's animation
   frame timer and ticks down any active effect's remaining lifetime.
5. **Camera** - `Camera2D.Follow` smoothly scrolls toward the current camera
   target's position, respecting its dead zone and the world's bounds.
6. **Render** - `WorldRenderer.BuildFrame` converts the current world and
   camera view into a flat glyph list - culled to the camera's current
   viewport rect (see Rendering above) - which `CanvasBridge.DrawFrameAsync`
   sends to JavaScript to paint onto the canvas.

### Asset Loading (Global vs. World Fallback)

Sprites, colors, and materials are resolved with a consistent "world
overrides global" rule (see [AssetFormat.md](AssetFormat.md) §1.1 for the
full reference):

- A sprite is loaded from a world-local `Sprites/{AssetName}/` folder if one
  exists there; otherwise from the shared `Global/Sprites/{AssetName}/`
  folder. The mere presence of the world-local folder is the override
  signal - no explicit flag is needed.
- A world's own optional `Colors.ini`/`Materials.ini` is merged over the
  global one, with world entries taking precedence for same-named
  codes/sections, while anything only defined globally still applies.

### Coordinate System

Game entities use floating-point world coordinates ("cells"), not integer
grid indices - the ASCII character grid is a rendering concept applied only
at draw time (via the camera transform), so movement and physics stay smooth
regardless of the grid-based visual language. See
[Architecture.md](Architecture.md#coordinate-system) for the full rationale.
