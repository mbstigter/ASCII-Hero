# World Design Tips

Practical rules of thumb for laying out worlds in the `*_objects.txt` grids. All distances are in
grid cells. They assume the default player and the default gravity (`Gravity = 40`); a world that
changes gravity or the player's walk speed changes these numbers too.

## Placing objects

- **The cell you mark in the objects grid is the object's top-left corner.** Everything the
  object covers extends to the right and down from that cell, so mark the top-left cell of
  where you want it, not its middle or bottom.
- This holds for every object and every frame. A sprite that changes size while it plays
  keeps its top-left corner fixed and grows or shrinks from there, to the right and down only.
- So design animated objects with that corner in mind. An object that should grow **left or
  up** cannot move its corner: author every frame at full size and leave the not-yet-extended
  cells empty. Then mark the top-left cell of the full-size sprite.

## The player

- The player is **3 cells wide and 3 cells tall**.
- A gap the player has to walk through needs **at least 3 empty rows** of headroom.
  Crawling lowers the player to **2 rows** (the empty cells in the crawling sprite do not
  count for collisions), so a crawl-through tunnel needs at least 2 empty rows.
- Walking speed is 12 cells/second. Steering in mid-air is weak, so a jump goes roughly where the
  run-up was heading.

## Jumping between platforms

| Jump | Comfortable | Absolute limit |
|---|---|---|
| Height difference, landing higher | **5 rows** | 6 rows (needs a perfect jump) |
| Horizontal gap, same height | **12 cells** | 13 cells |
| Horizontal gap while also climbing 5 rows | **9 cells** | 9 cells |
| Landing lower | **Up to 10 rows** when walking off | Higher drops hurt a little (see below) |

- Measure a horizontal gap from the right edge of one platform to the left edge of the next (the
  empty cells in between).
- Height and distance trade off against each other: the higher the target, the shorter the gap
  must be. Do not combine a 12-cell gap with a 5-row climb.
- There is no grace time for jumping just after running off a ledge, so jumps right at the edge
  of a platform need a precise press.
- The player keeps its footing on an edge while **at least half of one foot** (half a cell) is
  still on it; with less, it slides off.

## Stacking and edges

- Anything that falls - crates, stones, enemies and the player - stays on what it stands on only
  while **its middle is above it**. Something hanging more than halfway over an edge slides off.
- Stack crates and stones so each one's middle is over the one below. A body resting across two
  supports (a crate bridging a gap) is fine.
- Keep an enemy's patrol range at least half its width inside the platform's edges, or it will
  slide off at the end of its patrol.

## Falling

- Dropping **more than 10 rows** costs the player a bit of health (a fifth of the bar by default).
  Landing in water first, or on a hazard, does not count as a hard landing. The drop is counted
  from the highest point of the fall: a jump already rises about 6 rows, so jumping down onto a
  platform more than about 4 rows lower than the take-off also hurts.
- Do not make the player fall far as part of the normal route. Use steps, ladders or ropes for
  bigger height differences, or make the long drop a deliberate risk.
- **Place the player's start and every checkpoint on or just above the floor.** The player also
  falls from there right after the world starts and after losing a life, and a long drop there
  would hurt every time.

## Climbing and hanging

- A hangable object (e.g. a lying-flat pipe, ladder or rope) is in reach of a standing
  player's jump when it is **at most 5 rows above the floor the player stands on**.
- A player hanging from a pipe can jump up to another hangable pipe **1 or 2 rows above** and
  grab it, so a route of closely stacked pipes can be climbed using only the hands.
- Anything climbable or hangable that the player is meant to step off onto a ledge should end
  level with (or just above) that ledge.
- Grip depends on the player's and surface's friction. A very slippery surface can make the
  player slide or lose a hang; test intended routes with their chosen materials.
- Moving ladders, pipes and ropes: make sure both ends of the route are reachable, and that the
  player cannot get squeezed between the moving object and the scenery.

## Keys and locked gates

- A key is never used up: the player keeps it and can open and close its gate as often as wanted,
  and keeps it when moving between worlds and sub-worlds.
- **Give every lock name a unique name across the world and all its sub-worlds**, because carried
  keys follow the player through portals and a key would also work on a same-named gate elsewhere.
- A key- or switch-operated gate cannot close while the player or another solid moving object is
  in its closed shape. If any gate linked to a switch is blocked, none of that switch's targets change.

## Gear icons

- The HUD gear box and gear ring use the same separate **`icon`** clip. List the
  sprite asset as `Name = SpriteAsset` in `Global/Settings.ini` `[Gear]`; its
  `icon` clip is used automatically (see [AssetFormat.md §4.4](AssetFormat.md#44-globalsettingsini)).
- **An icon must be exactly one line tall and one to three characters wide.**
  A world sprite can be much larger: draw a compact, recognizable representation
  in its icon clip without changing the object's appearance in the world.
  Icons are centered, not automatically scaled down.
- In the icon's foreground and background color layers, use only `EmptyChar`
  and `^`. `^` uses the active interface foreground color in either layer.
  Empty foreground cells use the interface background color, allowing reversed
  shapes; empty background cells leave the panel backdrop unchanged.

## Repeatable objects

Objects such as ropes, lasers and conveyors are built from a small unit that is repeated to fill
`Span = N`, the total length in cells (width for horizontal clips, height for vertical ones). A span
that is not a multiple of the unit ends with a partial unit; for a clean end, pick a multiple.

| Object | Clips | Unit along its length | Thickness |
|---|---|---|---|
| Rope (still) | `horizontal` | 2 wide | 1 tall |
| Rope (running) | `horizontal_left`, `horizontal_right` | 3 wide | 1 tall |
| Rope (still) | `vertical` | 2 tall | 1 wide |
| Rope (running) | `vertical_up`, `vertical_down` | 3 tall | 1 wide |
| Vine | `vertical` | 2 tall | 1 wide |
| Laser | `left`, `right` | 3 wide | 1 tall |
| Laser | `up`, `down` | 3 tall | 1 wide |
| Conveyor | `horizontal_left`, `horizontal_right` | 3 wide | 1 tall |
| Conveyor | `vertical_up`, `vertical_down` | 3 tall | 3 wide |
| Ladder | `vertical` | 1 tall | 3 wide |
| Ladder | `horizontal` | 2 wide | 2 tall |
| Pipe | `horizontal` / `vertical` | 1 | 1 |
| Portal (horizontal) | `horizontal` | 2 wide | 1 tall |
| Portal (vertical) | `vertical` | 1 tall | 2 wide |
| Fan (lying flat) | `horizontal_up_*`, `horizontal_down_*` | 7 wide (one rotor) | 3 tall |
| Fan (upright) | `vertical_left_*`, `vertical_right_*` | 5 tall (one rotor) | 4 wide |
| Door (gate) | `default` / `ancient` | 2 wide | 1 tall |
| Hatch (gate) | `default` / `ancient` | 1 wide | 1 tall |
| Floor | `ancient` | 8 wide | 1 tall |
| Wall | `ancient` | 2 tall | 2 wide |
| Column | `ancient` | 1 tall | 3 wide |
| Ornament (frieze) | `ancient_1` | 4 wide | 2 tall |
| Water surface (still) | `still` | 2 wide | 1 tall |
| Water surface (flowing) | `horizontal_left`, `horizontal_right` | 3 wide | 1 tall |
| Waterfall | `vertical_down` | 3 tall | 1 wide |

Examples: `Span = 12` on a running rope gives four whole 3-cell units; `Span = 10` gives three units
plus one cell of a fourth.

- Running (animated) units look smoothest when the span is a multiple of the unit.
- Pick the animated clip whose direction matches the object's `ConveyorDirection` (or the water's
  `FlowDirection`), so the motion you see matches the way the player is carried. Running ropes and
  belts animate at exactly their own speed; flowing water's waves move at a third of its
  `FlowSpeed`, about as fast as things drift in it. Either way, a faster object also looks faster.
- For water that only flows while switched on, use the `still` clip with
  `ClipWhenOn = horizontal_left`/`horizontal_right`.

## General

- Place the player's start a few cells away from the world edge and from any hazard.
- Put the world end where the player can see it, or leave a trail of diamonds toward it.
- Give every hard jump a way back if the player misses it, unless the fall is meant to be the
  challenge.
