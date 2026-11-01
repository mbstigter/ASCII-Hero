# Crates Performance Changes: Review Verdict

Review of the changes documented in [CratesPerformanceInvestigation.md](CratesPerformanceInvestigation.md), especially section 7. This is review feedback, not a request for further optimization or an implementation plan.

## Verdict

The result looks like a **sound, focused improvement**, not an architectural workaround. Physics remains in C#, the browser adapter still only draws and handles browser input, and the changes address both the measured simulation and drawing costs. Preserve the approach and the performance gain. The findings below concern a few correctness boundaries and documentation claims, not a need to redesign the engine.

At review time, the solution built and the test run reported **61 passed, 1 skipped, 0 failed**. These results do not validate the browser-only visual edge cases noted below.

## Findings, in priority order

### 1. Define wake-up rules for changing forces and collision shapes

`PhysicsSystem.IntegrateBodies` skips integration for a sleeping body (`PhysicsSystem.cs:465-472`). `SleepSystem` handles contact, support removal, and the listed reaction forces, but not every change that could affect a sleeper.

- A `FlowZone2D` can switch from still to flowing (`FlowZone2D.cs:3-23`). An already-sleeping body in that medium would not recompute drag until another event wakes it.
- `AnimationSystem.Update` advances animation even for sleepers (`AnimationSystem.cs:55-60`). `Body2D.ApplyFrame` can change their collision shape. A shape change does not itself wake the body or refresh its kept contacts.

Existing Crates sprites do not expose these scenarios. **Recommendation:** define explicit sleep invalidation rules for changes to relevant forces and shapes, and test those rules before relying on sleeping for other object types. This is a general-engine edge case, not evidence that Crates is currently broken.

### 2. Correct the contact-storage decision record

`docs/Decisions.md:78-81` says contact history is "double-buffered." The implementation instead removed unused previous-frame history and clears/reuses current contact lists (`Body2D.cs:690-704`). Section 7 of the investigation report describes this accurately. Change the decision record to match the code.

### 3. Align collision documentation with the compound-shape resolver

`docs/Decisions.md:95-106` describes selecting the normal from one deepest rectangle and remeasuring the others on the same axis. Current `CollisionSystem.cs:647-735` instead considers every overlapping rectangle pair and picks the smallest of four worst-case directional pushes. `docs/Structure.md:531-552` also says rectangles are resolved independently; the current resolver applies one contact response per body pair per iteration.

Some stale descriptions predate the performance commit, but are particularly misleading beside the new compound-shape correctness fix. Update the decisions and structure descriptions together so the code, rationale, and reference documentation agree.

### 4. Make test names and report claims match their assertions

- `BodyLandingOnSleepingCrate_WakesItAndRestsOnTop` (`RestingStackTests.cs:115-128`) checks final contact and overlap, but does not assert that the target was asleep before landing or awake afterward.
- The skipped `BodyStandingOnStack_StackSettlesAndSleeps` (`RestingStackTests.cs:130-152`) asserts only the player's vertical speed, not whether the stack settled or slept.
- `PlayerMovingAmongCrates_NeverSinksIntoThem` (`RestingStackTests.cs:156-203`) is a valuable regression scenario, but its inline key sequence, six-event logging limit, and repeated pose dumps make its central assertion harder to read. Consider simplifying those diagnostics if they are no longer needed.

Either strengthen these assertions or narrow the names and section 7's description of wake-up coverage to what is actually tested. Retain the intentionally skipped loaded-stack scenario as a documented limitation rather than treating it as a new failure.

### 5. Clean up comments and qualify broad claims

- `Body2D.cs:568-573` has an incomplete sentence describing `_contactImpactSpeeds`, and `:600` has an unfinished XML `<summary>` tag. `CollisionSystem.cs:922-925` has a truncated summary. These are documentation defects, not identified runtime defects.
- `Body2D.SnapshotContactsForNextFrame` and `CollisionSystem.cs:73-76` still say "snapshot," although the method now clears and reuses contacts. A more accurate name/comment would clarify the lifecycle.
- `docs/Architecture.md:101-118` says every moving body integrates each frame and resting contacts are resolved every frame; note the sleeping exception. `docs/Design.md:41` says settled stacks cost "nothing," although some bookkeeping and broad-phase work remains; "substantially less" is more precise.
- `game-interop.js:13-16` unnecessarily claims "thousands" of repeated character/color combinations. Cached images are cell-sized (`:100-109`); a future font or glyph drawing outside its nominal cell could be clipped, unlike direct `fillText`. Check visual compatibility if the font changes. This is not a demonstrated problem with the current font.

## Preserve the current boundaries

Do **not** replace the sleeping-island design, move drawing logic into C#, introduce another rendering abstraction, or pursue warm starting solely to eliminate the skipped test. Ownership remains sensible: `CollisionSystem` resolves contacts, `SleepSystem` decides rest state, `Body2D` stores it, and JavaScript caches browser-specific glyph images. The loaded-stack limit is explicitly acknowledged and the reported gameplay and frame rate are good.

**Bottom line:** preserve the implementation. Address the sleep invalidation edge case and make the tests precise, then bring the comments and architecture, decisions, and structure documentation into agreement with the code.
