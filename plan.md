# Generic Game Object Handling — Plan

## Goal

Stop treating game objects by concrete type/name (platforms, player, etc.)
and instead distinguish them purely by composable characteristics: static vs.
dynamic vs. kinematic, subject to gravity or not, hazard, collectable, and so
on. Systems should iterate one generic list of objects rather than several
type-specific lists.

Target object categories:

- Static Objects: immovable objects like platforms and walls
- Dynamic Objects: objects affected by all physics forces
- Kinematic Objects: objects that move according to predefined paths
- Player: user-controlled character with special movement abilities
- Moving Enemies: AI-controlled characters that patrol or chase
- Static Enemies: non-moving hazards
- Collectables: items the player can gather

## Findings (current state)

- The codebase already avoids special-casing by *name* — there is no
  `if (name == "Platform")`-style logic anywhere.
- `IMovingBody` already lets `PhysicsSystem`/`CollisionSystem` treat the
  player and dynamic objects uniformly by capability, not concrete type —
  this is the right pattern and should be extended, not replaced.
- Special-casing remains at the **list/type level**:
  - `World2D` exposes three separate collections: `Player` (single
    instance), `List<StaticObject2D> Platforms`, and
    `List<DynamicObject2D> DynamicObjects` — there is no generic
    `List<Body2D>` that every system iterates.
  - `World2D.LoadAsync` branches into three separate construction paths
    (`PlayerSpawn` by section name, `Static` ini flag, else dynamic) and
    routes each into its own field/list instead of a single generic
    collection tagged with characteristics.
  - `AsciiRenderer.BuildFrame` and `CollisionSystem.Resolve` each contain
    three separate loops (platforms, dynamic objects, player) even though
    the loop bodies are already generic per-object logic.
  - `PhysicsSystem.Step` hardcodes player movement plus a separate loop for
    `DynamicObjects`; there's no shared "moving bodies" list reused between
    physics and collision.
  - Current type/capability model: `Body2D` (abstract base, `IsStatic`) →
    `GameObject2D` (sprite-backed) → `StaticObject2D`, `DynamicObject2D`,
    `Player2D`; `IMovingBody` unifies Player2D/DynamicObject2D. No
    `Kinematic`, enemy, or collectable concept exists yet, and `IsStatic`/
    `UseGravity` are the only characteristic flags today.

## Recommendations / Plan

1. **Add composable characteristic interfaces/flags** on top of the existing
   `Body2D`/`IMovingBody` chain instead of new parallel class hierarchies:
   - Keep `Body2D` as the universal base (`IsStatic`, `Position`, `Size`,
     `CollisionRects`).
   - Keep `IMovingBody` for anything with `Velocity`/`IsGrounded` — covers
     Dynamic, Kinematic, Player, Moving Enemies.
   - Keep gravity as a flag (`UseGravity`) usable by any moving body, not
     just `DynamicObject2D`.
   - Add small new capability markers: a hazard marker (damages the player
     on contact) for Static/Moving Enemies, a collectable marker (removed on
     player contact) for Collectables, and a kinematic-path concept
     (predefined motion, no force integration) for Kinematic Objects.
   - Only introduce a new concrete class where the construction/update logic
     genuinely differs (e.g. a `KinematicObject2D` for predefined-path
     motion, since its per-frame update is fundamentally different from
     force integration). Avoid a new subclass per noun in the category list.
   - Player-specific input handling stays behind the concrete `Player2D`
     type used only by the input-reading system — that's a legitimate
     type-specific concern (only one object takes player input), not a
     naming special-case.

2. **Replace the three parallel `World2D` collections with one generic
   list** (e.g. `List<Body2D> Objects`). Keep `Player` as a convenience
   reference into that same list (not a separate silo), so rendering,
   collision, and generic iteration never need a special branch for it.
   `CameraTarget` already models "pick one object generically" and is a good
   precedent for this pattern.

3. **Update `World2D.LoadAsync`** to build the one generic list, tagging
   each spawned object with the characteristics implied by its ini data
   (extending the existing `Static`/`Gravity` keys with something like
   `Kind`/`Behavior` for Kinematic/Enemy/Collectable) instead of branching
   into named lists. The `PlayerSpawn` branch can remain, since the player
   is a genuinely unique, singular entity — but everything else should
   construct into the same generic list, differentiated only by which
   interfaces/flags it implements.

4. **Update systems to iterate the generic list, filtering by interface:**
   - `PhysicsSystem`: iterate all `IMovingBody` objects (player + dynamic +
     kinematic + moving enemies) for gravity/velocity integration, instead
     of a hardcoded player special-case plus a separate `DynamicObjects`
     loop. Kinematic objects opt out of force integration via their own
     update path while still living in the same iterated list.
   - `CollisionSystem`: build its moving-bodies list from the generic
     collection instead of manually adding `world.Player` +
     `world.DynamicObjects`. Add hazard/collectable resolution as new
     generic passes ("any `IMovingBody` overlapping any hazard" / "...
     overlapping any collectable") rather than type-specific methods.
   - `AsciiRenderer`: iterate the single `Objects` list once for glyph
     generation instead of three separate loops (Platforms, DynamicObjects,
     Player).

5. **Add lifecycle support for collectables.** Nothing today models an
   object being removed from the world mid-game; the generic object list
   needs to support safe removal during/after iteration for "collected on
   contact" behavior.

6. **Extend `AssetFormat.md`'s ini schema** to express the new categories
   declaratively (e.g. a `Kind`/`Behavior` key such as `Kinematic`,
   `MovingEnemy`, `StaticEnemy`, `Collectable`, defaulting to
   `Static`/`Dynamic` as today) instead of only the current `Static`/
   `Gravity` booleans, so level authors can place any of the seven object
   types without engine code needing to know their names.

7. **Document the change in `Architecture.md`/`Decisions.md`** once
   implemented, following the existing pattern of explicitly stating "no
   special-casing by concrete type" (as already documented for
   `IMovingBody`), extended to cover the new hazard/collectable/kinematic
   behaviors and the unified object list.

This keeps the existing, already-generic collision/physics math untouched,
and turns the three-way `Platforms`/`DynamicObjects`/`Player` split — plus
the future need for enemies/collectables — into one list of `Body2D` objects
distinguished purely by which small, composable capability interfaces they
implement.
