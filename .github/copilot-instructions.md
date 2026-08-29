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

### Before Changing Architecture

Check [docs/Architecture.md](../ASCII%20Hero/docs/Architecture.md) and [docs/Decisions.md](../ASCII%20Hero/docs/Decisions.md).

### Before Changing Game Design

Check [docs/Design.md](../ASCII%20Hero/docs/Design.md) and [docs/Decisions.md](../ASCII%20Hero/docs/Decisions.md).

### Before Changing Asset File Formats

Check [docs/AssetFormat.md](../ASCII%20Hero/docs/AssetFormat.md).

### Validation

After code changes, build the solution and address build errors before considering the task complete.
