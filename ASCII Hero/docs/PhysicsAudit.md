# ASCII Hero – Physics-First Audit

Scope: `Game/Physics/*`, `Game/World/Body2D.cs`, `KinematicObject2D.cs`, `DynamicObject2D.cs`, `GameLoop.cs`, `Constants/*`, `Global/MaterialLibrary.ini`, the `MovingPlatforms` and `TestMovement` worlds.
Method: read-only static analysis. No files were changed apart from this report. No tests were run because the solution has no test project.
Line numbers refer to the files as they were at the time of the audit.

---

## A. Executive summary

The engine has a mostly coherent model:
- Point-mass rigid AABB compounds.
- A fixed 60 Hz step.
- Semi-implicit Euler integration.
- Force accumulation (gravity, buoyancy, quadratic drag, motor forces) using `a = F/m`.
- An inverse-mass impulse solver with restitution and Coulomb friction, run sequentially over 4 iterations.

That structure is sound, and most special cases are documented and deliberate.

The most consequential defect is not in the dynamics. It is in the **narrow-phase acceptance test `HasCharacterOverlap`**. This test samples **integer world lattice points**, not cell areas. As a result, contact detection depends on where a body sits relative to the integer grid, and on the direction of approach:

- A contact whose overlap interval in some axis contains no integer point is **rejected**.
- Surfaces whose separating edge is integer-aligned are detected immediately when approached from above or from the left. When approached from **below or from the right**, they are only detected once penetration reaches about one full cell.
- Surfaces at **fractional** coordinates (any moving platform, any body) are detected only when penetration crosses the next integer line.

This one mechanism explains **both known issues**:
- Jitter and fall-through on vertically moving platforms.
- The ball sinking into the top border.

It also predicts further symptoms that have not been reported yet (see F1).

Other significant findings:
- The climb and hang jump-off impulse is applied **twice** on the jump frame (F2, confirmed).
- Restitution leaks into resting-contact friction, so effective friction is `μ(1+e)g` (F4).
- Locomotion forces are not limited by surface friction and do not react against the support (F5).
- The jump overwrites the support's vertical velocity instead of adding to it (F6).
- The air "viscosity penalty" silently scales every self-generated force to 0.556 (F7).

---

## B. Reconstructed physical model

| Aspect | Implementation |
|---|---|
| Entities | `Body2D` holding compound AABBs (`CollisionRects` from `CollisionShapeBuilder`, row-run and column-run rectangles deduplicated). Dynamic bodies: `Player2D`, `DynamicObject2D`, `DynamicHazard2D`. Kinematic bodies: `KinematicObject2D` (`IsStatic = true`, prescribed `Velocity`). Static bodies: terrain. |
| Degrees of freedom | 2 translational per body. No rotation. |
| Units / axes | Cells and seconds. +Y points down. `g = 40 cells/s²` (`PhysicsConstants.DefaultGravity`). |
| Mass | `Mass = Density · Size.X · Size.Y` (`Body2D.cs:391`), i.e. bounding-box area times density. If mass is 0, 1 is used instead. |
| Time step | Fixed `dt = 1/60 s` with an accumulator (`GameLoop.RunFixedPhysicsSteps:524-534`). The frame delta is clamped to 0.1 s. |
| Integration | Semi-implicit Euler: `v += (F/m)dt; x += v dt` (`PhysicsSystem.cs:782-791`). Kinematic bodies use `x += v dt` (`KinematicObject2D.Move`). |
| Forces | Gravity `m g`. Buoyancy `ρ_medium · (m/ρ_body) · g · f_sub`. Quadratic drag `η · A · v_rel·|v_rel| · f_sub` per axis (`PhysicsSystem.cs:740-780`). Patrol motor and player walk motor, both of the form `k · m · (v_target − v) · s_medium`. |
| Impulses (non-force) | Jumps set `v.Y` directly (`PhysicsSystem.cs:189, 214, 371`). Hang snap sets `v.Y = 0` and snaps position (`CollisionSystem.cs:230-231`). |
| Step order | `PhysicsSystem.Step`: state machine → motor forces → integrate every body (in list order, kinematic bodies included). Then `CollisionSystem.Resolve`: snapshot contacts → climb/hang → 4 × (solids, then mover pairs) → world bounds → hazards. |
| Collision detection | Discrete. Spatial-hash broad phase (cell size 4). Narrow phase: rectangle overlap (strict `<`) plus `HasCharacterOverlap`. |
| Resolution | One minimum translation vector (MTV) per body pair per iteration, chosen as the minimum over 4 directions of the worst-case push. Full positional projection, split by inverse mass. If approaching, a normal impulse `j = −(1+e)v_n / Σw`. Coulomb friction capped by `μ|j|w_body`. |
| Materials | `e` and `μ` are combined as arithmetic means (`Combine`). The world bounds use only the body's own `e`. |
| Contact states | `IsGrounded` = there is a `SurfaceBottom` contact this step. This is derived state, except where `RemoveContact` deletes it during jump, climb, hang or swim. |
| Moving supports | Kinematic: other bodies see its `Velocity` as the contact frame. Finite-mass supports: horizontal "carry" uses `ReferenceVelocityX`, which is the displacement over the last step after collision. |

---

## C. Findings

### F1 – `HasCharacterOverlap` samples lattice points, not cell overlap (detection depends on approach direction)
- **Severity:** Critical  **Confidence:** Confirmed (code trace). The symptoms match both known issues.
- **Where:** `CollisionSystem.HasCharacterOverlap`, lines 1033-1082, called from `TryFindContact`, lines 995-999.
- **Implementation:** The loop runs over integer `gridX ∈ [floor(left), ceil(right))` and `gridY ∈ [floor(top), ceil(bottom))`. For each point it computes `local = floor(grid − Position)` on both bodies and requires both characters to be non-empty. Effectively it asks: *is there an integer point (gridX, gridY) that lies inside a non-empty cell of both bodies?*
- **Principle:** A contact predicate should depend only on relative geometry. It should be translation-invariant and symmetric under mirroring. A lattice-point test is neither.
- **Failure mechanism:** The overlap interval in each axis is half-open, `[max(top), min(bottom))`. An integer point lies inside it only when:
  - the lower bound is itself an integer, as for an integer-aligned surface approached from above or from the left, **or**
  - the interval straddles an integer.

  Consider body A moving up into a ceiling whose bottom is `W_b = 2` (an integer), with `A.top = 1.4`. The interval is `[1.4, 2)`. The only candidate point is `gridY = 1`, which gives `localAY = floor(1−1.4) = −1`, so it is skipped. **The contact is rejected until `A.top ≤ 1`, i.e. a full cell of penetration.** Then the full depth (≥ 1) is projected out in one step.

  For a platform at a fractional `T_y` (every moving platform), landing from above gives the interval `[T_y, P_bottom)`. It only contains `ceil(T_y)`, so the rider sinks by `1 − frac(T_y)` before detection, almost a full cell when `frac` is small.
- **Scenarios:**
  1. **TestMovement ball vs. top border (known issue 2).** The wall is `y ∈ [0,2)` and the ball moves at `v_y = −20`, i.e. 0.33 cells/step. The ball overlaps the wall by up to 1 cell before reflecting. The same ball on the floor, approached from above at integer `T_y`, is detected at the first sliver. This explains the asymmetry.
  2. **SteelVertical platform (known issue 1).** `PatrolSpeedY = 2` keeps `T_y` fractional. On each landing, and on each re-contact after the restitution micro-bounce, the rider sinks up to about 1 cell and then pops by the full depth. While it is sunk, `IsGrounded` is false: the pose flickers, jumps are gated off, and stance resets.
  3. **Fall-through at speed (known issue 1).** When detection finally happens on a 1-cell-thick platform, the depth is `d ∈ [1−frac(T_y), 1−frac(T_y) + v·dt]`. For the 3-row player, the MTV picks "push down" (`pushDown = 3 − d` from the column rect) once `d > 1.5`, i.e. once `v·dt > 0.5 + frac(T_y)`, which is `v > 30 cells/s` in the worst case. A fall of `v²/2g ≈ 11` cells reaches that speed. The rider is then ejected **below** the platform. (Strongly supported: the exact threshold depends on the sprite's rectangles.)
  4. **Predicted, not yet reported:**
	 - Walking **left** into an integer-aligned wall penetrates up to 1 cell (0.2 cells/step at walk speed, so about 5 steps), then pops. Walking right is detected immediately.
	 - Standing with only a partial-cell foothold over the **left** edge of a fractional-X platform (`T_x = 5.3`, foot cell `[5.0,6.0)`): the interval `[5.3,6.0)` contains no integer, so there is no support and the body falls. The same foothold on the right edge is supported.
- **Recommended fix:** Replace the point sampling with a cell-area test. For each non-empty cell of A, `[P_A + i, +1)`, intersecting the region, enumerate the (at most 2×2) cells of B that it overlaps with **positive area** and check whether they are non-empty. This is exact, symmetric, and translation-invariant. Cost is about 4× the current per-region work and is negligible.
  - Alternative: drop the character test for bodies whose rectangles are already exact. `CollisionShapeBuilder` already produces rectangles that cover only non-empty cells, so the test is redundant except for float-aligned cell interiors.
  - Trade-off: contacts will now be found at sub-cell depths everywhere. This may expose secondary issues (F3, F9) that the current one-cell "dead band" was hiding.

### F2 – Climb and hang jump-off impulse is applied twice
- **Severity:** High  **Confidence:** Confirmed
- **Where:** `PhysicsSystem.Step`, lines 189 and 214 (local `velocity.Y` is set), line 358 (`targetVelocityY = velocity.Y`), line 360 (`UpdateWalkForce`), and line 652. `player.Velocity` is not written until line 378.
- **Mechanism:** `UpdateWalkForce` computes `F_y = (targetY − player.Velocity.Y) · m · k · s`. Here `targetY` already holds the new jump velocity, while `player.Velocity.Y` still holds the old one (≈0 while climbing). The motor therefore adds a second impulse on top of the jump:
  - `Δv_extra = k · s · dt · Δv_jump = 40 · 0.556 · (1/60) · Δv_jump ≈ 0.37 · Δv_jump`.

  The ground jump (line 371) is **not** affected, because it runs after `UpdateWalkForce`.
- **Scenario:** Hang jump with `HangJumpSpeed = 22`, so `v = −22 · 0.556 = −12.2 c/s` is intended. The actual result is about `−16.7 c/s`, so jump height is about 1.87× the intended height (`h ∝ v²`).
- **Fix:** Assign `player.Velocity = velocity` before computing the motor, or compute the motor from the local `velocity`. One-line change with low risk, but climb and hang jump heights will drop and may need retuning.

### F3 – Restitution is applied to every resting contact; resting state is a limit cycle
- **Severity:** Medium  **Confidence:** Confirmed
- **Where:** `ResolveContact`, lines 849-862. There is no resting threshold.
- **Analysis:** At rest, the per-step approach speed is `a_{n+1} = g·dt − e·a_n`. Its fixed point is `a* = g·dt/(1+e)`, and the contact persists every step, so this is fine on static ground. However, the body oscillates with relative velocity `±e·a*` and its position is projected every step. Combined with F1 on fractional surfaces, every micro-bounce can lose contact detection. With `e = 1` (TestMovement walls), a gravity-affected body never settles.
- **Fix:** Use `e_eff = 0` when `|v_n| < g·dt·c` for some `c` around 2. This is a standard numerical rest threshold, not a gameplay hack. Document it as such.

### F4 – Friction cap is inflated by restitution: effective sliding deceleration = μ(1+e)g
- **Severity:** Medium  **Confidence:** Confirmed
- **Where:** `ResolveContact`, lines 889-890, and `ApplyCoulombFriction`, lines 944-949.
- **Principle:** Coulomb's law gives `|J_t| ≤ μ·J_n`. Over one step of resting contact, `J_n` should be `m·g·dt`, the impulse needed to cancel gravity. The code uses the full collision impulse `(1+e)·a* = g·dt`… only at the fixed point. On impact it uses `(1+e)·v_impact`, which is correct for impacts. But because of F3, the resting-state normal impulse comes from the bounce cycle, so friction depends on `e` and on the solver's history rather than on `μ` alone.
- In addition, `Math.Clamp(friction, 0, 1)` silently forbids `μ > 1` (e.g. rubber on rubber).
- **Fix:** Once F3 is fixed (resting `e = 0`), the normal impulse becomes `m·g·dt` and the cap becomes `μ·g·dt` per step, which is physically correct. Remove the upper clamp or document it.

### F5 – Locomotion forces are not limited by traction and do not react against the support
- **Severity:** Medium (design-level)  **Confidence:** Confirmed
- **Where:** `UpdateWalkForce` (lines 634-653) and `DynamicObject2D.UpdatePatrolDirection` (line 294).
- **Mechanism:** The walk motor is an external force `k·m·Δv` applied to the body's centre. It is not bounded by `μ·N`. It works the same on ice, on steel, and in mid-air (scaled by `AirControlMultiplier`). No equal and opposite force is applied to the support: walking on a finite-mass body (the ball, `BigDiamond`) does not push it back, so momentum is not conserved. The material `Friction` only acts in the contact solver, where it opposes the motor (the code handles this with the `GetSurfaceVelocityX` reference frame).
- **Assessment:** This is a deliberate simplification ("motor"), but it contradicts "explicit material parameters drive behaviour". Surface friction has no effect on how fast a player can accelerate or stop.
- **Fix (optional):** Clamp the horizontal motor force to `μ_combined · N` while grounded, where `N ≈ m·g` or the last normal impulse divided by `dt`. Apply `−F` to finite-mass supports. Risk: much lower grip on low-μ materials (water floors are `μ = 0.05`), so speeds would need retuning.

### F6 – Jump overwrites the support's vertical velocity instead of adding to it
- **Severity:** Medium  **Confidence:** Confirmed
- **Where:** `PhysicsSystem.cs:371` (`velocity.Y = −J`), and likewise lines 189 and 214.
- **Principle:** An impulse adds `Δv`. Relative to a support moving at `v_s`, the take-off velocity should be `v_s − J`. Horizontal carry is already handled relative to the support (`GetSurfaceVelocityX`), but vertical carry is not.
- **Scenario:** The player jumps from SteelVertical while it moves up at 2 c/s. The jump gives `−22.2` instead of `−24.2`, so jump height differs by direction of platform travel. A jump while slightly bouncing (F3) discards the current `v_y`.
- **Fix:** `velocity.Y += −J − (velocity.Y − v_support.Y)`, i.e. set the velocity *relative* to the support. A support-velocity accessor for Y is needed, analogous to `GetSurfaceVelocityX`.

### F7 – Air "viscosity penalty" hides a constant 0.556 gain on all self-generated forces
- **Severity:** Low–Medium (transparency)  **Confidence:** Confirmed
- **Where:** `ResolveMediumForceScale` (line 436): `1/(1 + 0.02·40) = 0.556`.
- **Effect:** All tuning constants are 1.8× their effective values: an effective jump of 22.2 c/s instead of 40, and an effective walk gain of 22 /s instead of 40. The parameters therefore no longer mean what their names say, and any change to Air's viscosity (which also affects drag) retunes all locomotion. Physically, viscosity already acts through drag, so applying it again to muscle force double-counts the medium.
- **Fix:** Normalise to the ambient medium (`scale = (1+η_air·c)/(1+η·c)`), or remove the scale and rely on drag. Then rescale the constants by 0.556. Behaviour stays identical, but the parameters become honest.

### F8 – Mass derived from bounding box and pose-dependent
- **Severity:** Low  **Confidence:** Strongly supported
- **Where:** `Body2D.Mass` (line 391). `Size` changes in `ApplyFrame` (line 619) whenever the frame or pose changes.
- **Effect:** Switching between walk and crawl, or between animation frames of different sizes, changes `m` without changing `v`, so momentum is not conserved through pose changes. Buoyancy (`V = m/ρ`) and collision shares change too. The mass also counts empty cells (the ball's corners). Standing up under a ceiling creates an overlap that the solver resolves by projection (this may push the body up or down).
- **Fix:** Fix mass per body at spawn, or compute it from non-empty cell count. Check headroom before allowing a stand-up.

### F9 – World bounds use different rules from solids
- **Severity:** Low  **Confidence:** Confirmed
- **Where:** `ResolveWorldBounds`, lines 543-583.
- **Differences:**
  - It uses the full bounding box rather than `CollisionRects`.
  - It uses only the body's own `e`, not `Combine`.
  - There is no friction.
  - It reflects velocity without checking whether the body is approaching. A body pushed outside by a solid correction while already moving inward still gets its velocity reversed, which injects energy.
  - It runs after the solver loop, so its clamp can re-introduce solid overlap that is not resolved until the next step.
  - Only the floor records a contact.
- **Fix:** Model the bounds as four static sentinel solids that go through `ResolveAgainstOtherBody`.

### F10 – Kinematic patrol is bang-bang (infinite acceleration at turn-around)
- **Severity:** Low  **Confidence:** Confirmed
- **Where:** `KinematicObject2D.Move`, lines 167-177.
- **Effect:** The velocity jumps from −2 to +2 instantly. When the platform reverses from rising to falling, it accelerates downward faster than `g`, so the rider legitimately separates for about `4/40 = 0.1 s` (6 steps). That is physically correct for a prescribed motion, but it adds to the jitter caused by F1/F3.
- **Fix (optional):** Give the platform a finite turn-around acceleration (`a_max < g` keeps riders seated). This is an explicit, physical parameter.

### F11 – `ReferenceVelocityX` includes positional corrections
- **Severity:** Low  **Confidence:** Confirmed
- **Where:** `Body2D.SnapshotContactsForNextFrame` (lines 159-163) and `ReferenceVelocityX` (line 178).
- **Effect:** Penetration pops (F1) feed directly into the carry speed of finite-mass supports, e.g. a 1-cell pop becomes 60 c/s for one step.
- **Fix:** Use the integrated velocity of supports that are not themselves in contact, or exclude projection displacement.

### F12 – Discrete detection; terminal speed allows tunnelling of 1-cell geometry
- **Severity:** Low (current worlds)  **Confidence:** Strongly supported
- **Analysis:** Air drag gives terminal speed `v_t = sqrt(m·g / (η·A))`. For the player, `m = 0.95·9 = 8.55` and `A = 3`, so `v_t = sqrt(8.55·40 / 0.06) ≈ 75 c/s`, which is 1.26 cells/step and exceeds a 1-cell platform. Reaching it requires a fall of more than about 70 cells, which is taller than current worlds (about 24 rows). F1 lowers the practical threshold to about 30 c/s for fractional surfaces.
- **Fix:** After F1, either clamp speed to `< min_thickness/dt` (an explicit numerical parameter) or add a swept test only along Y. Full CCD is not justified.

### Accepted, documented interventions (not defects)
- **Hang snap** (velocity zeroed and position snapped): an explicit constraint.
- **`RemoveContact` on jump, climb or swim:** this only edits derived state, but see F2 and F6.
- **Turning off gravity while climbing or hanging:** a stated model.
- **Airborne `targetX = v.X`:** gives a zero motor force, which is physically clean.
- **One-way platforms:** velocity-gated, consistent.
- **Separating-contact skip** (`v_n ≥ 0`): standard practice and correct.
- **Snap speed gate** (`MaxSnapSpeed`): an explicit parameter.

---

## D. Mathematical analysis

1. **Integration.** `v_{n+1} = v_n + a·dt` and `x_{n+1} = x_n + v_{n+1}·dt` is semi-implicit (symplectic) Euler. Under constant `g`, the per-step position error versus the exact solution is `g·dt²/2`. The jump apex height is `h = v₀²/2g + v₀·dt/2`, about a 1–2% bias, which is acceptable. Because the step is fixed, results do not depend on frame rate. The only variability is whether a frame runs 0, 1 or 2 steps, which affects rendering only.

2. **Motor stability.** For `dv/dt = k·(v_t − v)` with explicit Euler, `v_{n+1} − v_t = (1 − k·dt)(v_n − v_t)`.
   - Walk: `k = 40·0.556 = 22.2`, so `k·dt = 0.37`. Monotone convergence.
   - Patrol: `k·dt = 60/60 = 1`. Converges in exactly one step.
   - The motor oscillates if `k·dt > 1` (multiplier above about 108 for the player, or above 60 for patrol) and diverges if `k·dt > 2`. None of these limits are documented. **Recommend validating `k·dt ≤ 1` at load.**

3. **Drag stability.** The linearised rate is `λ = 2·η·A·|v|/m`. In water at `v = 20`: `λ = 2·0.15·3·20 / 8.55 = 2.1`, so `λ·dt = 0.035`. Stable.

4. **Resting contact.** See F3: `a* = g·dt/(1+e)`. The contact is re-detected every step only if penetration `a*·dt > 0`, which holds, and only if the detection predicate accepts it, which F1 violates on fractional surfaces.

5. **Impulse.** `j = −(1+e)·v_n / (w₁+w₂)`. This conserves linear momentum for mover–mover pairs. Kinetic energy changes by a factor `e²` along the normal. Correct.

6. **Double impulse (F2).** `Δv_total = Δv_jump · (1 + k·s·dt) = 1.37·Δv_jump`, which gives 1.87× the intended height.

7. **Friction (F4).** On impact with `|v_n| = u`, the friction cap is `μ(1+e)u`. That is correct for an impulsive contact. The defect is only in the resting regime.

---

## E. Collision analysis

- **Pipeline:** The broad phase uses a grid built once per step. Bodies moved by corrections in later iterations still use stale buckets; with cell size 4 this is harmless. The narrow phase uses rectangle overlap (strict) plus the character test (F1). Each pair gets a single MTV choosing, among four directions, the smallest of the worst-case pushes. Projection is followed by an impulse.
- **Strengths:**
  - The compound-shape MTV is symmetric by construction (the defect lives in F1, not here).
  - The separating-contact skip is correct.
  - Deduplicating rectangles prevents double impulses.
  - 4 Gauss-Seidel iterations are enough for corners.
- **Corners and floor-wall:** Each iteration resolves one MTV per pair. With separate floor and wall bodies, both get resolved. With a single L-shaped body, two iterations are needed (one per axis), which 4 iterations cover.
- **Thin geometry and high speed:** See F1 (fall-through above about 30 c/s on fractional surfaces) and F12.
- **Order dependence:** The solids pass runs before the mover pass and the world bounds come last (F9). With full projection, results depend on list order only when constraints conflict, such as a body squeezed between a kinematic platform and a ceiling. In that case the later constraint wins and the body may end up inside the ceiling, because a kinematic solid cannot be pushed. That is acceptable but unhandled (no crush rule).

---

## F. Physical consistency assessment

- **Follows its model:** force accumulation with `a = F/m`, gravity, buoyancy (Archimedes, scaled by submerged fraction), quadratic drag relative to flow, the impulse solver with momentum conservation, kinematic platforms as infinite-mass moving frames, and the fixed timestep.
- **Deliberate simplifications:** no rotation, AABB compounds, a velocity motor instead of a traction model (F5), no gravity while climbing or hanging, arithmetic-mean material combination, and mass from the bounding box (F8).
- **Numerical approximations causing error:** lattice-point detection (F1), restitution on resting contact (F3, F4), discrete detection (F12), and the snapshot-based reference velocity (F11).
- **Unexplained interventions:** the double jump impulse (F2, accidental), the absolute jump velocity (F6), the hidden 0.556 gain (F7), and the different rule set for world bounds (F9).
- **Insufficient evidence to decide:**
  - Whether the air-control force (a force with no reaction) is intended as a physical claim or as gameplay.
  - The intended crush behaviour.
  - Whether `AirControlMultiplier`, `PatrolLevelSpringGain`, etc. are meant to be "physical".
  - `docs/Decisions.md` and `docs/Design.md` were not cross-checked line by line.

---

## G. Testing strategy (deterministic, headless, `dt = 1/60`)

Add an xUnit project that drives `PhysicsSystem.Step` and `CollisionSystem.Resolve` with a synthetic `World2D` and `InputState`.

| # | Test | Setup | Expected / tolerance |
|---|---|---|---|
| T1 | Direction symmetry (F1) | 1×1 box, `e = 0`, at speed `v ∈ {1, 6, 20}` c/s toward a static wall from each of 4 directions. Repeat with the wall offset by fractions `{0, 0.1, 0.5, 0.9}`. | Maximum penetration observed after any `Resolve` is `≤ v·dt + 1e-9` in every case. Today, "from below" and "from the right" give about 1.0. |
| T2 | Rider on vertical platform | Player on a 1-high kinematic platform with `v_y = ±2`, 600 steps. | `IsGrounded` is true on ≥ 99% of steps, excluding a window of ≤ 6 steps after reversal (F10). Max penetration is `< 0.05`. |
| T3 | Landing at speed | Drop onto a fractional-Y platform at speeds of 10 to 70 c/s. | Body ends above the platform (`bottom ≤ top + ε`) for all `v < thickness/dt`. |
| T4 | Climb/hang jump (F2) | Hanging, press Jump once. | `v_y` after one step is `−HangJumpSpeed·s + g·dt` (gravity on) ±1e-9. Apex height matches `v₀²/2g` within 3%. |
| T5 | Free-fall invariant | No contacts, `η = 0`. | After n steps, `v = g·n·dt` exactly and `y = g·dt²·n(n+1)/2`. |
| T6 | Momentum in mover pair | Two bodies, `g = 0`, head-on, `e ∈ {0, 0.5, 1}`. | `Σ m·v` is conserved to 1e-9. KE after equals `e²` times KE_rel before. |
| T7 | Resting friction (F4) | Block on floor with initial `v_x = 5`, no motor. | Stopping distance is `v²/(2μg)` within 5%, independent of `e`. |
| T8 | Elastic box | Ball with `e = 1`, `g = 0`, in a closed box, 10 000 steps. | `|v|` is constant to 1e-9. The ball never overlaps a wall by more than `v·dt`. |
| T9 | Pose mass | Toggle crawl/walk. | Momentum `m·v` continuity (documents F8). |
| T10 | Motor gain validation | Load all worlds. | `k_eff·dt ≤ 1` for every motor. |

---

## H. Prioritised remediation plan

| Rank | Change | Correctness | Stability | Collision | Regression risk | Complexity |
|---|---|---|---|---|---|---|
| 1 | F1: replace the lattice sampling with a cell-area overlap test, with T1–T3 tests | High | High | High | Medium (exposes F3) | Low |
| 2 | F2: compute the motor from the post-impulse velocity, with T4 | High | – | – | Low (retune climb/hang jumps) | Trivial |
| 3 | F3: add a resting-contact restitution threshold (an explicit numerical constant) | Medium | High | Medium | Low | Low |
| 4 | F4: correct the friction cap and remove the `μ ≤ 1` clamp (follows from #3) | Medium | – | – | Low | Trivial |
| 5 | F6: make the jump relative to the support's velocity | Medium | – | – | Low | Low |
| 6 | F9: route world bounds through the regular solver | Low | Medium | Medium | Low | Low |
| 7 | F7: normalise or remove the medium force scale and rescale the constants | Transparency | – | – | Low (feel stays identical if rescaled) | Low |
| 8 | F11, F10, F8 | Low | Low | – | Low | Low–Medium |
| 9 | F5: traction-limited motor plus reaction force on the support | Design | – | – | High (feel changes) | Medium; needs a design decision |
| 10 | F12: speed clamp or Y-only sweep | Low | – | Low | Low | Low |

No rewrite is warranted. The architecture is sound, and the dominant problems are localised.

---

## I. Open questions

1. Is the walk/patrol "motor" meant to be a physical force (which would then need traction limits and a reaction force) or an accepted gameplay abstraction? This decides F5.
2. Is the air penalty in F7 intentional as a physical statement, or a tuning artefact?
3. Should materials allow `μ > 1`? Should combination use mean, min, or `sqrt(product)`? Only the mean is used now, with no rationale in code.
4. What is the intended behaviour when a body is crushed between a kinematic platform and terrain?
5. Should mass be per-body and constant, or pose-dependent (F8)?
6. Is jitter-free riding of reversing platforms required? If so, platform acceleration needs a parameter (F10).
7. What is the maximum fall height the level design allows? This decides whether F12 needs action.
8. A runtime capture (positions and contacts per step) from the MovingPlatforms world would quantitatively confirm the F1 thresholds stated above.
