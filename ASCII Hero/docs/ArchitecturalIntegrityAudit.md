# ASCII Hero — Architectural Integrity Audit

> This report records a read-only static audit. Saving this document does not implement or approve any recommendation. All recommendations remain awaiting approval. Statements about unchanged files refer to the audit itself, before this report was saved.

## 1. Executive summary

**The architecture is fundamentally sound. It does not need a redesign.**

The implementation preserves the most important boundaries:

- Blazor hosts the application; it does not act as the real-time rendering loop.
- Gameplay runs in C#, using continuous coordinates.
- Canvas and Web Audio interaction are isolated in small browser adapters.
- Physics and collision operate through a shared body model and capability interfaces.
- Assets use a common loading pipeline rather than separate loaders for each object category.
- The code favors explicit construction and straightforward control flow over elaborate infrastructure.

The main concern is **not excessive abstraction**. It is that some responsibilities and state have accumulated in a few central classes, while comments and initialization contracts have not always kept up.

### Most important findings

1. **Material identity has conflicting owners.**  
   World loading applies a placement-level `Material` override, but subsequent animation or pose changes overwrite `Body2D.MaterialName`. Rendering and medium resolution consume that property.

2. **Asset contracts are inconsistently enforced.**  
   HTTP failures other than 404 are treated as missing files. Missing pose definitions can silently do nothing despite documentation describing them as errors. World loading does not explicitly enforce a valid player placement.

3. **World-specific state outlives individual worlds.**  
   `GameLoop` retains physics, collision, and animation systems across loads. Some of their state describes the previous player/world. The pending-transition timer also lacks a world-load reset.

4. **Browser cleanup is incomplete.**  
   The canvas-focus `pointerdown` listener is never removed. More broadly, component disposal and in-flight startup/loading do not share an explicit lifetime contract.

5. **The strongest structural candidates are `World2D`, `CollisionSystem`, and `PhysicsSystem`.**  
   Each has a meaningful responsibility boundary worth examining—but none should be fragmented merely to reduce line count.

6. **Regression protection is insufficient for confident structural work.**  
   The repository contains two application projects, no separate test project was found, and Test Explorer returned no matching tests.

### Audit scope and confidence

This was a **read-only static audit** of application entry points, core game systems, body classes, asset handling, rendering paths, browser interop, and relevant documentation. Supporting components received varying levels of inspection; this is not a proof that every asset or execution path is correct.

- No repository files were changed during the audit.
- Git status remained clean during the audit.
- No builds, tests, profiling, or debugging sessions were run.
- Runtime consequences below are distinguished from directly confirmed source facts.
- Recommendations that correct error handling or state behavior are **not presented as behavior-neutral refactorings**.

**Suggested approach:** approve a few targeted corrections and a small regression suite before considering structural extraction.

---

## 2. Current architecture

Paths below are relative to `ASCII Hero/`. Most game code is under `ASCII Hero.Client/Game/`. Source line references describe the implementation at the time of the audit.

### Components and responsibilities

| Component | Actual responsibility |
|---|---|
| Host project | ASP.NET Core/Blazor page hosting and local development support. References the client project. |
| Client `Program.cs` | WebAssembly startup and `HttpClient` registration; conditional root-component registration supports standalone publishing. |
| `Pages/Home.razor` | Creates `CanvasBridge`, `WebAudioSoundPlayer`, and `GameLoop`; starts the game and disposes browser adapters. |
| `GameLoop` | Startup, game modes, asynchronous world loading, fixed-step scheduling, transitions, camera updates, sound dispatch, HUD/debug/modal presentation. |
| `World2D` | Loaded world data, object collection, player/camera references, removals, sounds, interactions, respawn state—and world loading/placement construction. |
| `Body2D` hierarchy | Shared sprite/frame state, collision shape, contacts, physical properties, animation, and specialized object behavior. |
| `PhysicsSystem` | Player locomotion decisions, force accumulation, medium effects, patrol/kinematic stepping, and integration. |
| `CollisionSystem` | Broad/narrow-phase collision, response, contact bookkeeping, climb/hang contacts, hazard/pickup consequences, and effect spawning. |
| Asset subsystem | Text/INI parsing, path fallback, sprite assembly, palettes/materials/sounds, and world summaries. |
| Rendering subsystem | Camera transforms, glyph construction, world/menu/loading rendering, UI widgets, and animation-driven sounds. |
| Browser subsystem | Keyboard forwarding, animation-frame callbacks, canvas drawing, and audio synthesis. |

### Important relationships

The main execution chain is:

**`Home` → `GameLoop` → world/systems/renderers → browser adapters**

The body hierarchy provides movement categories:

- `StaticObject2D`
- `DynamicObject2D`
- `KinematicObject2D`
- `Player2D`

Capabilities such as `IPhysicsBody`, `IGravityAffected`, `ICollectorBody`, `IHazardBody`, and `IWalkForceBody` allow systems to operate across those categories.

`TwoStateObject2D` provides a coherent shared mechanism for switches, gates, and devices. Hazard subclasses add hazard-related capabilities to different movement categories.

This is **a lightweight object-oriented game architecture with system-style processing**, not an ECS. That is appropriate for the current project.

### State ownership

State is primarily instance-owned, not globally shared:

- `GameLoop`: application mode, timers, input history, current world, active load.
- `World2D`: world membership, player, environmental data, interaction queues.
- Bodies: position, velocity-related state, contacts, animation and gameplay attributes.
- Systems: additional temporal state, such as previous input and airborne/contact history.
- JavaScript modules: canvas/audio handles and event listeners.

The weakness is that **ownership and lifetime do not always align**. Some system fields are effectively player/world state but live for the entire `GameLoop`.

### Data and frame flow

1. Startup reads global configuration, catalogs, colors, and sounds and initializes browser adapters.
2. The world-selection model handles input; separate rendering code draws it.
3. Selecting a world starts a task that is polled while loading frames continue.
4. World loading:
   - Reads world settings and layers.
   - Resolves global/world-local libraries.
   - Loads each distinct sprite with the required clip union.
   - Constructs placements and resolves links.
   - Publishes the completed world to the loop.
5. During play:
   - Handle action input.
   - Run accumulated fixed physics steps, subdividing fast motion.
   - Resolve collisions and apply pending removals after each substep.
   - Dispatch sounds and advance transitions.
   - Advance animation/effect state.
   - Update the camera.
   - Build glyphs, append overlays, and draw through interop.

Relevant implementations: [GameLoop.cs](../ASCII%20Hero.Client/Game/GameLoop.cs), [World2D.cs](../ASCII%20Hero.Client/Game/World/World2D.cs), [Home.razor](../ASCII%20Hero.Client/Pages/Home.razor).

---

## 3. Naming and consistency findings

### What is already consistent

- C# types and public members generally use PascalCase.
- Private fields generally use `_camelCase`.
- Async operations usually use the `Async` suffix.
- Interface names describe capabilities rather than implementation categories.
- Units are often explicit: `Seconds`, `Pixels`, `Cells`, and axis suffixes are valuable.
- The `2D` suffix is consistent across world objects and mathematical types.
- `Collectable` is consistently used; changing its spelling would provide little value.

### Meaningful inconsistencies

| Finding | Assessment |
|---|---|
| `IniValueParser.ParseDouble` returns zero on failure | Misleading relative to ordinary .NET `Parse` expectations. Its summary incorrectly calls this the standard .NET convention. |
| `InputState.Is…Pressed` means currently held | Understandable, but potentially confused with a fresh press. Edge detection is implemented separately in consumers. |
| `Body2D.IsStatic` is true for moving kinematic objects | Means “not dynamically displaced by collision response,” not “never moves.” Also influences render ordering. Document carefully; do not casually rename or redefine it. |
| `Player2D` comments refer to `Pose` where code now uses `Stance` | Concrete documentation drift, including an unresolved `cref`. |
| `Fore`/`Back` appear in names such as `BackgroundFore` | Compact but initially confusing: layer position and cell color channel are different concepts. Existing consistency makes a repository-wide rename low value. |
| `defaultDefaultFrame` in `SpriteLoader` | A small readability blemish, not a structural concern. |
| `Confirmed` versus `IsIntroComplete` | Minor Boolean naming inconsistency. Not worth a standalone migration. |
| `TryGetValue` returning a nullable value rather than `bool`/`out` | Nonstandard but consistent in the small asset APIs. Clarification is more valuable than wholesale renaming. |

### Runtime-sensitive names

These must not be treated as ordinary cosmetic identifiers:

- `OnFrame`, `OnKeyDown`, and `OnKeyUp`: invoked by name from JavaScript.
- JavaScript exports such as `initialize`, `drawFrame`, and `dispose`.
- `CellMetrics` properties and sound-definition properties used across JSON interop.
- INI keys and values such as `Kind`, `Type`, `Material`, `Default`, and `Mode`.
- Pose names, clip names, directional suffixes, asset paths, and filenames.
- Enum names parsed from authored configuration.

Private-member renames and ordinary C# helper renames are much safer, provided these contracts remain unchanged.

**Conclusion:** naming is sufficiently consistent overall. Favor a few semantic clarifications, not a broad naming campaign.

---

## 4. Class structure assessment

| Class/subsystem | Assessment |
|---|---|
| `World2D` | Strong runtime role, but loading and placement interpretation form a substantial second responsibility. Best candidate for a loading boundary. |
| `CollisionSystem` | Solver functionality is cohesive. Scoring, lives, keys, checkpoints, and level completion are a separate policy concern embedded within it. |
| `PhysicsSystem` | Shared integration is coherent. Player input/state-machine logic dominates the opening method and deserves clearer organization. |
| `GameLoop` | Appropriate orchestrator, but also owns presentation details and has an incomplete lifetime/reset contract. |
| `Body2D` | Large, but much of its functionality belongs together. Frame, silhouette, size, pose, and animation must remain synchronized. Improve organization before splitting. |
| `Player2D` | Reasonably focused on player capabilities and state. Much of the behavioral complexity lives elsewhere. |
| `DynamicObject2D` | Force-based patrol and pose intent are related. Keep together. |
| `KinematicObject2D` | Prescribed-motion behavior is distinct from dynamic patrol; sharing the algorithms would be misleading. |
| Hazard subclasses | Repeated capability properties are acceptable given different base movement categories. |
| `TwoStateObject2D` and derivatives | A useful, concrete abstraction. Preserve it. |
| `SpriteLoader` | Cohesive assembly of one asset from settings and layers. Does not warrant a framework or factory hierarchy. |
| `IniDocument` / `IniOverrideLoader` | Small, useful abstractions with clear purposes. Validation policy needs attention around them, not replacement. |
| `WorldRenderer` / `Camera` / `GlyphBuilder` | Focused and understandable. Some small duplication is acceptable. |
| `WorldSelectScreen` | Good separation of menu state/input from rendering. |
| `CanvasBridge` / `WebAudioSoundPlayer` | Appropriately thin. Lifetime and failure handling deserve local corrections. |

---

## 5. Detailed class-level findings

### 5.1 `World2D`: runtime aggregate plus loading pipeline

**Currently does:** world membership, environmental data, player/camera references, deferred work, interactions, respawn, asset loading, placement parsing, object construction, material application, and link resolution.

The runtime responsibilities are related. The loader is also related to the world, but changes for different reasons: asset schema evolution and construction rules rather than simulation behavior.

The loading implementation begins at `LoadAsync` around line 358; placement construction and typed `ObjectSection` parsing occupy much of the remainder.

**Why this matters:** a developer investigating a gameplay interaction must navigate the same class that implements file-format interpretation. Construction assumptions are spread across spawning and post-spawn configuration.

**Keep together:** object membership, removal queues, world interaction rules, respawn state, and world-owned data.

**Possible separation:** loading code only. Do not introduce a separate factory per object kind.

Additional observations:

- `Objects` is a public mutable `List<Body2D>`. This leaves mutation discipline conventional rather than enforced.
- However, the hazard/effect path explicitly snapshots before adding effects. It would be incorrect to claim every direct addition is currently unsafe.
- Color precomputation calls `GlyphBuilder.ResolveColor`, creating a source-level World→Rendering dependency. Since this helper is pure and browser-free, this is a modest boundary compromise—not grounds for a new service layer.

### 5.2 `Body2D`: coherent mechanics, one conflicting invariant

`Body2D` combines contacts, physical properties, sprite/frame state, animation, and shape derivation.

Most of that is justified: changing the frame also changes the silhouette used for collision. Separating animation from shape updates without a clear protocol could make correctness worse.

The important exception is material ownership:

- `World2D.ApplyMaterial`, lines 789–811, resolves physical coefficients and applies a placement material name.
- `Body2D.ApplyFrame`, lines 692–698, subsequently recomputes `MaterialName` from frame data.
- `WorldRenderer` and `PhysicsSystem` read that name.

**Confirmed fact:** a later frame change can replace the placement override while the previously resolved physical coefficients remain unchanged.

**Potential consequence:** visual material defaults and medium identity can diverge from placement intent. This was not reproduced in a running game.

Internal organization is also uneven: extensive contact APIs precede basic body state, and some related helpers are separated by unrelated members. A member-order pass is safer than immediately splitting the class.

### 5.3 `PhysicsSystem`: general integration with player-specific control

`Step`, lines 32–395, combines:

- Climb/hang/swim engagement.
- Stance changes.
- Input-edge tracking.
- Movement intent.
- Motor target decisions.
- Jump impulses.
- Support reactions.

Later methods implement more general force and medium calculations.

The issue is not that player motion must be separate from physics at all costs. It is that the top-level method makes **control policy, temporal state, and integration sequencing difficult to distinguish**.

Some state is also inconsistently located:

- Climb suppression resides in `PhysicsSystem`.
- Hang suppression resides on the player through `IHangerBody`.

These can be justified individually, but the lifetime difference should be deliberate.

**Keep together:** integration, force accumulation, medium calculations, traction, and support reactions.

**First improvement:** organize the player-control phase into a few meaningful private methods. Consider a separate controller only if that yields an actual ownership benefit.

### 5.4 `CollisionSystem`: geometry and gameplay outcomes

The solver includes genuinely interdependent work: contact generation, iterative response, friction, correction, and contact bookkeeping. Splitting those into many classes would make execution order harder to follow.

The clearer boundary is `ResolveHazardsAndCollectables`, lines 325–523. It includes player-specific score/life/ring/key/checkpoint/transition rules.

This contradicts an overly broad reading of comments claiming systems operate only through capabilities. The implementation uses a useful mixture of capability-based detection and concrete gameplay policies.

**Keep together:** broad phase, narrow phase, contact response, geometry overlap decisions, and contact history.

**Potential extraction:** consequences of a detected interaction—not a generalized event bus.

### 5.5 `GameLoop`: appropriate coordinator with lifetime and presentation drift

Its game-mode enum and explicit mode dispatch are suitable. No state-machine framework is needed.

The stronger concerns are:

- It has no explicit stop/disposal contract.
- Startup and world loading can outlive component disposal.
- It retains systems containing world-specific temporal state.
- Its end-screen ripple geometry, colors, label/frame layout, and rendering details expand its orchestration role.

A concrete reset issue exists: `_pendingTransitionElapsedSeconds` is reset when a transition completes, but not when a new world loads. Exiting during an unfinished transition can therefore carry elapsed time into a later world.

The loop also checks `PendingTransition` before entering the fixed-step batch, not after every substep. Remaining substeps can continue after a transition is triggered. This deserves a regression scenario before structural edits; it is not evidence that the entire timestep design is wrong.

### 5.6 Assets and browser adapters

The asset layering is good. Problems arise at contracts:

- `HttpAssetFileProvider`, lines 15–18, returns `null` for every unsuccessful HTTP response, although its interface documents missing-file behavior.
- `SpriteLoader.ParsePoses` does not enforce a valid declared default.
- `Body2D.SetPose`, lines 586–591, silently returns for a missing pose.
- `AssetFormat.md`, lines 384–385 and 416–427, describes stronger requirements.
- `World2D` unconditionally adds its player after placement scanning without explicitly validating that it was spawned.

Browser cleanup has a definite omission:

- `game-interop.js:38` registers an anonymous `pointerdown` listener.
- `dispose`, lines 106–119, cannot remove it.

Other lifecycle concerns are risks rather than demonstrated failures: callbacks and asynchronous loads can remain in flight while adapters are being disposed.

### 5.7 Documentation and implementation patterns

There is useful reasoning in the comments, particularly around contact response, traction, and movement intent. Preserve that reasoning.

However, documentation drift is noticeable:

- `CanvasBridge` claims to be the only JavaScript boundary, although audio has its own adapter.
- `CellMetrics` describes measured glyph dimensions, while JavaScript returns configured dimensions.
- `World2D.ApplyPendingRemovals` says once per frame, while the loop calls it per substep and on an action path.
- `Structure.md` describes `IProgress<int>`; loading uses `Progress<double>`.
- `Player2D.ResolvedPose` references the former `Pose` member.
- Several summaries contain incomplete sentences or malformed XML.

These are small defects individually, but collectively they reduce trust in the documentation.

---

## 6. Incremental recommendations

Each recommendation is independently selectable unless explicitly stated otherwise. Approval of a group is not required.

### ARCH-001 — Repair current-state documentation and comments

- **Category:** Documentation.
- **Priority:** Medium.
- **Scope:** `CanvasBridge`, `Body2D`, `Player2D`, `GameLoop`, `World2D`, and relevant architecture/structure sections.
- **Current situation:** Comments contain outdated ownership, timing, and naming claims.
- **Problem:** Readers can form an incorrect model of frame order, material lifetime, or browser boundaries.
- **Proposed improvement:** Correct the specific discrepancies listed in §5.7; repair broken summaries; explain `IsStatic` as response semantics. Preserve useful rationale.
- **Expected benefit:** More reliable navigation and fewer maintenance mistakes.
- **Behavioural risk:** None expected; documentation only.
- **Dependencies:** None.
- **Independence:** Independent. Individual files/topics may also be approved separately.
- **Implementation complexity:** Small.
- **Verification:** Compare each statement against its implementation; check documentation references and build diagnostics during implementation.
- **Decision:** Approved and implemented.

### ARCH-002 — Make numeric fallback naming explicit

- **Category:** Naming.
- **Priority:** Low.
- **Scope:** `IniValueParser.ParseDouble` and its C# callers.
- **Current situation:** `ParseDouble` silently returns zero on failure.
- **Problem:** The name and “standard .NET convention” comment suggest a different contract.
- **Proposed improvement:** Rename it to `ParseDoubleOrDefault` and document the existing zero fallback. Do not change parsing behavior.
- **Expected benefit:** Call sites reveal that malformed or absent values can become defaults.
- **Behavioural risk:** None expected; ordinary C# rename with unchanged implementation and asset keys.
- **Dependencies:** None.
- **Independence:** Independent.
- **Implementation complexity:** Small.
- **Verification:** Confirm null, empty, invalid, and valid inputs produce exactly the previous results; update all references.
- **Decision:** Approved and implemented.

### ARCH-003 — Use named arguments for dynamic patrol configuration

- **Category:** Readability.
- **Priority:** Low.
- **Scope:** `World2D.ConfigureDynamicPatrol`, particularly lines 847–855.
- **Current situation:** A long positional argument list passes paired axis limits, speeds, multipliers, directions, and force caps.
- **Problem:** Correctness is difficult to review, and adjacent same-typed values are easy to transpose.
- **Proposed improvement:** Use named arguments without changing values or evaluation order.
- **Expected benefit:** Easier comparison between authored settings and patrol parameters.
- **Behavioural risk:** None expected.
- **Dependencies:** None.
- **Independence:** Independent.
- **Implementation complexity:** Small.
- **Verification:** Compare each named parameter against the existing positional mapping; build.
- **Decision:** Approved and implemented.

### ARCH-004 — Reorganize `Body2D` without splitting it

- **Category:** Internal organization.
- **Priority:** Low.
- **Scope:** `Body2D.cs`.
- **Current situation:** Contacts, fundamental state, physical properties, animation, and helpers are interleaved.
- **Problem:** Top-to-bottom reading does not quickly establish the class’s model.
- **Proposed improvement:** Group basic state, physical/material properties, contact APIs, sprite/animation operations, and private helpers. Preserve field-initializer order.
- **Expected benefit:** Better discoverability without introducing new boundaries.
- **Behavioural risk:** Low; mechanical movement must not reorder initialization.
- **Dependencies:** None.
- **Independence:** Independent.
- **Implementation complexity:** Small.
- **Verification:** Review a movement-only diff; verify initialization order; build and run relevant regression checks.
- **Decision:** Approved and implemented.

### ARCH-005 — Preserve placement material identity across frame changes

- **Category:** Class structure.
- **Priority:** High.
- **Scope:** `Body2D.ApplyFrame`, `World2D.ApplyMaterial`, material consumers.
- **Current situation:** Spawn configuration and frame application both assign `MaterialName`.
- **Problem:** Frame changes can erase a placement override while resolved physical coefficients retain their spawn values.
- **Options and trade-offs:** Keep an explicit placement override separate from frame-derived material, or make effective material identity a spawn-owned value. The latter is simpler but requires confirming that frame-dependent material identity is never intended.
- **Proposed improvement:** Prefer explicitly preserving the placement override and applying it ahead of the frame-derived default. Keep mass behavior unchanged.
- **Expected benefit:** One clear precedence rule throughout the body’s lifetime.
- **Behavioural risk:** Medium; corrects behavior for affected animated/posed objects and must not unintentionally change material precedence.
- **Dependencies:** None; focused regression coverage is strongly recommended.
- **Independence:** Independent.
- **Implementation complexity:** Small.
- **Verification:** Spawn an overridden material, advance animation, change pose/state, and check effective material, physical coefficients, rendering defaults, and medium behavior.
- **Decision:** Approved and implemented.

### ARCH-006 — Distinguish missing assets from HTTP failures

- **Category:** Consistency.
- **Priority:** Medium.
- **Scope:** `HttpAssetFileProvider.TryReadTextAsync`.
- **Current situation:** All non-success responses return `null`.
- **Problem:** A server or authorization failure can trigger an optional-file fallback or misleading “missing asset” diagnosis.
- **Proposed improvement:** Return `null` for 404; propagate other unsuccessful statuses with HTTP/path context.
- **Expected benefit:** Fallback means “absent,” not “failed to retrieve.”
- **Behavioural risk:** Low; valid loads are unchanged, but error behavior intentionally becomes more explicit.
- **Dependencies:** None.
- **Independence:** Independent.
- **Implementation complexity:** Small.
- **Verification:** Test 200, 404, 403, 500, and transport failure; confirm world-local fallback occurs only for absence.
- **Decision:** Approved and implemented.

### ARCH-007 — Enforce the documented pose contract

- **Category:** Consistency.
- **Priority:** Medium.
- **Scope:** `SpriteLoader.ParsePoses`, player initialization, `Body2D.SetPose`.
- **Current situation:** Invalid defaults and missing poses can survive loading or silently leave the previous frame active.
- **Problem:** This differs from the documented hard-error contract and can leave a player incompletely initialized.
- **Proposed improvement:** Validate a declared pose set’s default; validate the player’s required code-driven poses when applicable; distinguish “no pose system” from “missing requested pose.”
- **Expected benefit:** Asset mistakes fail with useful asset/pose context.
- **Behavioural risk:** Medium; invalid assets that previously limped along may now be rejected. Preserve explicitly supported no-`[Poses]` behavior.
- **Dependencies:** None.
- **Independence:** Independent.
- **Implementation complexity:** Medium.
- **Verification:** Cover missing/unknown defaults, absent required poses, valid directional fallback, and supported single-clip assets.
- **Decision:** Approved and implemented.

### ARCH-008 — Validate the player-placement invariant

- **Category:** Other — validation.
- **Priority:** Medium.
- **Scope:** `World2D.LoadAsync` and player spawning.
- **Current situation:** The player object is created eagerly and added after scanning, without checking that a valid player placement initialized it.
- **Problem:** Missing placement can produce a nominally loaded world containing an uninitialized player; duplicate placements silently reuse the same instance.
- **Proposed improvement:** Require exactly one player placement for the current playable-world model and report the world name on failure.
- **Expected benefit:** Construction guarantees match what runtime systems assume.
- **Behavioural risk:** Low; invalid worlds are rejected earlier.
- **Dependencies:** None.
- **Independence:** Independent.
- **Implementation complexity:** Small.
- **Verification:** Test zero, one, and multiple player placements; verify existing worlds satisfy the invariant.
- **Decision:** Approved and implemented.

### ARCH-009 — Validate numeric domains used by patrol clamps

- **Category:** Consistency.
- **Priority:** Medium.
- **Scope:** Patrol configuration in `World2D`, numeric readers, and `DynamicObject2D` assumptions.
- **Current situation:** Several patrol values use permissive `Number` parsing despite later serving as speed/force limits.
- **Problem:** A negative cruise speed can invert `Math.Clamp` bounds; non-finite values can enter calculations. Other placement speeds already have stricter readers.
- **Proposed improvement:** Validate finite, supported ranges for patrol speed and force-limit inputs. Preserve optional defaults; explicitly settle supported gravity semantics rather than silently assuming them.
- **Expected benefit:** Invalid authoring is diagnosed at load time rather than inside simulation math.
- **Behavioural risk:** Medium; narrows accepted configuration. This is not permission to make every INI parser strict.
- **Dependencies:** None.
- **Independence:** Independent.
- **Implementation complexity:** Medium.
- **Verification:** Cover negative, zero, positive, non-finite, absent, and malformed inputs; confirm valid patrol behavior is unchanged.
- **Decision:** Approved and implemented.

### ARCH-010 — Remove the canvas-focus listener during disposal

- **Category:** Consistency.
- **Priority:** Medium.
- **Scope:** `wwwroot/js/game-interop.js`.
- **Current situation:** `pointerdown` uses an anonymous handler that captures the canvas.
- **Problem:** Disposal removes keyboard listeners but leaves this window-level listener attached.
- **Proposed improvement:** Store the handler and remove/reset it during disposal.
- **Expected benefit:** Correct cleanup and no retained focus action for a disposed canvas.
- **Behavioural risk:** Low; active-page focus behavior should remain identical.
- **Dependencies:** None.
- **Independence:** Independent.
- **Implementation complexity:** Small.
- **Verification:** Repeatedly enter/leave the page; confirm one active listener and none after disposal.
- **Decision:** Approved and implemented.

### ARCH-011 — Give game startup and loading an explicit lifetime

- **Category:** Class structure.
- **Priority:** Medium.
- **Scope:** `Home.razor`, `GameLoop`, browser adapters.
- **Current situation:** Home disposes adapters, but the loop has no stop contract for pending startup, loading, or frame work.
- **Problem:** Ownership is clear at construction but not during asynchronous teardown.
- **Options and trade-offs:** Home can remain the resource owner while the loop gains `StopAsync`, or the loop can own and dispose its adapters. Mixing both models risks double disposal.
- **Proposed improvement:** Prefer retaining Home’s ownership, adding explicit loop stopping/cancellation, and preventing late continuations from using disposed resources. Stop callbacks before releasing their .NET target.
- **Expected benefit:** Predictable navigation and failure cleanup.
- **Behavioural risk:** Medium; asynchronous ordering changes require targeted checks.
- **Dependencies:** None; ARCH-010 remains separately approvable.
- **Independence:** Independent.
- **Implementation complexity:** Medium.
- **Verification:** Leave during startup, loading, and active play; simulate failed initialization; confirm no late interop calls or unobserved teardown failures.
- **Decision:** Approved and implemented.

### ARCH-012 — Reset world-specific temporal state explicitly

- **Category:** Consistency.
- **Priority:** Medium.
- **Scope:** `GameLoop.LoadWorldAsync`, `PhysicsSystem`, `AnimationSystem`, relevant collision history.
- **Current situation:** Systems are reused, but only selected state is reset on world load.
- **Problem:** Previous-world input/airborne history and an unfinished transition timer can influence a new world.
- **Options and trade-offs:** Recreate world-scoped systems, or expose narrow reset operations. Recreation makes lifetime clearer; resets preserve existing ownership and avoid replacing unrelated configuration.
- **Proposed improvement:** Define a world-start reset contract, reset the transition timer, and initialize input history from current held keys where appropriate—not blindly to `false`.
- **Expected benefit:** New-world behavior no longer depends on how the previous world ended.
- **Behavioural risk:** Medium; input-edge semantics must be preserved intentionally.
- **Dependencies:** None.
- **Independence:** Independent.
- **Implementation complexity:** Medium.
- **Verification:** Load after exiting airborne, while holding controls, and during a pending transition; check first-frame input, sounds, and transition duration.
- **Decision:** Approved and implemented.

### ARCH-013 — Establish a small behavior-characterization test suite

- **Category:** Other — testability.
- **Priority:** High before structural refactoring.
- **Scope:** A proposed test project covering pure game/asset code.
- **Current situation:** No separate test project or matching discovered tests was found.
- **Problem:** Movement and state ordering are subtle enough that builds and manual play alone provide weak protection for refactoring.
- **Proposed improvement:** Start with narrowly selected tests: asset fallback, material persistence, pose/player initialization, collision response, and new-world state reset. Use an in-memory asset provider; do not introduce a mocking framework without need.
- **Expected benefit:** Objective evidence that individual improvements preserve valid behavior.
- **Behavioural risk:** None expected for shipped gameplay; test infrastructure requires separately approved configuration/dependencies.
- **Dependencies:** None.
- **Independence:** Independent; provides verification support for other recommendations.
- **Implementation complexity:** Medium.
- **Verification:** Tests should distinguish existing characterization from intended bug corrections and fail when the protected behavior is deliberately broken.
- **Decision:** Approved and implemented.

### ARCH-014 — Separate world loading from runtime world responsibilities

- **Category:** Class structure.
- **Priority:** Medium.
- **Scope:** `World2D.LoadAsync`, placement parsing/spawning, loading helpers.
- **Current situation:** Runtime state and asset construction occupy the same class.
- **Problem:** Different maintenance concerns share one large navigation surface.
- **Options and trade-offs:** See §7.1: private grouping, a loading partial file, or a concrete loader.
- **Proposed improvement:** Prefer a concrete loader only if runtime/loading separation is approved; use a partial file if the goal is navigation alone. Retain the explicit kind switch.
- **Expected benefit:** Clearer construction invariants and easier loading tests.
- **Behavioural risk:** Medium; spawn order, fallback rules, caching, and material application must remain unchanged.
- **Dependencies:** None; ARCH-013 strongly recommended first.
- **Independence:** Independent; the listed options are alternatives.
- **Implementation complexity:** Medium.
- **Verification:** Compare loaded world structure and properties across existing worlds; test invalid assets and fallback cases.
- **Decision:** Approved and implemented.

### ARCH-015 — Separate interaction consequences from collision geometry

- **Category:** Coupling.
- **Priority:** Medium.
- **Scope:** `CollisionSystem.ResolveHazardsAndCollectables` and associated effect helpers.
- **Current situation:** Geometry processing directly contains scoring, lives, inventory, checkpoints, and level-transition rules.
- **Problem:** Gameplay-rule changes require editing the collision subsystem.
- **Options and trade-offs:** See §7.2: local policy methods, a concrete interaction handler, or events.
- **Proposed improvement:** Prefer a small concrete handler for consequences while retaining overlap detection and contact history in collision. Avoid an event bus.
- **Expected benefit:** Gameplay rules become easier to inspect and test without disturbing solver mechanics.
- **Behavioural risk:** Medium; trigger ordering, edge detection, effects, and removal timing are observable.
- **Dependencies:** None; ARCH-013 strongly recommended first.
- **Independence:** Independent.
- **Implementation complexity:** Medium.
- **Verification:** Compare hazards, simultaneous contacts, every collectable variant, sound/effect counts, and transition precedence.
- **Decision:** Approved and implemented.

### ARCH-016 — Clarify the player-control phase inside physics

- **Category:** Internal organization.
- **Priority:** Medium.
- **Scope:** `PhysicsSystem.Step`, primarily lines 32–395.
- **Current situation:** Locomotion modes, input edges, intent, forces, and impulses form one long method.
- **Problem:** Ordering and ownership are difficult to review.
- **Options and trade-offs:** See §7.3: private methods first, or a separate player controller.
- **Proposed improvement:** First extract a few cohesive private phases without changing execution order. Consider a controller only if it clarifies state ownership beyond those methods.
- **Expected benefit:** A readable high-level step sequence with less interleaved detail.
- **Behavioural risk:** Medium; statement order and shared velocity/state are significant.
- **Dependencies:** None; ARCH-013 strongly recommended first.
- **Independence:** Independent; controller extraction is an alternative, not a required second stage.
- **Implementation complexity:** Medium.
- **Verification:** Characterize stance changes, jump-off, re-grab suppression, swimming, platform reactions, and held-key behavior.
- **Decision:** Approved and implemented.

### ARCH-017 — Move modal-screen presentation out of `GameLoop`

- **Category:** Class structure.
- **Priority:** Low.
- **Scope:** `AddEndScreenMessage` and its visual constants/widgets.
- **Current situation:** The loop calculates ripple boxes and glow/layout details.
- **Problem:** Presentation mechanics obscure mode orchestration.
- **Proposed improvement:** Move only glyph/layout generation to a concrete renderer following the existing menu/loading renderer pattern. Keep mode changes, confirmation input, timers, and sound decisions in the loop.
- **Expected benefit:** A smaller orchestration surface without a new screen framework.
- **Behavioural risk:** Low; layout and animation output must match.
- **Dependencies:** None.
- **Independence:** Independent.
- **Implementation complexity:** Small.
- **Verification:** Compare glyph output at representative elapsed times for completion, game-over, and exit-confirmation screens.
- **Decision:** Approved and implemented.

### ARCH-018 — Optionally distinguish held input from press edges

- **Category:** Naming.
- **Priority:** Low.
- **Scope:** `InputState` properties and their C# callers.
- **Current situation:** `IsJumpPressed`, for example, reports a held key; callers detect edges themselves.
- **Problem:** New code can mistake it for a one-tick event.
- **Proposed improvement:** Optionally use `Is…Down`/`IsDown` terminology. Leave JavaScript callback names, key codes, and edge-detection behavior unchanged.
- **Expected benefit:** More explicit input semantics.
- **Behavioural risk:** None expected; mechanical C# rename.
- **Dependencies:** None.
- **Independence:** Independent.
- **Implementation complexity:** Small.
- **Verification:** Update all references; confirm held-state and edge tests remain unchanged.
- **Decision:** Approved and implemented.

---

## 7. Larger refactoring options

### 7.1 `World2D` loading boundary — ARCH-014

#### Option A: Keep one class; improve grouping

**Advantages:** smallest change, no new construction API, minimal risk.  
**Disadvantages:** loading and runtime concerns remain together.

Best if the current problem is simply navigation.

#### Option B: `World2D.Loading.cs` partial

**Advantages:** separates source navigation while preserving private access and the current API.  
**Disadvantages:** does not reduce coupling or responsibilities; it can merely hide complexity in another file.

This is a legitimate organizational choice, but should not be described as an architectural separation.

#### Option C: Concrete `WorldLoader`

The loader owns reading, validation, caches, placement construction, and link setup. `World2D` owns the resulting runtime state.

**Advantages:** clearer boundary and better focused tests.  
**Disadvantages:** needs a controlled construction path; indiscriminately widening setters would weaken encapsulation.

**Preferred architectural option:** C, provided construction remains simple. A constructor or small internal initialization path is preferable to a general builder/factory framework.

**Do not split:** runtime interactions, removals, and respawn into separate managers merely because loading moved out.

### 7.2 Collision versus interaction policy — ARCH-015

#### Option A: Private policy methods within `CollisionSystem`

Lowest risk. Improves readability but leaves dependency ownership unchanged.

#### Option B: Concrete interaction handler

Collision identifies an interaction and calls a focused handler that updates the world/player and queues effects, sounds, and removals.

**Advantages:** separates geometry from game rules; enables focused tests.  
**Disadvantages:** ordering must remain explicit; passing excessive raw solver state would indicate the boundary is wrong.

#### Option C: Event bus or generalized command pipeline

**Advantages:** useful if many independent consumers eventually need interaction events.  
**Disadvantages:** introduces indirect execution and more lifetime/order concerns without demonstrated need.

**Preferred:** B for a real boundary; A if only local readability is desired. **Do not choose C now.**

### 7.3 Player control versus general physics — ARCH-016

#### Option A: Cohesive private phases

Keep the class, but let `Step` read as a sequence of locomotion-state resolution, intent/motor calculation, impulses, and integration.

**Advantages:** minimal ownership changes; easiest to preserve order.  
**Disadvantages:** does not resolve world/player state living in the system.

#### Option B: Concrete `PlayerController`

Own player input history and locomotion choices there, leaving shared force/integration mechanics in `PhysicsSystem`.

**Advantages:** makes player-specific state and testing clearer.  
**Disadvantages:** can create excessive parameter passing or circular interaction if both classes need each other’s private calculations.

**Preferred first step:** A. Consider B only after identifying a compact, one-way interface between decisions and physical application.

**Avoid:** one class per stance, a generic behavior framework, or rewriting the shared force model.

---

## 8. Prioritized approval groups

These groups are navigation aids, not approval bundles.

### Group A — Small, low-risk improvements

| ID | Improvement |
|---|---|
| ARCH-001 | Repair comments and current-state documentation |
| ARCH-002 | Explicit numeric-fallback naming |
| ARCH-003 | Named dynamic-patrol arguments |
| ARCH-004 | Reorganize `Body2D` members |

**Best immediate choices:** ARCH-001 and ARCH-003.

### Group B — Local structural and contract improvements

| ID | Improvement |
|---|---|
| ARCH-005 | Preserve placement material identity |
| ARCH-006 | Correct HTTP missing-file semantics |
| ARCH-007 | Enforce pose contracts |
| ARCH-008 | Validate player placement |
| ARCH-009 | Validate patrol numeric domains |
| ARCH-010 | Remove the focus listener |
| ARCH-011 | Explicit asynchronous lifetime |
| ARCH-012 | Reset world-specific temporal state |
| ARCH-013 | Establish targeted regression tests |
| ARCH-017 | Extract modal presentation |

**Highest value:** ARCH-005, ARCH-012, and ARCH-013.  
**Smallest concrete cleanup:** ARCH-010.  
**Not urgent:** ARCH-017.

### Group C — Larger structural options

| ID | Improvement |
|---|---|
| ARCH-014 | World loading boundary |
| ARCH-015 | Collision/interaction-policy boundary |
| ARCH-016 | Player-control organization/boundary |

Approve separately. None requires the others.

My preference is to attempt **ARCH-016’s local-method option first**, then choose between ARCH-014 and ARCH-015 based on which area is changing most frequently.

### Group D — Optional or debatable improvements

| ID | Improvement |
|---|---|
| ARCH-018 | Rename held-input properties |

Reasonable to reject. The existing convention is understandable once known.

### Areas where doing nothing is currently the best choice

- Keep the body hierarchy and capability interfaces.
- Keep direct construction rather than introducing broad DI.
- Keep the explicit object-kind switch.
- Keep dynamic and kinematic patrol algorithms separate.
- Keep the small hazard-property duplication.
- Keep `TwoStateObject2D`.
- Keep the lightweight UI widgets.
- Keep the pure glyph/color helpers.
- Do not introduce ECS, an event bus, a rendering framework, or a general asset factory.
- Do not replace every mutable collection with an abstraction without a demonstrated misuse.
- Do not pursue allocation/caching changes on this audit alone; performance decisions require measurements.
- Do not standardize every parser into identical strictness. Optional layers and required gameplay invariants genuinely need different policies.

---

## 9. Overall assessment and suggested order of work

### Is the architecture sound?

**Yes.** Its principal choices match a small browser-based ASCII platform game. The implementation remains recognizable and coherent despite iterative development.

### Is the class structure appropriate?

**Mostly.** The body model and small supporting classes are appropriate. The most useful boundaries to improve are:

1. Construction versus runtime state in `World2D`.
2. Interaction consequences versus physical collision in `CollisionSystem`.
3. Player-control phases versus shared integration in `PhysicsSystem`.

`Body2D` is not the first class I would split. `GameLoop` needs lifetime discipline before cosmetic size reduction.

### Are naming and patterns sufficiently consistent?

**Yes, with localized semantic drift.** There is no justification for a repository-wide renaming or uniform-pattern exercise. Documentation is less consistent with the implementation than the code’s naming is internally.

### Is the project becoming unnecessarily complicated?

Not primarily through abstraction. Complexity has accumulated through:

- Temporal state spread between bodies, systems, and the loop.
- Initialization assumptions enforced indirectly.
- Detailed comments that no longer reliably describe the current implementation.
- Gameplay policy embedded in central processing methods.

Those issues matter more than class counts or file lengths.

### Suggested sequence

1. **Approve a small baseline test effort:** ARCH-013.
2. **Correct the clearest state issue:** ARCH-005.
3. **Address world-lifetime state:** ARCH-012.
4. **Correct narrow boundary contracts:** ARCH-006, ARCH-007, ARCH-008, and selected ARCH-009 work.
5. **Fix cleanup:** ARCH-010, then consider ARCH-011.
6. **Apply selected documentation/readability changes:** ARCH-001–004.
7. **Only then consider structural changes individually:** ARCH-014–016.
8. **Leave optional presentation/naming work until useful:** ARCH-017–018.

**Bottom line:** preserve the architecture. Strengthen a few invariants, make lifetimes explicit, establish regression evidence, and separate only those responsibilities that demonstrably change for different reasons. There is no case here for a wholesale redesign.

## 10. Implementation outcome

All eighteen recommendations were approved and implemented incrementally, with a build and test run after each step, in commit `793fa1f` on `master`. Shipped gameplay was checked manually after the work.

### Per recommendation

| ID | Outcome | Choice made |
| --- | --- | --- |
| ARCH-001 | Outdated comments and structure docs corrected (`CanvasBridge`, `Body2D`, `Player2D`, `GameLoop`, `World2D`, `docs/Structure.md`). | Rationale kept; only false statements removed. |
| ARCH-002 | `ParseDouble` renamed to `ParseDoubleOrDefault`. | Parsing behavior unchanged. |
| ARCH-003 | Dynamic patrol configuration uses named arguments. | Adopted as a general project convention for paired X/Y parameters. |
| ARCH-004 | `Body2D` regrouped; contact and step tracking gathered at the end of the class. | Class not split. |
| ARCH-005 | A body's material is fixed at spawn: placement `Material`, else the sprite's `DefaultMaterial`. Frame, clip and pose changes no longer change it. | Simpler of the two options: the per-frame "dominant material" was removed, as no asset depends on material changing between frames. |
| ARCH-006 | `HttpAssetFileProvider` returns `null` only for 404; other failures throw `HttpRequestException` with path and status. | Fallback means "absent", never "failed to load". |
| ARCH-007 | Invalid default poses and missing required player poses are hard load errors. | Matches the documented contract. |
| ARCH-008 | A world must contain exactly one player placement. | Hard load error for none or several. |
| ARCH-009 | Patrol bounds, speeds and multipliers reject negative and infinite values. | Validation added at the loader, not in the patrol code. |
| ARCH-010 | The canvas-focus `pointerdown` listener is stored and removed on dispose. | Same pattern as the existing key listeners. |
| ARCH-011 | The game loop is stopped when the page is disposed; JS loop and listeners are stopped before the .NET reference is released. | No cancellation framework introduced. |
| ARCH-012 | `PhysicsSystem`, `CollisionSystem` and `AnimationSystem` have explicit `Reset()` methods, called when a world loads. | Explicit reset over recreating systems. |
| ARCH-013 | New MSTest project `ASCII Hero.Tests` (40 tests): INI parsing, HTTP asset provider, sprite pose contract, input state, contact impact tracking, and loading every shipped world. | Characterization tests of real assets rather than physics simulation tests. |
| ARCH-014 | Loading and spawning moved to `World2D.Loading.cs`. | Option B (partial class); option C (separate `WorldLoader`) was not justified. |
| ARCH-015 | Gameplay consequences moved to `InteractionHandler`; `CollisionSystem` keeps detection only. | Option B (concrete handler); no event bus. |
| ARCH-016 | `PhysicsSystem.Step` reads as named player-control phases followed by integration. | Phase methods, no new class. |
| ARCH-017 | End-screen drawing moved from `GameLoop` to `EndScreenRenderer`. | Same pattern as the existing renderers. |
| ARCH-018 | `InputState` members renamed from `...Pressed` to `...KeyDown`. | Rename only; edge detection stays with the callers that already track it. |

### Follow-up found during implementation: solid hazard contact

After the work, slow contact with solid hazards (snakes) was not registering. This was a pre-existing problem: hazard hits were detected by overlap, but the collision solver fully separates solid bodies, so only fast contacts left a brief overlap.

Choices made:

- Hazard detection uses the contacts the solver already records; plain overlap remains for passable hazards and collectables. No overlap tolerance was introduced.
- Each resolved contact records its closing speed before the collision response (`Body2D.GetContactImpactSpeed`, `HasContactWith`).
- A killable hazard is squashed only when landed on at least at its new placement setting `KillImpactSpeed` (default 5 cells/second, `GameDefaults.DefaultKillImpactSpeed`). A gentler touch from above is an ordinary hit, as is a hazard falling onto the player.
- No special squash bounce; any bounce comes from normal restitution.

Documented in `docs/AssetFormat.md`, `docs/Decisions.md`, `docs/Structure.md` and `docs/Features.md`.
