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
    /// Loads a world's background/object-placement files and the sprite assets they reference,
    /// building up Platforms and the Player's spawn position/sprite. See AssetFormat.md section 3 for the world file format and
    /// section 1.1 for the Global/World fallback rule applied by SpriteLoader. The optional
    /// <paramref name="progress"/> callback is invoked once after each of the first 4 logical
    /// steps completes (settings, palette/materials, background, object definitions), then
    /// fractionally within step 5 as each distinct sprite asset (and clip) any object definition
    /// needs gets loaded - one asset at a time, evenly dividing 4-5 by how many distinct assets
    /// there are, since this is where the bulk of load time (HTTP fetches) actually happens - and
    /// then fractionally within step 6 as every object placement is spawned from the now fully-
    /// cached sprites, one placement-grid row at a time, evenly dividing 5-6 by the grid's row
    /// count. Purely a UI hook; does not affect loading itself.
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

        var spriteLoader = new SpriteLoader(fileProvider);
        var spriteCache = new Dictionary<string, SpriteAsset>(StringComparer.OrdinalIgnoreCase);

        // Pre-scan every object definition (not the placement grid) to work out which sprite
        // assets this world actually needs and, per asset, the full union of clips any section
        // requests from it - a section only names the clip(s) it itself needs, but two sections
        // can share one asset while asking for different clips (e.g. a "Spikes" placement using
        // a hazard asset's "idle" clip and a "FlameTrap" placement using the same asset's "trap"
        // and "burst" clips), so loading must be driven by the union, not any single section.
        // A section may instead skip Asset entirely and use Material/Width/Height (see
        // docs/AssetFormat.md §3.x) - such sections need no on-disk sprite at all, so they are
        // simply skipped here; their synthesized asset is built on demand in the placement loop
        // below (cheap enough in-memory work that pre-loading/caching it up front like a real
        // sprite fetch isn't worth the complexity).
        var clipNamesByAsset = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sectionName in objectsIni.Section("ObjectCodes").Values.Distinct())
        {
            var objectSection = objectsIni.Section(sectionName);
            if (!objectSection.TryGetValue("Asset", out var assetName))
            {
                if (objectSection.TryGetValue("Material", out _))
                {
                    continue;
                }

                throw new FormatException($"Section '{sectionName}' of world '{worldName}' is missing required key 'Asset' (or 'Material' for a materials-only object).");
            }

            var clipName = objectSection.TryGetValue("Clip", out var clip) ? clip : "default";
            var effectClipName = objectSection.TryGetValue("EffectClip", out var effectClipText) ? effectClipText : null;
            var hazardHitEffectClipName = objectSection.TryGetValue("HazardHitEffectClip", out var hazardHitEffectClipText) ? hazardHitEffectClipText : null;
            var checkpointReachedEffectClipName = objectSection.TryGetValue("CheckpointReachedEffectClip", out var checkpointReachedEffectClipText) ? checkpointReachedEffectClipText : null;
            var levelEndReachedEffectClipName = objectSection.TryGetValue("LevelEndReachedEffectClip", out var levelEndReachedEffectClipText) ? levelEndReachedEffectClipText : null;
            var diedEffectClipName = objectSection.TryGetValue("DiedEffectClip", out var diedEffectClipText) ? diedEffectClipText : null;

            if (!clipNamesByAsset.TryGetValue(assetName, out var clipNames))
            {
                clipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                clipNamesByAsset[assetName] = clipNames;
            }

            clipNames.Add(clipName);
            if (objectSection.TryGetValue("OnClip", out var scanOnClipName) && !string.IsNullOrWhiteSpace(scanOnClipName))
            {
                clipNames.Add(scanOnClipName);
            }
            if (effectClipName is not null)
            {
                clipNames.Add(effectClipName);
            }
            if (hazardHitEffectClipName is not null)
            {
                clipNames.Add(hazardHitEffectClipName);
            }
            if (checkpointReachedEffectClipName is not null)
            {
                clipNames.Add(checkpointReachedEffectClipName);
            }
            if (levelEndReachedEffectClipName is not null)
            {
                clipNames.Add(levelEndReachedEffectClipName);
            }
            if (diedEffectClipName is not null)
            {
                clipNames.Add(diedEffectClipName);
            }
        }

        // Loads every distinct asset once, up front, with the full clip set it needs - so the
        // placement loop below never awaits mid-loop. Reports fractional progress through step 5
        // per asset loaded, since this is where the actual HTTP fetches (and so the bulk of load
        // time) happen.
        var assetsLoaded = 0;
        var assetsToLoad = clipNamesByAsset.Count;
        var assetLoadTasks = clipNamesByAsset.Select(async entry =>
        {
            await GetOrLoadSpriteAsync(spriteLoader, spriteCache, entry.Key, [.. entry.Value], worldName);
            assetsLoaded++;
            progress?.Report(4.0 + (double)assetsLoaded / assetsToLoad);
        }).ToList();
        await Task.WhenAll(assetLoadTasks);
        if (assetsToLoad == 0)
        {
            progress?.Report(5.0);
        }

        // Materials-only sections (see above) each get their own synthesized asset, keyed by
        // material name + size so identical placements (e.g. two same-size platforms of the same
        // material) share one synthesized asset instead of rebuilding an identical grid per
        // placement.
        var syntheticAssetCache = new Dictionary<(string Material, char Glyph, int Width, int Height), SpriteAsset>();

        for (var row = 0; row < height; row++)
        {
            // The placement loop itself is now purely synchronous (every sprite it needs was
            // already loaded above), so it finishes quickly; this reports step 6's progress row
            // by row for a smooth final crawl to 100%.
            progress?.Report(height == 0 ? 6.0 : 5.0 + (double)row / height);

            for (var col = 0; col < width; col++)
            {
                var code = objectsGrid[row, col];
                if (code == emptyChar)
                {
                    continue;
                }

                var codeKey = code.ToString();
                var sectionName = objectsIni.TryGetValue("ObjectCodes", codeKey);
                if (sectionName is null)
                {
                    continue;
                }

                var objectSection = objectsIni.Section(sectionName);
                var hasAsset = objectSection.TryGetValue("Asset", out var assetName);
                var hasMaterial = objectSection.TryGetValue("Material", out var syntheticMaterialName);
                if (!hasAsset && !hasMaterial)
                {
                    throw new FormatException($"Section '{sectionName}' of world '{worldName}' is missing required key 'Asset' (or 'Material' for a materials-only object).");
                }

                if (!objectSection.TryGetValue("Kind", out var kind))
                {
                    throw new FormatException($"Section '{sectionName}' of world '{worldName}' is missing required key 'Kind'.");
                }

                var repeatCount = objectSection.TryGetValue("Repeat", out var repeatText) && IniValueParser.TryParseInt(repeatText, out var parsedRepeat)
                    ? parsedRepeat
                    : 1;
                var position = new Vector2D(col, row);

                SpriteAsset sprite;
                string clipName;
                int frameIndex;
                string? effectClipName;
                string? hazardHitEffectClipName;
                string? checkpointReachedEffectClipName;
                string? levelEndReachedEffectClipName;
                string? diedEffectClipName;

                if (hasAsset)
                {
                    clipName = objectSection.TryGetValue("Clip", out var clip) ? clip : "default";
                    effectClipName = objectSection.TryGetValue("EffectClip", out var effectClipText) ? effectClipText : null;
                    hazardHitEffectClipName = objectSection.TryGetValue("HazardHitEffectClip", out var hazardHitEffectClipText) ? hazardHitEffectClipText : null;
                    checkpointReachedEffectClipName = objectSection.TryGetValue("CheckpointReachedEffectClip", out var checkpointReachedEffectClipText) ? checkpointReachedEffectClipText : null;
                    levelEndReachedEffectClipName = objectSection.TryGetValue("LevelEndReachedEffectClip", out var levelEndReachedEffectClipText) ? levelEndReachedEffectClipText : null;
                    diedEffectClipName = objectSection.TryGetValue("DiedEffectClip", out var diedEffectClipText) ? diedEffectClipText : null;

                    // The sprite
                    // effect clip if any) was already loaded and cached during the pre-scan above.
                    sprite = await GetOrLoadSpriteAsync(spriteLoader, spriteCache, assetName!, [clipName], worldName);
                    frameIndex = sprite.GetClip(clipName).DefaultFrame;
                }
                else
                {
                    // Materials-only object (see docs/AssetFormat.md §3.x): no Asset/Clip/EffectClip -
                    // Width/Height give the placement's own size directly, and the material's own
                    // Character (not an authored sprite grid) supplies every cell's glyph.
                    if (!objectSection.TryGetValue("Width", out var widthText) || !IniValueParser.TryParseInt(widthText, out var syntheticWidth) || syntheticWidth < 1)
                    {
                        throw new FormatException($"Section '{sectionName}' of world '{worldName}' has 'Material' but is missing a valid 'Width'.");
                    }

                    if (!objectSection.TryGetValue("Height", out var heightText) || !IniValueParser.TryParseInt(heightText, out var syntheticHeight) || syntheticHeight < 1)
                    {
                        throw new FormatException($"Section '{sectionName}' of world '{worldName}' has 'Material' but is missing a valid 'Height'.");
                    }

                    var syntheticMaterial = world.Materials.Get(syntheticMaterialName);
                    // Character on the placement itself lets it override its own glyph away from
                    // the material's shared Character (e.g. simulating localized pollution on an
                    // otherwise-clean Water body) without affecting every other object using
                    // that material.
                    var syntheticGlyphOverride = objectSection.TryGetValue("Character", out var syntheticCharText) ? IniValueParser.ParseColorCode(syntheticCharText) : null;
                    if (syntheticGlyphOverride is null && syntheticMaterial.Character is null)
                    {
                        throw new FormatException($"Section '{sectionName}' of world '{worldName}' uses materials-only Material '{syntheticMaterialName}', which has no Character configured in MaterialLibrary.ini (and no 'Character' override was given on this placement).");
                    }

                    var syntheticGlyph = syntheticGlyphOverride ?? syntheticMaterial.Character!.Value;

                    var syntheticKey = (syntheticMaterialName!, syntheticGlyph, syntheticWidth, syntheticHeight);
                    if (!syntheticAssetCache.TryGetValue(syntheticKey, out var syntheticSprite))
                    {
                        syntheticSprite = SyntheticSpriteFactory.Build(syntheticMaterialName!, syntheticGlyph, syntheticWidth, syntheticHeight);
                        syntheticAssetCache[syntheticKey] = syntheticSprite;
                    }

                    sprite = syntheticSprite;
                    clipName = "default";
                    frameIndex = 0;
                    effectClipName = null;
                    hazardHitEffectClipName = null;
                    checkpointReachedEffectClipName = null;
                    levelEndReachedEffectClipName = null;
                    diedEffectClipName = null;
                }


                var gravityAffected = !objectSection.TryGetValue("GravityAffected", out var gravityText) || !bool.TryParse(gravityText, out var parsedGravity) || parsedGravity;
                // Restitution is not defaulted here - absent means "use whatever the
                // spawned body's resolved material provides" (see the post-spawn material
                // resolution below), only an explicit ini value overrides that.
                var restitutionOverride = objectSection.TryGetValue("Restitution", out var restitutionText) && IniValueParser.TryParseDouble(restitutionText, out var parsedRestitution)
                    ? (double?)parsedRestitution
                    : null;
                // Friction/Density mirror Restitution above: absent means "use whatever the
                // spawned body's resolved material/computed footprint provides", only an explicit
                // ini value overrides that. Friction lets this object type override its
                // material's default grip independent of any other type sharing that material;
                // Density lets this object type override its material's default density outright
                // (e.g. a body whose effective density changes at runtime/per-instance, like
                // boiling water) while still inheriting that material's Friction/Restitution and
                // still having Mass (see Body2D.Mass) computed from this overridden Density rather
                // than the material's own.
                var frictionOverride = objectSection.TryGetValue("Friction", out var frictionText) && IniValueParser.TryParseDouble(frictionText, out var parsedFriction)
                    ? (double?)parsedFriction
                    : null;
                var densityOverride = objectSection.TryGetValue("Density", out var densityText) && IniValueParser.TryParseDouble(densityText, out var parsedDensity)
                    ? (double?)parsedDensity
                    : null;
                var hardnessOverride = objectSection.TryGetValue("Hardness", out var hardnessText) && IniValueParser.TryParseDouble(hardnessText, out var parsedHardness) && parsedHardness > 0
                    ? (double?)parsedHardness
                    : null;
                // Lets a placement spawn its asset
                // DefaultMaterial/per-cell material layer (e.g. re-using the one Ball asset to
                // test Rubber vs. Steel-ball behavior in TestPhysics) without needing a second,
                // otherwise-identical sprite asset just to carry a different DefaultMaterial.
                var materialOverride = objectSection.TryGetValue("Material", out var materialOverrideText) ? materialOverrideText : null;
                // Lets a placement give its own instance a distinct color from its sprite's own
                // default (e.g. TestPhysics distinguishing its four Ball placements by material)
                // without needing a separate sprite asset per color - mirrors materialOverride
                // above, and is inserted into WorldRenderer's fallback chain the same way.
                var foreColorOverride = IniValueParser.ParseColorCode(objectSection.TryGetValue("ForegroundColor", out var foreColorText) ? foreColorText : null);
                var backColorOverride = IniValueParser.ParseColorCode(objectSection.TryGetValue("BackgroundColor", out var backColorText) ? backColorText : null);
                var initialVelocityX = objectSection.TryGetValue("InitialVelocityX", out var velocityXText) && IniValueParser.TryParseDouble(velocityXText, out var parsedVelocityX)
                    ? parsedVelocityX
                    : 0.0;
                var initialVelocityY = objectSection.TryGetValue("InitialVelocityY", out var velocityYText) && IniValueParser.TryParseDouble(velocityYText, out var parsedVelocityY)
                    ? parsedVelocityY
                    : 0.0;
                var initialVelocity = new Vector2D(initialVelocityX, initialVelocityY);

                var killable = objectSection.TryGetValue("Killable", out var killableText) && bool.TryParse(killableText, out var parsedKillable) && parsedKillable;
                var effectPersists = objectSection.TryGetValue("EffectPersists", out var effectPersistsText) && bool.TryParse(effectPersistsText, out var parsedEffectPersists) && parsedEffectPersists;
                var lockName = objectSection.TryGetValue("Lock", out var lockNameText) && !string.IsNullOrWhiteSpace(lockNameText) ? lockNameText : null;
                var linkName = objectSection.TryGetValue("Link", out var linkNameText) && !string.IsNullOrWhiteSpace(linkNameText) ? linkNameText : null;
                var onClipName = objectSection.TryGetValue("OnClip", out var onClipText) && !string.IsNullOrWhiteSpace(onClipText) ? onClipText : null;
                var initialOn = objectSection.TryGetValue("InitialState", out var initialStateText) && string.Equals(initialStateText, "On", StringComparison.OrdinalIgnoreCase);
                var killSoundName = objectSection.TryGetValue("KillSound", out var killSoundNameText) ? killSoundNameText : null;
                var hazardDamage = objectSection.TryGetValue("Damage", out var damageText) && IniValueParser.TryParseDouble(damageText, out var parsedDamage) && parsedDamage >= 0
                    ? parsedDamage
                    : GameDefaults.DefaultHazardDamage;

                // Patrol (Kind = DynamicHazard only, see below) - PatrolMinX/PatrolMaxX let a
                // placement author a custom back-and-forth range; when Patrol is enabled without
                // an explicit range, it defaults to the entire width of the world (this body's
                // left edge sweeping from the world's left edge to its right edge), so "patrol
                // this hazard" works with zero extra authoring for the common full-level case.
                var patrol = objectSection.TryGetValue("Patrol", out var patrolText) && bool.TryParse(patrolText, out var parsedPatrol) && parsedPatrol;
                var patrolMinXOverride = objectSection.TryGetValue("PatrolMinX", out var patrolMinXText) && IniValueParser.TryParseDouble(patrolMinXText, out var parsedPatrolMinX)
                    ? (double?)parsedPatrolMinX
                    : null;
                var patrolMaxXOverride = objectSection.TryGetValue("PatrolMaxX", out var patrolMaxXText) && IniValueParser.TryParseDouble(patrolMaxXText, out var parsedPatrolMaxX)
                    ? (double?)parsedPatrolMaxX
                    : null;
                // PatrolForceMultiplierX/Y tune how strongly (and so how quickly it reaches its
                // cruising speed, mass-scaled like gravity) this hazard patrols on each axis,
                // defaulting to GameDefaults.PatrolForceMultiplier when unset. PatrolCruiseSpeedX/Y set the actual
                // steady-state patrol speed the force converges to and holds, also defaulting to
                // GameDefaults.PatrolCruiseSpeed when unset. PatrolInitialDirectionX ("Left"/"Right") -
                // named to match KinematicObject's own per-axis PatrolInitialDirectionX/Y below,
                // since a future vertically-patrolling hazard would need the same X/Y distinction -
                // overrides which way it starts heading, instead of the default inference toward
                // whichever bound is farther.
                var patrolForceXOverride = objectSection.TryGetValue("PatrolForceMultiplierX", out var patrolForceXText) && IniValueParser.TryParseDouble(patrolForceXText, out var parsedPatrolForceX)
                    ? (double?)parsedPatrolForceX
                    : null;
                var patrolForceYOverride = objectSection.TryGetValue("PatrolForceMultiplierY", out var patrolForceYText) && IniValueParser.TryParseDouble(patrolForceYText, out var parsedPatrolForceY)
                    ? (double?)parsedPatrolForceY
                    : null;
                var patrolCruiseSpeedOverride = objectSection.TryGetValue("PatrolCruiseSpeedX", out var patrolCruiseSpeedText) && IniValueParser.TryParseDouble(patrolCruiseSpeedText, out var parsedPatrolCruiseSpeed)
                    ? (double?)parsedPatrolCruiseSpeed
                    : null;
                // PatrolCruiseSpeedY lets a DynamicHazard's vertical patrol cruise at a different
                // speed than its horizontal one (PatrolCruiseSpeedX); each axis falls back to
                // the global default independently.
                var patrolCruiseSpeedYOverride = objectSection.TryGetValue("PatrolCruiseSpeedY", out var patrolCruiseSpeedYText) && IniValueParser.TryParseDouble(patrolCruiseSpeedYText, out var parsedPatrolCruiseSpeedY)
                    ? (double?)parsedPatrolCruiseSpeedY
                    : null;
                var patrolInitialDirectionRight = objectSection.TryGetValue("PatrolInitialDirectionX", out var patrolDirectionText)
                    ? (bool?)string.Equals(patrolDirectionText, "Right", StringComparison.OrdinalIgnoreCase)
                    : null;
                // PatrolMaxForceX/Y cap the patrol force per axis in multiples of g (see
                // DynamicObject2D.PatrolMaxForceX/Y); unset uses GameDefaults.DefaultPatrolMaxForceX/Y.
                var patrolMaxForceXOverride = objectSection.TryGetValue("PatrolMaxForceX", out var maxForceXText) && IniValueParser.TryParseDouble(maxForceXText, out var parsedMaxForceX)
                    ? (double?)parsedMaxForceX
                    : null;
                var patrolMaxForceYOverride = objectSection.TryGetValue("PatrolMaxForceY", out var maxForceYText) && IniValueParser.TryParseDouble(maxForceYText, out var parsedMaxForceY)
                    ? (double?)parsedMaxForceY
                    : null;
                // PatrolStayOnPlatform makes a DynamicHazard's horizontal range follow the platform it
                // stands on (see DynamicObject2D.PatrolStayOnPlatform) instead of fixed columns.
                var patrolStayOnPlatform = objectSection.TryGetValue("PatrolStayOnPlatform", out var stayOnPlatformText) && bool.TryParse(stayOnPlatformText, out var parsedStayOnPlatform) && parsedStayOnPlatform;
                // PatrolInitialDirectionY ("Up"/"Down") overrides which way a DynamicHazard starts
                // heading on its vertical patrol, mirroring PatrolInitialDirectionX for the
                // horizontal axis - see DynamicHazard2D.SetPatrol.
                var patrolInitialDirectionDownHazard = objectSection.TryGetValue("PatrolInitialDirectionY", out var patrolDirectionYTextHazard)
                    ? (bool?)string.Equals(patrolDirectionYTextHazard, "Down", StringComparison.OrdinalIgnoreCase)
                    : null;

                // Patrol range/speed keys shared by both Kind = DynamicHazard and Kind = KinematicObject:
                // PatrolMinY/PatrolMaxY let a body patrol vertically instead of (or as well as)
                // horizontally - both DynamicHazard2D and KinematicObject2D support independent
                // per-axis patrol (a body configured with both X and Y bounds patrols diagonally,
                // each axis bouncing between its own bounds on its own schedule). An axis with no
                // min/max pair configured simply isn't patrolled on that axis.
                var patrolMinYOverride = objectSection.TryGetValue("PatrolMinY", out var patrolMinYText) && IniValueParser.TryParseDouble(patrolMinYText, out var parsedPatrolMinY)
                    ? (double?)parsedPatrolMinY
                    : null;
                var patrolMaxYOverride = objectSection.TryGetValue("PatrolMaxY", out var patrolMaxYText) && IniValueParser.TryParseDouble(patrolMaxYText, out var parsedPatrolMaxY)
                    ? (double?)parsedPatrolMaxY
                    : null;
                var patrolSpeedX = objectSection.TryGetValue("PatrolSpeedX", out var patrolSpeedXText) && IniValueParser.TryParseDouble(patrolSpeedXText, out var parsedPatrolSpeedX)
                    ? parsedPatrolSpeedX
                    : 0.0;
                var patrolSpeedY = objectSection.TryGetValue("PatrolSpeedY", out var patrolSpeedYText) && IniValueParser.TryParseDouble(patrolSpeedYText, out var parsedPatrolSpeedY)
                    ? parsedPatrolSpeedY
                    : 0.0;
                // PatrolInitialDirectionX ("Left"/"Right") / PatrolInitialDirectionY ("Up"/"Down")
                // let a placement explicitly pin which way a KinematicObject starts heading on
                // that axis, instead of the default inference toward whichever bound is farther
                // from the spawn position - e.g. so two platforms sharing the same range can be
                // made to start in opposite phase. Expressed the same intuitive way as
                // DynamicHazard's own PatrolInitialDirectionX. Remember Y increases downward (see docs/Architecture.md's
                // Coordinate System), so "Down" means toward PatrolMaxY.
                var patrolInitialDirectionTowardMaxX = objectSection.TryGetValue("PatrolInitialDirectionX", out var patrolDirectionXText)
                    ? (bool?)string.Equals(patrolDirectionXText, "Right", StringComparison.OrdinalIgnoreCase)
                    : null;
                var patrolInitialDirectionTowardMaxY = objectSection.TryGetValue("PatrolInitialDirectionY", out var patrolDirectionYText)
                    ? (bool?)string.Equals(patrolDirectionYText, "Down", StringComparison.OrdinalIgnoreCase)
                    : null;

                // Passable/Climbable/Hangable are plain per-instance flags on Body2D (see its own
                // doc comments) rather than anything gated by Kind - any static placement can set
                // any combination of them. Passable defaults to true for hazards/collectables
                // (never blocking today, matching prior hardcoded behavior) and false otherwise
                // (an ordinary wall/platform still blocks unless explicitly overridden).
                var defaultPassable = kind is "StaticHazard" or "KinematicHazard" or "Collectable" or "Switch" or "FlowZone";
                var passable = objectSection.TryGetValue("Passable", out var passableText) && bool.TryParse(passableText, out var parsedPassable)
                    ? parsedPassable
                    : defaultPassable;
                var climbable = objectSection.TryGetValue("Climbable", out var climbableText) && bool.TryParse(climbableText, out var parsedClimbable) && parsedClimbable;
                var hangable = objectSection.TryGetValue("Hangable", out var hangableText) && bool.TryParse(hangableText, out var parsedHangable) && parsedHangable;
                var mediumCandidate = objectSection.TryGetValue("Medium", out var mediumText) && bool.TryParse(mediumText, out var parsedMedium) && parsedMedium;

                // X defaults to the entire world width (this body's left edge sweeping from the
                // world's left edge to its right edge) when Patrol is enabled without any explicit
                // range - unless the placement configured a vertical-only patrol via
                // PatrolMinY/PatrolMaxY with no X override at all, in which case this body patrols
                // vertically only rather than also silently sweeping the full world width.
                void ConfigureDynamicPatrol(DynamicObject2D body)
                {
                    if (!patrol)
                    {
                        return;
                    }

                    var hasExplicitX = patrolMinXOverride.HasValue || patrolMaxXOverride.HasValue;
                    var hasExplicitY = patrolMinYOverride.HasValue || patrolMaxYOverride.HasValue;
                    double? bodyPatrolMinX = null;
                    double? bodyPatrolMaxX = null;
                    if (hasExplicitX || !hasExplicitY)
                    {
                        bodyPatrolMinX = patrolMinXOverride ?? 0.0;
                        bodyPatrolMaxX = patrolMaxXOverride ?? world.WidthCells - body.Size.X;
                    }

                    body.SetPatrol(
                        bodyPatrolMinX, bodyPatrolMaxX,
                        patrolCruiseSpeedOverride ?? Constants.GameDefaults.PatrolCruiseSpeed,
                        patrolMinYOverride, patrolMaxYOverride,
                        patrolCruiseSpeedYOverride ?? Constants.GameDefaults.PatrolCruiseSpeed,
                        patrolForceXOverride ?? Constants.GameDefaults.PatrolForceMultiplier,
                        patrolForceYOverride ?? Constants.GameDefaults.PatrolForceMultiplier,
                        patrolInitialDirectionRight,
                        patrolInitialDirectionDownHazard,
                        patrolMaxForceXOverride ?? Constants.GameDefaults.DefaultPatrolMaxForceX,
                        patrolMaxForceYOverride ?? Constants.GameDefaults.DefaultPatrolMaxForceY,
                        patrolStayOnPlatform);
                }

                IPhysicsBody? movingBody = null;
                Body2D spawnedBody;

                switch (kind)
                {
                    case "Player":
                        world.Player.Spawn(sprite);
                        world.Player.Position = position;
                        world.PlayerSpawnPosition = position;
                        world.Player.HazardHitEffectClipName = hazardHitEffectClipName;
                        world.Player.CheckpointReachedEffectClipName = checkpointReachedEffectClipName;
                        world.Player.LevelEndReachedEffectClipName = levelEndReachedEffectClipName;
                        world.Player.DiedEffectClipName = diedEffectClipName;
                        world.Player.Lives = world.StartingLives;
                        world.Player.Health = GameDefaults.PlayerFullHealth;
                        var playerMaterial = world.Materials.Get(materialOverride ?? world.Player.MaterialName);
                        world.Player.Density = densityOverride ?? playerMaterial.Density;
                        world.Player.Friction = frictionOverride ?? playerMaterial.Friction;
                        world.Player.Restitution = restitutionOverride ?? playerMaterial.Restitution;
                        world.Player.Hardness = hardnessOverride ?? playerMaterial.Hardness;
                        world.Player.Viscosity = playerMaterial.Viscosity;
                        // Player-only "muscle power"/cruising-speed overrides - default to
                        // PhysicsSystem's own constants (see Player2D.WalkForceMultiplier/WalkSpeed/
                        // CrawlSpeed) unless this world's Player section sets its own.
                        if (objectSection.TryGetValue("WalkForceMultiplier", out var walkForceMultiplierText) && IniValueParser.TryParseDouble(walkForceMultiplierText, out var parsedWalkForceMultiplier))
                        {
                            world.Player.WalkForceMultiplier = parsedWalkForceMultiplier;
                        }
                        if (objectSection.TryGetValue("WalkSpeed", out var walkSpeedOverrideText) && IniValueParser.TryParseDouble(walkSpeedOverrideText, out var parsedWalkSpeedOverride))
                        {
                            world.Player.WalkSpeed = parsedWalkSpeedOverride;
                        }
                        if (objectSection.TryGetValue("CrawlSpeed", out var crawlSpeedOverrideText) && IniValueParser.TryParseDouble(crawlSpeedOverrideText, out var parsedCrawlSpeedOverride))
                        {
                            world.Player.CrawlSpeed = parsedCrawlSpeedOverride;
                        }
                        if (IsCameraTarget(objectSection))
                        {
                            world.CameraTarget = world.Player;
                        }
                        continue;

                    case "StaticObject":
                        var platform = new StaticObject2D();
                        platform.Spawn(sprite, clipName, frameIndex, position, repeatCount);
                        world.Objects.Add(platform);
                        spawnedBody = platform;
                        break;

                    case "Gate":
                        if (lockName is not null && linkName is not null)
                        {
                            throw new FormatException($"Gate section '{sectionName}' of world '{worldName}' has both 'Lock' and 'Link'; a gate is opened by either a key or a switch, not both.");
                        }

                        var gate = new Gate2D { LockName = lockName, LinkName = linkName };
                        gate.SpawnTwoState(sprite, clipName, onClipName, initialOn, position, repeatCount);
                        world.Objects.Add(gate);
                        spawnedBody = gate;
                        break;

                    case "Switch":
                        var latching = objectSection.TryGetValue("Latching", out var latchingText) && bool.TryParse(latchingText, out var parsedLatching) && parsedLatching;
                        var switchObject = new Switch2D { Latching = latching, LinkName = linkName };
                        switchObject.SpawnTwoState(sprite, clipName, onClipName, initialOn, position, repeatCount);
                        world.Objects.Add(switchObject);
                        spawnedBody = switchObject;
                        break;

                    case "Device":
                        var device = new Device2D { LinkName = linkName };
                        device.SpawnTwoState(sprite, clipName, onClipName, initialOn, position, repeatCount);
                        world.Objects.Add(device);
                        spawnedBody = device;
                        break;

                    case "FlowZone":
                        // Y increases downward (see docs/Architecture.md's Coordinate System), so "Up" is -Y.
                        var flowDirection = (objectSection.TryGetValue("FlowDirection", out var flowDirectionText) ? flowDirectionText.Trim().ToLowerInvariant() : null) switch
                        {
                            "left" => new Vector2D(-1, 0),
                            "right" => new Vector2D(1, 0),
                            "up" => new Vector2D(0, -1),
                            "down" => new Vector2D(0, 1),
                            _ => throw new FormatException($"FlowZone section '{sectionName}' of world '{worldName}' needs a 'FlowDirection' of Left, Right, Up or Down.")
                        };
                        var flowStrength = objectSection.TryGetValue("FlowStrength", out var flowStrengthText) && IniValueParser.TryParseDouble(flowStrengthText, out var parsedFlowStrength)
                            ? parsedFlowStrength
                            : throw new FormatException($"FlowZone section '{sectionName}' of world '{worldName}' needs a numeric 'FlowStrength'.");
                        var flowZone = new FlowZone2D { LinkName = linkName, FlowDirection = flowDirection, FlowStrength = flowStrength };
                        flowZone.SpawnTwoState(sprite, clipName, onClipName, initialOn, position, repeatCount);
                        world.Objects.Add(flowZone);
                        spawnedBody = flowZone;
                        break;

                    case "DynamicObject":
                        var dynamicObject = new DynamicObject2D();
                        dynamicObject.Spawn(sprite, clipName, frameIndex, position, initialVelocity, gravityAffected, repeatCount);
                        ConfigureDynamicPatrol(dynamicObject);
                        world.Objects.Add(dynamicObject);
                        movingBody = dynamicObject;
                        spawnedBody = dynamicObject;
                        break;

                    case "KinematicObject":
                        var kinematicObject = new KinematicObject2D();
                        kinematicObject.Spawn(sprite, clipName, frameIndex, position, initialVelocity, repeatCount);
                        if (patrol)
                        {
                            kinematicObject.SetPatrol(
                                patrolMinXOverride, patrolMaxXOverride, patrolSpeedX,
                                patrolMinYOverride, patrolMaxYOverride, patrolSpeedY,
                                patrolInitialDirectionTowardMaxX, patrolInitialDirectionTowardMaxY);
                        }
                        world.Objects.Add(kinematicObject);
                        kinematicObject.LinkName = linkName;
                        movingBody = kinematicObject;
                        spawnedBody = kinematicObject;
                        break;

                    case "KinematicHazard":
                        var kinematicHazard = new KinematicHazard2D();
                        kinematicHazard.Spawn(sprite, clipName, frameIndex, position, initialVelocity, repeatCount);
                        kinematicHazard.EffectClipName = effectClipName;
                        kinematicHazard.IsKillable = killable;
                        kinematicHazard.EffectPersists = effectPersists;
                        kinematicHazard.KillSoundName = killSoundName;
                        kinematicHazard.Damage = hazardDamage;
                        if (patrol)
                        {
                            kinematicHazard.SetPatrol(
                                patrolMinXOverride, patrolMaxXOverride, patrolSpeedX,
                                patrolMinYOverride, patrolMaxYOverride, patrolSpeedY,
                                patrolInitialDirectionTowardMaxX, patrolInitialDirectionTowardMaxY);
                        }
                        world.Objects.Add(kinematicHazard);
                        movingBody = kinematicHazard;
                        spawnedBody = kinematicHazard;
                        break;

                    case "DynamicHazard":
                        var dynamicHazard = new DynamicHazard2D();
                        dynamicHazard.Spawn(sprite, clipName, frameIndex, position, initialVelocity, gravityAffected, repeatCount);
                        dynamicHazard.EffectClipName = effectClipName;
                        dynamicHazard.IsKillable = killable;
                        dynamicHazard.EffectPersists = effectPersists;
                        dynamicHazard.KillSoundName = killSoundName;
                        dynamicHazard.Damage = hazardDamage;
                        ConfigureDynamicPatrol(dynamicHazard);
                        world.Objects.Add(dynamicHazard);
                        movingBody = dynamicHazard;
                        spawnedBody = dynamicHazard;
                        break;

                    case "StaticHazard":
                        var staticHazard = new StaticHazard2D();
                        staticHazard.Spawn(sprite, clipName, frameIndex, position, repeatCount);
                        staticHazard.EffectClipName = effectClipName;
                        staticHazard.IsKillable = killable;
                        staticHazard.EffectPersists = effectPersists;
                        staticHazard.KillSoundName = killSoundName;
                        staticHazard.Damage = hazardDamage;
                        world.Objects.Add(staticHazard);
                        spawnedBody = staticHazard;
                        break;

                    case "Collectable":
                        // Type distinguishes what picking this up actually does (see
                        // CollectableType). Required per-placement: an absent/unrecognized Type is
                        // left null so the collectable does nothing on pickup, rather than
                        // silently falling back to some default variant.
                        var collectableType = objectSection.TryGetValue("Type", out var collectableTypeText) && Enum.TryParse<CollectableType>(collectableTypeText, ignoreCase: true, out var parsedCollectableType)
                            ? parsedCollectableType
                            : (CollectableType?)null;
                        var collectable = collectableType == CollectableType.Key ? new Key2D() : new Collectable2D();
                        collectable.Spawn(sprite, clipName, frameIndex, position, collectableType, repeatCount);
                        if (collectable is Key2D key)
                        {
                            key.LockName = lockName;
                        }

                        collectable.EffectClipName = effectClipName;
                        collectable.EffectPersists = effectPersists;
                        world.Objects.Add(collectable);
                        spawnedBody = collectable;
                        break;

                    default:
                        throw new FormatException($"Unknown object Kind '{kind}' in section '{sectionName}' of world '{worldName}'.");
                }

                if (spawnedBody is not Gate2D)
                {
                    spawnedBody.IsPassable = passable;
                }

                spawnedBody.SoundName = objectSection.TryGetValue("Sound", out var soundNameText) ? soundNameText : null;
                spawnedBody.IsClimbable = climbable;
                spawnedBody.IsHangable = hangable;
                spawnedBody.IsMediumCandidate = mediumCandidate;
                spawnedBody.ForeColorOverride = foreColorOverride;
                spawnedBody.BackColorOverride = backColorOverride;

                // Density/Friction always come from the spawned body's own resolved material
                // (see Body2D.MaterialName/ApplyFrame) unless this object type's ini section
                // overrides it via Material (e.g. TestPhysics re-using the Ball asset with
                // different materials); Restitution/Friction/Density can each be independently
                // overridden too - Restitution (e.g. TestMovement's "weightless, perfectly
                // elastic" placeholder ball tuning), Friction (grip independent of the shared
                // material), Density (this one object type's density independent of the shared
                // material, e.g. a body whose effective density changes per-instance/at runtime).
                // Mass itself (see Body2D.Mass) is always computed from Density times footprint -
                // a body whose real-world mass shouldn't just fall out of its footprint size (e.g.
                // a snake much lighter than a human despite a similar bounding box) should use a
                // dedicated, appropriately-calibrated material instead (see Global/MaterialLibrary.ini).
                var material = world.Materials.Get(materialOverride ?? spawnedBody.MaterialName);
                spawnedBody.Density = densityOverride ?? material.Density;
                spawnedBody.Friction = frictionOverride ?? material.Friction;
                spawnedBody.Restitution = restitutionOverride ?? material.Restitution;
                spawnedBody.Hardness = hardnessOverride ?? material.Hardness;
                spawnedBody.Viscosity = material.Viscosity;
                if (materialOverride is not null)
                {
                    // Keep MaterialName consistent with the material this override just resolved,
                    // not just Density/Friction/Restitution above - otherwise WorldRenderer's own
                    // material color resolution (which reads MaterialName) would keep using the
                    // sprite's own baked-in material (e.g. Ball's DefaultMaterial = Rubber) even
                    // for a placement that overrode Material to something else (e.g. Plastic),
                    // silently ignoring that override's own ForegroundColor/BackgroundColor.
                    spawnedBody.MaterialName = materialOverride;
                }

                if (movingBody is not null && IsCameraTarget(objectSection))
                {
                    world.CameraTarget = movingBody;
                }
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

    private static async Task<SpriteAsset> GetOrLoadSpriteAsync(
        SpriteLoader spriteLoader,
        Dictionary<string, SpriteAsset> cache,
        string assetName,
        IReadOnlyList<string> clipNames,
        string worldName)
    {
        if (cache.TryGetValue(assetName, out var cached))
        {
            return cached;
        }

        var sprite = await spriteLoader.LoadAsync(assetName, clipNames, worldName);
        cache[assetName] = sprite;
        return sprite;
    }

    /// <summary>
    /// Whether an object's ini section explicitly opts in as the body the camera should follow
    /// (<c>CameraTarget = true</c>). Absent or unparsable values default to false, i.e. "leave
    /// the camera target as whatever it already is" (ultimately the player, unless some other
    /// object claims it).
    /// </summary>
    private static bool IsCameraTarget(IReadOnlyDictionary<string, string> objectSection) =>
        objectSection.TryGetValue("CameraTarget", out var cameraTargetText)
        && bool.TryParse(cameraTargetText, out var isCameraTarget)
        && isCameraTarget;
}
