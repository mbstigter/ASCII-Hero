# Copilot Instructions

## Project Guidelines

AsciiHero is a browser-based retro ASCII platform game built with C# and .NET 10.

### Working Principles

- Prefer simple, explicit C# over unnecessary abstractions.
- Keep game logic independent from Blazor UI rendering.
- Keep JavaScript interop minimal and isolated.
- Do not introduce game engines, frameworks, or NuGet packages unless explicitly justified.
- Do not use Blazor component rendering as the real-time game loop.
- Preserve smooth movement and scrolling.
- Treat the ASCII character grid as a visual representation, not as the physics coordinate system.
- Do not resurrect rejected design approaches unless explicitly asked.
- Reuse existing code patterns when designing or implementing new game functionality (e.g., interface properties on hazards, World2D.LoadAsync case blocks, SetFrame clip swapping).
- Use named arguments for calls with long lists of same-typed parameters, especially paired X/Y parameters.

### Documentation Guidelines

- Use generic examples in documentation (e.g., AssetFormat.md) rather than referencing specific concrete game assets, as those are spec/reference docs, not historical records.

### Asset File Comments

- When writing or editing comments in .txt and .ini asset files (level/sprite/settings files, e.g. under wwwroot/Assets), write them in language appropriate for a level designer: describe what the setting/placement does and any relevant in-game/design intent, but avoid referencing internal implementation details, class/method names, development history, or test/validation framing.

### Before Changing Architecture

Check [docs/Architecture.md](../ASCII%20Hero/docs/Architecture.md) and [docs/Decisions.md](../ASCII%20Hero/docs/Decisions.md).

### Before Changing Game Design

Check [docs/Design.md](../ASCII%20Hero/docs/Design.md) and [docs/Decisions.md](../ASCII%20Hero/docs/Decisions.md).

### Before Changing Asset File Formats

Check [docs/AssetFormat.md](../ASCII%20Hero/docs/AssetFormat.md).

### Keeping docs/Structure.md Up To Date

[docs/Structure.md](../ASCII%20Hero/docs/Structure.md) describes the program's components and key flows. Whenever a change adds, removes, renames, or moves a class/subsystem, or changes an existing flow it describes (e.g. startup order, per-frame tick order, asset loading/fallback rules), update the corresponding section of docs/Structure.md as part of that same change - treat it the same way as docs/Decisions.md, not as a separate follow-up task.

### Local Development and Debugging

- For local dev/debug launches of AsciiHero, launch the browser in app mode (msedge.exe --app=<url>, chromeless window) via launchSettings.json rather than a normal browser tab, so the canvas has focus immediately without an address bar competing for it.

### Validation

After code changes, build the solution and address build errors before considering the task complete.

Then run the MSTest suite in `ASCII Hero/ASCII Hero.Tests` (Test Explorer or `dotnet test "ASCII Hero.slnx"`) and keep all tests green. When changing behavior that has tests, update or add tests in the same change; when refactoring, tests must pass unchanged.

### Source Control

- Never commit or push to Git without the user's explicit consent; leave changes uncommitted and ask first.

### Decision Documentation

- docs/Decisions.md is maintained as a compact, current-state-only reference of architecture/design decisions grouped by subsystem, NOT a chronological historical log. When a decision is added, changed, or superseded/reverted, update docs/Decisions.md in place (edit/remove bullets) rather than appending historical entries.

## Testing Guidelines

### Automated Testing with MSTest

- Understand the basics of automated testing: it involves writing code to verify that your application behaves as expected.
- Set up MSTest for AsciiHero by adding the MSTest NuGet package to your project.
- Create a test project in your solution to organize your tests separately from your main application code.
- Write test methods using the `[TestMethod]` attribute to define what should be tested.
- Use assertions (e.g., `Assert.AreEqual`, `Assert.IsTrue`) to check if the actual output matches the expected output.
- Run tests using the Test Explorer in Visual Studio or via the command line with `dotnet test`.
- Maintain tests by regularly updating them as your application changes, ensuring they remain relevant and accurate.
- **Step-by-Step Setup for MSTest:**
  1. Right-click on your solution in Visual Studio, choose `Add` > `New Project`.
  2. Select `MSTest Test Project` from the list of templates.
  3. Name the project (e.g., `AsciiHero.Tests`) and click `Create`.
  4. In the new test project, open the default `UnitTest1.cs` file.
  5. Write a simple test method:
     ```csharp
     [TestMethod]
     public void TestMethod1()
     {
         Assert.IsTrue(true);
     }
     ```
  6. Build the solution to restore NuGet packages and build the test project.
  7. Open the Test Explorer (`Test` > `Test Explorer`).
  8. Run the test by clicking the `Run All` button in the Test Explorer.
  9. Check the test results in the Test Explorer window.
  10. Update and expand your tests as needed, following the naming and organizational conventions you establish.

## Visual Studio Workflow and Terminal Reliability

You are operating inside Visual Studio 2026. Prefer the most reliable available tools for working with the open solution.

### Tool selection

* Use IDE-aware tools for code navigation, symbol references, and code understanding when available.
* Prefer appropriate Visual Studio build, testing, and diagnostic capabilities when directly accessible.
* Use terminal commands when they are necessary or provide a clear advantage.
* Do not assume an IDE capability is available to you just because it exists in the Visual Studio interface.

### Terminal stability

* Never send multiline commands directly to the integrated terminal.
* Prefer short, single-line commands with simple quoting.
* Avoid multiline PowerShell expressions, large inline payloads, complex command chaining, and commands that require interactive input.
* For complex operations, create a script using file-editing tools and execute it with a short, single-line command.
* Never create a multiline script by passing its contents directly through the terminal.
* Execute commands sequentially and verify their results before proceeding.

### Build and test

* Make focused changes and validate them incrementally.
* Use the smallest relevant build or test operation first.
* Inspect errors and test results before deciding on the next action.
* Do not repeatedly retry a failed or apparently hung command without investigating the cause.

### Recovery

If terminal execution hangs:

1. Stop sending commands to the affected terminal.
2. Check whether the shell is waiting for additional input or a process is still running.
3. Interrupt the operation if necessary.
4. Use a fresh terminal session if the existing one is unusable.
5. Retry using a simpler command or a script file.

Prioritize reliable execution and accurate verification over minimizing the number of commands.
