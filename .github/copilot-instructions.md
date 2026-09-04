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
