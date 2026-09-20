# Plan: Ambient Medium (Buoyancy, Drag, Viscosity)

## Goal

Give every point in a world an ambient medium (defaulting to `Air`) that
physically affects any body passing through it - not just contact/collision
response, but continuous buoyancy and drag while immersed. This is the
physics groundwork for water (and similar) volumes behaving differently from
open air, e.g. a falling object sinking slower through `Water` than falling
through `Air`. It is deliberately scoped as a general, material-driven
concept, not a player-only "swim" feature.

## Relationship to existing notes

- `docs/Decisions.md` already flags this as deferred: *"Medium drag/buoyancy
  (air/liquid) is deliberately treated as a distinct concept from surface
  friction - not yet implemented."* This plan implements that.
- `docs/Design.md`'s planned **Swim stance** item is narrower (player input/
  pose while swimming) and should be treated as a follow-up built on top of
  this work, not a prerequisite for it - see "Relationship to Swim stance"
  below.
- `docs/AssetFormat.md` §4.2 already documents `Density` on `MaterialLibrary.ini`
  entries as driving "mass and buoyancy," and already ships `Air`/`Water`
  materials with distinct densities - this plan is what finally makes that
  true.

## Core concepts

- **Ambient medium**: the material a body is currently immersed in. Defaults
  to `Air` everywhere; overridden to a `Passable` object's own resolved
  material wherever a body overlaps it (e.g. a `Water` volume).
- **Buoyancy**: a continuous upward force proportional to the difference
  between the medium's density and the body's own resolved density. Applies
  naturally via the existing density values - negligible for ordinary solids
  in `Air`, significant in `Water`.
- **Drag**: a continuous velocity-opposing force scaled by a new `Viscosity`
  material field. Conceptually distinct from Coulomb friction (which only
  exists at solid-contact time); drag applies continuously while immersed,
  independent of any contact.
- Both are plain additional terms in the existing mass-scaled force
  accumulator (`PhysicsSystem.StepMovingBodyWithForces`), alongside gravity/
  walk-force/patrol-force. No new special-cased velocity path. Terminal
  velocity in a given medium emerges naturally from the balance of forces
  rather than being explicitly modeled.

## Locked design decisions

1. **Drag mechanics**: pure force term (linear/quadratic in velocity, scaled
   by `Viscosity`), added to the force accumulator. No explicit
   terminal-velocity convergence step - it emerges from the forces.
2. **Opt-in/out**: new capability interface `IMediumAffected` (naming TBD at
   implementation time), mirroring the existing `IGravityAffected` pattern.
   Bodies opt **out** by not implementing it; ordinary physics bodies are
   affected by default.
3. **Submerged state**: no stored/tracked flag (no `ContactType`-style
   state). Instead, expose a derived read-only property, e.g.
   `IPhysicsBody.CurrentMedium`, recomputed fresh every frame - the same
   pattern `IsGrounded` already uses (derived from `HasContact`, not
   separately stored). Future pose/animation logic (e.g. a swim pose) can
   query this directly with no new state-tracking machinery.
4. **Overlapping passable volumes tie-break**: if a body overlaps more than
   one passable material volume at once, the **highest-density** medium
   wins. Deterministic, no placement-order dependency.
5. **Submersion boundary**: binary in/out per body (no partial-submersion
   blending at medium edges) - simplest to start; revisit later only if it
   looks wrong visually.

## Open questions still to resolve during implementation

These weren't blocking enough to need answers before implementation, but
should be revisited then:

- Exact functional form of drag (linear in velocity, quadratic, or a
  combination) and reasonable default `Viscosity` values for `Air`/`Water`.
- Whether `IMediumAffected` needs any parameters/hooks beyond "is affected"
  (e.g. a per-body multiplier), or is a pure marker interface.
- Confirm existing `Air`/`Water` `Density` values in `MaterialLibrary.ini`
  still make sense once they're load-bearing for buoyancy on every body, not
  just materials-only decoration.

## Relationship to Swim stance (already-planned item)

Treat this ambient-medium work as the **prerequisite physics layer**
underneath the already-planned Swim stance item in `docs/Design.md`:

- Ambient buoyancy/drag apply to *any* body (player, `MovingEnemy`, dynamic
  props) passing through `Water`, giving the general "falls through water
  differently than air" feel - independent of the player.
- The Swim capability (`ISwimmerBody`, four-directional swim input, swim
  pose/clips) becomes a *player input/animation* layer added afterward; it
  should not need to reimplement buoyancy math if this lands first.
- Recommended sequencing: implement and validate ambient medium standalone
  (testable via any falling `Dynamic` prop in the existing Water world)
  before picking up Swim stance.

## Steps

1. Add `Viscosity` field to `MaterialLibrary.ini` format (Global + world-local
   merge), with sensible defaults (near-zero for solids like `Steel`/`Glass`,
   small for `Air`, larger for `Water`).
2. Update `docs/AssetFormat.md` §4.2 to document the new `Viscosity` field.
3. Add a per-frame "resolve current medium" query for physics bodies:
   default `Air`; overridden by the highest-density `Passable` material the
   body's collision rectangle overlaps, reusing existing passable-overlap/
   spatial-grid machinery rather than an unbounded new scan.
4. Expose the resolved medium as a derived read-only property (e.g.
   `IPhysicsBody.CurrentMedium`), computed fresh each frame - no stored flag.
5. Add an `IMediumAffected` capability interface (mirroring
   `IGravityAffected`); have relevant existing physics bodies implement it
   by default.
6. Add buoyancy and drag force terms to `PhysicsSystem.StepMovingBodyWithForces`
   for any `IMediumAffected` body, using resolved body density vs. medium
   density (buoyancy) and medium `Viscosity` vs. body velocity (drag).
7. Validate manually in the existing Water world: a falling `Dynamic` prop
   should visibly sink slower / float depending on its material's density
   relative to `Water`'s density, with no perceptible change to ordinary
   `Air` falls.
8. Build the solution and confirm no regressions in existing physics/
   collision behavior (platform carry, jump arcs, patrol movement).
9. Update `docs/Decisions.md`: replace the "deliberately deferred" bullet
   with a current-state bullet describing the implemented ambient-medium
   force model (buoyancy/drag as accumulator forces, `IMediumAffected`,
   `CurrentMedium`, highest-density tie-break).
10. Update `docs/Design.md`: mark ambient medium as done, and adjust the
	Swim stance planned item to note it now builds on this ambient-medium
	layer rather than needing its own buoyancy math.
