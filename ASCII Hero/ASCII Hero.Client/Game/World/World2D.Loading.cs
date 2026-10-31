using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Rendering;

namespace ASCII_Hero.Client.Game.World;

public partial class World2D
{
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
        // Gravity always pulls down: a finite, non-negative magnitude (0 = weightless). Inverted
        // gravity is not a supported concept, so a negative or malformed value is rejected here
        // rather than producing undefined physics later.
        var gravityText = settings.TryGetValue("Physics", "Gravity") ?? globalSettings.TryGetValue("Physics", "Gravity");
        if (gravityText is not null)
        {
            if (!IniValueParser.TryParseDouble(gravityText, out var gravity) || !double.IsFinite(gravity) || gravity < 0)
            {
                throw new FormatException($"World '{worldName}' [Physics] Gravity '{gravityText}' must be a finite number of at least 0.");
            }

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

        if (world.Player.Sprite is null)
        {
            throw new FormatException($"World '{worldName}' has no player placement; exactly one is required.");
        }

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
            if (world.Player.Sprite is not null)
            {
                throw section.Error("places the player a second time; a world has exactly one player placement");
            }

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
        var killImpactSpeed = section.FiniteNonNegativeNumber("KillImpactSpeed") ?? GameDefaults.DefaultKillImpactSpeed;
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
                    KillImpactSpeed = killImpactSpeed,
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
                    KillImpactSpeed = killImpactSpeed,
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
                    KillImpactSpeed = killImpactSpeed,
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
            patrolMinX: route.MinX,
            patrolMaxX: route.MaxX,
            cruiseSpeedX: section.FiniteNonNegativeNumber("PatrolCruiseSpeedX") ?? GameDefaults.PatrolCruiseSpeed,
            patrolMinY: route.MinY,
            patrolMaxY: route.MaxY,
            cruiseSpeedY: section.FiniteNonNegativeNumber("PatrolCruiseSpeedY") ?? GameDefaults.PatrolCruiseSpeed,
            forceMultiplierX: section.FiniteNonNegativeNumber("PatrolForceMultiplierX") ?? GameDefaults.PatrolForceMultiplier,
            forceMultiplierY: section.FiniteNonNegativeNumber("PatrolForceMultiplierY") ?? GameDefaults.PatrolForceMultiplier,
            initialDirectionRight: route.TowardMaxX,
            initialDirectionDown: route.TowardMaxY,
            maxForceX: section.NonNegativeNumber("PatrolMaxForceX") ?? GameDefaults.DefaultPatrolMaxForceX,
            maxForceY: section.NonNegativeNumber("PatrolMaxForceY") ?? GameDefaults.DefaultPatrolMaxForceY,
            stayOnPlatform: section.Flag("PatrolStayOnPlatform"));
    }

    /// <summary>Configures a kinematic body's patrol from its placement, if it patrols (see <see cref="ReadPatrolRoute"/>).</summary>
    private static void ConfigureKinematicPatrol(World2D world, KinematicObject2D body, ObjectSection section)
    {
        if (ReadPatrolRoute(world, body, section) is not { } route)
        {
            return;
        }

        body.SetPatrol(
            patrolMinX: route.MinX,
            patrolMaxX: route.MaxX,
            patrolSpeedX: section.FiniteNonNegativeNumber("PatrolSpeedX") ?? GameDefaults.PatrolCruiseSpeed,
            patrolMinY: route.MinY,
            patrolMaxY: route.MaxY,
            patrolSpeedY: section.FiniteNonNegativeNumber("PatrolSpeedY") ?? GameDefaults.PatrolCruiseSpeed,
            initialDirectionTowardMaxX: route.TowardMaxX,
            initialDirectionTowardMaxY: route.TowardMaxY);
    }

    /// <summary>
    /// Reads a placement's patrol route, or null if it doesn't patrol. A placement patrols when
    /// <c>Patrol = true</c>, or when it sets any of <c>PatrolMinX</c>/<c>PatrolMaxX</c>/<c>PatrolMinY</c>/<c>PatrolMaxY</c>
    /// without <c>Patrol = false</c>. An axis with either bound set is patrolled, a missing bound
    /// defaulting to that world edge; with no bounds at all the body patrols the full world width.
    /// </summary>
    private static PatrolRoute? ReadPatrolRoute(World2D world, Body2D body, ObjectSection section)
    {
        var minX = section.FiniteNonNegativeNumber("PatrolMinX");
        var maxX = section.FiniteNonNegativeNumber("PatrolMaxX");
        var minY = section.FiniteNonNegativeNumber("PatrolMinY");
        var maxY = section.FiniteNonNegativeNumber("PatrolMaxY");
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

        var route = new PatrolRoute(
            hasX ? minX ?? 0.0 : null,
            hasX ? maxX ?? world.WidthCells - body.Size.X : null,
            hasY ? minY ?? 0.0 : null,
            hasY ? maxY ?? world.HeightCells - body.Size.Y : null,
            section.TowardMax("PatrolInitialDirectionX", "Left", "Right"),
            section.TowardMax("PatrolInitialDirectionY", "Up", "Down"));

        if (route.MinX > route.MaxX)
        {
            throw section.Error($"has PatrolMinX {route.MinX} greater than PatrolMaxX {route.MaxX}");
        }

        if (route.MinY > route.MaxY)
        {
            throw section.Error($"has PatrolMinY {route.MinY} greater than PatrolMaxY {route.MaxY}");
        }

        return route;
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
    /// decides an object's shape, direction or patrol (Span, Width, Height, directions, conveyor and
    /// flow speeds, patrol bounds/speeds/forces) reject an unreadable value with an error naming the
    /// section and world.
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

        /// <summary>Like <see cref="NonNegativeNumber"/> but also rejects Infinity - for values that must be an actual amount (patrol bounds, speeds, multipliers).</summary>
        public double? FiniteNonNegativeNumber(string key)
        {
            var text = Text(key);
            if (text is null)
            {
                return null;
            }

            return IniValueParser.TryParseDouble(text, out var value) && value >= 0 && double.IsFinite(value)
                ? value
                : throw Error($"has invalid '{key}' value '{text}' (expected a finite number, 0 or more)");
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
