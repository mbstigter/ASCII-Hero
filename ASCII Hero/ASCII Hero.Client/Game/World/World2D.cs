using System.Globalization;
using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.World;

/// <summary>Holds all entities that make up the current game state.</summary>
public class World2D
{
    public Player2D Player { get; } = new();

    /// <summary>
    /// The body the camera should follow. Defaults to <see cref="Player"/>, but a level can
    /// designate a different body instead (e.g. the bouncing ball in LevelBallTest) by setting
    /// <c>CameraTarget = true</c> on that object's section in the level's objects.ini.
    /// </summary>
    public IPhysicsBody CameraTarget { get; set; } = null!;

    /// <summary>
    /// Every body in the world - static or moving, player or otherwise - as one generic
    /// collection. Systems (<see cref="Physics.PhysicsSystem"/>, <see cref="Physics.CollisionSystem"/>,
    /// <see cref="Rendering.AsciiRenderer"/>) iterate this list and filter by capability
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

    /// <summary>Background layer of the level, purely visual - one glyph per world cell.</summary>
    public char[,] BackgroundChars { get; private set; } = new char[0, 0];

    /// <summary>Foreground color code per background cell, same dimensions as <see cref="BackgroundChars"/>.</summary>
    public char[,] BackgroundFore { get; private set; } = new char[0, 0];

    /// <summary>Background (fill) color code per background cell, same dimensions as <see cref="BackgroundChars"/>.</summary>
    public char[,] BackgroundBack { get; private set; } = new char[0, 0];

    /// <summary>"No cell here" marker used by this level's background/object grids.</summary>
    public char EmptyChar { get; private set; } = ' ';

    /// <summary>The resolved Global+Level color palette, used to render every fore/back color code.</summary>
    public ColorPalette Palette { get; private set; } = null!;

    /// <summary>Gravity acceleration, in world cells per second squared.</summary>
    public double Gravity { get; private set; } = 40;

    /// <summary>Width of the world, in cells, derived from the background layer.</summary>
    public int WidthCells { get; private set; }

    /// <summary>Height of the world, in cells, derived from the background layer.</summary>
    public int HeightCells { get; private set; }

    /// <summary>
    /// Loads a level's background/object-placement files and the sprite assets they reference,
    /// building up Platforms and the Player's spawn position/sprite. Replaces what used to be a
    /// hardcoded constructor - see AssetFormat.md section 3 for the level file format and
    /// section 1.1 for the Global/Level fallback rule applied by SpriteLoader.
    /// </summary>
    public static async Task<World2D> LoadAsync(IAssetFileProvider fileProvider, string levelName)
    {
        var world = new World2D();
        var levelFolder = $"{AssetPathResolver.LevelsRoot}/{levelName}";

        var settingsContent = await fileProvider.TryReadTextAsync($"{levelFolder}/{levelName}_settings.ini");
        var settings = IniDocument.Parse(settingsContent ?? string.Empty);
        var emptyChar = ParseEmptyChar(settings.TryGetValue("Layout", "EmptyChar"));
        world.EmptyChar = emptyChar;

        var globalSettingsContent = await fileProvider.TryReadTextAsync($"{AssetPathResolver.GlobalRoot}/Settings.ini");
        var globalSettings = IniDocument.Parse(globalSettingsContent ?? string.Empty);
        if (TryParseDouble(globalSettings.TryGetValue("Physics", "Gravity"), out var gravity))
        {
            world.Gravity = gravity;
        }

        world.Palette = await ColorPalette.LoadAsync(fileProvider, levelName);

        var backgroundContent = await fileProvider.TryReadTextAsync($"{levelFolder}/{levelName}_background_characters.txt")
            ?? throw new FileNotFoundException($"Missing required background layer for level '{levelName}'.");
        var backgroundFrames = AssetTextReader.ParseCharsLayer(backgroundContent, emptyChar);
        world.BackgroundChars = backgroundFrames[0];
        var width = world.BackgroundChars.GetLength(1);
        var height = world.BackgroundChars.GetLength(0);
        world.WidthCells = width;
        world.HeightCells = height;

        var backgroundForeContent = await fileProvider.TryReadTextAsync($"{levelFolder}/{levelName}_background_foregroundcolors.txt");
        var backgroundBackContent = await fileProvider.TryReadTextAsync($"{levelFolder}/{levelName}_background_backgroundcolors.txt");
        world.BackgroundFore = AssetTextReader.ParseSecondaryLayer(backgroundForeContent, backgroundFrames, emptyChar)[0];
        world.BackgroundBack = AssetTextReader.ParseSecondaryLayer(backgroundBackContent, backgroundFrames, emptyChar)[0];

        var objectsIniContent = await fileProvider.TryReadTextAsync($"{levelFolder}/{levelName}_objects.ini")
            ?? throw new FileNotFoundException($"Missing required object placement definitions for level '{levelName}'.");
        var objectsIni = IniDocument.Parse(objectsIniContent);

        var objectsContent = await fileProvider.TryReadTextAsync($"{levelFolder}/{levelName}_objects.txt")
            ?? throw new FileNotFoundException($"Missing required object placement grid for level '{levelName}'.");
        var objectsGrid = AssetTextReader.ParseFixedSizeGrid(objectsContent, width, height, emptyChar);

        var spriteLoader = new SpriteLoader(fileProvider);
        var spriteCache = new Dictionary<string, SpriteAsset>(StringComparer.OrdinalIgnoreCase);

        for (var row = 0; row < height; row++)
        {
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
                if (!objectSection.TryGetValue("Asset", out var assetName))
                {
                    throw new FormatException($"Section '{sectionName}' of level '{levelName}' is missing required key 'Asset'.");
                }

                if (!objectSection.TryGetValue("Kind", out var kind))
                {
                    throw new FormatException($"Section '{sectionName}' of level '{levelName}' is missing required key 'Kind'.");
                }

                var clipName = objectSection.TryGetValue("Clip", out var clip) ? clip : "default";
                var repeatCount = objectSection.TryGetValue("Repeat", out var repeatText) && TryParseInt(repeatText, out var parsedRepeat)
                    ? parsedRepeat
                    : 1;
                var position = new Vector2D(col, row);

                var sprite = await GetOrLoadSpriteAsync(spriteLoader, spriteCache, assetName, [clipName], levelName);
                var frameIndex = sprite.GetClip(clipName).DefaultFrame;

                var gravityAffected = !objectSection.TryGetValue("GravityAffected", out var gravityText) || !bool.TryParse(gravityText, out var parsedGravity) || parsedGravity;
                var restitution = objectSection.TryGetValue("Restitution", out var restitutionText) && TryParseDouble(restitutionText, out var parsedRestitution)
                    ? parsedRestitution
                    : 1.0;
                var initialVelocityX = objectSection.TryGetValue("InitialVelocityX", out var velocityXText) && TryParseDouble(velocityXText, out var parsedVelocityX)
                    ? parsedVelocityX
                    : 0.0;
                var initialVelocityY = objectSection.TryGetValue("InitialVelocityY", out var velocityYText) && TryParseDouble(velocityYText, out var parsedVelocityY)
                    ? parsedVelocityY
                    : 0.0;
                var initialVelocity = new Vector2D(initialVelocityX, initialVelocityY);

                IPhysicsBody? movingBody = null;

                switch (kind)
                {
                    case "Player":
                        world.Player.Spawn(sprite);
                        world.Player.Position = position;
                        if (IsCameraTarget(objectSection))
                        {
                            world.CameraTarget = world.Player;
                        }
                        continue;

                    case "StaticObject":
                        var platform = new StaticObject2D();
                        platform.Spawn(sprite, clipName, frameIndex, position, repeatCount);
                        world.Objects.Add(platform);
                        break;

                    case "DynamicObject":
                        var dynamicObject = new DynamicObject2D();
                        dynamicObject.Spawn(sprite, clipName, frameIndex, position, initialVelocity, gravityAffected, restitution, repeatCount);
                        world.Objects.Add(dynamicObject);
                        movingBody = dynamicObject;
                        break;

                    case "KinematicObject":
                        var kinematicObject = new KinematicObject2D();
                        kinematicObject.Spawn(sprite, clipName, frameIndex, position, initialVelocity, repeatCount);
                        world.Objects.Add(kinematicObject);
                        movingBody = kinematicObject;
                        break;

                    case "MovingEnemy":
                        var movingEnemy = new MovingEnemy2D();
                        movingEnemy.Spawn(sprite, clipName, frameIndex, position, initialVelocity, gravityAffected, restitution, repeatCount);
                        world.Objects.Add(movingEnemy);
                        movingBody = movingEnemy;
                        break;

                    case "StaticEnemy":
                        var staticEnemy = new StaticEnemy2D();
                        staticEnemy.Spawn(sprite, clipName, frameIndex, position, repeatCount);
                        world.Objects.Add(staticEnemy);
                        break;

                    case "Collectable":
                        var collectable = new Collectable2D();
                        collectable.Spawn(sprite, clipName, frameIndex, position, repeatCount);
                        world.Objects.Add(collectable);
                        break;

                    default:
                        throw new FormatException($"Unknown object Kind '{kind}' in section '{sectionName}' of level '{levelName}'.");
                }

                if (movingBody is not null && IsCameraTarget(objectSection))
                {
                    world.CameraTarget = movingBody;
                }
            }
        }

        // Player participates in the generic Objects list alongside every other body.
        world.Objects.Add(world.Player);

        // No object explicitly claimed the camera via CameraTarget = true; default to the player.
        world.CameraTarget ??= world.Player;

        return world;
    }

    private static async Task<SpriteAsset> GetOrLoadSpriteAsync(
        SpriteLoader spriteLoader,
        Dictionary<string, SpriteAsset> cache,
        string assetName,
        IReadOnlyList<string> clipNames,
        string levelName)
    {
        if (cache.TryGetValue(assetName, out var cached))
        {
            return cached;
        }

        var sprite = await spriteLoader.LoadAsync(assetName, clipNames, levelName);
        cache[assetName] = sprite;
        return sprite;
    }

    private static char ParseEmptyChar(string? rawValue) =>
        string.IsNullOrEmpty(rawValue) ? ' ' : rawValue[0];

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

    /// <summary>
    /// Parses a numeric ini value with <see cref="CultureInfo.InvariantCulture"/> so a decimal
    /// point (e.g. "1.0") is never misread as a thousands separator under locales where '.'
    /// is the group separator (which silently turned "1.0" into 10 and sent physics values
    /// like Restitution wildly out of range).
    /// </summary>
    private static bool TryParseDouble(string? rawValue, out double value) =>
        double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TryParseInt(string? rawValue, out int value) =>
        int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
