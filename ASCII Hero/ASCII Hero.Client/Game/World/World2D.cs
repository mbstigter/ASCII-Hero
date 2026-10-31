using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Rendering;

namespace ASCII_Hero.Client.Game.World;

/// <summary>Holds all entities that make up the current game state.</summary>
public partial class World2D
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
    /// collectable picked up this frame. Called by the game loop after each physics substep
    /// (and after a player action that might queue a removal), once all systems that might
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
    /// from its sprite's <see cref="SpriteAsset.DefaultMaterial"/> or its placement's <c>Material</c>.
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

}
