# AsciiHero — Feature Overview

AsciiHero is a browser-based retro platform game engine built with C# and .NET 10 (Blazor WebAssembly).
Everything you see is ASCII, but movement, physics and scrolling are smooth and modern.

This page is a tour of what the engine lets you build. For exact syntax, see
[AssetFormat.md](AssetFormat.md). For internals, see [Structure.md](Structure.md) and
[Architecture.md](Architecture.md).

## Why you might like it

- **Everything is plain text.** Sprites, levels, colors, materials and sounds are `.txt`/`.ini`
  files that you can edit in Notepad. There are no binary formats, no editor tools and no
  recompiling.
- **Data-driven.** Most new content is a new file, not new code.
- **Smooth, not blocky.** The ASCII grid is only how the game is drawn. Physics and movement use
  continuous coordinates.
- **Runs anywhere a browser does.** It is published as a static site, with no server needed.

## Level design

- **Freely sized worlds** with smooth horizontal and vertical camera scrolling.
- **Layered level files.** A background layer is drawn in characters, foreground colors and
  background colors. A separate object layer places things in the world.
- **Simple placement codes.** Put a single-character code in the object grid, such as a digit
  (`0`-`9`) or a letter (`A`-`Z`), and map each code to an object type in the level's
  `objects.ini`. A marker is needed only at an object's anchor cell, however large the object
  is.
- **Reusable object types.** A world can define up to 36 object types (one per code), and each
  can be placed any number of times. Every placement shares its type's tuning. Give one
  instance its own code to make it special, for example a faster enemy.
- **Tileable pieces.** Author one small unit and use `Repeat` to build platforms and walls of any
  length.
- **Materials-only objects.** Declare a `Material`, `Width` and `Height` to get a solid block
  with no sprite files.
- **Per-placement overrides.** Reuse one sprite with a different material, colors or glyph for
  each placement. `Material`, `Friction`, `Restitution` and `Density` can each be overridden
  independently.
- **Behavior flags on any object.** `Passable` makes solid-looking terrain walk-through, for
  example a secret passage. `Climbable` makes a ladder. `Hangable` makes a pipe or bar. Combine
  them freely.
- **Configurable display.** `Settings.ini` sets the font, cell size and viewport size in
  columns and rows.
- **Camera control.** The camera follows the player by default. Mark any body with
  `CameraTarget = true` to follow that instead.
- **World-selection screen.** Each world can ship an optional (animated) ASCII thumbnail and a
  title. Worlds are listed in `Worlds.ini`.
- **Per-world overrides.** A world can override or extend sprites, the color palette, materials
  and sounds. It needs only its own files, and everything else falls back to `Global/`.

## Object types (`Kind`)

| Kind | What it is |
|---|---|
| `Player` | Spawn point and hero sprite |
| `StaticObject` | Immovable terrain such as platforms and walls |
| `DynamicObject` | Moves under gravity or its own velocity, for example a bouncing ball |
| `KinematicObject` | Moves on a predefined path and is never affected by gravity, so it suits moving platforms. It can bounce between bounds on X and/or Y at a set `PatrolSpeedX`/`PatrolSpeedY`, and riders are carried along |
| `StaticHazard` | Non-moving danger such as spikes or toxic plants |
| `DynamicHazard` | A hazard that moves, such as a patrolling creature or a flying butterfly |
| `KinematicHazard` | A hazard on a predefined path (constant velocity or bounded patrol), such as a saw blade or laser |
| `Collectable` | A non-solid pickup with a configurable effect |

### Collectables (`Type`)

- `Diamond` adds 1 to the score (diamonds).
- `Ring` counts a ring and adds 5 to the score; every 25 rings (per world) give an extra life. Does not heal.
- `Checkpoint` sets the player's respawn point.
- `LevelEnd` finishes the level.
opens the gate with
  the same `Lock` (see below).

Add `EffectClip` to play an animation on pickup. Add `EffectPersists = true` to leave the effect
behind as a permanent "already used" marker, for example a lit checkpoint.

### Enemies and moving hazards

- **Patrol behavior** with `Patrol = true`, using force-based movement.
- **Bounded patrol ranges** with `PatrolMinX`/`PatrolMaxX` and `PatrolMinY`/`PatrolMaxY`.
  X and Y can be combined for diagonal patrols. If you set no range, the hazard patrols the
  full world width.
- **Tunable feel** with `PatrolForceMultiplierX`/`Y`, cruise speed keys and
  `PatrolMaxForceX`/`Y` force caps.
- **Flying enemies** by giving a hazard vertical force with `PatrolMaxForceY`; gravity stays on.
- **Automatic facing.** Hazards turn to face their direction of travel and play their
  `move_left`/`move_right` clips.
- **Stompable enemies.** A killable hazard can play a `KillSound` when the player squashes it by
  landing on top. Its `Sound` plays instead when it hurts the player, so a single contact plays
  only one of the two.

## Sprites and animation

- **Layered sprite files.** Each clip has characters, foreground colors, background colors and
  materials. Sizes are inferred from the content.
- **Multiple clips per sprite**, such as `idle`, `walk_left` and `walk_right`.
- **Multi-frame animation**, with frames separated by `//end` in the file.
- **Playback modes:** `Loop`, `PingPong` and `Once`, with configurable frame duration and start
  frame.
- **Idle animation**, so characters need not be static. For example, a player can blink while
  standing.
- **Static shape variants.** Frames without animation can describe interchangeable silhouettes.
- **Stance and pose system.** A sprite can define Walk, Crawl, Climb, Hang, Clamber and Swim
  poses, with facing-aware clips.
- **Clip folders.** Complex sprites can group clips into per-stance subfolders.
- **Effect clips.** Play effect animations for events such as taking a hit or reaching a
  checkpoint.

## Color

- **Colors are part of the ASCII language.** Each cell has a foreground and a background color.
- **One-character color codes** (`0`-`9`, `A`-`Z`) map to a shared palette in `ColorPalette.ini`.
  A preview page is included.
- **Layered defaults:** a per-cell code, then an asset default, then a world default, then an
  engine fallback. A sprite can therefore be one color with no color files at all.
- **World palettes.** A world can add or recolor codes, for example icy blues for a winter level.

## Materials and physics

- **Named materials** in `MaterialLibrary.ini` define `Density`, `Friction` and `Restitution`,
  plus an optional glyph and colors.
- **Whole-object materials.** Set a `DefaultMaterial` per sprite; combine materials by placing
  separate objects.
- **Realistic contact response.** Bounce and friction come from both bodies' materials combined.
  Slick ice and grippy rubber behave differently.
- **Moving platforms carry riders.** Friction drags whatever stands on a moving body.
- **Fluids and media.** Bodies in `Water`-like materials get buoyancy and quadratic drag.
  Swimming through a viscous medium is harder than walking on land.
- **Tunable per placement:** `Restitution`, `GravityAffected`, and `InitialVelocityX`/`Y`.
- **Custom materials.** Add your own, for example a low-friction `Ice`, to the global library
  or to a single world.

## Player abilities

- Walk and crawl, with configurable `WalkSpeed`, `CrawlSpeed` and `WalkForceMultiplier`.
- Jump, with a different launch strength when jumping from the ground, a ladder or a hanging
  surface.
- **Climb** ladders, from any side including mid-air, with sideways step-off.
- **Hang and shimmy** from pipes and ropes. Pull up into a compact clamber pose, swing off with
  a jump, or let go.
- **Swim** through water and other fluids.
- Lives, a health bar with per-hazard damage, rings, respawn and checkpoints.

Climbable and hangable surfaces are ordinary terrain objects marked with `Climbable = true` or
`Hangable = true` in their `objects.ini` section. They are usually also marked
`Passable = true`.

## Sound

- **Synthesized audio.** No audio files are needed. Sounds are described in `SoundLibrary.ini`
  with a waveform (`Square`, `Triangle`, `Sawtooth`, `Sine` or `Noise`), a pitch slide, a
  duration, a volume, an attack and an optional vibrato.
- **Reference by name** from an object's `Sound` and `KillSound` keys or from a sprite's
  `[Sounds]` section. On a hazard, `Sound` plays when it hurts the player and `KillSound` when
  the player squashes it. On a collectable, `Sound` plays when it is picked up.
- **Per-pose footstep sounds.** `[Sounds]` maps a pose (`Walk`, `Crawl`, `Climb`, `Hang`,
  `Clamber`, `Swim`) to a sound played on each animation frame step. `JumpStart` and `Land`
  play when the player leaves and returns to the ground.
`DiamondPickup`, `RingPickup`, `KeyPickup`, `DoorOpen`, `Checkpoint`, `LevelEnd`,
  `Squash`, `Jump`, `Land`, `Step`, `SoftStep`, `Grab`, `Splash`, `MenuMove` and `MenuSelect` (subtle sounds for moving through the world selection menu and choosing a world).
- **Per-world sound overrides.** A world can replace or add sounds with its own
  `SoundLibrary.ini`.
- **Global switches.** `Settings.ini` has `Enabled` and `MasterVolume` for sound.
- **Silence is a valid choice.** A sound that is not named is never played.

## Authoring workflow

plus optional color layers.
2. Describe it in `{Asset}_settings.ini`.
3. Give it a code in your level's `objects.ini`.
4. Type the code into `objects.txt`.
5. Reload the browser.

## Under the hood

- Game logic is independent of the Blazor UI. The real-time loop does not use component
  rendering.
- JavaScript interop is minimal and isolated.
- It uses no game engines or third-party packages.
- Assets are loaded over HTTP and fall back from world to global by asset name.

## Known limitations and planned work

- Hazards have no chase behavior yet.
- Kinematic objects move at constant velocity or bounce between bounds. Arbitrary movement
  paths are not supported yet.
- Buttons and levers are operated only by the player; enemies cannot flip them yet.
- Respawn has no invulnerability window.

See [Design.md](Design.md#planned--future-work) for the full list.
