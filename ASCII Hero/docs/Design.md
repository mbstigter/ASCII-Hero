# Game Design

## Vision

AsciiHero is a retro browser-based platform game that looks and feels like an
animated ASCII/text-mode game, while providing smooth, modern movement and
scrolling. The game should feel deliberately constrained by ASCII aesthetics,
not like a pixel-art game with ASCII characters placed on top.

## Visual Style

- Pure ASCII/text-mode visual language.
- Monospaced glyphs form the game world (e.g. `@`, `#`, `█`, `░`, `│`, `─`).
- Colour is part of the ASCII visual language. Each cell's foreground/background
  colour is a single-character code (`0-9` plus `A-V`, 32 possible codes)
  looked up in a shared palette (`Colors.ini`, see
  [AssetFormat.md](AssetFormat.md)). Codes are allocated mnemonically and
  added as needed (e.g. `K`=blacK, `W`=White, `R`=Red) rather than filled in
  sequentially — only as many as are actually used need to be defined.
- The visual grid is a rendering concept only — it does not restrict movement
  or physics (see [Architecture.md](Architecture.md#coordinate-system)).
- Sprites and levels are authored as plain-text ASCII assets; see
  [AssetFormat.md](AssetFormat.md) for the file format reference.
- Idle characters/objects are not required to be perfectly static: a clip may
  define multiple frames purely for subtle animation (e.g. the player
  occasionally blinking while standing still), reusing the same frame
  mechanism also used for static shape variants (see
  [AssetFormat.md](AssetFormat.md#21-layers)).

## Gameplay

- 2D platforming with responsive player movement.
- Gravity and jumping.
- Platform collision.
- Smooth horizontal and vertical camera scrolling.

## Experience Goals

- Retro computer/terminal aesthetic.
- Smooth, responsive controls.
- Clear visual hierarchy.
- Strong sense of movement despite the text-based rendering.
- Consistent ASCII aesthetic throughout the game.

## Planned / Future Work

- **Swim stance.** The stance/facing system (see
  [AssetFormat.md §2.6](AssetFormat.md) and
  [Decisions.md](Decisions.md#-stances-facing-is-resolved-from-each-clip-names-own-suffix-not-a-fixed-slot-positionflag))
  already supports a stance declaring all four directions plus idle via clip
  suffixes (`swim_idle`, `swim_left`, `swim_right`, `swim_up`, `swim_down`) -
  no further rendering/asset-format plumbing is needed for that part. Still
  to design/implement when this is picked up:
  - A water volume/trigger concept in the level format (or reuse of an
    existing object type) so `CollisionSystem`/`World2D` can detect the
    player entering/leaving water.
  - A swim capability on the player (an `ISwimmerBody`-style interface,
    following the existing `IClimberBody`/`IHangerBody` pattern) plus
    `PhysicsSystem` logic for buoyancy/four-directional swim movement,
    analogous to how climbing resolves `Facing` from input directly.
  - The `Swim` stance's `[Stances]` line and its five `swim_*` clip assets
    (art + `Player_settings.ini` entries), once the above physics exists.
