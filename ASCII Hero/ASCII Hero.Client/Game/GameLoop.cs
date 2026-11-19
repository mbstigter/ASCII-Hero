using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Audio;
using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Menu;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.Rendering;
using ASCII_Hero.Client.Game.World;
using Microsoft.JSInterop;

namespace ASCII_Hero.Client.Game;

/// <summary>
/// Ties together world state, physics, collision, camera and rendering, and drives them once
/// per animation frame. Invoked from JS via requestAnimationFrame, never via Blazor's StateHasChanged.
/// </summary>
public class GameLoop(CanvasBridge canvasBridge, IAssetFileProvider assetFileProvider, ISoundPlayer soundPlayer)
{
    // ---------------------------------------------------------------------------------------
    // Modes
    // ---------------------------------------------------------------------------------------

    /// <summary>The state <see cref="OnFrame"/> is in; exactly one is active at a time.</summary>
    private enum GameMode
    {
        /// <summary>World-selection screen; no <see cref="World2D"/> exists yet.</summary>
        WorldSelecting,

        /// <summary>A confirmed world is loading in the background; the selection screen and a filling progress bar are drawn.</summary>
        LoadingWorld,

        /// <summary>Normal Physics/Collision/Camera/Render tick against the loaded <see cref="World2D"/>.</summary>
        Playing,

        /// <summary>Gameplay is frozen and a message is shown, then the game returns to <see cref="WorldSelecting"/>.</summary>
        WorldComplete,

        /// <summary>Same as <see cref="WorldComplete"/>, but shown when the player lost their last life.</summary>
        GameOver,

        /// <summary>Gameplay is frozen and an "Exit Game?" message is shown; confirming returns to <see cref="WorldSelecting"/>, Escape resumes <see cref="Playing"/>.</summary>
        ConfirmExit,

        /// <summary>Gameplay is frozen and the gear ring (<see cref="GearRingScreen"/>) is shown; once it has closed again, <see cref="Playing"/> resumes.</summary>
        GearRing,
    }

    private GameMode _mode = GameMode.WorldSelecting;

    /// <summary>Guards <see cref="OnFrame"/> against overlapping calls; an overlapping frame is simply dropped.</summary>
    private bool _isProcessingFrame;

    /// <summary>Set by <see cref="Stop"/>; once set, <see cref="OnFrame"/> does nothing.</summary>
    private bool _isStopped;

    // ---------------------------------------------------------------------------------------
    // Systems and world state
    // ---------------------------------------------------------------------------------------

    private readonly InputState _input = new();
    private readonly PhysicsSystem _physics = new();
    private readonly CollisionSystem _collision = new();
    private readonly Camera _camera = new();
    private readonly WorldRenderer _renderer = new();
    private readonly AnimationSystem _animation = new();

    private World2D _world = null!;

    /// <summary>
    /// The worlds of the current play session by sub-world number (0 is the main world), kept so that
    /// going back through a portal finds a world exactly as it was left: opened gates, collected items and all.
    /// </summary>
    private readonly Dictionary<int, World2D> _sessionWorlds = [];
    private WorldSelectScreen _worldSelect = null!;

    /// <summary>Unspent time carried between frames by the fixed-timestep physics loop (always less than one fixed step).</summary>
    private double _physicsAccumulatorSeconds;

    // ---------------------------------------------------------------------------------------
    // Viewport
    // ---------------------------------------------------------------------------------------

    private double _viewportWidthCells;
    private double _viewportHeightCells;
    private int _viewportWidthPixels;
    private int _viewportHeightPixels;

    // ---------------------------------------------------------------------------------------
    // World selection and loading
    // ---------------------------------------------------------------------------------------

    /// <summary>Sound played when the world-select highlight moves (see Global/SoundLibrary.ini).</summary>
    private const string MenuMoveSoundName = "MenuMove";

    /// <summary>Sound played when a world is confirmed (see Global/SoundLibrary.ini).</summary>
    private const string MenuSelectSoundName = "MenuSelect";

    /// <summary>Progress bar shown while <see cref="GameMode.LoadingWorld"/> is active; created when a world is confirmed.</summary>
    private UIBar? _loadingBar;

    /// <summary>The in-flight world load; polled each frame while <see cref="GameMode.LoadingWorld"/> is active, never awaited inline.</summary>
    private Task? _loadWorldTask;

    // ---------------------------------------------------------------------------------------
    // Pending transitions and end screens (world complete / game over)
    // ---------------------------------------------------------------------------------------

    /// <summary>How long the player's world-end or death effect and sound play before the transition is carried out (see <see cref="World2D.PendingTransition"/>).</summary>
    private const double PendingTransitionSeconds = 2.0;

    /// <summary>How long the player's portal effect and sound play before the move to the target world (see <see cref="PendingTransition.EnterWorld"/>).</summary>
    private const double PortalTransitionSeconds = 0.6;

    private double _pendingTransitionElapsedSeconds;

    /// <summary>Set once no confirm key is held on an end screen, so a key still held from gameplay doesn't dismiss it instantly.</summary>
    private bool _endScreenKeysReleased;

    /// <summary>Draws the active <see cref="GameMode.WorldComplete"/>/<see cref="GameMode.GameOver"/>/<see cref="GameMode.ConfirmExit"/> message box.</summary>
    private readonly EndScreenRenderer _endScreenRenderer = new();

    /// <summary>Time since the end screen opened; drives the ripple animation.</summary>
    private double _endScreenElapsedSeconds;

    // Per-screen woosh sound (played as the ripple opens; see Global/SoundLibrary.ini).
    private const string WorldCompleteWooshSoundName = "WorldCompleteWoosh";
    private const string GameOverWooshSoundName = "GameOverWoosh";
    private const string ConfirmExitWooshSoundName = "ConfirmExitWoosh";

    /// <summary>Interface colors (Global/InterfaceColors.ini), loaded in <see cref="StartAsync"/>.</summary>
    private InterfaceColors _colors = InterfaceColors.Fallback;

    // ---------------------------------------------------------------------------------------
    // Gear ring
    // ---------------------------------------------------------------------------------------

    private readonly GearRingScreen _gearRing = new();
    private readonly GearRingRenderer _gearRingRenderer = new();

    // Woosh sounds as the gear ring opens and closes (see Global/SoundLibrary.ini).
    private const string GearRingOpenSoundName = "GearRingOpenWoosh";
    private const string GearRingCloseSoundName = "GearRingCloseWoosh";

    // ---------------------------------------------------------------------------------------
    // HUD (top-left): frame, text row and health bar, with the gear box to its right
    // ---------------------------------------------------------------------------------------

    private readonly UIFrame _hudBox = new(col: 1, row: 1, width: 36, height: 4);

    /// <summary>Lives/Rings/Score on the first row and the "Health:" caption on the second.</summary>
    private readonly UILabel _hudText = new(col: 3, row: 2, width: 32, height: 2);

    /// <summary>Health gauge (0-1) right of the "Health:" caption.</summary>
    private readonly UIBar _hudBar = new(col: 11, row: 3, width: 20, height: 1, minValue: 0, maxValue: GameDefaults.PlayerFullHealth) { CurrentValue = GameDefaults.PlayerFullHealth };

    /// <summary>Gear box right of the HUD box: the selected gear's icon and count. The gear ring opens out of it.</summary>
    private readonly UIFrame _hudGearBox = new(col: 38, row: 1, width: 7, height: 4);

    /// <summary>The selected gear's icon (first line, dimmed while empty) and dimmed count as "x3" (second line), centered inside <see cref="_hudGearBox"/>.</summary>
    private readonly UILabel _hudGearLabel = new(col: 39, row: 2, width: 5, height: 2);

    // ---------------------------------------------------------------------------------------
    // Debug overlays (top-right) and debug keys
    // ---------------------------------------------------------------------------------------

    private const double FpsSmoothingFactor = 0.1;

    /// <summary>FPS overlay, toggled by <see cref="InputState.IsFpsToggleKeyDown"/>.</summary>
    private readonly UILabel _fpsLabel = new(col: 0, row: 0, width: 12, height: 1);

    /// <summary>World object-count overlay, toggled by <see cref="InputState.IsObjectCounterToggleKeyDown"/>; sits under the FPS overlay.</summary>
    private readonly UILabel _objectCounterLabel = new(col: 0, row: 1, width: 12, height: 1);

    private bool _showFpsOverlay;
    private bool _showObjectCounterOverlay;

    /// <summary>Smoothed frames per second, based on the raw (unclamped) frame time.</summary>
    private double _smoothedFps;

    // ---------------------------------------------------------------------------------------
    // Edge-detection state for keys that trigger once per press
    // ---------------------------------------------------------------------------------------

    private bool _wasFpsToggleKeyDown;
    private bool _wasObjectCounterToggleKeyDown;
    private bool _wasActionKeyDown;
    private bool _wasEscapeKeyDown;
    private bool _wasGearRingKeyDown;

    // ---------------------------------------------------------------------------------------
    // Startup
    // ---------------------------------------------------------------------------------------

    public async Task StartAsync(string canvasElementId)
    {
        var worlds = await WorldCatalog.LoadAllAsync(assetFileProvider);
        ApplyInterfaceColors(await InterfaceColors.LoadAsync(assetFileProvider));

        var globalSettingsContent = await assetFileProvider.TryReadTextAsync($"{AssetPathResolver.GlobalRoot}/Settings.ini");
        var globalSettings = IniDocument.Parse(globalSettingsContent ?? string.Empty);
        var fontFamily = globalSettings.TryGetValue("Render", "FontFamily") ?? RenderConstants.DefaultFontFamily;
        var viewportColumns = IniValueParser.TryParseInt(globalSettings.TryGetValue("Render", "ViewportColumns"), out var parsedColumns)
            ? parsedColumns
            : RenderConstants.DefaultViewportColumns;
        var viewportRows = IniValueParser.TryParseInt(globalSettings.TryGetValue("Render", "ViewportRows"), out var parsedRows)
            ? parsedRows
            : RenderConstants.DefaultViewportRows;

        // The font's native pixel size has no default and must be supplied explicitly. These are
        // the final on-screen cell size (already scaled), so a non-uniform value stretches the font.
        if (!IniValueParser.TryParseInt(globalSettings.TryGetValue("Render", "FontWidthPixels"), out var fontWidthPixels))
        {
            throw new InvalidOperationException("Global/Settings.ini is missing a required [Render] FontWidthPixels value.");
        }
        if (!IniValueParser.TryParseInt(globalSettings.TryGetValue("Render", "FontHeightPixels"), out var fontHeightPixels))
        {
            throw new InvalidOperationException("Global/Settings.ini is missing a required [Render] FontHeightPixels value.");
        }

        if (_isStopped)
        {
            return;
        }

        var cellMetrics = await canvasBridge.InitializeAsync(canvasElementId, this, fontFamily, viewportColumns, viewportRows, fontWidthPixels, fontHeightPixels);
        if (_isStopped)
        {
            return;
        }

        ApplyCellMetrics(cellMetrics, viewportColumns, viewportRows);

        var soundEnabled = !bool.TryParse(globalSettings.TryGetValue("Sound", "Enabled"), out var parsedSoundEnabled) || parsedSoundEnabled;
        var masterVolume = IniValueParser.TryParseDouble(globalSettings.TryGetValue("Sound", "MasterVolume"), out var parsedMasterVolume)
            ? Math.Clamp(parsedMasterVolume, 0.0, 1.0)
            : 1.0;
        await soundPlayer.InitializeAsync(soundEnabled ? masterVolume : 0.0);

        // Register the global sounds for the menu; a world's own library replaces them once loaded.
        var globalSounds = await SoundLibrary.LoadAsync(assetFileProvider, null);
        if (_isStopped)
        {
            return;
        }

        await soundPlayer.SetSoundsAsync(globalSounds);
        if (_isStopped)
        {
            return;
        }

        var visibleSlotCount = WorldSelectRenderer.ComputeVisibleSlotCount(_viewportWidthCells, worlds.Count);
        _worldSelect = new WorldSelectScreen(worlds, visibleSlotCount);
        _mode = GameMode.WorldSelecting;
    }

    /// <summary>Stores the interface colors and applies them to the fixed HUD/overlay widgets.</summary>
    private void ApplyInterfaceColors(InterfaceColors colors)
    {
        _colors = colors;
        GlyphBuilder.DefaultForeColor = colors.Interface.Normal;

        _hudBox.ForeColor = colors.Interface.Normal;
        _hudText.ForeColor = colors.Interface.Normal;
        _hudText.HighlightColor = colors.Interface.Dark;
        _hudBar.ForeColor = colors.Interface.Normal;
        _hudGearBox.ForeColor = colors.Interface.Normal;
        _hudGearLabel.ForeColor = colors.Interface.Normal;
        _hudGearLabel.HighlightColor = colors.Interface.Dark;
        _fpsLabel.ForeColor = colors.Interface.Normal;
        _objectCounterLabel.ForeColor = colors.Interface.Normal;
    }

    private void ApplyCellMetrics(CellMetrics cellMetrics, int viewportColumns, int viewportRows)
    {
        // Fall back to the previous/default cell size if the browser reports an invalid one.
        var width = cellMetrics.CellWidthPixels;
        var height = cellMetrics.CellHeightPixels;

        if (!double.IsFinite(width) || width <= 0)
        {
            width = _renderer.CellWidthPixels > 0 ? _renderer.CellWidthPixels : RenderConstants.DefaultCellWidthPixels;
        }
        if (!double.IsFinite(height) || height <= 0)
        {
            height = _renderer.CellHeightPixels > 0 ? _renderer.CellHeightPixels : RenderConstants.DefaultCellHeightPixels;
        }

        _renderer.CellWidthPixels = width;
        _renderer.CellHeightPixels = height;

        // The viewport is always exactly viewportColumns x viewportRows cells.
        _viewportWidthCells = viewportColumns;
        _viewportHeightCells = viewportRows;
        _viewportWidthPixels = (int)Math.Round(viewportColumns * _renderer.CellWidthPixels);
        _viewportHeightPixels = (int)Math.Round(viewportRows * _renderer.CellHeightPixels);
    }

    /// <summary>
    /// Marks the loop as stopped: frame processing, startup and world loading check this after each
    /// await and return without touching the browser adapters again. Called by the page before it
    /// disposes those adapters. A JS call already in flight at that moment may still complete or
    /// fault; nothing new is started.
    /// </summary>
    public void Stop() => _isStopped = true;

    // ---------------------------------------------------------------------------------------
    // JS entry points
    // ---------------------------------------------------------------------------------------

    [JSInvokable]
    public void OnKeyDown(string code) => _input.KeyDown(code);

    [JSInvokable]
    public void OnKeyUp(string code) => _input.KeyUp(code);

    [JSInvokable]
    public async Task OnFrame(double deltaSeconds)
    {
        if (_isProcessingFrame || _isStopped)
        {
            return;
        }

        // The JS frame loop starts inside canvasBridge.InitializeAsync, before StartAsync has
        // finished (sound setup, world-select screen); skip frames until startup completes.
        if (_worldSelect is null)
        {
            return;
        }

        _isProcessingFrame = true;
        try
        {
            UpdateOverlayToggles();
            UpdateSmoothedFps(deltaSeconds);

            // Clamp delta to avoid huge jumps after tab switches, etc.
            deltaSeconds = Math.Clamp(deltaSeconds, 0, 0.1);

            switch (_mode)
            {
                case GameMode.WorldSelecting:
                    await OnWorldSelectingFrameAsync(deltaSeconds);
                    break;
                case GameMode.LoadingWorld:
                    await OnLoadingWorldFrameAsync();
                    break;
                case GameMode.Playing:
                    await OnPlayingFrameAsync(deltaSeconds);
                    break;
                case GameMode.WorldComplete:
                case GameMode.GameOver:
                    await OnEndScreenFrameAsync(deltaSeconds);
                    break;
                case GameMode.ConfirmExit:
                    await OnConfirmExitFrameAsync(deltaSeconds);
                    break;
                case GameMode.GearRing:
                    await OnGearRingFrameAsync(deltaSeconds);
                    break;
            }
        }
        finally
        {
            _isProcessingFrame = false;
        }
    }

    private void UpdateOverlayToggles()
    {
        var fpsToggleKeyDown = _input.IsFpsToggleKeyDown;
        if (fpsToggleKeyDown && !_wasFpsToggleKeyDown)
        {
            _showFpsOverlay = !_showFpsOverlay;
        }
        _wasFpsToggleKeyDown = fpsToggleKeyDown;

        var objectCounterToggleKeyDown = _input.IsObjectCounterToggleKeyDown;
        if (objectCounterToggleKeyDown && !_wasObjectCounterToggleKeyDown)
        {
            _showObjectCounterOverlay = !_showObjectCounterOverlay;
        }
        _wasObjectCounterToggleKeyDown = objectCounterToggleKeyDown;
    }

    private void UpdateSmoothedFps(double rawDeltaSeconds)
    {
        if (rawDeltaSeconds <= 0)
        {
            return;
        }

        var instantaneousFps = 1.0 / rawDeltaSeconds;
        _smoothedFps = _smoothedFps <= 0
            ? instantaneousFps
            : _smoothedFps + (instantaneousFps - _smoothedFps) * FpsSmoothingFactor;
    }

    // ---------------------------------------------------------------------------------------
    // World selection and loading frames
    // ---------------------------------------------------------------------------------------

    /// <summary>Handles the world-selection screen: moves the highlight and starts loading the chosen world on confirm.</summary>
    private async Task OnWorldSelectingFrameAsync(double deltaSeconds)
    {
        var previousSelectedIndex = _worldSelect.SelectedIndex;
        _worldSelect.Update(_input, deltaSeconds);

        if (_worldSelect.SelectedIndex != previousSelectedIndex)
        {
            soundPlayer.Play(MenuMoveSoundName);
        }
        if (_worldSelect.Confirmed)
        {
            soundPlayer.Play(MenuSelectSoundName);

            // Start the load without awaiting it, so frames keep being drawn while it runs.
            _mode = GameMode.LoadingWorld;
            _loadingBar = WorldLoadingRenderer.CreateLoadingBar(_colors, _viewportWidthCells, _viewportHeightCells, World2D.LoadStepCount);
            _loadWorldTask = LoadWorldAsync(_worldSelect.SelectedWorld.WorldName);

            await OnLoadingWorldFrameAsync();
            return;
        }

        var glyphs = WorldSelectRenderer.BuildFrame(
            _worldSelect, _colors, _viewportWidthCells, _viewportHeightCells,
            _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        await DrawAsync(glyphs);
    }

    private async Task LoadWorldAsync(string worldName)
    {
        var progress = new Progress<double>(stepsCompleted =>
        {
            _loadingBar?.CurrentValue = stepsCompleted;
        });
        var world = await World2D.LoadAsync(assetFileProvider, worldName, progress);
        if (_isStopped)
        {
            return;
        }

        _world = world;
        _sessionWorlds.Clear();
        _sessionWorlds[0] = world;
        await soundPlayer.SetSoundsAsync(_world.Sounds);
        if (_isStopped)
        {
            return;
        }

        ResetForNewActiveWorld();
    }

    /// <summary>
    /// Clears everything the systems carry between frames, which belongs to the previous active world,
    /// and snaps the camera to the new one. Time spent loading is not gameplay time.
    /// </summary>
    private void ResetForNewActiveWorld()
    {
        _physicsAccumulatorSeconds = 0;
        _pendingTransitionElapsedSeconds = 0;

        // State the systems carry between frames belongs to the previous world. Keys still held
        // from the menu seed the edge history, so they do not count as fresh presses in the first frame.
        _physics.Reset(_input);
        _collision.Reset();
        _collision.ArmPortals(_world);
        _animation.Reset();
        _wasActionKeyDown = _input.IsActionKeyDown;

        _camera.SnapTo(
            _world.CameraTarget.Position,
            _world.CameraTarget.Size,
            _world.WidthCells,
            _world.HeightCells,
            _viewportWidthCells,
            _viewportHeightCells);
    }

    /// <summary>
    /// Carries out <see cref="PendingTransition.EnterWorld"/>: moves the player, with the progress they carry,
    /// to the entered portal's target world (loading it on first use). A target that fails to load is logged and
    /// play simply continues in the current world.
    /// </summary>
    private async Task EnterPortalTargetAsync()
    {
        var source = _world;
        var portal = source.PendingPortal!;
        source.PendingTransition = PendingTransition.None;
        source.PendingPortal = null;

        try
        {
            if (!_sessionWorlds.TryGetValue(portal.TargetWorld, out var target))
            {
                target = await World2D.LoadAsync(assetFileProvider, source.WorldName, subWorld: portal.TargetWorld);
                if (_isStopped)
                {
                    return;
                }

                _sessionWorlds[portal.TargetWorld] = target;
            }

            target.Player.CarryOverFrom(source.Player);
            target.EnterAt(portal.TargetEntry);
            target.Player.Wake();
            InteractionHandler.OnPortalArrived(target, target.Player);
            _world = target;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to enter world {portal.TargetWorld} of '{source.WorldName}': {ex}");
            return;
        }

        ResetForNewActiveWorld();
    }

    /// <summary>Draws the selection screen with the loading bar, and switches to <see cref="GameMode.Playing"/> once the load completes.</summary>
    private async Task OnLoadingWorldFrameAsync()
    {
        if (_loadingBar is null || _loadWorldTask is null)
        {
            return;
        }

        var glyphs = WorldLoadingRenderer.BuildLoadingFrame(
            _worldSelect, _colors, _loadingBar, _viewportWidthCells, _viewportHeightCells,
            _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        await DrawAsync(glyphs);

        if (_loadWorldTask.IsCompleted)
        {
            try
            {
                await _loadWorldTask;
                _mode = GameMode.Playing;
            }
            catch (Exception ex)
            {
                // No error screen exists; log to the browser console and return to world selection.
                Console.Error.WriteLine($"Failed to load world '{_worldSelect.SelectedWorld.WorldName}': {ex}");
                _mode = GameMode.WorldSelecting;
                _worldSelect.ResetConfirmation();
            }
            finally
            {
                _loadingBar = null;
                _loadWorldTask = null;
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Playing frame
    // ---------------------------------------------------------------------------------------

    private async Task OnPlayingFrameAsync(double deltaSeconds)
    {
        // Escape asks for confirmation before abandoning the world.
        var escapeDown = _input.IsEscapeKeyDown;
        if (escapeDown && !_wasEscapeKeyDown)
        {
            _wasEscapeKeyDown = true;
            _mode = GameMode.ConfirmExit;
            _endScreenKeysReleased = false;
            _endScreenElapsedSeconds = 0;
            soundPlayer.Play(ConfirmExitWooshSoundName);
            return;
        }
        _wasEscapeKeyDown = escapeDown;

        // Holding the gear ring key pauses play and opens the gear ring; releasing it picks the highlighted gear.
        var gearRingKeyDown = _input.IsGearRingKeyDown;
        if (gearRingKeyDown && !_wasGearRingKeyDown
            && _world.PendingTransition == PendingTransition.None
            && _world.Player.Gear.Count > 0)
        {
            _wasGearRingKeyDown = true;
            _gearRing.Open(_world.Player.Gear, _world.Player.SelectedGear, _input);
            _mode = GameMode.GearRing;
            soundPlayer.Play(GearRingOpenSoundName);
            return;
        }
        _wasGearRingKeyDown = gearRingKeyDown;

        HandleActionKeys();
        if (_world.PendingTransition == PendingTransition.None)
        {
            RunFixedPhysicsSteps(deltaSeconds);
        }
        PlayQueuedSounds();

        if (_world.PendingTransition == PendingTransition.EnterWorld)
        {
            // Physics stays frozen while the player's portal effect and the sound play; with no effect there is no wait.
            _pendingTransitionElapsedSeconds += deltaSeconds;
            if (_world.Player.PortalEnteredEffectClipName is null || _pendingTransitionElapsedSeconds >= PortalTransitionSeconds)
            {
                _pendingTransitionElapsedSeconds = 0;
                await EnterPortalTargetAsync();
                return;
            }
        }
        else if (_world.PendingTransition != PendingTransition.None && AdvancePendingTransition(deltaSeconds))
        {
            return;
        }

        _animation.Update(_world, deltaSeconds);
        PlayQueuedSounds();

        _camera.Follow(
            _world.CameraTarget.Position,
            _world.CameraTarget.Size,
            _world.WidthCells,
            _world.HeightCells,
            _viewportWidthCells,
            _viewportHeightCells,
            deltaSeconds);

        var glyphs = _renderer.BuildFrame(_world, _camera, _viewportWidthCells, _viewportHeightCells);
        AddHud(glyphs);
        AddDebugOverlays(glyphs);
        await DrawWorldAsync(glyphs);
    }

    /// <summary>
    /// Counts down the pending transition set by <see cref="CollisionSystem"/> (physics is frozen
    /// meanwhile, effects keep animating). Once <see cref="PendingTransitionSeconds"/> have passed,
    /// carries it out: a world end opens the world-complete screen, a death respawns the player or,
    /// with no lives left, opens the game-over screen. Returns whether the mode changed, so the
    /// caller skips the rest of this playing frame.
    /// </summary>
    private bool AdvancePendingTransition(double deltaSeconds)
    {
        _pendingTransitionElapsedSeconds += deltaSeconds;
        if (_world.PendingTransition == PendingTransition.Arrive)
        {
            if (_pendingTransitionElapsedSeconds >= PortalTransitionSeconds)
            {
                _world.PendingTransition = PendingTransition.None;
                _pendingTransitionElapsedSeconds = 0;
            }

            return false;
        }

        if (_pendingTransitionElapsedSeconds < PendingTransitionSeconds)
        {
            return false;
        }

        var transition = _world.PendingTransition;
        _world.PendingTransition = PendingTransition.None;
        _pendingTransitionElapsedSeconds = 0;

        if (transition == PendingTransition.Death && _world.Player.Lives > 0)
        {
            _world.Respawn();
            return false;
        }

        _mode = transition == PendingTransition.WorldEnd ? GameMode.WorldComplete : GameMode.GameOver;
        _endScreenKeysReleased = false;
        _endScreenElapsedSeconds = 0;
        soundPlayer.Play(_mode == GameMode.WorldComplete ? WorldCompleteWooshSoundName : GameOverWooshSoundName);
        return true;
    }

    /// <summary>
    /// Handles the once-per-press action key: operates a switch next to the player, otherwise opens
    /// a key gate next to it, otherwise throws the selected gear.
    /// </summary>
    private void HandleActionKeys()
    {
        var actionKeyDown = _input.IsActionKeyDown;
        if (actionKeyDown && !_wasActionKeyDown && (_world.TryOperateSwitch() || _world.TryOpenGate() || _world.TryThrow()))
        {
            _world.ApplyPendingRemovals();
        }
        _wasActionKeyDown = actionKeyDown;
    }

    /// <summary>
    /// Runs Physics and Collision in identically sized fixed steps, carrying any remainder to the
    /// next frame. Pending removals are applied after every step.
    /// </summary>
    private void RunFixedPhysicsSteps(double deltaSeconds)
    {
        _physicsAccumulatorSeconds += deltaSeconds;
        while (_physicsAccumulatorSeconds >= PhysicsConstants.FixedPhysicsStepSeconds)
        {
            // Fast bodies: split the step so no body moves further than MaxStepTravelCells per
            // substep, so discrete collision cannot tunnel through thin walls. Ordinary motion
            // stays a single step.
            var substeps = CountSubsteps();
            var substepSeconds = PhysicsConstants.FixedPhysicsStepSeconds / substeps;
            for (var i = 0; i < substeps; i++)
            {
                _physics.Step(_world, _input, substepSeconds);
                _collision.Resolve(_world, substepSeconds);
                _world.ApplyPendingRemovals();
            }
            _physicsAccumulatorSeconds -= PhysicsConstants.FixedPhysicsStepSeconds;
        }
    }

    /// <summary>
    /// Substeps needed this fixed step: the fastest moving body's travel in one step (estimated
    /// from its current velocity plus one step of gravity) divided by
    /// <see cref="PhysicsConstants.MaxStepTravelCells"/>, capped at
    /// <see cref="PhysicsConstants.MaxSubsteps"/>.
    /// </summary>
    private int CountSubsteps()
    {
        var gravityGain = Math.Abs(_world.Gravity) * PhysicsConstants.FixedPhysicsStepSeconds;
        var maxSpeed = 0.0;
        foreach (var body in _world.Objects)
        {
            if (body is IPhysicsBody movingBody && !body.IsStatic)
            {
                var speed = Math.Max(Math.Abs(movingBody.Velocity.X), Math.Abs(movingBody.Velocity.Y) + gravityGain);
                maxSpeed = Math.Max(maxSpeed, speed);
            }
        }

        var travel = maxSpeed * PhysicsConstants.FixedPhysicsStepSeconds;
        return Math.Clamp((int)Math.Ceiling(travel / PhysicsConstants.MaxStepTravelCells), 1, PhysicsConstants.MaxSubsteps);
    }

    /// <summary>Forwards every sound queued on the world this frame to the sound player.</summary>
    private void PlayQueuedSounds()
    {
        foreach (var soundName in _world.DrainPendingSounds())
        {
            soundPlayer.Play(soundName);
        }
    }

    /// <summary>Adds the HUD: lives, rings, score and health bar, plus the gear box with the selected gear.</summary>
    private void AddHud(List<Glyph> glyphs)
    {
        var cellWidth = _renderer.CellWidthPixels;
        var cellHeight = _renderer.CellHeightPixels;

        var panelColor = InterfaceBackdropColor;
        _hudBox.BackColor = panelColor;
        _hudText.BackColor = panelColor;
        _hudBar.BackColor = panelColor;
        AddPanel(glyphs, _hudBox, panelColor);
        UIRenderer.AddFrame(glyphs, _hudBox, cellWidth, cellHeight);

        _hudText.Lines.Clear();
        _hudText.Lines.Add($"Lives:{{{_world.Player.Lives,3}}}  Rings:{{{_world.Player.Rings,3}}}  Score:{{{_world.Player.Score,4}}}");
        _hudText.Lines.Add("Health:");
        UIRenderer.AddLabel(glyphs, _hudText, cellWidth, cellHeight);

        _hudBar.CurrentValue = _world.Player.Health;
        UIRenderer.AddBar(glyphs, _hudBar, cellWidth, cellHeight);

        if (_world.Player.SelectedGear is { } selectedGear)
        {
            _hudGearBox.BackColor = panelColor;
            _hudGearLabel.BackColor = panelColor;
            AddPanel(glyphs, _hudGearBox, panelColor);
            UIRenderer.AddFrame(glyphs, _hudGearBox, cellWidth, cellHeight);

            var icon = CenterIn(selectedGear.Icon.ToString(), _hudGearLabel.Width);
            _hudGearLabel.Lines.Clear();
            _hudGearLabel.Lines.Add(selectedGear.Count > 0 ? icon : "{" + icon + "}");
            _hudGearLabel.Lines.Add("{" + CenterIn("x" + selectedGear.Count, _hudGearLabel.Width) + "}");
            UIRenderer.AddLabel(glyphs, _hudGearLabel, cellWidth, cellHeight);
        }
    }

    /// <summary>Fills <paramref name="frame"/>'s whole area with <paramref name="panelColor"/>, so it reads as a solid panel over the world; nothing when null.</summary>
    private void AddPanel(List<Glyph> glyphs, UIFrame frame, string? panelColor)
    {
        if (panelColor is null)
        {
            return;
        }

        for (var row = 0; row < frame.Height; row++)
        {
            for (var col = 0; col < frame.Width; col++)
            {
                glyphs.Add(GlyphBuilder.BuildGlyph((frame.Col + col) * _renderer.CellWidthPixels, (frame.Row + row) * _renderer.CellHeightPixels, ' ', null, panelColor));
            }
        }
    }

    /// <summary>Pads <paramref name="text"/> on both sides to <paramref name="width"/>, centered (an odd spare cell goes right).</summary>
    private static string CenterIn(string text, int width) => text.PadLeft((width + text.Length) / 2).PadRight(width);

    /// <summary>Adds the FPS and object-count overlays, right-aligned to the top-right corner.</summary>
    private void AddDebugOverlays(List<Glyph> glyphs)
    {
        if (_showFpsOverlay)
        {
            var fpsText = $"{_smoothedFps:F0} FPS";
            _fpsLabel.Col = _viewportWidthCells - fpsText.Length;
            _fpsLabel.Lines.Clear();
            _fpsLabel.Lines.Add(fpsText);
            UIRenderer.AddLabel(glyphs, _fpsLabel, _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        }

        if (_showObjectCounterOverlay)
        {
            var objectCountText = $"{_world.Objects.Count} Obj";
            _objectCounterLabel.Col = _viewportWidthCells - objectCountText.Length;
            _objectCounterLabel.Lines.Clear();
            _objectCounterLabel.Lines.Add(objectCountText);
            UIRenderer.AddLabel(glyphs, _objectCounterLabel, _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        }
    }

    // ---------------------------------------------------------------------------------------
    // End screen frame (world complete / game over)
    // ---------------------------------------------------------------------------------------

    /// <summary>Shows the frozen last frame with a ripple-animated message box until a confirm key press, then returns to world selection.</summary>
    private async Task OnEndScreenFrameAsync(double deltaSeconds)
    {
        _endScreenElapsedSeconds += deltaSeconds;
        var confirmPressed = _input.IsConfirmKeyDown;
        if (!confirmPressed)
        {
            _endScreenKeysReleased = true;
        }
        else if (_endScreenKeysReleased)
        {
            _worldSelect.ResetConfirmation();
            _mode = GameMode.WorldSelecting;
            return;
        }

        var glyphs = _renderer.BuildFrame(_world, _camera, _viewportWidthCells, _viewportHeightCells);
        AddHud(glyphs);
        AddEndScreenMessage(glyphs);
        await DrawWorldAsync(glyphs);
    }

    /// <summary>
    /// Draws the frozen frame
    /// selection; a fresh Escape press cancels and resumes play.
    /// </summary>
    private async Task OnConfirmExitFrameAsync(double deltaSeconds)
    {
        _endScreenElapsedSeconds += deltaSeconds;

        var escapeDown = _input.IsEscapeKeyDown;
        if (escapeDown && !_wasEscapeKeyDown)
        {
            _wasEscapeKeyDown = true;
            _mode = GameMode.Playing;
            return;
        }
        _wasEscapeKeyDown = escapeDown;

        var confirmPressed = _input.IsConfirmKeyDown;
        if (!confirmPressed)
        {
            _endScreenKeysReleased = true;
        }
        else if (_endScreenKeysReleased)
        {
            _worldSelect.ResetConfirmation();
            _mode = GameMode.WorldSelecting;
            return;
        }

        var glyphs = _renderer.BuildFrame(_world, _camera, _viewportWidthCells, _viewportHeightCells);
        AddHud(glyphs);
        AddEndScreenMessage(glyphs);
        await DrawWorldAsync(glyphs);
    }

    /// <summary>
    /// Draws the frozen frame with the gear ring on top. Releasing the gear ring key selects the
    /// highlighted gear as the ring starts closing (so the gear box already shows it as the ring
    /// shrinks back into it); once closed, play resumes. Keys still held from the ring don't act
    /// again in play: a cancelling Escape doesn't ask to exit, and a held action key doesn't throw.
    /// </summary>
    private async Task OnGearRingFrameAsync(double deltaSeconds)
    {
        var previousIndex = _gearRing.SelectedIndex;
        var wasClosing = _gearRing.IsClosing;
        _gearRing.Update(_input, deltaSeconds);

        if (_gearRing.SelectedIndex != previousIndex)
        {
            soundPlayer.Play(MenuMoveSoundName);
        }

        if (_gearRing.IsClosing && !wasClosing)
        {
            if (_gearRing.Confirmed)
            {
                _world.Player.SelectedGear = _gearRing.SelectedSlot;
            }

            soundPlayer.Play(GearRingCloseSoundName);
        }

        if (_gearRing.IsClosed)
        {
            _wasActionKeyDown = _input.IsActionKeyDown;
            _wasEscapeKeyDown = _input.IsEscapeKeyDown;
            _wasGearRingKeyDown = _input.IsGearRingKeyDown;
            _mode = GameMode.Playing;
            return;
        }

        var glyphs = _renderer.BuildFrame(_world, _camera, _viewportWidthCells, _viewportHeightCells);
        AddHud(glyphs);
        _gearRingRenderer.AddRing(
            glyphs, _gearRing,
            originCol: _hudGearBox.Col + (_hudGearBox.Width / 2),
            originRow: _hudGearBox.Row + (_hudGearBox.Height / 2),
            _colors.Interface.Dark, _colors.Interface.Normal, _colors.Interface.Light, InterfaceBackdropColor,
            _viewportWidthCells, _viewportHeightCells, _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        await DrawWorldAsync(glyphs);
    }

    /// <summary>
    /// Picks the end screen's message and tint
    /// for the current mode and draws it via <see cref="EndScreenRenderer"/> on top of the world.
    /// </summary>
    private void AddEndScreenMessage(List<Glyph> glyphs)
    {
        var isWorldComplete = _mode == GameMode.WorldComplete;
        var isConfirmExit = _mode == GameMode.ConfirmExit;
        var text = isConfirmExit ? "Exit Game?" : isWorldComplete ? "World Complete!" : "Game Over!";
        var tints = isConfirmExit ? _colors.Amber : isWorldComplete ? _colors.Green : _colors.Red;

        _endScreenRenderer.AddMessage(
            glyphs, text, tints.Dark, tints.Normal, tints.Light, InterfaceBackdropColor, _endScreenElapsedSeconds,
            _viewportWidthCells, _viewportHeightCells, _renderer.CellWidthPixels, _renderer.CellHeightPixels);
    }

    // ---------------------------------------------------------------------------------------
    // Drawing
    // ---------------------------------------------------------------------------------------

    private Task DrawAsync(List<Glyph> glyphs) =>
        canvasBridge.DrawFrameAsync(_viewportWidthPixels, _viewportHeightPixels, _renderer.CellWidthPixels, _renderer.CellHeightPixels, glyphs, backdropColor: _colors.Background);

    /// <summary>Draws a frame of the loaded world, filling the canvas with its <see cref="World2D.WorldBackdrop"/> first.</summary>
    private Task DrawWorldAsync(List<Glyph> glyphs) =>
        canvasBridge.DrawFrameAsync(_viewportWidthPixels, _viewportHeightPixels, _renderer.CellWidthPixels, _renderer.CellHeightPixels, glyphs,
            backdropColor: CanvasBackdrop);

    /// <summary>The loaded world's <see cref="World2D.InterfaceBackdrop"/> as a color, or null (no panel).</summary>
    private string? InterfaceBackdropColor => GlyphBuilder.ResolveColor(_world.Palette, null, _world.InterfaceBackdrop);

    /// <summary>The loaded world's canvas color:
    private string CanvasBackdrop => GlyphBuilder.ResolveColor(_world.Palette, _colors.Background, _world.WorldBackdrop)!;
}
