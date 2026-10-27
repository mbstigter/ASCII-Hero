# Physics Audit - Response

Question audited: *does the implementation produce behaviour that follows consistently from its declared physical model, within the limitations of its numerical approximations?*

Verdict after the follow-up work: **yes, for the modelled scope.** Remaining deviations are deliberate simplifications (listed below), not inconsistencies. The original audit is in [PhysicsAudit.md](PhysicsAudit.md); current decisions live in [Decisions.md](Decisions.md). This file records how the audit's findings were addressed.

## Resolved findings

| Finding | Issue | Resolution |
|---|---|---|
| F1 | Character overlap sampled lattice points (direction-dependent) | Overlap requires positive area between unit cells at continuous positions |
| F2 | Climb/hang jump-off impulse applied twice | Motor computed from the post-jump velocity |
| F3 | Restitution on every resting contact (limit cycle) | Impacts below `RestingBounceSpeedThreshold` do not bounce |
| F4 | Friction cap inflated by restitution; `μ ≤ 1` clamp | Resting normal impulse no longer bounces; geometric-mean friction, no upper clamp |
| F5 | Locomotion not traction-limited, no reaction | Traction-limited (calibrated by `GroundTractionMultiplier`), equal and opposite force on movable supports, no double-counted foot friction |
| F6 | Jump overwrote support vertical velocity | Relative to support velocity; reaction impulse on movable supports |
| F7 | Hidden medium force scale on self-generated forces | Removed; drag and buoyancy only; movement recalibrated |
| F8 | Mass from bounding box, pose-dependent | Mass = density x spawn footprint, fixed for the body's lifetime |
| F9 | World bounds followed different rules from solids | Symmetric, contact-free safety clamp; levels provide real floors/walls (deliberately not routed through the solver) |
| F10 | Bang-bang kinematic patrol | Acceleration-limited braking/reversal |
| F11 | `ReferenceVelocityX` included collision pushes | Measured from end of previous collision pass |
| F12 | Tunnelling of 1-cell geometry at terminal speed | Automatic substepping by maximum travel per step |

## Additional improvements (beyond the audit)

| Area | Issue | Resolution |
|---|---|---|
| One-way platforms | Non-physical directional solids | Removed |
| Static friction | Pushed bodies crept despite sufficient grip | Friction also takes back the matching step displacement |
| Terrain | Mover corrections could leave bodies inside solids | Final solids-only cleanup pass |
| Solver | Stacks not converging within a step | 8 solver iterations |

## Deliberate simplifications (not defects)

- No rotation: bodies are axis-aligned and never tip over.
- Weight through stacks propagates via iterated contact impulses, not an exact solver; very tall stacks may compress slightly.
- Climbing/hanging are scripted (gravity suspended, no grip limit, no reaction).
- Added mass in fluids is not modelled.
- World edges are a safety net, not material surfaces.

## Open (not physics correctness)

- **Performance:** the Crates world runs ~7 FPS vs ~30 in TestPhysics. Many mutually touching dynamic bodies cost far more than many static ones. Unmeasured suspects: no sleeping of resting bodies, per-cell `HasCharacterOverlap` per touching pair per iteration, per-contact allocations (LINQ, `new List<Contact>`, `_movingBodies.IndexOf`) causing WASM GC pauses, and rendering many multi-cell sprites. Profile Crates vs TestPhysics before choosing a fix.
- **Planned: grip-based climbing/hanging and moving ladders/pipes.** Holding a climbable/hangable records a grip contact; gravity stays on and the grip carries the weight; the motor is grip-limited (scaled by material friction) with an equal and opposite reaction on a finite-mass gripped body; movement and jump-off are relative to the gripped body, so `KinematicObject2D` patrol can drive moving ladders/pipes. Current speeds and jump strengths remain the targets. Open: grip-loss rules, friction for bodies sliding past, interaction with the hang snap.
