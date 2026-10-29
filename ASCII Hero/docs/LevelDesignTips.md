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
| Landing lower | Anything; falling does not hurt | - |

- Measure a horizontal gap from the right edge of one platform to the left edge of the next (the
  empty cells in between).
- Height and distance trade off against each other: the higher the target, the shorter the gap
  must be. Do not combine a 12-cell gap with a 5-row climb.
- There is no grace time for jumping just after running off a ledge, so jumps right at the edge
  of a platform need a precise press.

## Climbing and hanging

- A hangable object (pipe, rope) is in reach of a standing player's jump when it is **at most
  5 rows above the floor the player stands on**.
- Anything climbable or hangable that the player is meant to step off onto a ledge should end
  level with (or just above) that ledge.
- Moving ladders, pipes and ropes: make sure both ends of the route are reachable, and that the
  player cannot get squeezed between the moving object and the scenery.

## Repeatable objects

Objects such as ropes, lasers and conveyors are built from a small unit that is repeated with
`Repeat = N`. The total length is always a **multiple of the unit**, so plan gaps and ledges around
these sizes.

| Object | Clips | Unit along its length | Thickness |
|---|---|---|---|
| Rope (still) | `horizontal` | 2 wide | 1 tall |
| Rope (running) | `horizontal_left`, `horizontal_right` | 3 wide | 1 tall |
| Rope (still) | `vertical` | 2 tall | 1 wide |
| Rope (running) | `vertical_up`, `vertical_down` | 3 tall | 1 wide |
| Laser | `horizontal` | 3 wide | 1 tall |
| Laser | `vertical` | 3 tall | 1 wide |
| Conveyor | `horizontal_left`, `horizontal_right` | 2 wide | 1 tall |
| Conveyor | `vertical_up`, `vertical_down` | 2 tall | 3 wide |
| Ladder | `vertical` | 1 tall | 3 wide |
| Ladder | `horizontal` | 1 wide | 3 tall |
| Pipe | `horizontal` / `vertical` | 1 | 1 |
| Water surface | `horizontal` / `vertical` | 2 | 1 |

Examples: a still rope with `Repeat = 4` is 8 cells long, a running rope with `Repeat = 4` is 12; a conveyor with `Repeat = 6` is 12 cells long.

- Write the unit in the comment next to `Repeat` (for example `; Each repeat adds 3 cells of width`)
  so the next designer does not have to look it up.
- Pick the animated clip whose direction matches the object's `ConveyorDirection`, so the motion
  you see matches the way the player is carried.

## General

- Place the player's start a few cells away from the world edge and from any hazard.
- Put the level end where the player can see it, or leave a trail of diamonds toward it.
- Give every hard jump a way back if the player misses it, unless the fall is meant to be the
  challenge.
