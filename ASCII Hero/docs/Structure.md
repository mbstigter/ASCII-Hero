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
  game code, static assets (sprites, levels, fonts), and the small amount of
  JavaScript interop the game needs. The entire game runs client-side inside
  this project once the page loads.

All gameplay code lives under `ASCII Hero.Client/Game/`, organized into one
folder per subsystem, described below in roughly the order data flows through
them at startup and each frame.

## Subsystems

### Assets

Everything to do with reading the plain-text asset format (sprites, levels,
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
  Knows nothing about folders or the Global/Level fallback rule.
- **`AssetPathResolver`** - resolves which folder (`Levels/{LevelName}/Sprites/{Asset}`
  or `Global/Sprites/{Asset}`) a sprite's files should be read from, applying
  the "level-local folder overrides global" rule: presence of a level-local
  settings file is itself the override signal, no explicit flag needed.
- **`ColorPalette`** - the resolved single-character color code -> CSS color
  lookup, merging a level's own optional `Colors.ini` over `Global/Colors.ini`.
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

### World

The live game state and the entity types that make it up.

- **`World2D`** - holds everything that makes up the current game state: the
  player, every other body in one generic `Objects` list, the background
  layer, the resolved color palette, gravity, world dimensions, and which
  body the camera should follow. `World2D.LoadAsync` is the level loader: it
  reads a level's settings, background, and object-placement files, resolves
  each placement's sprite and concrete body type, and assembles the finished
  world. Also owns deferred removal (`QueueRemoval`/`ApplyPendingRemovals`),
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
	and `IsClambering` (regular fully-stretched hang vs. a compact clamber/
	shimmy pose that fits through narrower spaces).

  positions and velocities. World coordinates are continuous cells, not
  pixels or integer grid indices - see Coordinate System below.

### Physics

- **`PhysicsSystem`** - applies player input to horizontal velocity and
  jumping, applies gravity to any `IGravityAffected` body, and integrates
  position from velocity for every `IPhysicsBody` each frame. Also resolves
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
  dropping. `Up` also doubles as a `Jump` trigger (see
  `InputState.IsUpPressed`/`IsJumpPressed` below), but every call site gives
  Up's directional/posture meaning priority whenever one applies for the
  current stance, so a plain Up press from `Hang` always pulls into
  `Clamber` first and never accidentally swings off, and a plain Up press
  while climbing always keeps climbing rather than letting go - only a jump
  press with Up *not* held (i.e. just Space/`ControlLeft`) actually
  jumps/lets go in either case. Instead of Up/Down, a Jump press from `Hang`
  swings/jumps off with an upward-plus-current-lateral-velocity impulse (not
  available from `Clamber`), and a Jump press while climbing likewise lets
  go of the ladder and launches upward. All three launch impulses (ground
  jump, climb jump-off, hang jump-off) use their own progressively weaker
  speed constant, reflecting how much "grip"/momentum each stance has behind
  it. While climbing, vertical movement is locked to a
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
  `IsClimbable`/`IsHangable` terrain. Both climbing and hanging touch checks
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
  target with no smoothing (used once at level load); `Follow` smoothly
  catches up each frame afterward.

### Rendering

- **`AsciiRenderer`** - translates the floating-point game world into a flat
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
  `IsJumpPressed`, etc.), so gameplay code never depends on raw DOM key
  codes. Two full, independent key sets are supported for local
  co-op/preference - "Player 1" (arrow keys + `Space`) and "Player 2"
  (`WASD` + `Left Ctrl`) - rather than one shared movement set plus one
  shared jump key. `IsJumpPressed` includes `IsUpPressed` (Up also jumps,
  matching arrow-key/WASD platformer convention), in addition to each key
  set's own dedicated jump key (`Space`/`ControlLeft`) - see `PhysicsSystem`
  above for how each stance's own directional meaning of Up (climb, pull
  into `Clamber`, stand up) is still given priority over its jump meaning
  wherever both could otherwise apply to the same press.

### Game Loop

- **`GameLoop`** - ties every subsystem above together and drives them once
  per animation frame. Owns one instance of each system (input, physics,
  collision, camera, renderer, animation) and one loaded `World2D`. Driven
  entirely by JavaScript's `requestAnimationFrame` calling back into C# -
  never by Blazor's `StateHasChanged`.

## Key Flows

### Startup

1. The Blazor host (`ASCII Hero` project's `Program.cs`) configures the web
   app, registers an `HttpClient` for prerendering, and maps the root
   component with WebAssembly interactive rendering.
2. Once the WebAssembly runtime takes over in the browser, the client
   project's `Program.cs` registers its own `HttpClient` (used by
   `HttpAssetFileProvider` to fetch asset files as static web content).
3. The hosting page creates a `GameLoop` and calls `StartAsync`, which:
   - Loads the level's `World2D` up front via `World2D.LoadAsync` (reading
	 settings, background, palette, and every object placement, resolving
	 each placement's sprite through `SpriteLoader`) - so gameplay never
	 stalls mid-frame waiting on a network fetch.
   - Initializes `CanvasBridge`, which sets up the canvas and reports back
	 the real measured pixel size of one glyph cell for the active font.
   - Snaps the camera immediately onto the level's designated camera target
	 (the player by default, or another body if a level opts one in).

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
6. **Render** - `AsciiRenderer.BuildFrame` converts the current world and
   camera view into a flat glyph list - culled to the camera's current
   viewport rect (see Rendering above) - which `CanvasBridge.DrawFrameAsync`
   sends to JavaScript to paint onto the canvas.

### Asset Loading (Global vs. Level Fallback)

Sprites, colors, and materials are resolved with a consistent "level
overrides global" rule (see [AssetFormat.md](AssetFormat.md) §1.1 for the
full reference):

- A sprite is loaded from a level-local `Sprites/{AssetName}/` folder if one
  exists there; otherwise from the shared `Global/Sprites/{AssetName}/`
  folder. The mere presence of the level-local folder is the override
  signal - no explicit flag is needed.
- A level's own optional `Colors.ini`/`Materials.ini` is merged over the
  global one, with level entries taking precedence for same-named
  codes/sections, while anything only defined globally still applies.

### Coordinate System

Game entities use floating-point world coordinates ("cells"), not integer
grid indices - the ASCII character grid is a rendering concept applied only
at draw time (via the camera transform), so movement and physics stay smooth
regardless of the grid-based visual language. See
[Architecture.md](Architecture.md#coordinate-system) for the full rationale.
