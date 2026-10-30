using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Rendering;

namespace ASCII_Hero.Client.Game.World;

/// <summary>Holds all entities that make up the current game state.</summary>
public class World2D
{
    public Player2D Player { get; } = new();

    /// <summary>
    /// The body the camera should follow. Defaults to <see cref="Player"/>, but a world can
    /// designate a different body instead (e.g. the bouncing ball in TestMovement) by setting
    /// <c>CameraTarget = true</c> on that object's section in the world's objects.ini.
    /// </summary>
    public IPhysicsBody CameraTarget { get; set; } = null!;

    /// <summary>
    /// Every body in the world - static or moving, player or otherwise - as one generic
    /// collection. Systems (<see cref="Physics.PhysicsSystem"/>, <see cref="Physics.CollisionSystem"/>,
    /// <see cref="Rendering.WorldRenderer"/>) iterate this list and filter by capability
    /// interface (<see cref="IPhysicsBody"/>, <see cref="IHazardBody"/>, <see cref="ICollectableBody"/>)
    /// rather than by concrete type, so no system needs to know about every noun in the game.
    /// </summary>
    public List<Body2D> Objects { get; } = [];

    private readonly List<Body2D> _pendingRemovals = [];

    /// <summary>
    /// Marks a body for removal from <see cref="Objects"/>, applied at the start of the next
    /// call to <see cref="ApplyPendingRemovals"/> rather than immediately, so callers iterating
    /// <see cref="Objects"/> (e.g. <see cref="Physics.CollisionSystem"/> resolving overlaps) never
    /// mutate the list they're enumerating. Safe to call more than once for the same body.
    /// </summary>
    public void QueueRemoval(Body2D body)
    {
        _pendingRemovals.Add(body);
    }

    /// <summary>
    /// Removes every body queued via <see cref="QueueRemoval"/> since the last call, e.g. a
    /// collectable picked up this frame. Called once per frame after all systems that might
    /// queue a removal have run.
    /// </summary>
    public void ApplyPendingRemovals()
    {
        if (_pendingRemovals.Count == 0)
        {
            return;
        }

        foreach (var body in _pendingRemovals)
        {
            Objects.Remove(body);
        }

        _pendingRemovals.Clear();
    }

    /// <summary>
    /// Background layer of the world, purely visual - one glyph per world cell, sized to
    /// <see cref="WidthCells"/>/<see cref="HeightCells"/>. Optional: a world with no
    /// <c>{WorldName}_background_characters.txt</c> file gets an all-empty grid here (draws
    /// nothing).
    /// </summary>
    public char[,] BackgroundChars { get; private set; } = new char[0, 0];

    /// <summary>Foreground color code per background cell, same dimensions as <see cref="BackgroundChars"/>.</summary>
    public char[,] BackgroundFore { get; private set; } = new char[0, 0];

    /// <summary>Background (fill) color code per background cell, same dimensions as <see cref="BackgroundChars"/>.</summary>
    public char[,] BackgroundBack { get; private set; } = new char[0, 0];

    /// <summary>
    /// Optional foreground layer of the world, purely visual - drawn last, on top of every game
    /// object (see <see cref="Rendering.WorldRenderer"/>), same dimensions as
    /// <see cref="BackgroundChars"/>. A world with no <c>{WorldName}_foreground_characters.txt</c>
    /// file gets an all-empty grid here, which draws nothing - this layer is entirely optional
    /// (e.g. prison bars, foliage overhang, anything meant to always sit above the player and
    /// every other object without needing a full z-order system).
    /// </summary>
    public char[,] ForegroundChars { get; private set; } = new char[0, 0];

    /// <summary>Foreground color code per foreground cell, same dimensions as <see cref="ForegroundChars"/>.</summary>
    public char[,] ForegroundFore { get; private set; } = new char[0, 0];

    /// <summary>Background (fill) color code per foreground cell, same dimensions as <see cref="ForegroundChars"/>.</summary>
    public char[,] ForegroundBack { get; private set; } = new char[0, 0];

    /// <summary>
    /// Precomputed (resolved once at load time, see <see cref="PrecomputeLayerColors"/>) CSS
    /// foreground color for each <see cref="BackgroundChars"/> cell, or null for that cell's
    /// resolved default (see <see cref="Rendering.GlyphBuilder.BuildGlyph"/>). Background/
    /// foreground layers are purely static level data - the palette, per-cell color codes, and
    /// this world's own default color codes are all already final by the time <see cref="LoadAsync"/>
    /// returns - so resolving each cell's actual color once here, rather than every frame in
    /// <see cref="Rendering.WorldRenderer"/>, removes a palette lookup per visible background/
    /// foreground cell every frame with no behavior change. Empty cells are left null, same as
    /// the underlying character grid.
    /// </summary>
    public string?[,] BackgroundForeColors { get; private set; } = new string?[0, 0];

    /// <summary>See <see cref="BackgroundForeColors"/> - the background layer's resolved fill color.</summary>
    public string?[,] BackgroundBackColors { get; private set; } = new string?[0, 0];

    /// <summary>See <see cref="BackgroundForeColors"/>, for <see cref="ForegroundChars"/> instead.</summary>
    public string?[,] ForegroundForeColors { get; private set; } = new string?[0, 0];

    /// <summary>See <see cref="BackgroundForeColors"/>, for <see cref="ForegroundChars"/> instead.</summary>
    public string?[,] ForegroundBackColors { get; private set; } = new string?[0, 0];

    /// <summary>"No cell here" marker used by this world's background/object grids.</summary>
    public char EmptyChar { get; private set; } = ' ';

    /// <summary>The resolved Global+World color palette, used to render every fore/back color code.</summary>
    public ColorPalette Palette { get; private set; } = null!;

    /// <summary>
    /// Color code (see <c>Global/ColorPalette.ini</c>) this world's own <c>[Colors]
    /// DefaultForegroundColor</c> settings.ini key resolves to, used by <see cref="Rendering.WorldRenderer"/>
    /// as the fallback for any cell (background or object) whose own color code is absent/empty
    /// and whose sprite has no own default either. Null when not set, in which case the
    /// hardcoded renderer default applies. See docs/AssetFormat.md §3.
    /// </summary>
    public char? DefaultForeColor { get; private set; }

    /// <summary>See <see cref="DefaultForeColor"/>.</summary>
    public char? DefaultBackColor { get; private set; }

    /// <summary>
    /// The resolved Global+World material library. Each body resolves its
    /// <see cref="Body2D.Density"/>/<see cref="Body2D.Friction"/>/<see cref="Body2D.Restitution"/>
    /// from the material name(s) resolved onto its active <see cref="SpriteFrame.Materials"/>.
    /// </summary>
    public MaterialLibrary Materials { get; private set; } = null!;

    /// <summary>The resolved Global+World sound library; empty (silent) when no SoundLibrary.ini defines any sound.</summary>
    public SoundLibrary Sounds { get; private set; } = SoundLibrary.Empty;

    private readonly List<string> _pendingSounds = [];

    /// <summary>
    /// Queues a named sound (see <see cref="Sounds"/>) to be played by <see cref="GameLoop"/> at
    /// the end of the current frame. A null/empty name means "no sound configured" and is ignored,
    /// so callers can pass an object's optional sound name straight through. Game logic never
    /// touches audio output itself.
    /// </summary>
    public void PlaySound(string? soundName)
    {
        if (!string.IsNullOrEmpty(soundName))
        {
            _pendingSounds.Add(soundName);
        }
    }

    /// <summary>Returns and clears every sound queued via <see cref="PlaySound"/> since the last call.</summary>
    public IReadOnlyList<string> DrainPendingSounds()
    {
        if (_pendingSounds.Count == 0)
        {
            return [];
        }

        var drained = _pendingSounds.ToArray();
        _pendingSounds.Clear();
        return drained;
    }

    /// <summary>Gravity acceleration, in world cells per second squared.</summary>
    public double Gravity { get; private set; } = Constants.PhysicsConstants.DefaultGravity;

    /// <summary>
    /// The player's lives when this world is loaded - from this world's own <c>[World] Lives</c>
    /// settings.ini key, falling back to <see cref="GameDefaults.PlayerStartingLives"/>.
    /// </summary>
    public int StartingLives { get; private set; } = GameDefaults.PlayerStartingLives;

    /// <summary>
    /// How many rings add one life - from <c>[World] RingsPerLife</c>, falling back to
    /// <see cref="GameDefaults.DefaultRingsPerLife"/>.
    /// </summary>
    public int RingsPerLife { get; private set; } = GameDefaults.DefaultRingsPerLife;

    /// <summary>
    /// Width of the world, in cells - always derived from <c>_objects.txt</c>'s own content (the
    /// world's sole authoritative layout definition, along with <c>_objects.ini</c>), never from
    /// the purely-visual/optional background or foreground art layers.
    /// </summary>
    public int WidthCells { get; private set; }

    /// <summary>Height of the world, in cells - see <see cref="WidthCells"/> for how it's derived.</summary>
    public int HeightCells { get; private set; }

    /// <summary>
    /// The last <see cref="CollectableType.Checkpoint"/> collectable's position the player
    /// triggered, set by <see cref="Physics.CollisionSystem.ResolveHazardsAndCollectables"/>. Null
    /// until the first checkpoint is triggered. Consumed by <see cref="Respawn"/> as the
    /// preferred respawn position, falling back to <see cref="PlayerSpawnPosition"/> when null.
    /// </summary>
    public Vector2D? RespawnPoint { get; set; }

    /// <summary>
    /// The player's original spawn position, recorded once from its <c>Player</c> placement in
    /// <c>{World}_objects.ini</c> during <see cref="LoadAsync"/>. Used by <see cref="Respawn"/> as
    /// the fallback respawn position when <see cref="RespawnPoint"/> hasn't been set yet (no
    /// checkpoint reached).
    /// </summary>
    public Vector2D PlayerSpawnPosition { get; private set; }

    /// <summary>
    /// Set by <see cref="Physics.CollisionSystem.ResolveHazardsAndCollectables"/> when the player
    /// reaches a <see cref="CollectableType.LevelEnd"/> or loses their last health. While not
    /// <see cref="World.PendingTransition.None"/>, <see cref="GameLoop"/> freezes physics so the
    /// triggering effect and sound can play, then carries out the transition and resets this.
    /// </summary>
    public PendingTransition PendingTransition { get; set; }

    /// <summary>The player's collision rectangles grown by one cell on every side - "next to" for action key interactions.</summary>
    private List<Physics.Rect> PlayerReach() => Player.CollisionRects
        .Select(rect => new Physics.Rect(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2))
        .ToList();

    /// <summary>
    /// Opens one closed key-operated gate next to the player, if the player holds a key with the
    /// gate's <see cref="Gate2D.LockName"/>: the gate opens (and stays open), that key is consumed
    /// and the gate's <c>Sound</c> plays. "Next to" means within one cell of the player's collision
    /// shape. Returns whether a gate was opened. Called by <see cref="GameLoop"/> on an action key press.
    /// </summary>
    public bool TryOpenGate()
    {
        if (Player.Keys.Count == 0)
        {
            return false;
        }

        var reach = PlayerReach();

        foreach (var gate in Objects.OfType<Gate2D>())
        {
            if (gate.IsOn || gate.LockName is null)
            {
                continue;
            }

            var keyIndex = Player.Keys.FindIndex(key => string.Equals(key, gate.LockName, StringComparison.OrdinalIgnoreCase));
            if (keyIndex < 0 || !gate.CollisionRects.Any(gateRect => reach.Any(gateRect.Overlaps)))
            {
                continue;
            }

            Player.Keys.RemoveAt(keyIndex);
            gate.SetState(true);
            PlaySound(gate.SoundName);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Operates one switch next to the player (see <see cref="Switch2D.Operate"/>) and plays its
    /// <c>Sound</c>. A switch whose state doesn't change (an already-pressed button) does nothing.
    /// Returns whether a switch was operated. Called by <see cref="GameLoop"/> on an action key press.
    /// </summary>
    public bool TryOperateSwitch()
    {
        var reach = PlayerReach();

        foreach (var switchObject in Objects.OfType<Switch2D>())
        {
            if (!switchObject.CollisionRects.Any(switchRect => reach.Any(switchRect.Overlaps)))
            {
                continue;
            }

            if (switchObject.Operate())
            {
                PlaySound(switchObject.SoundName);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Pairs every <see cref="Switch2D"/> with the <see cref="ILinkTarget"/> sharing its link name
    /// and applies its initial state to it. One-to-many: an unmatched, duplicated-switch or target-less
    /// link is a level authoring error and fails loudly.
    /// </summary>
    private static void ResolveLinks(World2D world, string worldName)
    {
        var targets = world.Objects.OfType<ILinkTarget>().Where(target => target.LinkName is not null).ToList();

        var switches = world.Objects.OfType<Switch2D>().ToList();
        foreach (var duplicateSwitch in switches.Where(s => s.LinkName is not null).GroupBy(s => s.LinkName!, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            throw new FormatException($"More than one switch in world '{worldName}' has Link '{duplicateSwitch.Key}'; each link has exactly one switch.");
        }

        foreach (var switchObject in switches)
        {
            if (switchObject.LinkName is null)
            {
                throw new FormatException($"A switch in world '{worldName}' is missing its 'Link' key.");
            }

            var matches = targets.Where(candidate => string.Equals(candidate.LinkName, switchObject.LinkName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                throw new FormatException($"Switch with Link '{switchObject.LinkName}' in world '{worldName}' has no matching target.");
            }

            foreach (var target in matches)
            {
                switchObject.Targets.Add(target);
                target.SetState(switchObject.IsOn);
            }
        }

        foreach (var orphan in targets.Where(target => !switches.Any(s => string.Equals(s.LinkName, target.LinkName, StringComparison.OrdinalIgnoreCase))))
        {
            throw new FormatException($"Object with Link '{orphan.LinkName}' in world '{worldName}' has no matching switch.");
        }
    }

    /// <summary>
    /// Resets the player back to a respawn state - position to <see cref="RespawnPoint"/> if set,
    /// otherwise <see cref="PlayerSpawnPosition"/>; velocity to <see cref="Vector2D.Zero"/> so it
    /// doesn't retain any pre-death momentum; and health back to a full bar. Called by
    /// <see cref="GameLoop"/> once the <see cref="World.PendingTransition.Death"/> delay has passed
    /// and the player still has lives left. Held keys and rings are kept.
    /// </summary>
    public void Respawn()
    {
        Player.Position = RespawnPoint ?? PlayerSpawnPosition;
        Player.Velocity = Vector2D.Zero;
        Player.Health = GameDefaults.PlayerFullHealth;
    }

    /// <summary>
    /// Total number of logical steps <see cref="LoadAsync"/> reports through its optional
    /// <c>progress</c> callback - used by <see cref="Menu.WorldSelectScreen"/>/
    /// <see cref="Rendering.WorldSelectRenderer"/> to size the "Loading [World]" progress bar
    /// shown while a world loads (see <c>GameLoop</c>'s LoadingWorld <c>GameMode</c>).
    /// </summary>
    public const int LoadStepCount = 6;

    /// <summary>
    /// Loads a world's settings, layers and object placements together with the sprite assets
    /// they reference, then spawns every object and resolves switch links. See docs/AssetFormat.md
    /// for the file formats and the Global/World fallback rule. The optional <paramref name="progress"/>
    /// callback reports 1-4 after settings, palette/materials, layers and object definitions,
    /// then fractionally through 4-5 per sprite asset loaded and 5-6 per placement-grid row
    /// spawned. Purely a UI hook; it does not affect loading itself.
    /// </summary>
    public static async Task<World2D> LoadAsync(IAssetFileProvider fileProvider, string worldName, IProgress<double>? progress = null)
    {
        var world = new World2D();
        var worldFolder = $"{AssetPathResolver.WorldsRoot}/{worldName}";

        var settingsContent = await fileProvider.TryReadTextAsync($"{worldFolder}/{worldName}_settings.ini");
        var settings = IniDocument.Parse(settingsContent ?? string.Empty);
        var emptyChar = IniValueParser.ParseEmptyChar(settings.TryGetValue("Layout", "EmptyChar"));
        world.EmptyChar = emptyChar;
        world.DefaultForeColor = IniValueParser.ParseColorCode(settings.TryGetValue("Colors", "DefaultForegroundColor"));
        world.DefaultBackColor = IniValueParser.ParseColorCode(settings.TryGetValue("Colors", "DefaultBackgroundColor"));
        world.StartingLives = IniValueParser.TryParseInt(settings.TryGetValue("World", "Lives"), out var startingLives) && startingLives > 0
            ? startingLives
            : GameDefaults.PlayerStartingLives;
        world.RingsPerLife = IniValueParser.TryParseInt(settings.TryGetValue("World", "RingsPerLife"), out var ringsPerLife) && ringsPerLife > 0
            ? ringsPerLife
            : GameDefaults.DefaultRingsPerLife;

        var globalSettingsContent = await fileProvider.TryReadTextAsync($"{AssetPathResolver.GlobalRoot}/Settings.ini");
        var globalSettings = IniDocument.Parse(globalSettingsContent ?? string.Empty);
        if (IniValueParser.TryParseDouble(globalSettings.TryGetValue("Physics", "Gravity"), out var gravity))
        {
            world.Gravity = gravity;
        }
        progress?.Report(1);

        world.Palette = await ColorPalette.LoadAsync(fileProvider, worldName);
        world.Materials = await MaterialLibrary.LoadAsync(fileProvider, worldName);
        world.Sounds = await SoundLibrary.LoadAsync(fileProvider, worldName);
        progress?.Report(2);

        // _objects.txt/_objects.ini is the world's sole authoritative layout definition -
        // WidthCells/HeightCells are always inferred from _objects.txt's own content (widest
        // line, line count), never from the background/foreground art layers, which are always
        // purely visual and optional, padded to these dimensions like any other secondary layer.
        var objectsContentForSizing = await fileProvider.TryReadTextAsync($"{worldFolder}/{worldName}_objects.txt")
            ?? throw new FileNotFoundException($"Missing required object placement grid for world '{worldName}'.");
        var sizingFrames = AssetTextReader.ParseCharsLayer(objectsContentForSizing, emptyChar);
        var width = sizingFrames[0].GetLength(1);
        var height = sizingFrames[0].GetLength(0);
        world.WidthCells = width;
        world.HeightCells = height;

        var backgroundContent = await fileProvider.TryReadTextAsync($"{worldFolder}/{worldName}_background_characters.txt");
        var backgroundFrames = AssetTextReader.ParseFixedSizeFrames(backgroundContent ?? string.Empty, width, height, emptyChar);
        world.BackgroundChars = backgroundFrames.Count > 0 ? backgroundFrames[0] : new char[height, width];

        var backgroundForeContent = await fileProvider.TryReadTextAsync($"{worldFolder}/{worldName}_background_foregroundcolors.txt");
        var backgroundBackContent = await fileProvider.TryReadTextAsync($"{worldFolder}/{worldName}_background_backgroundcolors.txt");
        world.BackgroundFore = AssetTextReader.ParseFixedSizeSecondaryFrames(backgroundForeContent, 1, width, height, emptyChar)[0];
        world.BackgroundBack = AssetTextReader.ParseFixedSizeSecondaryFrames(backgroundBackContent, 1, width, height, emptyChar)[0];

        // Foreground layer (see its own doc comment on ForegroundChars) is entirely optional - a
        // world with no {worldName}_foreground_characters.txt simply gets an all-empty grid at
        // the world's own dimensions (from _objects.txt, see width/height above), same as an
        // absent background layer file already does. Dimensions are fixed, not independently
        // inferred - a foreground decoration is placed against the same grid, not sized on its
        // own.
        var foregroundContent = await fileProvider.TryReadTextAsync($"{worldFolder}/{worldName}_foreground_characters.txt");
        var foregroundFrames = AssetTextReader.ParseFixedSizeFrames(foregroundContent ?? string.Empty, width, height, emptyChar);
        world.ForegroundChars = foregroundFrames.Count > 0 ? foregroundFrames[0] : new char[height, width];
        var foregroundForeContent = await fileProvider.TryReadTextAsync($"{worldFolder}/{worldName}_foreground_foregroundcolors.txt");
        var foregroundBackContent = await fileProvider.TryReadTextAsync($"{worldFolder}/{worldName}_foreground_backgroundcolors.txt");
        world.ForegroundFore = AssetTextReader.ParseFixedSizeSecondaryFrames(foregroundForeContent, 1, width, height, emptyChar)[0];
        world.ForegroundBack = AssetTextReader.ParseFixedSizeSecondaryFrames(foregroundBackContent, 1, width, height, emptyChar)[0];

        // Background/foreground layers are static level data - resolve every cell's actual
        // color once here (rather than every frame in WorldRenderer) now that the palette and
        // every color code/default this world will ever have are already final.
        world.BackgroundForeColors = PrecomputeLayerColors(world.BackgroundFore, world.Palette, emptyChar, RenderConstants.DefaultForeColor, world.DefaultForeColor);
        world.BackgroundBackColors = PrecomputeLayerColors(world.BackgroundBack, world.Palette, emptyChar, RenderConstants.DefaultBackColor, world.DefaultBackColor);
        world.ForegroundForeColors = PrecomputeLayerColors(world.ForegroundFore, world.Palette, emptyChar, RenderConstants.DefaultForeColor, world.DefaultForeColor);
        world.ForegroundBackColors = PrecomputeLayerColors(world.ForegroundBack, world.Palette, emptyChar, RenderConstants.DefaultBackColor, world.DefaultBackColor);
        progress?.Report(3);

        var objectsIniContent = await fileProvider.TryReadTextAsync($"{worldFolder}/{worldName}_objects.ini")
            ?? throw new FileNotFoundException($"Missing required object placement definitions for world '{worldName}'.");
        var objectsIni = IniDocument.Parse(objectsIniContent);

        var objectsGrid = AssetTextReader.ParseFixedSizeGrid(objectsContentForSizing, width, height, emptyChar);
        progress?.Report(4);

        // Loads every distinct sprite asset once, up front, with the union of clips any object
        // section needs from it, so the placement loop below never awaits. Reports fractional
        // progress through step 5 per asset, since this is where the HTTP fetches happen.
        var spriteLoader = new SpriteLoader(fileProvider);
        var spriteCache = new Dictionary<string, SpriteAsset>(StringComparer.OrdinalIgnoreCase);
        var clipNamesByAsset = CollectClipNamesByAsset(objectsIni, worldName);
        var assetsLoaded = 0;
        await Task.WhenAll(clipNamesByAsset.Select(async entry =>
        {
            spriteCache[entry.Key] = await spriteLoader.LoadAsync(entry.Key, [.. entry.Value], worldName);
            assetsLoaded++;
            progress?.Report(4.0 + (double)assetsLoaded / clipNamesByAsset.Count);
        }));
        if (clipNamesByAsset.Count == 0)
        {
            progress?.Report(5.0);
        }

        // Materials-only sections get a synthesized asset, shared between placements with the
        // same material, glyph and size.
        var syntheticSpriteCache = new Dictionary<(string Material, char Glyph, int Width, int Height), SpriteAsset>();

        for (var row = 0; row < height; row++)
        {
            progress?.Report(5.0 + (double)row / height);

            for (var col = 0; col < width; col++)
            {
                var code = objectsGrid[row, col];
                if (code == emptyChar)
                {
                    continue;
                }

                var sectionName = objectsIni.TryGetValue("ObjectCodes", code.ToString());
                if (sectionName is null)
                {
                    continue;
                }

                var section = new ObjectSection(objectsIni.Section(sectionName), sectionName, worldName);
                SpawnPlacement(world, section, new Vector2D(col, row), spriteCache, syntheticSpriteCache);
            }
        }

        ResolveLinks(world, worldName);

        // Player participates in the generic Objects list alongside every other body.
        world.Objects.Add(world.Player);

        // No object explicitly claimed the camera via CameraTarget = true; default to the player.
        world.CameraTarget ??= world.Player;
        progress?.Report(6);

        return world;
    }

    /// <summary>Clip used by a placement with no <c>Clip</c> key, and by every materials-only object.</summary>
    private const string DefaultClipName = "default";

    /// <summary>
    /// Scans every object definition (not the placement grid) for the sprite assets this world
    /// needs and, per asset, the union of clips any section requests from it - two sections can
    /// share one asset while asking for different clips, so loading is driven by the union.
    /// Materials-only sections need no sprite file and are skipped.
    /// </summary>
    private static Dictionary<string, HashSet<string>> CollectClipNamesByAsset(IniDocument objectsIni, string worldName)
    {
        var clipNamesByAsset = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var sectionName in objectsIni.Section("ObjectCodes").Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var section = new ObjectSection(objectsIni.Section(sectionName), sectionName, worldName);
            var assetName = section.Text("Asset");
            if (assetName is null)
            {
                if (section.Text("Material") is null)
                {
                    throw section.Error("is missing required key 'Asset' (or 'Material' for a materials-only object)");
                }

                continue;
            }

            if (!clipNamesByAsset.TryGetValue(assetName, out var clipNames))
            {
                clipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                clipNamesByAsset[assetName] = clipNames;
            }

            clipNames.Add(section.Text("Clip") ?? DefaultClipName);
            if (section.Text("OnClip") is { } onClipName)
            {
                clipNames.Add(onClipName);
            }

            clipNames.UnionWith(EffectClips.Read(section).Names);
        }

        return clipNamesByAsset;
    }

    /// <summary>
    /// Resolves a placement's sprite, starting clip/frame and effect clips: either from its
    /// (already loaded) <c>Asset</c>, or - for a materials-only object - a sprite synthesized from
    /// <c>Material</c>, <c>Width</c>, <c>Height</c> and the material's (or placement's own)
    /// <c>Character</c>.
    /// </summary>
    private static (SpriteAsset Sprite, string ClipName, int FrameIndex, EffectClips Effects) ResolveSprite(
        World2D world,
        ObjectSection section,
        Dictionary<string, SpriteAsset> spriteCache,
        Dictionary<(string Material, char Glyph, int Width, int Height), SpriteAsset> syntheticSpriteCache)
    {
        if (section.Text("Asset") is { } assetName)
        {
            var sprite = spriteCache[assetName];
            var clipName = section.Text("Clip") ?? DefaultClipName;
            return (sprite, clipName, sprite.GetClip(clipName).DefaultFrame, EffectClips.Read(section));
        }

        var materialName = section.Text("Material")
            ?? throw section.Error("is missing required key 'Asset' (or 'Material' for a materials-only object)");
        if (!world.Materials.Contains(materialName))
        {
            throw section.Error($"uses Material '{materialName}', which is not defined in MaterialLibrary.ini");
        }

        var syntheticWidth = section.PositiveInt("Width") ?? throw section.Error("has 'Material' but no 'Width'");
        var syntheticHeight = section.PositiveInt("Height") ?? throw section.Error("has 'Material' but no 'Height'");

        // A Character on the placement overrides the material's shared Character for this
        // placement only (e.g. localized pollution on otherwise-clean water).
        var glyphOverride = section.Values.TryGetValue("Character", out var characterText) ? IniValueParser.ParseColorCode(characterText) : null;
        var glyph = glyphOverride
            ?? world.Materials.Get(materialName).Character
            ?? throw section.Error($"uses materials-only Material '{materialName}', which has no Character in MaterialLibrary.ini (and no 'Character' override was given)");

        var key = (materialName, glyph, syntheticWidth, syntheticHeight);
        if (!syntheticSpriteCache.TryGetValue(key, out var syntheticSprite))
        {
            syntheticSprite = SyntheticSpriteFactory.Build(materialName, glyph, syntheticWidth, syntheticHeight);
            syntheticSpriteCache[key] = syntheticSprite;
        }

        return (syntheticSprite, DefaultClipName, 0, EffectClips.None);
    }

    /// <summary>
    /// Spawns one object placement: builds the body for its <c>Kind</c>, applies the settings
    /// every body shares (passability, climbing, conveyor, sound, colors, material) and adds it to
    /// <see cref="Objects"/>. The player is configured in place instead (see <see cref="SpawnPlayer"/>).
    /// </summary>
    private static void SpawnPlacement(
        World2D world,
        ObjectSection section,
        Vector2D position,
        Dictionary<string, SpriteAsset> spriteCache,
        Dictionary<(string Material, char Glyph, int Width, int Height), SpriteAsset> syntheticSpriteCache)
    {
        var kind = section.Required("Kind");
        var span = section.PositiveInt("Span") ?? 0;
        var (sprite, clipName, frameIndex, effects) = ResolveSprite(world, section, spriteCache, syntheticSpriteCache);

        if (kind == "Player")
        {
            SpawnPlayer(world, section, sprite, position, effects);
            return;
        }

        var linkName = section.Text("Link");
        var lockName = section.Text("Lock");
        var onClipName = section.Text("OnClip");
        var initialOn = string.Equals(section.Text("InitialState"), "On", StringComparison.OrdinalIgnoreCase);
        var initialVelocity = new Vector2D(section.Number("InitialVelocityX") ?? 0.0, section.Number("InitialVelocityY") ?? 0.0);
        var gravityAffected = section.Flag("GravityAffected", fallback: true);
        var killable = section.Flag("Killable");
        var effectPersists = section.Flag("EffectPersists");
        var killSoundName = section.Text("KillSound");
        var damage = section.Number("Damage") is { } parsedDamage && parsedDamage >= 0 ? parsedDamage : GameDefaults.DefaultHazardDamage;

        T SpawnTwoState<T>(T body) where T : TwoStateObject2D
        {
            body.SpawnTwoState(sprite, clipName, onClipName, initialOn, position, span);
            return body;
        }

        Body2D body;
        switch (kind)
        {
            case "StaticObject":
                var staticObject = new StaticObject2D();
                staticObject.Spawn(sprite, clipName, frameIndex, position, span);
                body = staticObject;
                break;

            case "Gate":
                if (lockName is not null && linkName is not null)
                {
                    throw section.Error("has both 'Lock' and 'Link'; a gate is opened by either a key or a switch, not both");
                }

                body = SpawnTwoState(new Gate2D { LockName = lockName, LinkName = linkName });
                break;

            case "Switch":
                body = SpawnTwoState(new Switch2D { Latching = section.Flag("Latching"), LinkName = linkName });
                break;

            case "Device":
                body = SpawnTwoState(new Device2D { LinkName = linkName });
                break;

            case "FlowZone":
                var flowDirection = section.Direction("FlowDirection")
                    ?? DirectionFromClipName(clipName)
                    ?? throw section.Error("needs a 'FlowDirection' of Left, Right, Up or Down (or a clip name ending in _left, _right, _up or _down)");
                var flowSpeed = section.NonNegativeNumber("FlowSpeed") ?? GameDefaults.DefaultFlowSpeed;
                body = SpawnTwoState(new FlowZone2D { LinkName = linkName, FlowDirection = flowDirection, FlowSpeed = flowSpeed });
                break;

            case "DynamicObject":
                var dynamicObject = new DynamicObject2D();
                dynamicObject.Spawn(sprite, clipName, frameIndex, position, initialVelocity, gravityAffected, span);
                ConfigureDynamicPatrol(world, dynamicObject, section);
                body = dynamicObject;
                break;

            case "DynamicHazard":
                var dynamicHazard = new DynamicHazard2D
                {
                    EffectClipName = effects.Effect,
                    IsKillable = killable,
                    EffectPersists = effectPersists,
                    KillSoundName = killSoundName,
                    Damage = damage,
                };
                dynamicHazard.Spawn(sprite, clipName, frameIndex, position, initialVelocity, gravityAffected, span);
                ConfigureDynamicPatrol(world, dynamicHazard, section);
                body = dynamicHazard;
                break;

            case "KinematicObject":
                var kinematicObject = new KinematicObject2D { LinkName = linkName };
                kinematicObject.Spawn(sprite, clipName, frameIndex, position, initialVelocity, span);
                ConfigureKinematicPatrol(world, kinematicObject, section);
                body = kinematicObject;
                break;

            case "KinematicHazard":
                var kinematicHazard = new KinematicHazard2D
                {
                    EffectClipName = effects.Effect,
                    IsKillable = killable,
                    EffectPersists = effectPersists,
                    KillSoundName = killSoundName,
                    Damage = damage,
                };
                kinematicHazard.Spawn(sprite, clipName, frameIndex, position, initialVelocity, span);
                ConfigureKinematicPatrol(world, kinematicHazard, section);
                body = kinematicHazard;
                break;

            case "StaticHazard":
                var staticHazard = new StaticHazard2D
                {
                    EffectClipName = effects.Effect,
                    IsKillable = killable,
                    EffectPersists = effectPersists,
                    KillSoundName = killSoundName,
                    Damage = damage,
                };
                staticHazard.Spawn(sprite, clipName, frameIndex, position, span);
                body = staticHazard;
                break;

            case "Collectable":
                // An absent or unrecognized Type leaves the collectable without an effect on
                // pickup rather than silently falling back to some default variant.
                CollectableType? collectableType = Enum.TryParse<CollectableType>(section.Text("Type"), ignoreCase: true, out var parsedType)
                    ? parsedType
                    : null;
                var collectable = collectableType == CollectableType.Key ? new Key2D { LockName = lockName } : new Collectable2D();
                collectable.Spawn(sprite, clipName, frameIndex, position, collectableType, span);
                collectable.EffectClipName = effects.Effect;
                collectable.EffectPersists = effectPersists;
                body = collectable;
                break;

            default:
                throw section.Error($"has unknown Kind '{kind}'");
        }

        // A gate's passability follows its open/closed state instead. Hazards, collectables,
        // switches and flow zones never block unless explicitly set to.
        if (body is not Gate2D)
        {
            body.IsPassable = section.Flag("Passable", fallback: kind is "StaticHazard" or "KinematicHazard" or "Collectable" or "Switch" or "FlowZone");
        }

        body.SoundName = section.Text("Sound");
        body.IsClimbable = section.Flag("Climbable");
        body.IsHangable = section.Flag("Hangable");
        body.IsMediumCandidate = section.Flag("Medium");
        body.ConveyorVelocity = ReadConveyorVelocity(section, clipName);
        body.ForeColorOverride = IniValueParser.ParseColorCode(section.Values.TryGetValue("ForegroundColor", out var foreColorText) ? foreColorText : null);
        body.BackColorOverride = IniValueParser.ParseColorCode(section.Values.TryGetValue("BackgroundColor", out var backColorText) ? backColorText : null);
        ApplyMaterial(world, body, section);
        world.Objects.Add(body);

        if (body is IPhysicsBody physicsBody && section.Flag("CameraTarget"))
        {
            world.CameraTarget = physicsBody;
        }
    }

    /// <summary>
    /// Configures <see cref="Player"/> from its placement: spawn position, effect clips, lives,
    /// health, material and the player-only movement overrides.
    /// </summary>
    private static void SpawnPlayer(World2D world, ObjectSection section, SpriteAsset sprite, Vector2D position, EffectClips effects)
    {
        var player = world.Player;
        player.Spawn(sprite);
        player.Position = position;
        world.PlayerSpawnPosition = position;
        player.HazardHitEffectClipName = effects.HazardHit;
        player.CheckpointReachedEffectClipName = effects.CheckpointReached;
        player.LevelEndReachedEffectClipName = effects.LevelEndReached;
        player.DiedEffectClipName = effects.Died;
        player.Lives = world.StartingLives;
        player.Health = GameDefaults.PlayerFullHealth;
        ApplyMaterial(world, player, section);
        player.WalkForceMultiplier = section.Number("WalkForceMultiplier") ?? player.WalkForceMultiplier;
        player.WalkSpeed = section.Number("WalkSpeed") ?? player.WalkSpeed;
        player.CrawlSpeed = section.Number("CrawlSpeed") ?? player.CrawlSpeed;

        if (section.Flag("CameraTarget"))
        {
            world.CameraTarget = player;
        }
    }

    /// <summary>
    /// Resolves a body's physical properties from its material - the placement's <c>Material</c>
    /// if set, otherwise the material its sprite provides - with <c>Density</c>, <c>Friction</c>,
    /// <c>Restitution</c> and <c>Hardness</c> each independently overridable by the placement.
    /// Mass (see <see cref="Body2D.Mass"/>) always follows from Density times footprint.
    /// </summary>
    private static void ApplyMaterial(World2D world, Body2D body, ObjectSection section)
    {
        var materialOverride = section.Text("Material");
        var materialName = materialOverride ?? body.MaterialName;
        if (materialName is not null && !world.Materials.Contains(materialName))
        {
            throw section.Error(materialOverride is not null
                ? $"uses Material '{materialName}', which is not defined in MaterialLibrary.ini"
                : $"uses an asset whose DefaultMaterial '{materialName}' is not defined in MaterialLibrary.ini");
        }

        var material = world.Materials.Get(materialName);
        body.Density = section.Number("Density") ?? material.Density;
        body.Friction = section.Number("Friction") ?? material.Friction;
        body.Restitution = section.Number("Restitution") ?? material.Restitution;
        body.Hardness = section.Number("Hardness") is { } hardness && hardness > 0 ? hardness : material.Hardness;
        body.Viscosity = material.Viscosity;

        // Keeps the renderer's material colors in line with the overriding material.
        if (materialOverride is not null)
        {
            body.MaterialName = materialOverride;
        }
    }

    /// <summary>
    /// The surface velocity from <c>ConveyorDirection</c>/<c>ConveyorSpeed</c> (see
    /// <see cref="Body2D.ConveyorVelocity"/>), or zero for a placement with neither. A direction
    /// without a speed uses <see cref="GameDefaults.DefaultConveyorSpeed"/>; a speed without a
    /// direction takes the direction from the clip name's <c>_left</c>/<c>_right</c>/<c>_up</c>/<c>_down</c> suffix.
    /// </summary>
    private static Vector2D ReadConveyorVelocity(ObjectSection section, string clipName)
    {
        var speed = section.NonNegativeNumber("ConveyorSpeed");
        var direction = section.Direction("ConveyorDirection");
        if (direction is null && speed is not null)
        {
            direction = DirectionFromClipName(clipName)
                ?? throw section.Error("has 'ConveyorSpeed' but no 'ConveyorDirection', and its clip name does not end in _left, _right, _up or _down");
        }

        if (direction is not { } conveyorDirection)
        {
            return default;
        }

        var conveyorSpeed = speed ?? GameDefaults.DefaultConveyorSpeed;
        return new Vector2D(conveyorDirection.X * conveyorSpeed, conveyorDirection.Y * conveyorSpeed);
    }

    /// <summary>Configures a dynamic body's patrol from its placement, if it patrols (see <see cref="ReadPatrolRoute"/>).</summary>
    private static void ConfigureDynamicPatrol(World2D world, DynamicObject2D body, ObjectSection section)
    {
        if (ReadPatrolRoute(world, body, section) is not { } route)
        {
            return;
        }

        body.SetPatrol(
            route.MinX, route.MaxX, section.Number("PatrolCruiseSpeedX") ?? GameDefaults.PatrolCruiseSpeed,
            route.MinY, route.MaxY, section.Number("PatrolCruiseSpeedY") ?? GameDefaults.PatrolCruiseSpeed,
            section.Number("PatrolForceMultiplierX") ?? GameDefaults.PatrolForceMultiplier,
            section.Number("PatrolForceMultiplierY") ?? GameDefaults.PatrolForceMultiplier,
            route.TowardMaxX, route.TowardMaxY,
            section.Number("PatrolMaxForceX") ?? GameDefaults.DefaultPatrolMaxForceX,
            section.Number("PatrolMaxForceY") ?? GameDefaults.DefaultPatrolMaxForceY,
            section.Flag("PatrolStayOnPlatform"));
    }

    /// <summary>Configures a kinematic body's patrol from its placement, if it patrols (see <see cref="ReadPatrolRoute"/>).</summary>
    private static void ConfigureKinematicPatrol(World2D world, KinematicObject2D body, ObjectSection section)
    {
        if (ReadPatrolRoute(world, body, section) is not { } route)
        {
            return;
        }

        body.SetPatrol(
            route.MinX, route.MaxX, section.Number("PatrolSpeedX") ?? GameDefaults.PatrolCruiseSpeed,
            route.MinY, route.MaxY, section.Number("PatrolSpeedY") ?? GameDefaults.PatrolCruiseSpeed,
            route.TowardMaxX, route.TowardMaxY);
    }

    /// <summary>
    /// Reads a placement's patrol route, or null if it doesn't patrol. A placement patrols when
    /// <c>Patrol = true</c>, or when it sets any of <c>PatrolMinX</c>/<c>PatrolMaxX</c>/<c>PatrolMinY</c>/<c>PatrolMaxY</c>
    /// without <c>Patrol = false</c>. An axis with either bound set is patrolled, a missing bound
    /// defaulting to that world edge; with no bounds at all the body patrols the full world width.
    /// </summary>
    private static PatrolRoute? ReadPatrolRoute(World2D world, Body2D body, ObjectSection section)
    {
        var minX = section.Number("PatrolMinX");
        var maxX = section.Number("PatrolMaxX");
        var minY = section.Number("PatrolMinY");
        var maxY = section.Number("PatrolMaxY");
        var hasX = minX is not null || maxX is not null;
        var hasY = minY is not null || maxY is not null;

        if (!(section.OptionalFlag("Patrol") ?? (hasX || hasY)))
        {
            return null;
        }

        if (!hasX && !hasY)
        {
            hasX = true;
        }

        return new PatrolRoute(
            hasX ? minX ?? 0.0 : null,
            hasX ? maxX ?? world.WidthCells - body.Size.X : null,
            hasY ? minY ?? 0.0 : null,
            hasY ? maxY ?? world.HeightCells - body.Size.Y : null,
            section.TowardMax("PatrolInitialDirectionX", "Left", "Right"),
            section.TowardMax("PatrolInitialDirectionY", "Up", "Down"));
    }

    /// <summary>
    /// The direction a <c>Left</c>/<c>Right</c>/<c>Up</c>/<c>Down</c> value names, as a unit vector.
    /// Y increases downward (see docs/Architecture.md's Coordinate System), so "Up" is -Y.
    /// </summary>
    private static Vector2D? ParseDirection(string text) => text.ToLowerInvariant() switch
    {
        "left" => new Vector2D(-1, 0),
        "right" => new Vector2D(1, 0),
        "up" => new Vector2D(0, -1),
        "down" => new Vector2D(0, 1),
        _ => null,
    };

    /// <summary>
    /// The direction a clip name's <c>_left</c>/<c>_right</c>/<c>_up</c>/<c>_down</c> suffix names,
    /// or null if it has none - the same suffixes sprite poses use for facing.
    /// </summary>
    private static Vector2D? DirectionFromClipName(string clipName)
    {
        var separator = clipName.LastIndexOf('_');
        return separator < 0 ? null : ParseDirection(clipName[(separator + 1)..]);
    }

    /// <summary>
    /// Resolves every cell of a background/foreground color-code layer against <paramref name="palette"/>
    /// once at load time, producing the fully-resolved color grid consumed directly by
    /// <see cref="Rendering.WorldRenderer"/> - see <see cref="BackgroundForeColors"/>.
    /// </summary>
    private static string?[,] PrecomputeLayerColors(char[,] codes, ColorPalette palette, char emptyChar, string? hardcodedFallback, char? levelDefault)
    {
        var height = codes.GetLength(0);
        var width = codes.GetLength(1);
        var resolved = new string?[height, width];

        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                var code = GlyphBuilder.NullIfEmpty(codes[row, col], emptyChar);
                resolved[row, col] = GlyphBuilder.ResolveColor(palette, hardcodedFallback, code, levelDefault);
            }
        }

        return resolved;
    }

    /// <summary>A patrol route as passed to <c>SetPatrol</c>: per-axis bounds (null = not patrolled) and optional starting directions.</summary>
    private readonly record struct PatrolRoute(double? MinX, double? MaxX, double? MinY, double? MaxY, bool? TowardMaxX, bool? TowardMaxY);

    /// <summary>The effect clips a placement names; all null for a materials-only object.</summary>
    private sealed record EffectClips(string? Effect, string? HazardHit, string? CheckpointReached, string? LevelEndReached, string? Died)
    {
        public static readonly EffectClips None = new(null, null, null, null, null);

        public static EffectClips Read(ObjectSection section) => new(
            section.Text("EffectClip"),
            section.Text("HazardHitEffectClip"),
            section.Text("CheckpointReachedEffectClip"),
            section.Text("LevelEndReachedEffectClip"),
            section.Text("DiedEffectClip"));

        public IEnumerable<string> Names => new[] { Effect, HazardHit, CheckpointReached, LevelEndReached, Died }.OfType<string>();
    }

    /// <summary>
    /// One object section of a world's objects.ini with typed key readers. Optional flags and
    /// tuning numbers fall back to their default when absent or unreadable; keys whose value
    /// decides an object's shape or direction (Span, Width, Height, directions, conveyor and flow
    /// speeds) reject an unreadable value with an error naming the section and world.
    /// </summary>
    private readonly record struct ObjectSection(IReadOnlyDictionary<string, string> Values, string Name, string WorldName)
    {
        /// <summary>The trimmed value of <paramref name="key"/>, or null when absent or blank.</summary>
        public string? Text(string key) =>
            Values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

        public string Required(string key) => Text(key) ?? throw Error($"is missing required key '{key}'");

        public bool Flag(string key, bool fallback = false) => OptionalFlag(key) ?? fallback;

        public bool? OptionalFlag(string key) => bool.TryParse(Text(key), out var value) ? value : null;

        public double? Number(string key) => IniValueParser.TryParseDouble(Text(key), out var value) ? value : null;

        public double? NonNegativeNumber(string key)
        {
            var text = Text(key);
            if (text is null)
            {
                return null;
            }

            return IniValueParser.TryParseDouble(text, out var value) && value >= 0
                ? value
                : throw Error($"has invalid '{key}' value '{text}' (expected a number, 0 or more)");
        }

        public int? PositiveInt(string key)
        {
            var text = Text(key);
            if (text is null)
            {
                return null;
            }

            return IniValueParser.TryParseInt(text, out var value) && value >= 1
                ? value
                : throw Error($"has invalid '{key}' value '{text}' (expected a whole number, 1 or more)");
        }

        public Vector2D? Direction(string key)
        {
            var text = Text(key);
            if (text is null)
            {
                return null;
            }

            return ParseDirection(text) ?? throw Error($"has invalid '{key}' value '{text}' (expected Left, Right, Up or Down)");
        }

        /// <summary>
        /// Reads a starting-direction key: true for <paramref name="maxWord"/>, false for
        /// <paramref name="minWord"/>, null when absent.
        /// </summary>
        public bool? TowardMax(string key, string minWord, string maxWord)
        {
            var text = Text(key);
            if (text is null)
            {
                return null;
            }

            if (string.Equals(text, maxWord, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(text, minWord, StringComparison.OrdinalIgnoreCase)
                ? false
                : throw Error($"has invalid '{key}' value '{text}' (expected {minWord} or {maxWord})");
        }

        public FormatException Error(string problem) => new($"Section '{Name}' of world '{WorldName}' {problem}.");
    }
}
