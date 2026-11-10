# ASCII Hero

A retro platform game made entirely of ASCII characters, running in your browser.
Everything you see is text, but the movement, physics and scrolling are smooth and modern.

**[Play it here](https://mbstigter.github.io/ASCII-Hero/)**

Built with C# and .NET 10 (Blazor WebAssembly). It needs no server, plugins or installs.

## What makes it different

- **Smooth, not blocky.** The character grid is only how the game is drawn. Movement and
  physics use continuous coordinates, so the hero walks, jumps, climbs, hangs and swims
  without snapping from cell to cell.
- **Real physics.** Materials with friction, restitution and mass; pushable crates and
  rolling balls; water, wind and flowing rivers; fall damage and squashable enemies.
- **Everything is plain text.** Sprites, levels, colors, materials and sounds are `.txt` and
  `.ini` files you can edit in Notepad. Most new content is a new file, not new code.
- **Rich level parts.** Moving platforms, conveyors, ropes, vines and ladders, doors and
  hatches, keys, buttons and levers, fans, lasers, enemies and collectables.
- **Synthesized sound.** Sound effects are generated from a text description, so there are no
  audio files.

## Controls

| Action | Keys |
|---|---|
| Move, climb, crouch | Arrow keys or `W` `A` `S` `D` |
| Jump | `Space` or `Left Ctrl` |
| Action (keys, buttons, levers) | `Enter` or `E` |

`Escape` opens the Exit Game prompt.

## Running it locally

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
git clone https://github.com/mbstigter/ASCII-Hero.git
cd ASCII-Hero
dotnet run --project "ASCII Hero/ASCII Hero"
```

Or open `ASCII Hero.slnx` in Visual Studio and press F5. To run the tests:

```
dotnet test "ASCII Hero.slnx"
```

## Making your own levels

Levels are made of sprites, objects and settings in `ASCII Hero/ASCII Hero.Client/wwwroot/Assets`.
Start with the docs:

- [Features](ASCII%20Hero/docs/Features.md): a tour of what you can build
- [Level design tips](ASCII%20Hero/docs/LevelDesignTips.md): sizes and distances that suit the hero
- [Asset format](ASCII%20Hero/docs/AssetFormat.md): the full file format reference

## For developers

- [Design](ASCII%20Hero/docs/Design.md): the game design
- [Architecture](ASCII%20Hero/docs/Architecture.md): how the engine is organized
- [Structure](ASCII%20Hero/docs/Structure.md): components and key flows
- [Decisions](ASCII%20Hero/docs/Decisions.md): the current design and architecture decisions

## Credits

The game uses the Web437 IBM VGA 8x14 bitmap font from The Ultimate Oldschool PC Font Pack
(CC BY-SA 4.0). See
[THIRD-PARTY-NOTICES.md](ASCII%20Hero/ASCII%20Hero.Client/wwwroot/fonts/THIRD-PARTY-NOTICES.md).
