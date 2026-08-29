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
				Player_idle_chars.txt
				Player_idle_fore.txt
				Player_idle_back.txt
				Player_idle_material.txt
				Player_walk_left_chars.txt
				Player_walk_left_fore.txt
				Player_walk_left_back.txt
			BrickWall/
				BrickWall_settings.ini
				BrickWall_default_chars.txt
				BrickWall_default_fore.txt
				BrickWall_default_back.txt
	Levels/
		Level1/
			Level1_settings.ini
			Level1_background_chars.txt
			Level1_background_fore.txt
			Level1_background_back.txt
			Level1_objects.txt
			Level1_objects.ini
			Colors.ini            (optional, level-specific overrides/additions)
			Materials.ini          (optional, level-specific overrides/additions)
			Sprites/
				ToxicPlant/         (optional, level-specific sprite - a boss unique
									 to this level, for example)
					ToxicPlant_settings.ini
					ToxicPlant_middle_chars.txt
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
lowercase-prefixed, matching the asset/clip name (`Player_idle_chars.txt`).

## 2. Sprite files

Pattern: `{AssetName}_{clipName}_{chars|fore|back|material}.txt`, plus one
`{AssetName}_settings.ini` per asset folder.

- `clipName` defaults to `default` for single-clip static assets (platforms, tiles).
- Animated assets have one set of layer files per clip (`idle`, `walk_left`, ...).
- The loader derives `AssetName` from the containing folder name, and cross-checks
  it against the file name prefixes as a cheap sanity check (fails loudly if a
  folder was renamed but its files weren't, or vice versa).

### 2.1 Layers

| File suffix     | Contents                                                            |
|------------------|----------------------------------------------------------------------|
| `_chars.txt`     | The visible glyph for each cell. Required.                          |
| `_fore.txt`      | Foreground color code per cell (see `Global/Colors.ini`). Optional.  |
| `_back.txt`      | Background color code per cell (see `Global/Colors.ini`). Optional.  |
| `_material.txt`  | Material code per cell (see `Global/Materials.ini`). Optional.      |

All layer files for the same clip share identical dimensions (rows and columns).
Dimensions are never authored explicitly — they're inferred from the content of
`_chars.txt` (its longest line and line count). Lines shorter than the inferred
width are padded on the right with `EmptyChar`, so trailing empty columns never
need to actually be typed out. The same padding applies to missing rows: if
`_fore.txt`, `_back.txt`, or `_material.txt` has fewer lines than `_chars.txt` (or
is missing a file entirely, for the optional layers), the missing rows are treated
as entirely `EmptyChar`.

Multiple **frames** within one clip (a sub-animation, e.g. a subtle 2-frame idle
wobble) are separated by a line containing only `//end`. This is a **frame**
separator, not a clip separator — different clips (`idle`, `walk_left`, ...)
always live in their own separate files (per the naming pattern above) and never
need a delimiter between them, since a clip's files only ever contain that one
clip's frame(s). A single-frame clip (the common case, e.g. `ToxicPlant_middle`)
simply omits `//end` entirely - there is nothing to separate. The frame boundary,
when used, applies at identical line positions across all layer files for that
clip (a `_fore.txt` frame boundary lines up with the corresponding `_chars.txt`
frame boundary).

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
material, excluded from collision) wherever `_chars.txt` contains `EmptyChar` at
that position. Other layer files must also use `EmptyChar` at the same position;
the loader does not currently need distinct empty markers per layer.

### 2.3 Materials — whole-object shorthand and per-cell precision

Both are supported, chosen per-asset:

**Whole-object shorthand** — no `_material.txt` file is needed at all:

```ini
[Physics]
DefaultMaterial = Glass
```

Every non-empty cell of the asset uses `DefaultMaterial`.

**Per-cell precision** — add `{AssetName}_{clipName}_material.txt`, same
dimensions as `_chars.txt`, containing single-character material codes:

```ini
[MaterialCodes]
. = (inherit DefaultMaterial)
R = Rubber
```

```
Player_idle_material.txt
-------------------------
.....
..R..
.....
```

Cells in `_material.txt` marked with `EmptyChar` are skipped (no material, no
collision), matching whatever is empty in `_chars.txt`. Cells present in
`_chars.txt` but using the "inherit" code in `_material.txt` fall back to
`DefaultMaterial`. This lets an object be defined mostly as one material with a
few cells overridden (e.g. a glass ball with a rubber-lined rim).

### 2.4 `settings.ini` sections (per-asset)

```ini
[Layout]
EmptyChar = ' '

[Physics]
DefaultMaterial = Flesh

[MaterialCodes]
. = (inherit DefaultMaterial)
B = Bone
```

## 3. World/level files

Structurally, a world is just another multi-layer grid asset (background layer),
plus an additional object-placement layer.

```
Level1_settings.ini
Level1_background_chars.txt
Level1_background_fore.txt
Level1_background_back.txt
Level1_background_material.txt   (optional)
Level1_objects.txt
Level1_objects.ini
```

- `Level1_background_*` follows the exact same rules as a sprite clip (empty
  cells, optional materials, `//end` frames — though a world background typically
  has a single frame). This layer is purely visual/background terrain (e.g. distant
  mountains); it may optionally carry per-cell materials for background elements
  that are also physically solid (e.g. a rock ledge drawn as background art).
- `Level1_objects.txt` is a **separate** grid, at the same dimensions as
  `Level1_background_chars.txt`, used only to mark where object instances spawn.
  It never overwrites or conflicts with the background layers — both are
  composited independently at load/render time. Its dimensions are **not**
  inferred independently from its own content (most rows may be far shorter than
  the world width, since they typically contain only one or two markers) —
  instead the loader reads `Level1_background_chars.txt` first to establish the
  world's width/height, then reads `Level1_objects.txt` against those same
  dimensions, padding any missing rows/columns with `EmptyChar`.

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
  The object's own sprite dimensions (from its `_chars.txt`) determine its full
  footprint — multi-cell objects do not need repeated markers.
- Empty cells in `Level1_objects.txt` use the same space-is-empty convention as
  every other layer.

### 3.2 `Level1_objects.ini`

```ini
[ObjectCodes]
0 = PlayerSpawn
1 = BrickPlatform
P0 = PlayerSpawn
E0 = Goblin

[PlayerSpawn]
Asset = Player
Clip = idle

[Goblin]
Asset = Goblin
Clip = idle
Facing = Left
```

Each `[ObjectCodes]` entry maps a placement code to a section name, which in turn
specifies which sprite asset/clip to spawn and any additional per-type properties.
Per-instance overrides (e.g. one specific enemy with a custom patrol range) can use
a dedicated numbered code (`E1`) with its own `[E1]` section, falling back to a
shared template section for common properties.

## 4. Global files

### 4.1 `Global/Colors.ini`

Defines the shared color palette referenced by every `_fore.txt`/`_back.txt` file
across all sprites and worlds. (Format finalized when the color system is
implemented — placeholder structure: one section or line per color code mapping to
an RGB value.)

### 4.2 `Global/Materials.ini`

Defines the shared material library, referenced by every `_material.txt` file and
by `DefaultMaterial` in any `settings.ini`.

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

- **Everything is a grid of parallel text-file layers** (chars/fore/back/material)
  plus one `.ini` for metadata — sprites, world backgrounds, and object placement
  all follow this same shape, so there is only one loading concept to implement.
- **Object placement never overwrites visual art.** The placement grid is a
  separate file from the background grid, avoiding the classic "special character
  eats a cell that could otherwise hold background art" problem.
- **Whole-object vs. per-cell precision is a free choice**, not a fork in the
  format — omitting the optional per-cell file simply falls back to a single
  default value from `settings.ini`.
