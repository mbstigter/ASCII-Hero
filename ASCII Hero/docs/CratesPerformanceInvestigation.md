# Crates World Performance Investigation

Status: **investigation only - no code changed.** Findings are source-based; no runtime
profile has been captured yet. Uncertainties are labelled explicitly.

## 1. Symptom

- The `Crates` world drops to as low as ~5 FPS, even while the player stands still and
  nothing visibly moves.
- Every other world runs smoothly. `TestPhysics` is a convenient smooth comparison.
- The slowdown occurs under F5, Ctrl+F5 and the published (AOT) build alike.
- Input is sluggish too: even the Escape key press registers late.

### What these observations tell us

| Observation | Interpretation |
|---|---|
| Slow in F5, Ctrl+F5 **and** published AOT | Not a debugger or interpreter artefact. The per-frame workload itself is too expensive. |
| Escape registers late | The browser main thread is saturated. In Blazor WebAssembly, game code, JS interop, canvas drawing and keyboard callbacks (`OnKeyDown`/`OnKeyUp` via `invokeMethodAsync`) share one thread, so key events wait behind long frames. Confirms long frames, but not whether physics or rendering is the cost. |
| Only Crates is affected | Points at what makes Crates unique: ~30 simultaneously active, mutually contacting moving bodies. Its render workload is ordinary (see section 2). |

## 2. What Crates contains (from the asset files)

| Item | Count / value |
|---|---|
| Light crates (`Crate`, mass 30) | 21 |
| Heavy crates (`HeavyCrate`, mass 84) | 9 |
| Moving bodies incl. player | 31 |
| Static objects | 1 floor (80 x 2) |
| World / viewport | 80 x 25 cells (whole world visible) |
| Crate sprite | 4 x 3, fully filled, one collision rectangle |
| Background / foreground | empty |
| Hazards, collectables, media, patrols | none |

Render workload is modest: ~360 crate glyphs + 160 floor glyphs + player/HUD. The
distinguishing feature is the number of **contacting dynamic bodies**, not visual complexity.

## 3. Findings

### F1. Nothing ever comes to rest computationally (confirmed by source)

- `PhysicsSystem.IntegrateBodies` integrates every moving body every step; there is no
  sleeping/resting state.
- Each step gravity adds ~0.67 cells/s and moves a resting crate ~0.011 cells into its
  support; `CollisionSystem` then pushes it back out. In a stack this correction propagates
  through finite-mass bodies.
- `CollisionSystem.Resolve` runs 8 solver iterations (`PhysicsConstants.SolverIterations`)
  plus a final solids pass, every step, regardless of visible motion.

Restitution below `RestingBounceSpeedThreshold` (1.0) is already suppressed, so visible
micro-bouncing is **not** implied. The concern is the amount of work, not visible jitter.

### F2. The narrow phase allocates on every call, even without contact (confirmed by source)

- `Body2D.CollisionRects` builds a new translated `List<Rect>` via LINQ on every access.
- `CollisionSystem.ResolveAgainstOtherBody` allocates a `List<Contact>`, combines materials,
  and re-reads `otherContactTarget.CollisionRects` inside its per-rectangle loop, wrapping
  each body rectangle in a new one-element collection - all **before** knowing whether the
  pair overlaps.
- Spatial-grid candidates are "shares a bucket" (cell size 4), so nearby but non-touching
  crates also pay this cost.

Query count per collision substep (source-derived, not measured):
`31 movers x (8 solids + 8 movers + 1 final solids) = 527 spatial queries`, each followed by
narrow-phase calls on its candidates. Densely packed stacks make candidate sets larger.

### F3. Contact history is copied every substep (confirmed by source)

- `Body2D.SnapshotContactsForNextFrame` creates a new dictionary and copies every contact
  list for **every object** each substep; `AddContact` then recreates lists.
- A source search found **no callers of `HadContactLastFrame`**, the only reader of
  `_previousContacts`. The copy may be unused work (verify before removing - it is part of
  `IPhysicsBody`).
- The architecture review added impact-speed tracking (`_contactImpactSpeeds`) to every
  recorded contact, including crate-on-crate contacts in a world with no hazards. This is the
  one new per-contact cost found in commit `793fa1f`; its size is unmeasured.

### F4. Fixed-step catch-up amplifies any slowdown (confirmed by source)

- `GameLoop` clamps frame delta to 0.1 s and drains it in 1/60 s steps
  (`RunFixedPhysicsSteps`). A slow frame therefore triggers up to ~6 full physics+collision
  steps before the next render, which keeps the next frame slow (a bounded "spiral").
- `CountSubsteps` subdivides the **whole world's** step by the fastest body (up to 8x). This
  matters while crates fall or are shoved; at rest it should return to 1.

Illustrative arithmetic only: if one physics step cost ~33 ms, six of them (~200 ms) would
match the reported ~5 FPS. This number is hypothetical.

### F5. Mostly pre-existing, not introduced by the architecture review

The parent of `793fa1f` already had the 8 solver iterations, `IndexOf` pairing, allocating
`CollisionRects`, per-pair `List<Contact>`, contact snapshot copying and
`HasCharacterOverlap`. Crates is simply the world that exercises them with the most
contacting bodies. Only impact-speed bookkeeping (F3) is new.

### F6. Areas found less likely (not ruled out)

| Area | Why less likely in Crates |
|---|---|
| Gravity/friction maths | Small bounded arithmetic; surrounding repetition/allocation dominates by inspection |
| `HasCharacterOverlap` | Clipped to the intersection, exits on first hit; filled crates are easy cases |
| Medium resolution | No medium volumes; returns Air fallback (still an `Objects` scan per mover) |
| Animation / poses / patrol | Crates are single-frame, pose-less, non-patrolling |
| Hazards / collectables | Empty lists, early return |
| Blazor component rendering | Not used as the game loop |
| Rendering / interop | One batched `drawFrame` call; glyph count similar to other worlds. Per-frame list/array allocation and per-glyph `fillText` are real costs but not Crates-specific |

`_movingBodies.IndexOf(other)` in the mover pairing loop is an O(n) lookup per candidate per
iteration - a minor extra cost that grows with mover count.

## 4. Likely root cause (hypothesis, ranked)

1. **Collision solver workload scaling with contacting dynamic bodies** - 8 iterations x
   ~31 bodies x bucket neighbours, each call allocating rectangles/lists (F1, F2).
2. **GC pressure** from those allocations plus per-substep contact snapshots (F2, F3).
   Blazor WebAssembly GC is non-concurrent, so pauses stall the single thread.
3. **Catch-up amplification** turning a moderately expensive step into a sustained ~5 FPS
   (F4).

Rendering/interop is the main **alternative** explanation not yet excluded by measurement,
but nothing Crates-specific was found there.

## 5. How to confirm (no code changes needed)

### 5.1 Visible FPS on the Escape screen

The in-game FPS overlay is hidden on the "Exit Game?" screen. Use the browser's own counter
instead: in the app-mode Edge window press F12, then Ctrl+Shift+P, and run
**"Show frame rendering stats"**. Then compare:

1. Crates, settled, standing still.
2. Press Escape and wait a few seconds on "Exit Game?" (physics stops, the same world is
   still drawn).
3. TestPhysics, standing still.

| Result on Escape screen | Indicates |
|---|---|
| FPS recovers to normal | Simulation (physics/collision) is the cost |
| FPS stays low | Rendering/interop/GC or a shared cost |

### 5.2 Browser performance recording

Record ~5 s with DevTools **Performance** for each of the three scenarios above. Compare:

- Scripting/WebAssembly time per frame vs. `drawFrame`/paint/raster time.
- Long tasks and gaps (GC pauses appear as long uninterrupted WebAssembly blocks).
- Delay between a key press and its `OnKeyDown` callback.

Managed method names may not resolve in an AOT/WebAssembly capture; the split between
"inside OnFrame before drawFrame" and "drawFrame/canvas" is still informative.

### 5.3 Optional: desktop CPU/memory profile of the simulation

The physics is browser-independent (the test project already loads worlds from disk). A
BenchmarkDotNet benchmark or MSTest that loads Crates and runs N fixed physics+collision
steps would give a .NET CPU and allocation profile, with TestPhysics as the baseline. This
requires adding a new measurement artifact, so it was not done in this read-only
investigation.

## 6. Recommendations (apply only after measurement confirms)

In priority order if the simulation is confirmed as the cost:

1. **Remove per-call allocations in the narrow phase.** Avoid materialising translated
   `CollisionRects` lists and per-pair `List<Contact>`; do a cheap AABB rejection before any
   per-pair setup. Positions change within solver iterations, so any caching must respect
   that.
2. **Stop copying contact history every substep** if `HadContactLastFrame` is confirmed
   unused (or swap two reusable dictionaries instead of allocating), and keep impact-speed
   tracking cheap.
3. **Cheapen candidate pairing**: replace `_movingBodies.IndexOf(other)` with a stored index;
   keep candidate order identical, since resolution is order-sensitive.
4. **Consider early exit from solver iterations** when an iteration applies no significant
   correction - behaviour-preserving for converged stacks.
5. **Only if still needed, add body sleeping** for resting contact groups, with reliable
   wake-up on push, impact or loss of support. This is the most behaviour-sensitive option.
6. **Guard against catch-up spiralling** (e.g. cap physics steps per frame) as a safety net -
   it limits the symptom, not the cause, and slows simulated time under load.

If rendering is confirmed instead: reuse glyph/array buffers in `WorldRenderer`/`CanvasBridge`
and reduce redundant `fillStyle` changes in `drawFrame`.

### Not recommended

- Lowering gravity, friction or `SolverIterations` blindly - changes gameplay before the
  bottleneck is proven.
- Quantising physics to the ASCII grid, or moving physics into JavaScript/Blazor rendering -
  contradicts docs/Architecture.md.
- Package or framework upgrades - nothing points at the runtime itself.

Each change should be measured against the same baseline (Crates and TestPhysics) before and
after, with the MSTest suite kept green.

## 7. Response: what was done

Temporary timing counters were added to measure each part of a frame and step, and removed
again afterwards. Measurements showed **both** suspects were real: physics/collision when stacks
are active, and drawing (30-56 ms per frame) in every busy scene.

### 7.1 Physics changes

| Change | Physical behaviour |
|---|---|
| Narrow phase: cheap bounding-box rejection first; no per-call rectangle/contact list allocations | Unchanged - same contacts, same order |
| Contact lists reused per step; unused previous-frame contact history (`HadContactLastFrame`) removed | Unchanged - nothing read it |
| Mover pairing uses a stored index instead of `IndexOf` | Unchanged |
| Solver stops iterating once an iteration changes nothing significant | Unchanged for converged contacts; at most 8 iterations as before |
woken by contact from an awake body, loss of support, a gate or switch state change, or a change of its own collision shape |
| Compound-shape collision fix: every overlapping rectangle pair is checked and the deepest push per direction is used | **Correctness fix**: the player (several rectangles) could sink a full cell into a crate. Regression test `PlayerMovingAmongCrates_NeverSinksIntoThem` |

Not done, and why:

- **No gameplay changes**: gravity, friction, masses and `SolverIterations` (8) are unchanged.
  Raising `SolverIterations` to 12-32 was measured: it reduced residual velocities but made no
  loaded stack sleep, and cost more.
- **No catch-up cap change**: still at most six steps per frame, as before.
- **Warm starting not added**: it would let a loaded stack converge faster, but the player never
  sleeps, so a stack the player stands on stays awake anyway. Accepted as a known limit (ignored
  test `BodyStandingOnStack_StackSettles`)
  Design.md: keep stacks the player stands on small.

### 7.2 Drawing changes

- `drawFrame` now draws each character + color combination once into a small cached image, and
  copies that image for every later cell. Text drawing was the dominant cost; copying an image is
  far cheaper. Backgrounds are still plain filled cells. The cache is cleared when the font or
  cell size changes or a web font finishes loading.
- A per-frame color palette (sending color indices instead of color codes) was tried and then
  **reverted**: it duplicated the game's own palette system for a small gain.
- C# rendering (`WorldRenderer`, `GlyphBuilder`, `Glyph`) and the one-call-per-frame interop
  contract are unchanged.

### 7.3 Results

- Crates runs at a smooth frame rate, including with the player on a stack and while crates move
  (checked with the in-game FPS overlay; no final per-section numbers, the counters were removed).
- Settled stacks with nothing standing on them cost substantially less: they are no longer
  integrated or solved, though some bookkeeping and broad-phase work remains.
- MSTest: 62 passed, 1 skipped (the known limit above), 0 failed.

### 7.4 Assessment

- **Architecture**: physics stays in C#, independent of rendering; JavaScript still only draws
  what C# sends and contains no game logic. Rendering is still not used as the game loop.
- **Physics**: no behaviour was approximated or tuned for the Crates level. All changes either
  skip provably redundant work (rejection, early exit, sleeping of bodies at rest) or fix a
  correctness bug. Sleeping is the one behaviour-sensitive change. Tests cover waking by loss of
  support (`RemovingSupport_WakesStackAndItFalls`), by an awake body landing
  (`BodyLandingOnSleepingCrate_WakesItAndRestsOnTop`) and by a gate opening underneath
  (`OpeningGateUnderSleepingCrate_WakesItAndItFalls`). Waking on a collision-shape change has no
  automated test (no shipped movable object changes shape).
