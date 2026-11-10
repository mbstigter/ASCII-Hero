# Level Design Tips

Practical rules of thumb for laying out worlds in the `*_objects.txt` grids. All distances are in
grid cells. They assume the default player and the default gravity (`Gravity = 40`); a world that
changes gravity or the player's walk speed changes these numbers too.

## The player

- The player is **3 cells wide and 3 cells tall**.
- A gap the player has to walk through needs **at least 3 empty rows** of headroom. Crawling lowers the player to
  **2 rows** (the empty cells in the crawling sprite do not count for collisions), so a
  crawl-through tunnel needs at least 2 empty rows.
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

## Falling

- Dropping **more than 10 rows** costs the player a bit of health (a fifth of the bar by default).
  Landing in water first, or on a hazard, does not count as a hard landing. The drop is counted
  from the highest point of the fall: a jump already rises about 6 rows, so jumping down onto a
  platform more than about 4 rows lower than the take-off also hurts.
- Do not make the player fall far as part of the normal route. Use steps, ladders or ropes for
  bigger height differences, or make the long drop a deliberate risk.
- **Place the player's start and every checkpoint on or just above the floor.** The player also
  falls from there right after the level starts and after losing a life, and a long drop there
  would hurt every time.

## Climbing and hanging

- A hangable object (e.g. a lying-flat pipe, ladder or rope) is in reach of a standing player's jump when it is **at most
  5 rows above the floor the player stands on**.
- Anything climbable or hangable that the player is meant to step off onto a ledge should end
  level with (or just above) that ledge.
- Moving ladders, pipes and ropes: make sure both ends of the route are reachable, and that the
  player cannot get squeezed between the moving object and the scenery.

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
| Fan (lying flat) | `horizontal_up_*`, `horizontal_down_*` | 7 wide (one rotor) | 3 tall |
| Fan (upright) | `vertical_left_*`, `vertical_right_*` | 5 tall (one rotor) | 4 wide |
| Door (closed) | `closed` | 1 tall | 2 wide |
| Hatch (closed) | `closed` | 1 wide | 1 tall |
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
- Put the level end where the player can see it, or leave a trail of diamonds toward it.
- Give every hard jump a way back if the player misses it, unless the fall is meant to be the
  challenge.
