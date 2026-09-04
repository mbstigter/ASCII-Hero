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

- **Hang jump/swing debounce clears too late.** `IHangerBody.SuppressHangUntilClear`
  (see [Decisions.md](Decisions.md)) now correctly keeps a jump/swing off a
  pipe/rope from being instantly cancelled, but it isn't released again until
  the player fully clears the hangable surface's overlap - for a modest jump
  arc (e.g. swinging up to a pipe one character above, or sideways onto an
  adjacent platform/wall) that point in the arc comes later than intended, so
  the player can't yet snap onto a new hangable/solid surface reached mid-arc
  as readily as ladder jump-off allows. Needs a mechanism closer to ladder's
  (where the debounce is released on landing as well as loss of overlap) -
  still to be designed.
- **Material-Based Collision Response.** Move surface-dependent collision
  behavior (bounciness, friction, etc.) onto a per-material concept (see
  `Materials.ini`) instead of ad-hoc per-body-type checks in `CollisionSystem`.
- **Force-Based Movement (non-player first).** Move `DynamicObject2D`/
  `MovingEnemy2D` movement onto a force/acceleration model rather than direct
  velocity assignment, ahead of doing the same for the player (see the
  existing `TODO` in `PhysicsSystem.Step`).
- **Force-Based Movement for the player.** Once the non-player groundwork
  above exists, revisit player movement (currently driven directly by
  velocity assignment from input - see the existing `TODO` in
  `PhysicsSystem.Step`) to apply movement as a force causing acceleration
  instead, consistent with the rest of the physics model.
- **`MovingEnemy` behavior.** Patrol/chase movement is not yet implemented
  (see `MovingEnemy2D`'s doc comment) - still to design/implement:
  - Animated sprites.
  - Movement paths.
  - Grounded and flying varieties.
- **`KinematicObject` behavior.** Beyond constant-velocity motion, still to
  design/implement:
  - Sprites no different from `StaticObject`.
  - Movement paths.
- **Enhance sprites**, especially Crawl and Clamber, which currently feel
  underdeveloped compared to Walk/Hang - consider adding a third animation
  frame for these (and other) animated clips.
- **Single per-body `EffectClipName` may not scale.** `IEffectTrigger` (see
  `CollisionSystem.ResolveHazardsAndCollectables`) currently exposes one
  static clip name per body, e.g. the player's is reserved for an ordinary
  (non-fatal) hazard contact "spark". That's fine while hazard contact is the
  only situation triggering a player effect, but if more situations are added
  later (e.g. fall damage, a death animation, a power-up flash) they would
  all compete for the same single clip slot and overwrite each other. If that
  happens, revisit this as an effect *request* (e.g. a method call or queued
  clip name per contact/event) rather than a static per-body property.
