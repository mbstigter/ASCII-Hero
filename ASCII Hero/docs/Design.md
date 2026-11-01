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
  added as needed rather than filled in
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
- Ladders and pipes can move (along a path); conveyors and ropes (horizontal
  and vertical) have a moving surface. The player holds on by grip and is
  carried along. When a conveyor/rope carries the player past its end, the grip simply
  ends and the player falls - there is no automatic dismount; levels place a
  floor or ledge at the end where a landing is intended.
- Stacks of movable objects cost substantially less once they have come to rest. While the
  player (or anything else that keeps moving) stands on a stack, the whole stack
  is simulated every step, so keep stacks the player can stand on small - a few
  objects rather than a large pile.

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
- **Material-Based Collision Response.** A body's
  `Density`/`Friction`/`Restitution` are resolved from a named
  material (`MaterialLibrary.ini`, see [Decisions.md](Decisions.md)) rather
  than ad-hoc per-body-type checks, and two contacting bodies' values
  combine generically via `CollisionSystem.CombineFriction`/`CombineRestitution`. Still open: any
  material-driven behavior beyond the physical constants themselves (e.g. a
  distinct sound/visual cue per material on contact).
- **`DynamicHazard` behavior.** Linear patrol (back-and-forth along the X axis,
  under its own mass-scaled force - see
  [Decisions.md](Decisions.md)) is implemented via `IPatrolBody`/`Patrol`
  (see [AssetFormat.md §3.4](AssetFormat.md)), including per-placement
  `PatrolMinX`/`PatrolMaxX` bounds (e.g. the `Enemies` world's `SnakeTwo`
  confined to the platform it starts on, while `SnakeOne` still sweeps the
  full world). Facing/animation while patrolling is also implemented -
  `DynamicObject2D.UpdatePose()` (called each physics step) calls `SetPose` so a
  `Snake` visually turns to face (and animate through) its `move_left`/
  `move_right` clips as it changes direction. Grounded and flying varieties
  both already exist (a flying hazard gets vertical wing power from the
  `PatrolMaxForceY` placement key, used together with `Patrol` and with gravity
  still on - e.g. `Butterfly`). Chase behavior is not implemented - still to
  design/implement:
  - Chase-the-player movement.
- **`KinematicObject` behavior.** Beyond acceleration-limited back-and-forth
  patrol, still to design/implement:
  - Sprites no different from `StaticObject`.
  - Movement paths.
- **Enhance sprites**, especially Crawl and Clamber, which currently feel
  underdeveloped compared to Walk/Hang - consider adding a third animation
  frame for these (and other) animated clips.
- **Show load errors in the game (low priority).** A world that fails to load
  (e.g. a `Link` with no matching switch) only reports the reason in the
  browser console and silently returns to world selection. Consider showing
  the message on screen, e.g. on the world selection screen.
- **Losing a life is a short event, not an instant reset**: the player's
  `died` effect and the hazard's sound play with physics frozen for about two
  seconds, then
  invulnerability window after respawning; revisit if respawning next to a
  hazard feels unfair.
- **Keys and gates are matched by a required `Lock` name** on both the key
  (`CollectableType.Key`) and the gate (`Kind = Gate`). A key without
  `Lock` is inert, like a collectable without `Type`. Gates can alternatively be
  driven by a button or lever through a shared `Link` name. The HUD shows only a
  single key icon (shown while at least one key is held) with a small
  superscript count (`¹`-`⁹`, `ⁿ` for more than 9), not a list, since a world
  may use many locks.
