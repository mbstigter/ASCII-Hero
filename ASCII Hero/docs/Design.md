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
  `Density`/`Friction`/`Restitution` are resolved from a named
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
