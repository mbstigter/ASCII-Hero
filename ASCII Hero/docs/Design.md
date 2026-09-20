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
  colour is a single-character code (`0-9` plus `A-Z`, 36 possible codes)
  looked up in a shared palette (`ColorPalette.ini`, see
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
  no further rendering/asset-format plumbing is needed for that part. The
  underlying ambient-medium physics (buoyancy/drag while immersed in a
  passable `Water`-like volume, `Body2D.CurrentMedium`, `IMediumAffected` -
  see [Decisions.md](Decisions.md)) is already implemented and applies
  generically to any body, the player included, so this item no longer needs
  its own buoyancy math. Still to design/implement when this is picked up:
  - A swim capability on the player (an `ISwimmerBody`-style interface,
    following the existing `IClimberBody`/`IHangerBody` pattern) for
    four-directional swim input/movement while `CurrentMedium` indicates the
    player is submerged, analogous to how climbing resolves `Facing` from
    input directly - built on top of the existing buoyancy/drag forces, not
    replacing them.
  - The `Swim` stance's `[Stances]` line and its five `swim_*` clip assets
    (art + `Player_settings.ini` entries), once the above capability exists.

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
- **Material-Based Collision Response.** Largely done - a body's
  `Density`/`Friction`/`Restitution`/`Mass` are resolved from a named
  material (`MaterialLibrary.ini`, see [Decisions.md](Decisions.md)) rather
  than ad-hoc per-body-type checks, and two contacting bodies' values
  combine generically via `CollisionSystem.Combine`. Still open: any
  material-driven behavior beyond the physical constants themselves (e.g. a
  distinct sound/visual cue per material on contact).
- **`MovingEnemy` behavior.** Linear patrol (back-and-forth along the X axis,
  under its own mass-scaled force - see
  [Decisions.md](Decisions.md)) is now implemented via `IPatrolBody`/`Patrol`
  (see [AssetFormat.md §3.4](AssetFormat.md)), including per-placement
  `PatrolMinX`/`PatrolMaxX` bounds (e.g. the `Enemies` world's `SnakeTwo`
  confined to the platform it starts on, while `SnakeOne` still sweeps the
  full world). Facing/animation while patrolling is also implemented -
  `MovingEnemy2D.UpdatePatrolDirection()` calls `SetPose` each frame so a
  `Snake` visually turns to face (and animate through) its `move_left`/
  `move_right` clips as it changes direction. Grounded and flying varieties
  both already exist (a flying enemy simply opts out of gravity via the
  existing `GravityAffected = false` placement key, used together with
  `Patrol` - e.g. `Butterfly`). Chase behavior is not implemented - still to
  design/implement:
  - Chase-the-player movement.
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
