# ASCII Hero — Asset File Format Reference

This document is the authoritative reference for how sprites, worlds, colors, and
materials are authored as plain text files. Every file described here is meant to be
readable and editable directly in Notepad — no binary formats, no special tooling
required.

## 1. Folder structure

```
Assets/
	Global/
		Settings.ini
		Colors.ini
		Materials.ini
		Sprites/
			Player/
				Player_settings.ini
				Player_walk_idle_characters.txt
				Player_walk_idle_foregroundcolors.txt
				Player_walk_idle_backgroundcolors.txt
				Player_walk_idle_materials.txt
				Player_walk_left_characters.txt
				Player_walk_left_foregroundcolors.txt
				Player_walk_left_backgroundcolors.txt
			BrickWall/
				BrickWall_settings.ini
				BrickWall_default_characters.txt
				BrickWall_default_foregroundcolors.txt
				BrickWall_default_backgroundcolors.txt
	Levels/
		Level1/
			Level1_settings.ini
			Level1_background_characters.txt
			Level1_background_foregroundcolors.txt
			Level1_background_backgroundcolors.txt
			Level1_objects.txt
			Level1_objects.ini
			Colors.ini            (optional, level-specific overrides/additions)
			Materials.ini          (optional, level-specific overrides/additions)
			Sprites/
				BossPlant/           (optional, level-specific sprite - a boss unique
									 to this level, for example)
						BossPlant_settings.ini
						BossPlant_idle_characters.txt
						...
```

- **`Global/`** holds everything shared across every level: the color palette, the
  material library, game-wide default settings, and every sprite that's reusable
  across multiple levels (the player, common enemies, common platform/wall types,
  ...). This is the default place to put a new sprite unless it's genuinely
  one-level-only.
- **`Levels/{LevelName}/`** holds one folder per level, containing that level's own
  background/object-placement files, plus optional level-specific `Sprites/`,
  `Colors.ini`, and `Materials.ini` for anything that only makes sense within that
  one level (a unique boss sprite, a level-specific color not used anywhere else,
  a special material only this level's puzzle needs).

### 1.1 Override/fallback resolution

When loading an asset (sprite, color code, or material code) for a given level, the
loader looks in that level's own `Levels/{LevelName}/` folder **first**, and falls
back to `Global/` only if not found there:

- **Sprites**: `Levels/{LevelName}/Sprites/{AssetName}/` is checked before
  `Global/Sprites/{AssetName}/`. This lets a level override a global sprite (e.g. a
  level-specific reskinned platform) simply by placing a same-named folder locally,
  with no engine-level "override" flag needed - presence of the level-local folder
  is itself the override signal.
- **Colors/materials**: a level's own `Colors.ini`/`Materials.ini` (if present) is
  merged over `Global/Colors.ini`/`Global/Materials.ini` - entries with the same
  code/section name in the level file take precedence; entries only defined
  globally still apply unchanged. A level with no local `Colors.ini`/`Materials.ini`
  simply uses the global ones as-is.

This keeps the common case (everything shared globally) requiring zero extra
files, while still allowing full per-level customization when actually needed.

Naming case convention: global shared files use "first word capitalized" naming
(`Settings.ini`, `Colors.ini`, `Materials.ini`). Per-asset files stay entirely
lowercase-prefixed, matching the asset/clip name (`Player_walk_idle_characters.txt`).

## 2. Sprite files

Pattern: `{AssetName}_{clipName}_{characters|foregroundcolors|backgroundcolors|materials}.txt`,
plus one `{AssetName}_settings.ini` per asset folder.

- `clipName` defaults to `default` for single-clip static assets (platforms, tiles).
- Animated assets have one set of layer files per clip (`walk_idle`, `walk_left`, ...).
- The loader derives `AssetName` from the containing folder name, and cross-checks
  it against the file name prefixes as a cheap sanity check (fails loudly if a
  folder was renamed but its files weren't, or vice versa).

### 2.1 Layers

| File suffix                | Contents                                                            |
|------------------------------|----------------------------------------------------------------------|
| `_characters.txt`           | The visible glyph for each cell. Required.                          |
| `_foregroundcolors.txt`     | Foreground color code per cell (see `Global/Colors.ini`). Optional.  |
| `_backgroundcolors.txt`     | Background color code per cell (see `Global/Colors.ini`). Optional.  |
| `_materials.txt`            | Material code per cell (see `Global/Materials.ini`). Optional.      |

All layer files for the same clip share identical dimensions (rows and columns).
Dimensions are never authored explicitly — they're inferred from the content of
`_characters.txt` (its longest line and line count). Lines shorter than the
inferred width are padded on the right with `EmptyChar`, so trailing empty
columns never need to actually be typed out. The same padding applies to missing
rows: if `_foregroundcolors.txt`, `_backgroundcolors.txt`, or `_materials.txt`
has fewer lines than `_characters.txt` (or is missing a file entirely, for the
optional layers), the missing rows are treated as entirely `EmptyChar`.

Multiple **frames** within one clip are separated by a line containing only
`//end`. This is a **frame** separator, not a clip separator — different clips
(`walk_idle`, `walk_left`, ...) always live in their own separate files (per the
naming pattern above) and never need a delimiter between them, since a clip's
files only ever contain that one clip's frame(s). A single-frame clip (the
common case, e.g. `BrickWall_default`) simply omits `//end` entirely - there is
nothing to separate. The frame boundary, when used, applies at identical line
positions across all layer files for that clip (a `_foregroundcolors.txt` frame
boundary lines up with the corresponding `_characters.txt` frame boundary).

Frames within a clip serve two purposes, both using the exact same
`//end`-separated mechanism:

- **True animation** — frames play back over time when the asset declares animation
  timing via an `[Animation]` section in its settings.ini (see §2.4). Examples:
  `Player_walk_idle` (3 subtle frames cycling to avoid a perfectly static character) and
  an enemy's `idle` clip animating left/middle/right frames. The clip's starting frame
  is controlled asset-wide by `[Animation] DefaultFrame` (see §2.4) — e.g. a
  Left/Center/Right clip can start at the Center frame so `PingPong` mode bounces
  symmetrically (Center, Right, Center, Left, ...).
- **Static shape variants** (when no `[Animation]` section exists) — frames are
  not played back over time at all; a clip simply always renders its
  `DefaultFrame` (or frame 0 if unset), letting one clip describe several
  interchangeable silhouettes of a non-animating object without needing separate
  clips per shape (e.g. a fence post with left-cap/middle/right-cap frames), even
  though only one is ever shown per asset.

The loader does not need to distinguish these cases structurally - whether a
clip's frames animate is determined by the presence of an `[Animation]` section
in the asset's settings.ini.

### 2.2 Empty space

A plain space character (`' '`) means "no cell here" in every layer. This is the
default and covers the common case directly (sprites naturally have blank padding
around their silhouette, e.g. the corners of a round ball's bounding box).

This is overridable per-asset via `settings.ini`, for the rare case a sprite
actually needs literal blank-glyph cells that still physically exist:

```ini
[Layout]
EmptyChar = ' '
```

A cell is "empty" (not part of the object at all — no glyph, no color, no
material, excluded from collision) wherever `_characters.txt` contains `EmptyChar`
at that position. Other layer files must also use `EmptyChar` at the same
position; the loader does not currently need distinct empty markers per layer.

### 2.3 Materials — whole-object shorthand and per-cell precision

Both are supported, chosen per-asset:

**Whole-object shorthand** — no `_materials.txt` file is needed at all:

```ini
[Physics]
DefaultMaterial = Glass
```

Every non-empty cell of the asset uses `DefaultMaterial`.

**Per-cell precision** — add `{AssetName}_{clipName}_materials.txt`, same
dimensions as `_characters.txt`, containing single-character material codes:

```ini
[MaterialCodes]
. = (inherit DefaultMaterial)
R = Rubber
```

```
Player_walk_idle_materials.txt
-------------------------
.....
..R..
.....
```

Cells in `_materials.txt` marked with `EmptyChar` are skipped (no material, no
collision), matching whatever is empty in `_characters.txt`. Cells present in
`_characters.txt` but using the "inherit" code in `_materials.txt` fall back to
`DefaultMaterial`. This lets an object be defined mostly as one material with a
few cells overridden (e.g. a glass ball with a rubber-lined rim).

### 2.4 `settings.ini` sections (per-asset)

```ini
[Layout]
EmptyChar = ' '

[Physics]
DefaultMaterial = Flesh

[Animation]
FrameDurationSeconds = 0.5
Mode = Loop
DefaultFrame = 0

[MaterialCodes]
. = (inherit DefaultMaterial)
B = Bone
```

**`[Layout]`** — controls visual interpretation of layer files:
- `EmptyChar` (default `' '`) — which character in layer files means "no cell here."
- `TileAxis` (default `None`) — whether this asset is a tileable unit (see §2.5).

**`[Physics]`** — controls material assignment:
- `DefaultMaterial` — the material code used for every non-empty cell when no
  per-cell `_materials.txt` file exists, or when a cell in `_materials.txt` uses
  the inherit marker.

**`[Animation]`** (optional) — controls frame-over-time playback for clips with
multiple frames. If omitted, multi-frame clips remain static at frame `DefaultFrame`
(or `0` if that's also unset):
- `FrameDurationSeconds` (required for animation to occur) — how long each frame
  displays before advancing to the next, in seconds. Must be parseable as a
  floating-point number. If absent or unparseable, the clip does not animate.
- `Mode` (default `Loop`) — playback pattern:
  - `Loop`: cycles sequentially, wrapping at the end (0, 1, 2, 0, 1, 2, ...).
  - `PingPong`: bounces back and forth (0, 1, 2, 1, 0, 1, 2, 1, ...).
  - `Once`: advances sequentially like `Loop` (0, 1, 2, ...) but stops and holds
    on the last frame instead of wrapping back to the first. Useful for a
    one-shot transformation that should visibly play through once and then
    stay there (e.g. an enemy's "crumble" clip on being killed, if the object
    persists afterward as a decorative husk) - as opposed to `Off`, which never
    animates at all.
  - `Off`: disables playback entirely — the clip holds forever on `DefaultFrame`
    even though it has multiple frames and `FrameDurationSeconds` is set. Useful
    for an inanimate variant of an otherwise-animated asset that still wants to
    share the same multi-frame art/settings file (e.g. a dead enemy variant that
    should render a fixed pose while a living one animates).
- `DefaultFrame` (default `0`) — the frame index a clip starts at, whether or not
  it animates. Useful for starting an animated Left/Center/Right clip at the
  Center frame so `PingPong` bounces symmetrically, or for a static shape-variant
  clip that should always render a frame other than `0`.

Animation settings are resolved **per clip**, not asset-wide, since clips of the
same asset can have very different frame counts and desired pacing (e.g. a
3-frame idle head-turn bouncing slowly vs. a 2-frame walk cycle looping
quickly). The `[Animation]` section above is the **default** applied to every
clip of the asset; an optional `[Animation.{clipName}]` section overrides any
of its three keys for one specific clip only, falling back to the asset-wide
default for any key it doesn't itself set:

```ini
[Animation]
FrameDurationSeconds = 1.5
Mode = PingPong
DefaultFrame = 1

[Animation.walk_left]
FrameDurationSeconds = 0.2
Mode = Loop
DefaultFrame = 0
```

Here, every clip defaults to the slow `PingPong` idle-style timing, except
`walk_left`, which overrides all three keys for a fast `Loop`ing walk cycle
starting at frame `0`. A genuinely different animation *state* (e.g. dead vs.
alive) still requires a separate asset (its own settings.ini) — per-clip
`[Animation.{clipName}]` overrides one clip's timing, not a per-instance
runtime toggle.

**`[MaterialCodes]`** — maps single-character codes in `_materials.txt` to material
names (see §2.3).

### 2.5 Tileable assets (`TileAxis`)

Some objects — platforms, walls, fences — are naturally repetitive: the same
small unit repeated however many times are needed to reach a given length.
Rather than hand-authoring every possible length as its own asset (or
duplicating cells inside one oversized `_characters.txt`), an asset can declare
itself tileable along one axis via `[Layout] TileAxis` in its `settings.ini`:

```ini
[Layout]
EmptyChar = ' '
TileAxis = Horizontal
```

- **`TileAxis = Horizontal`** — the asset's `_characters.txt` (and matching
  `_foregroundcolors.txt`/`_backgroundcolors.txt`/`_materials.txt`) is authored
  **one cell wide** (any height). A placement's `Repeat` count (see §3.2)
  repeats that single column side-by-side to build up the actual platform
  length at spawn time.
- **`TileAxis = Vertical`** — the mirror case: the asset is authored **one
  cell tall** (any width), and `Repeat` stacks that single row to build up the
  actual wall/column height.
- **`TileAxis = None`** (the default, and the only option when the key is
  omitted) — the asset is not tileable; it is always used exactly as
  authored, `Repeat` has no effect, and every other asset (`Player`, `Ball`,
  ...) is entirely unaffected by this feature.

Tiling happens once, in memory, at spawn time — it repeats the authored unit's
characters/foregroundcolors/backgroundcolors/materials grids to produce a
normal, full-size frame that is otherwise indistinguishable from one that had
been hand-authored at that exact size. Collision shape derivation (§ see
`CollisionShapeBuilder`), rendering, and every other consumer of a loaded frame
work identically either way; only the small authored asset files on disk and
the loading step differ.

`SteelPlatform` and `BrickWall` are the reference tileable assets: `SteelPlatform`
is authored one cell wide with `TileAxis = Horizontal`, `BrickWall` is authored
one cell tall with `TileAxis = Vertical`.

### 2.6 Stances and facing (`[Stances]`)

Some assets — the player, and later any NPC that visibly changes posture or
direction — need more than one whole-body pose, each of which can also face
`Idle` (facing the viewer), `Left`, or `Right`. This is a second, orthogonal
axis on top of everything in §2.1-§2.5: whichever clip is currently selected
for the active stance/facing combination still authors its own frames,
`EmptyChar`, materials, and its own resolved `[Animation]`/`[Animation.{clipName}]`
timing exactly as described above — a stance/facing pair is simply *which
clip* is currently active, not a new kind of clip content.

Declared via an optional `[Stances]` section in `settings.ini`:

```ini
[Stances]
Default = Walk
Walk = walk_idle, walk_left, walk_right
Crawl = crawl_idle, crawl_left, crawl_right
```

- Each non-`Default` key names a stance (`Walk`, `Crawl`, ...); its value is a
  comma-separated list of clip names in the order **`Idle, Left, Right`**
  (matching the facing enum order the engine uses). `Left`/`Right` are
  optional — a line with only one clip name means that stance always shows
  the same clip regardless of facing (e.g. a stance with no meaningful
  sideways pose).
- `Default` names the stance active at spawn (required whenever `[Stances]`
  is present at all).
- Every clip named here is an ordinary clip following the normal
  `{AssetName}_{clipName}_*` file-naming convention (§2) - `[Stances]` only
  adds grouping metadata on top of clips that already exist; it introduces no
  new file-naming rule.
- `[Stances]` is entirely optional. Assets that don't declare it behave
  exactly as before: whatever single clip a placement/spawn requests (e.g.
  `Clip = idle` in a level's `objects.ini`) is the only clip ever shown, with
  no stance/facing switching at all.
- Frame counts are independent per clip - a stance's `Idle` clip can have three
  frames (e.g. a subtle idle head-turn) while its `Left`/`Right` clips have
  two frames each (e.g. a walk cycle), or one frame (a static crouched pose).
  Nothing about `[Stances]` requires matching frame counts across facings or
  across stances.
- Switching stance/facing at runtime re-derives the object's size and
  collision shape from whichever clip's frame is now active, the same way any
  other clip/frame switch already does - a stance with a different silhouette
  (e.g. a shorter `Crawl` pose) is picked up automatically, with no special
  pre-transition validation needed anywhere in the format or the loader.

Note the two different orderings used by this format, both intentional:
**`[Stances]` clip lists are authored `Idle, Left, Right`** (matching the
facing enum), while **a single clip's own multi-frame `_characters.txt` for a
Left/Idle/Right-style animation (e.g. `Player_walk_idle`'s head-turn) is authored
`Left, Idle, Right`**, since that is the natural sequence for a symmetric
`PingPong` head-turn to bounce through.

## 3. World/level files

Structurally, a world is just another multi-layer grid asset (background layer),
plus an additional object-placement layer.

```
Level1_settings.ini
Level1_background_characters.txt
Level1_background_foregroundcolors.txt
Level1_background_backgroundcolors.txt
Level1_background_materials.txt   (optional)
Level1_objects.txt
Level1_objects.ini
```

- `Level1_background_*` follows the exact same rules as a sprite clip (empty
  cells, optional materials, `//end` frames — though a world background typically
  has a single frame). This layer is purely visual/background terrain (e.g. distant
  mountains); it may optionally carry per-cell materials for background elements
  that are also physically solid (e.g. a rock ledge drawn as background art).
  **Not yet implemented**: `World2D.LoadAsync` does not currently read
  `Level1_background_materials.txt` or expose any per-cell background material
  data — this is a documented design intent for a future alternative to
  defining solid terrain via `_objects.ini`, not a working feature today. Omit
  the file until this is implemented.
- `Level1_objects.txt` is a **separate** grid, at the same dimensions as
  `Level1_background_characters.txt`, used only to mark where object instances
  spawn.
  It never overwrites or conflicts with the background layers — both are
  composited independently at load/render time. Its dimensions are **not**
  inferred independently from its own content (most rows may be far shorter than
  the world width, since they typically contain only one or two markers) —
  instead the loader reads `Level1_background_characters.txt` first to
  establish the world's width/height, then reads `Level1_objects.txt` against
  those same dimensions, padding any missing rows/columns with `EmptyChar`.

### 3.1 Object placement codes

- A **single digit `0`-`9`** is the default, simplest code and unambiguously
  occupies one grid cell (solves the "adjacent 1x1 objects" case, since no
  multi-character parsing is needed).
- When more than 10 object types are needed in one level, extend to
  **uppercase-letter + digit** (`A0`-`Z9`). A 2-character code still represents a
  single logical grid cell — it is read as one placement token even though it
  visually spans two characters in the `.txt` file. This is an accepted minor
  readability tradeoff in exchange for keeping the placement grid aligned 1:1
  with the background grid's dimensions.
- Leading-letter groupings (e.g. `P` = player/spawn, `E` = enemy, `M` = moving
  object, `S` = static/scenery) are a **level-author convention only**. The
  engine treats every code as an opaque lookup key into `[ObjectCodes]` — it does
  not parse or assign meaning to the letter prefix itself.
- A marker only needs to appear once, at an object's anchor cell (its top-left).
  The object's own sprite dimensions (from its `_characters.txt`) determine its
  full footprint — multi-cell objects do not need repeated markers.
- Empty cells in `Level1_objects.txt` use the same space-is-empty convention as
  every other layer.

### 3.2 `Level1_objects.ini`

```ini
[ObjectCodes]
0 = Player
1 = BrickPlatform
P0 = Player
E0 = Goblin

[Player]
Asset = Player
Clip = walk_idle
Kind = Player

[Goblin]
Asset = Goblin
Clip = idle
Kind = MovingEnemy
Facing = Left
```

Every placement section requires two keys: `Asset` (which sprite asset to spawn)
and `Kind` (which of the game's object categories it spawns as - see below).
Both are validated at load time; a section missing either throws a load error
rather than silently spawning something unintended. `Clip` (default `"default"`)
selects which clip within the asset to display/collide against.

Each `[ObjectCodes]` entry maps a placement code to a section name, which in turn
specifies which sprite asset/clip to spawn and any additional per-type properties.
Per-instance overrides (e.g. one specific enemy with a custom patrol range) can use
a dedicated numbered code (`E1`) with its own `[E1]` section, falling back to a
shared template section for common properties.

There is no placement-time frame-selection key — a clip's starting/static frame is
always controlled asset-wide via `[Animation] DefaultFrame` in the asset's own
settings.ini (see §2.4), not per-placement. This keeps starting frame a purely
asset-level concern rather than something every placement needs to repeat.

An optional `Repeat` key (default `1`) selects how many times a tileable
asset's authored unit (see §2.5, `TileAxis`) is repeated along its declared
axis for this placed instance. It has no effect on non-tileable assets. This
lets one small authored unit produce platforms/walls of whatever length each
placement actually needs, without hand-authoring a separate asset per length:

```ini
[SteelPlatform]
Asset = SteelPlatform
Clip = default
Kind = StaticObject
Repeat = 8

[BrickWall]
Asset = BrickWall
Clip = default
Kind = StaticObject
Repeat = 6
```

A `Kind` value of `DynamicObject` marks a placement as a body that moves
under its own velocity/gravity (a `DynamicObject2D`, e.g. a bouncing ball),
reading `GravityAffected` (default `true`), `Restitution` (default `1.0`),
and `InitialVelocityX`/`InitialVelocityY` (default `0`) to configure its
initial motion:

```ini
[BouncingBall]
Asset = Ball
Clip = default
Kind = DynamicObject
GravityAffected = false
Restitution = 1.0
InitialVelocityX = 14
InitialVelocityY = 10
```

`Kind` is a **mandatory** key for every placement - there is no default
category and no separate `Static` key to fall back on. Each value corresponds
exactly to a concrete body class name with the `2D` suffix dropped (e.g.
`Kind = MovingEnemy` spawns a `MovingEnemy2D`, `Kind = StaticObject` spawns a
`StaticObject2D`), so the mapping is discoverable directly from the codebase
rather than needing to be memorized separately. Valid values: `Player` (the
player's spawn point and sprite - exactly one placement per level should use
this), `StaticObject` (immovable terrain, e.g. a platform), `DynamicObject`
(moves under its own velocity/gravity, e.g. a bouncing ball),
`KinematicObject` (moves at a constant, predefined velocity - never affected
by gravity, restitution, or `GravityAffected`/`Restitution` keys),
`MovingEnemy` (an AI-controlled hazard that moves like a `DynamicObject` and
damages the player on contact), `StaticEnemy` (a non-moving hazard, e.g.
spikes), and `Collectable` (a non-solid, non-moving item removed from the
world when the player contacts it). `KinematicObject`/`MovingEnemy`/
`StaticEnemy`/`Collectable` placements read the same
`GravityAffected`/`Restitution`/`InitialVelocityX`/`InitialVelocityY` keys as
`DynamicObject` where applicable (`KinematicObject` ignores
`GravityAffected`/`Restitution` entirely, `StaticEnemy`/`Collectable` ignore
all of them since they never move):

```ini
[Coin]
Asset = Coin
Clip = default
Kind = Collectable

[SpikeTrap]
Asset = Spikes
Clip = default
Kind = StaticEnemy

[PatrolGoblin]
Asset = Goblin
Clip = idle
Kind = MovingEnemy
InitialVelocityX = 4
```

Any placement (the `Player` section, or a non-static object) may also set
`CameraTarget = true` to have the camera follow that body instead of the
player. If no placement sets it, the camera defaults to following the player;
if more than one does, the last one loaded (in top-to-bottom, left-to-right
grid scan order) wins:

```ini
[BouncingBall]
Asset = Ball
Clip = default
Kind = DynamicObject
CameraTarget = true
```

Any non-`Player` placement may also set `Passable`, `Climbable`, and/or
`Hangable` (each default `false`, except `Passable` which defaults to `true`
for `Kind = StaticEnemy`/`Collectable`) to control how it interacts with
moving bodies, independent of its `Kind`:

- **`Passable`** — if `true`, the object never blocks movement even though it
  is otherwise solid terrain (e.g. a wall placement used as a level design
  "secret passage"). `StaticEnemy`/`Collectable` default to `true` since
  neither has ever blocked movement (a hazard is meant to be walked into, a
  collectable is picked up on contact) — this default simply keeps that
  existing behavior data-driven instead of hardcoded by `Kind`.
- **`Climbable`** — if `true`, the player can climb straight up/down while
  overlapping this object (e.g. a ladder), with gravity suspended for the
  duration. Usually paired with `Passable = true` so the object doesn't also
  block horizontal movement.
- **`Hangable`** — if `true`, the player can hang from and move laterally
  along this object while overlapping it from below (e.g. a pipe/bar), with
  gravity mostly suspended for the duration. Usually paired with
  `Passable = true` for the same reason as `Climbable`.

```ini
[Ladder]
Asset = Ladder
Clip = default
Kind = StaticObject
Passable = true
Climbable = true

[HangingPipe]
Asset = Pipe
Clip = default
Kind = StaticObject
Passable = true
Hangable = true
```


## 4. Global files

### 4.1 `Global/Colors.ini`

Defines the shared color palette referenced by every `_foregroundcolors.txt`/
`_backgroundcolors.txt` file across all sprites and worlds. (Format finalized
when the color system is implemented — placeholder structure: one section or
line per color code mapping to an RGB value.)

### 4.2 `Global/Materials.ini`

Defines the shared material library, referenced by every `_materials.txt` file
and by `DefaultMaterial` in any `settings.ini`.

```ini
[Glass]
Density = 2.5
Friction = 0.4
Restitution = 0.1

[Rubber]
Density = 1.1
Friction = 0.9
Restitution = 0.8

[Steel]
Density = 7.8
Friction = 0.3
Restitution = 0.3

[Air]
Density = 0.0012
Friction = 0.0
Restitution = 0.0

[Water]
Density = 1.0
Friction = 0.05
Restitution = 0.0
```

Fields:
- **Density** — relative mass per world-cell "volume"; drives mass and buoyancy.
- **Friction** — `0` = frictionless, `1` = very grippy.
- **Restitution** — bounciness; `0` = no bounce, `1` = perfectly elastic.

### 4.3 `Global/Settings.ini`

Game-wide defaults that apply unless overridden by a more specific per-asset
`settings.ini` (e.g. default gravity, default empty-char). Extended as new
game-wide concepts emerge.

## 5. Design rationale summary

- **Everything is a grid of parallel text-file layers**
  (characters/foregroundcolors/backgroundcolors/materials) plus one `.ini` for
  metadata — sprites, world backgrounds, and object placement all follow this
  same shape, so there is only one loading concept to implement.
- **Object placement never overwrites visual art.** The placement grid is a
  separate file from the background grid, avoiding the classic "special character
  eats a cell that could otherwise hold background art" problem.
- **Whole-object vs. per-cell precision is a free choice**, not a fork in the
  format — omitting the optional per-cell file simply falls back to a single
  default value from `settings.ini`.
