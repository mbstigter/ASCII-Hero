# Implementation review — `793fa1f`

> Review of the architectural cleanup and hazard-contact follow-up in commit `793fa1f`, including the implementation choices recorded in §10 of [ArchitecturalIntegrityAudit.md](ArchitecturalIntegrityAudit.md). This document records findings only; saving it does not approve or implement any follow-up changes. Source line references describe the implementation reviewed at that commit.

## Overall verdict

**The implementation is mostly sound and appropriately incremental.** The structural choices in §10 are reasonable; no redesign or additional framework is warranted.

However, I would not consider every recommendation fully closed yet:

- **ARCH-011:** stopping asynchronous startup/loading is incomplete.
- **ARCH-012:** previous-world state is cleared, but held-key history is initialized incorrectly for the stated intent.
- **ARCH-013:** the tests are useful, but do not sufficiently protect the behavioral changes.
- **Hazard follow-up:** the direction of impact is lost when recording speed, and the solid/passable distinction is less precise than documented.
- **ARCH-001:** several inaccurate comments remain.
- **ARCH-009:** gravity semantics remain unresolved.

### Validation

- Reviewed local `master`, which matches **`793fa1f`**.
- Included the existing, uncommitted §10 update in the review.
- **Solution build: passed.**
- **MSTest: 40 passed, 0 failed.**
- No source or documentation edits made during the review itself.
- No browser gameplay session performed. The behavioral findings below come from source tracing; the existing tests do not reproduce all of them.

**Reference convention:** `Client/` means `ASCII Hero/ASCII Hero.Client/`; `Tests/` means `ASCII Hero/ASCII Hero.Tests/`; documentation paths begin with `ASCII Hero/docs/`.

---

## 1. Prioritized findings

### REV-001 — `Stop()` does not prevent late adapter initialization or calls

**Priority: High · ARCH-011 · Incomplete implementation**

**References**

- `Client/Game/GameLoop.cs:167–208`
- `Client/Game/GameLoop.cs:256–260`
- `Client/Game/GameLoop.cs:385–395`
- `Client/Pages/Home.razor:44–55`
- `Client/Game/Browser/CanvasBridge.cs:22–26,45–55`

`Home.DisposeAsync` correctly calls `Stop()` before disposing the adapters. The bridge also correctly stops JavaScript before releasing its .NET callback reference.

The problem is that `Stop()` only sets a flag. Not all asynchronous continuations observe it.

#### Concrete startup sequence

1. `StartAsync` is awaiting catalog or configuration loading.
2. The page is disposed; `Stop()` runs and the adapters are disposed.
3. Startup resumes.
4. It still calls `canvasBridge.InitializeAsync` at line 193.
5. Only **after initialization** does it check `_isStopped`.

This can initialize a canvas module and callback reference after their owner has already disposed them.

There are further unchecked continuations:

- Audio initialization and sound registration at lines 205–208.
- World loading followed by `soundPlayer.SetSoundsAsync` at lines 394–395.

If disposal stops frame polling, a subsequently faulted world-load task can also lose its normal observation path.

**Incremental change**

Complete the existing lifetime design rather than replacing it:

- Check stopped state after asynchronous boundaries and before adapter calls.
- Coordinate adapter initialization with disposal so an initialization already in progress cannot publish resources after disposal.
- Ensure the outstanding startup/load task is observed during shutdown.
- Keep Home as the single adapter owner.

A flag alone is insufficient if initialization itself can overlap disposal.

**Verification**

Use controllable delayed operations to stop during:

- Catalog loading.
- Canvas import/initialization.
- Audio initialization.
- World loading.

Assert that no new resources or adapter calls appear after shutdown completes.

---

### REV-002 — A side-impact speed can qualify a gentle top contact as a squash

**Priority: Medium · Hazard follow-up · New correctness issue**

**References**

- `Client/Game/World/Body2D.cs:508–513,597–621`
- `Client/Game/Physics/CollisionSystem.cs:411–415`
- `Client/Game/Physics/CollisionSystem.cs:824–844`
- `Tests/World/ContactImpactTests.cs:10–21`

Impact speed is stored using only the **other body** as the key. Contact direction is stored separately.

The kill decision effectively asks:

> Did this pair have a bottom contact, and was their highest impact speed on any contact above the threshold?

That is not equivalent to:

> Was the bottom contact’s closing speed above the threshold?

For example, within one solver step:

- Side contact: closing speed 8.
- Bottom contact with the same hazard: closing speed 1.
- `KillImpactSpeed`: 5.

The stored pair maximum is 8, so the bottom-contact branch classifies this as a kill. Compound shapes and iterative corrections make multiple contact directions a relevant case.

The current test explicitly verifies a maximum **per body**, but does not verify that this is suitable for the directional kill rule.

**Incremental change**

Store impact speed by **contact direction and other body**, then query the `SurfaceBottom` impact for squash classification.

Retaining the maximum across solver iterations is sensible—provided it is the maximum for the relevant direction.

**Verification**

Cover:

- Hard side impact plus gentle top contact: hit.
- Hard top impact plus side contact: kill.
- Later zero-speed iterations: preserve the original top-impact speed.
- Next substep: clear all previous impact speeds.

---

### REV-003 — Hazard routing conflates physical contacts, grip contacts, and overlap

**Priority: Medium · Hazard follow-up · Implementation/documentation mismatch**

**References**

- `Client/Game/Physics/CollisionSystem.cs:397–424`
- `Client/Game/Physics/CollisionSystem.cs:187–234`
- `Client/Game/World/Body2D.cs:623–624`
- `ASCII Hero/docs/Decisions.md:353–360`
- `ASCII Hero/docs/ArchitecturalIntegrityAudit.md:840–842`

The documentation describes two paths:

- Solid hazards: solver-recorded physical contacts.
- Passable hazards: overlap.

The implementation instead uses:

`HasContactWith(hazard) || Overlaps(body, hazard)`

for every hazard.

Furthermore, `HasContactWith` includes **all contact types**, including `Climbable` and `Hangable`. Those are not physical collision-response contacts.

This matters in `IsKillingImpact`: any non-bottom contact causes an early return before the overlap-based test. A passable hazard that also provides a grip capability can therefore take a different kill-classification path merely because a grip contact was recorded.

Conversely, a solid hazard without a recorded physical contact can still enter through the overlap fallback, contrary to the stated contract.

**Incremental change**

Make the distinction explicit:

- Use the relevant **physical surface contacts** for physically resolved hazard pairs.
- Use overlap and relative approach for non-resolving/passable pairs.
- Do not use a grip contact as evidence that a physical collision occurred.

Base this on the collision rules actually supported by the body categories. Do not assume that changing the hazard predicate alone fixes any existing dynamic-body passability semantics.

**Verification**

Include static, kinematic, and dynamic hazards, plus a passable hazard with a climbable/hangable capability. Verify that unrelated capabilities do not change squash versus hit classification.

---

### REV-004 — Resetting edge history to `false` creates fresh presses from held keys

**Priority: Medium · ARCH-012 · Partially achieved intent**

**References**

- `Client/Game/Physics/PhysicsSystem.cs:32–39`
- `Client/Game/Physics/PhysicsSystem.cs:190–205`
- `Client/Game/GameLoop.cs:397–407`

Resetting the transition timer and system history is a clear improvement.

However, `PhysicsSystem.Reset()` sets all previous-key fields to `false`. The first physics step then compares them against currently held keys.

For example, if Down remains held through world loading:

- `_wasDownKeyDown` becomes `false`.
- `downPressedThisFrame` becomes `true`.
- A walking player is changed to Crawl at lines 199–201 without a new press.

`GameLoop` already uses the correct contrasting pattern for action input: it initializes `_wasActionKeyDown` from the current input state.

**Incremental change**

Let the reset operation initialize edge-history fields from current held input, while clearing locomotion-specific history independently.

Do not change ordinary held-input behavior or the existing ground-jump policy as part of this fix.

**Verification**

Compare a new world with Down/Up/Jump already held against the same world with a genuine post-load press. Assert that only the latter creates an input edge.

---

### REV-005 — The test suite does not protect the most consequential changes

**Priority: Medium · ARCH-013 · Useful foundation, incomplete regression protection**

**References**

- `Tests/World/ContactImpactTests.cs:10–39`
- `Tests/World/ShippedWorldsTests.cs:23–30`
- `ASCII Hero/docs/ArchitecturalIntegrityAudit.md:827`

The 40 passing tests are valuable. In particular, loading every shipped world is a good check against accidentally incompatible validation changes.

But:

- Contact-impact tests manipulate contact records directly.
- Shipped-world tests load assets and assert a world exists.
- Neither exercises the full **physics → collision → interaction → removal** path.

The suite does not establish that:

- A slow solid-hazard collision produces damage.
- A sufficiently fast landing produces exactly one kill.
- A falling hazard hits rather than being squashed.
- Multiple contact directions do not mix impact speeds.
- Held keys are initialized correctly across world loads.
- A placement material survives animation/pose changes.
- Stop/disposal works during asynchronous initialization.

**Incremental change**

Add a small number of focused behavioral tests using the existing MSTest setup. No additional framework is needed.

The highest-value additions are tests for REV-001–004, plus one material-persistence test.

**Assessment of the §10 choice**

“Real-asset characterization rather than physics simulation tests” is a reasonable **starting point**, but insufficient as the sole verification strategy for this commit’s physics-related follow-up.

---

### REV-006 — Gravity can still invalidate otherwise validated patrol limits

**Priority: Medium · ARCH-009 · Residual gap, not a newly introduced regression**

**References**

- `Client/Game/World/World2D.Loading.cs:84–89`
- `Client/Game/World/World2D.Loading.cs:569–581`
- `Client/Game/World/DynamicObject2D.cs:292–296,313–320`

Patrol bounds, cruise speeds, and multipliers now receive useful validation.

Gravity still accepts any value parsed by `TryParseDouble`. Dynamic patrol computes its force limits using:

`PatrolMaxForce * mass * gravity`

Negative gravity therefore produces a negative force limit, reversing the minimum and maximum passed to `Math.Clamp`.

The original recommendation explicitly included settling supported gravity semantics. That part remains open.

**Incremental change**

Record and enforce the supported gravity domain:

- If inverted gravity is unsupported, reject it at loading with world/key context.
- If supported, force-limit magnitude must not inherit gravity’s sign.
- Explicitly define zero-gravity behavior and reject non-finite gravity.

**Important distinction:** allowing positive infinity for an explicitly unlimited patrol force cap is intentional. It should not be removed indiscriminately.

---

### REV-007 — Some comments still describe behavior that the code does not have

**Priority: Low · ARCH-001 and follow-up documentation**

Examples:

| Reference | Remaining discrepancy |
|---|---|
| `Client/Game/GameLoop.cs:256–258` | Claims in-flight startup returns without touching adapters; REV-001 shows this is not guaranteed. |
| `Client/Game/World/Body2D.cs:486–490` | Says contacts are never mutated by another system; `PhysicsSystem` deliberately calls `RemoveContact`. |
| `Client/Game/Physics/CollisionSystem.cs:23–28` | Says a hazard never physically pushes a body back out; solid hazards do participate in collision response. |
| `Client/Game/Physics/CollisionSystem.cs:7–10` | Broadly claims no concrete-type special cases; `IsFootLocomotingPlayer` at lines 812–813 checks `Player2D`. |
| `Client/Game/World/IPhysicsBody.cs:16–20` | Includes the world floor as grounding; `ResolveWorldBounds` explicitly records no contacts at `CollisionSystem.cs:447–454`. |
| `Client/Game/Browser/CanvasBridge.cs:5–7` | Still says reporting happens after font loading, but the JS initializer does not await font loading. |
| `Client/Game/Physics/InteractionHandler.cs:7–10` | Describes hazard “overlaps” even though solid hazard detection now includes contacts. |

There are also remaining sentence fragments around `Body2D`’s contact/snapshot comments.

**Incremental change**

Correct these specific statements. Do not undertake another broad documentation rewrite.

The original audit sections are historical; their “Awaiting approval” entries should not be mistaken for current implementation documentation. §10 is the outcome record.

---

## 2. Verdict per recommendation

“Achieved” means the implementation meets the intended scope based on the reviewed source; it is not a blanket guarantee of all runtime behavior.

| ID | Verdict | Assessment and references |
|---|---|---|
| **ARCH-001** | **Partial** | Several useful corrections were made, but the concrete inaccuracies in REV-007 remain. |
| **ARCH-002** | **Achieved** | `ParseDoubleOrDefault` makes the existing fallback explicit without changing parsing behavior. `Client/Game/Assets/IniValueParser.cs:35–36`; parser tests pass. |
| **ARCH-003** | **Achieved** | Dynamic and kinematic patrol configuration now use named arguments. `World2D.Loading.cs:569–601`. |
| **ARCH-004** | **Achieved** | Contact/step tracking is grouped at the end of `Body2D`, beginning around line 486. Keeping the class intact was appropriate. |
| **ARCH-005** | **Achieved; regression test needed** | Material identity is initialized from the sprite and overridden during world construction; `ApplyFrame` no longer recomputes it. `Body2D.cs:280–293,478–483`; `World2D.Loading.cs:511–534`. |
| **ARCH-006** | **Achieved** | Only 404 returns `null`; other unsuccessful responses throw with context. `Client/Game/Assets/IAssetFileProvider.cs:17–34`. HTTP tests pass. |
| **ARCH-007** | **Achieved** | Declared pose defaults and required player poses are validated; unknown requested poses throw. `SpriteLoader.cs:182–251`; `Player2D.cs:181–198`; `Body2D.cs:374–377`. |
| **ARCH-008** | **Achieved; negative tests needed** | Missing and duplicate player placements are rejected. `World2D.Loading.cs:195–198,319–322`. |
| **ARCH-009** | **Mostly achieved** | Finite/nonnegative patrol amounts and ordered bounds are checked. `World2D.Loading.cs:569–645,748–759`. Gravity remains unresolved: REV-006. |
| **ARCH-010** | **Achieved** | The focus listener is stored and removed using the same pattern as keyboard listeners. `Client/wwwroot/js/game-interop.js:39–40,121–123`. |
| **ARCH-011** | **Partial** | Correct shutdown ordering, but incomplete handling of asynchronous work: REV-001. |
| **ARCH-012** | **Partial** | Transition, collision, physics, and animation histories reset. Held-key initialization remains inconsistent: REV-004. `GameLoop.cs:397–407`; `AnimationSystem.cs:16–21`. |
| **ARCH-013** | **Foundation achieved** | Existing MSTest setup is useful and all 40 tests pass. Behavioral coverage needs the targeted additions in REV-005. |
| **ARCH-014** | **Achieved as the selected organizational option** | `World2D.Loading.cs` separates navigation without pretending to establish a new runtime boundary. A separate loader was not necessary. |
| **ARCH-015** | **Achieved structurally** | `InteractionHandler` owns consequences; collision owns detection/history. `InteractionHandler.cs:19–70`; `CollisionSystem.cs:329–394`. Hazard classification itself still needs REV-002/003. |
| **ARCH-016** | **Achieved** | `Step` now shows meaningful phases while retaining the reviewed sequencing, including committing velocity before the motor calculation. `PhysicsSystem.cs:42–60`. No controller framework was introduced. |
| **ARCH-017** | **Achieved** | `EndScreenRenderer` builds glyphs and owns presentation details; mode/input remain outside it. `EndScreenRenderer.cs:38–76`. |
| **ARCH-018** | **Achieved** | The diff is a rename from held “Pressed” state to `…KeyDown`, with mappings and caller-owned edge detection preserved. `Client/Game/Browser/InputState.cs:12–57`. |

---

## 3. Assessment of the principal §10 choices

### ARCH-005 — Fix material at spawn

**Sound.** The current asset model does not require per-frame material changes. Removing the dominant-material calculation is simpler and more coherent than retaining unused precedence machinery.

The important invariant is now clearer:

> Sprite default or placement override determines material identity; animation does not.

I would keep this choice and add the missing persistence test—not reintroduce per-frame material selection.

### ARCH-014 — Use a partial class

**Sound for the stated goal.** It improves source organization while preserving private access and avoiding a new construction API.

It does **not** reduce `World2D`’s logical responsibilities or dependencies. That is acceptable because §10 explicitly chose the organizational option and did not claim otherwise.

### ARCH-015 — Concrete `InteractionHandler`

**Sound.** A static concrete handler is sufficient here. There is no need for an interface, event bus, factory, or injected policy framework.

The direction of dependency remains straightforward: collision detects, handler applies consequences. The remaining issues are in detection/classification, not evidence that the extraction was wrong.

### ARCH-016 — Named phases, no new class

**Sound.** This is the least disruptive improvement to the long physics method. The reviewed diff preserves the significant order of state resolution, impulses, motor calculation, and integration.

### ARCH-018 — Rename only

**Sound.** It clarifies held-state semantics without centralizing edge detection prematurely. REV-004 concerns world-reset initialization, not the rename.

---

## 4. Hazard follow-up: correctness and state lifetime

### What is correct

**Generic capability dispatch**

The kill decision checks `IKillerBody` and `IKillableBody`; it does not identify particular sprite names or concrete hazard classes.

- `CollisionSystem.cs:367–369`
- The kill threshold is loaded once and assigned to the hazard variants.
- No player-specific squash bounce was introduced.

**Pre-response speed capture**

The solver computes relative normal speed before changing velocities:

- `CollisionSystem.cs:742–754`

It records that captured value afterward:

- `CollisionSystem.cs:804`

This avoids losing the impact speed when collision response brings the bodies to rest.

**Reciprocal contact direction**

`RecordContact` records opposite surface directions on the two bodies:

- `CollisionSystem.cs:824–844`

For an ordinary hazard falling from above onto the player, the player receives a top—not bottom—contact, so it does not enter the bottom-contact squash branch.

**Substep-local clearing**

All bodies are snapshotted and cleared at the beginning of every `Resolve`:

- `CollisionSystem.cs:46–53`

Both contact and impact maps are cleared:

- `Body2D.cs:634–644`

Hazard classification runs after resolution:

- `CollisionSystem.cs:133`

Consequently, I found **no cross-substep stale-impact leak in the normal resolve path**. Keeping a maximum during the iterations of one resolve is also sensible; otherwise later resting iterations could erase the original impact.

### What prevents full approval

1. **The retained maximum loses contact direction** — REV-002.
2. **Any-contact and overlap fallback are mixed rather than explicitly routed** — REV-003.
3. **The tests do not exercise the complete solver path** — REV-005.

These are local corrections. They do not justify reverting contact-based detection or introducing a new collision architecture.

---

## 5. Suggested incremental follow-up

In practical order:

1. **Complete asynchronous shutdown** — REV-001.
2. **Associate impact speed with contact direction** — REV-002.
3. **Make physical-contact versus overlap routing explicit** — REV-003.
4. **Initialize edge history from held input on world reset** — REV-004.
5. **Add focused regression tests alongside those fixes** — REV-005.
6. **Settle the remaining gravity contract** — REV-006.
7. **Correct the listed comments and documentation** — REV-007.

Each can be approved separately, with tests accompanying the affected behavior.

**Bottom line:** retain the implemented structural choices. The work improves organization without architectural inflation. The remaining concerns are specific lifetime and behavioral-contract gaps—not a reason to redesign ASCII Hero.
