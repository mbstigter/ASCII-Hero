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
        LevelComplete,

        /// <summary>Same as <see cref="LevelComplete"/>, but shown when the player lost their last life.</summary>
        GameOver,

        /// <summary>Gameplay is frozen and an "Exit game?" message is shown; confirming returns to <see cref="WorldSelecting"/>, Escape resumes <see cref="Playing"/>.</summary>
        ConfirmExit,
    }

    private GameMode _mode = GameMode.WorldSelecting;

    /// <summary>Guards <see cref="OnFrame"/> against overlapping calls; an overlapping frame is simply dropped.</summary>
    private bool _isProcessingFrame;

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
    // Pending transitions and end screens (level complete / game over)
    // ---------------------------------------------------------------------------------------

    /// <summary>How long the player's level-end or death effect and sound play before the transition is carried out (see <see cref="World2D.PendingTransition"/>).</summary>
    private const double PendingTransitionSeconds = 2.0;

    private double _pendingTransitionElapsedSeconds;

    /// <summary>Set once no confirm key is held on an end screen, so a key still held from gameplay doesn't dismiss it instantly.</summary>
    private bool _endScreenKeysReleased;

    /// <summary>Message of the active <see cref="GameMode.LevelComplete"/>/<see cref="GameMode.GameOver"/> screen; one text row (with a space either side) inside the final ripple box, on a solid background so it stays readable over the world.</summary>
    private readonly UILabel _endScreenLabel = new(col: 0, row: 0, width: 20, height: 1, backColor: EndScreenTextBackColor);

    /// <summary>Border around the end screen message; redrawn at several sizes while the ripple animation plays.</summary>
    private readonly UIFrame _endScreenFrame = new(col: 0, row: 0, width: 0, height: 0);

    /// <summary>Time since the end screen opened; drives the ripple animation.</summary>
    private double _endScreenElapsedSeconds;

    /// <summary>Number of shrinking steps from the largest ripple box down to the box that holds the message.</summary>
    private const int EndScreenRippleSteps = 12;

    /// <summary>How long each ripple step lasts.</summary>
    private const double EndScreenStepSeconds = 0.06;

    /// <summary>How many boxes are visible at once while the ripple converges.</summary>
    private const int EndScreenRippleTrail = 4;

    /// <summary>Per step, the box shrinks by this many cells on each axis (1 per side).</summary>
    private const int EndScreenStepCells = 2;

    private const string EndScreenTextBackColor = "#000000";

    // Per-screen message box color and woosh sound (played as the ripple opens; see Global/SoundLibrary.ini).
    private const string LevelCompleteColor = RenderConstants.DefaultForeColor;
    private const string LevelCompleteWooshSoundName = "LevelCompleteWoosh";

    private const string GameOverColor = "#ff0000";
    private const string GameOverWooshSoundName = "GameOverWoosh";

    private const string ConfirmExitColor = "#ffb000";
    private const string ConfirmExitWooshSoundName = "ConfirmExitWoosh";

    // ---------------------------------------------------------------------------------------
    // HUD (top-left): frame, text row, health bar and key icon
    // ---------------------------------------------------------------------------------------

    private readonly UIFrame _hudBox = new(col: 1, row: 1, width: 36, height: 4, foreColor: RenderConstants.DefaultForeColor);

    /// <summary>Lives/Rings/Score on the first row and the "Health:" caption on the second.</summary>
    private readonly UILabel _hudText = new(col: 3, row: 2, width: 32, height: 2, foreColor: RenderConstants.DefaultForeColor);

    /// <summary>Health gauge (0-1) right of the "Health:" caption.</summary>
    private readonly UIBar _hudBar = new(col: 11, row: 3, width: 20, height: 1, minValue: 0, maxValue: GameDefaults.PlayerFullHealth, foreColor: RenderConstants.DefaultForeColor) { CurrentValue = GameDefaults.PlayerFullHealth };

    /// <summary>Key icon right of the health bar; shown while the player holds at least one key.</summary>
    private readonly UILabel _hudKeyLabel = new(col: 33, row: 3, width: 3, height: 1, foreColor: RenderConstants.DefaultForeColor);

    // ---------------------------------------------------------------------------------------
    // Debug overlays (top-right) and debug keys
    // ---------------------------------------------------------------------------------------

    private const double FpsSmoothingFactor = 0.1;

    /// <summary>FPS overlay, toggled by <see cref="InputState.IsFpsToggleKeyPressed"/>.</summary>
    private readonly UILabel _fpsLabel = new(col: 0, row: 0, width: 12, height: 1, foreColor: RenderConstants.DefaultForeColor);

    /// <summary>World object-count overlay, toggled by <see cref="InputState.IsObjectCounterToggleKeyPressed"/>; sits under the FPS overlay.</summary>
    private readonly UILabel _objectCounterLabel = new(col: 0, row: 1, width: 12, height: 1, foreColor: RenderConstants.DefaultForeColor);

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

    // ---------------------------------------------------------------------------------------
    // Startup
    // ---------------------------------------------------------------------------------------

    public async Task StartAsync(string canvasElementId)
    {
        var worlds = await WorldCatalog.LoadAllAsync(assetFileProvider);

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

        var cellMetrics = await canvasBridge.InitializeAsync(canvasElementId, this, fontFamily, viewportColumns, viewportRows, fontWidthPixels, fontHeightPixels);
        ApplyCellMetrics(cellMetrics, viewportColumns, viewportRows);

        var soundEnabled = !bool.TryParse(globalSettings.TryGetValue("Sound", "Enabled"), out var parsedSoundEnabled) || parsedSoundEnabled;
        var masterVolume = IniValueParser.TryParseDouble(globalSettings.TryGetValue("Sound", "MasterVolume"), out var parsedMasterVolume)
            ? Math.Clamp(parsedMasterVolume, 0.0, 1.0)
            : 1.0;
        await soundPlayer.InitializeAsync(soundEnabled ? masterVolume : 0.0);

        // Register the global sounds for the menu; a world's own library replaces them once loaded.
        await soundPlayer.SetSoundsAsync(await SoundLibrary.LoadAsync(assetFileProvider, null));

        var visibleSlotCount = WorldSelectRenderer.ComputeVisibleSlotCount(_viewportWidthCells, worlds.Count);
        _worldSelect = new WorldSelectScreen(worlds, visibleSlotCount);
        _mode = GameMode.WorldSelecting;
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
        if (_isProcessingFrame)
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
                case GameMode.LevelComplete:
                case GameMode.GameOver:
                    await OnEndScreenFrameAsync(deltaSeconds);
                    break;
                case GameMode.ConfirmExit:
                    await OnConfirmExitFrameAsync(deltaSeconds);
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
        var fpsToggleKeyDown = _input.IsFpsToggleKeyPressed;
        if (fpsToggleKeyDown && !_wasFpsToggleKeyDown)
        {
            _showFpsOverlay = !_showFpsOverlay;
        }
        _wasFpsToggleKeyDown = fpsToggleKeyDown;

        var objectCounterToggleKeyDown = _input.IsObjectCounterToggleKeyPressed;
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
            _loadingBar = WorldLoadingRenderer.CreateLoadingBar(_worldSelect, _viewportWidthCells, _viewportHeightCells, World2D.LoadStepCount);
            _loadWorldTask = LoadWorldAsync(_worldSelect.SelectedWorld.WorldName);

            await OnLoadingWorldFrameAsync();
            return;
        }

        var glyphs = WorldSelectRenderer.BuildFrame(
            _worldSelect, _viewportWidthCells, _viewportHeightCells,
            _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        await DrawAsync(glyphs);
    }

    private async Task LoadWorldAsync(string worldName)
    {
        var progress = new Progress<double>(stepsCompleted =>
        {
            if (_loadingBar is not null)
            {
                _loadingBar.CurrentValue = stepsCompleted;
            }
        });
        _world = await World2D.LoadAsync(assetFileProvider, worldName, progress);
        await soundPlayer.SetSoundsAsync(_world.Sounds);

        // Time spent loading is not gameplay time.
        _physicsAccumulatorSeconds = 0;

        // Keys still held from the menu must not count as fresh presses in the first frame.
        _wasActionKeyDown = _input.IsActionPressed;

        _camera.SnapTo(
            _world.CameraTarget.Position,
            _world.CameraTarget.Size,
            _world.WidthCells,
            _world.HeightCells,
            _viewportWidthCells,
            _viewportHeightCells);
    }

    /// <summary>Draws the selection screen with the loading bar, and switches to <see cref="GameMode.Playing"/> once the load completes.</summary>
    private async Task OnLoadingWorldFrameAsync()
    {
        if (_loadingBar is null || _loadWorldTask is null)
        {
            return;
        }

        var glyphs = WorldLoadingRenderer.BuildLoadingFrame(
            _worldSelect, _loadingBar, _viewportWidthCells, _viewportHeightCells,
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
        var escapeDown = _input.IsEscapePressed;
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

        HandleActionKeys();
        if (_world.PendingTransition == PendingTransition.None)
        {
            RunFixedPhysicsSteps(deltaSeconds);
        }
        PlayQueuedSounds();

        if (_world.PendingTransition != PendingTransition.None && AdvancePendingTransition(deltaSeconds))
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
        await DrawAsync(glyphs);
    }

    /// <summary>
    /// Counts down the pending transition set by <see cref="CollisionSystem"/> (physics is frozen
    /// meanwhile, effects keep animating). Once <see cref="PendingTransitionSeconds"/> have passed,
    /// carries it out: a level end opens the level-complete screen, a death respawns the player or,
    /// with no lives left, opens the game-over screen. Returns whether the mode changed, so the
    /// caller skips the rest of this playing frame.
    /// </summary>
    private bool AdvancePendingTransition(double deltaSeconds)
    {
        _pendingTransitionElapsedSeconds += deltaSeconds;
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

        _mode = transition == PendingTransition.LevelEnd ? GameMode.LevelComplete : GameMode.GameOver;
        _endScreenKeysReleased = false;
        _endScreenElapsedSeconds = 0;
        soundPlayer.Play(_mode == GameMode.LevelComplete ? LevelCompleteWooshSoundName : GameOverWooshSoundName);
        return true;
    }

    /// <summary>Handles the once-per-press action key (operates switches, opens key gates).</summary>
    private void HandleActionKeys()
    {
        var actionKeyDown = _input.IsActionPressed;
        if (actionKeyDown && !_wasActionKeyDown && (_world.TryOperateSwitch() || _world.TryOpenGate()))
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
            _physics.Step(_world, _input, PhysicsConstants.FixedPhysicsStepSeconds);
            _collision.Resolve(_world);
            _world.ApplyPendingRemovals();
            _physicsAccumulatorSeconds -= PhysicsConstants.FixedPhysicsStepSeconds;
        }
    }

    /// <summary>Forwards every sound queued on the world this frame to the sound player.</summary>
    private void PlayQueuedSounds()
    {
        foreach (var soundName in _world.DrainPendingSounds())
        {
            soundPlayer.Play(soundName);
        }
    }

    /// <summary>Adds the HUD: lives, rings, score, health bar and key icon.</summary>
    private void AddHud(List<Glyph> glyphs)
    {
        var cellWidth = _renderer.CellWidthPixels;
        var cellHeight = _renderer.CellHeightPixels;

        UIRenderer.AddFrame(glyphs, _hudBox, cellWidth, cellHeight);

        _hudText.Lines.Clear();
        _hudText.Lines.Add($"Lives:{_world.Player.Lives,3}  Rings:{_world.Player.Rings,3}  Score:{_world.Player.Score,4}");
        _hudText.Lines.Add("Health:");
        UIRenderer.AddLabel(glyphs, _hudText, cellWidth, cellHeight);

        _hudBar.CurrentValue = _world.Player.Health;
        UIRenderer.AddBar(glyphs, _hudBar, cellWidth, cellHeight);

        if (_world.Player.Keys.Count > 0)
        {
            _hudKeyLabel.Lines.Clear();
            _hudKeyLabel.Lines.Add("o╤" + ToSuperscriptDigit(_world.Player.Keys.Count));
            UIRenderer.AddLabel(glyphs, _hudKeyLabel, cellWidth, cellHeight);
        }
    }

    /// <summary>Superscript for 1-9 held keys; 'ⁿ' for more than 9.</summary>
    private static char ToSuperscriptDigit(int count) => count is >= 1 and <= 9 ? "¹²³⁴⁵⁶⁷⁸⁹"[count - 1] : 'ⁿ';

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
    // End screen frame (level complete / game over)
    // ---------------------------------------------------------------------------------------

    /// <summary>Shows the frozen last frame with a ripple-animated message box until a confirm key press, then returns to world selection.</summary>
    private async Task OnEndScreenFrameAsync(double deltaSeconds)
    {
        _endScreenElapsedSeconds += deltaSeconds;
        var confirmPressed = _input.IsConfirmPressed;
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
        await DrawAsync(glyphs);
    }

    /// <summary>
    /// Draws the frozen frame with an "Exit game?"
    /// selection; a fresh Escape press cancels and resumes play.
    /// </summary>
    private async Task OnConfirmExitFrameAsync(double deltaSeconds)
    {
        _endScreenElapsedSeconds += deltaSeconds;

        var escapeDown = _input.IsEscapePressed;
        if (escapeDown && !_wasEscapeKeyDown)
        {
            _wasEscapeKeyDown = true;
            _mode = GameMode.Playing;
            return;
        }
        _wasEscapeKeyDown = escapeDown;

        var confirmPressed = _input.IsConfirmPressed;
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
        await DrawAsync(glyphs);
    }

    /// <summary>
    /// Draws the end screen message in a centered box: a short trail
    /// onto the final box (just big enough for the message), then the message appears inside it.
    /// Colored per screen (see <see cref="LevelCompleteColor"/>, <see cref="GameOverColor"/> and
    /// <see cref="ConfirmExitColor"/>). Only adds glyphs on top of the world.
    /// </summary>
    private void AddEndScreenMessage(List<Glyph> glyphs)
    {
        var isLevelComplete = _mode == GameMode.LevelComplete;
        var isConfirmExit = _mode == GameMode.ConfirmExit;
        var text = isConfirmExit ? "Exit game?" : isLevelComplete ? "Level Complete!" : "Game Over!";
        var color = isConfirmExit ? ConfirmExitColor : isLevelComplete ? LevelCompleteColor : GameOverColor;

        var finalWidth = text.Length + 4;
        var finalHeight = 3;

        // Level 0 is the final box; each level above it is EndScreenStepCells larger on each axis.
        var level = Math.Max(0, EndScreenRippleSteps - (int)(_endScreenElapsedSeconds / EndScreenStepSeconds));
        var outermostLevel = level == 0 ? 0 : Math.Min(level + EndScreenRippleTrail - 1, EndScreenRippleSteps);

        _endScreenFrame.ForeColor = color;
        for (var frameLevel = outermostLevel; frameLevel >= level; frameLevel--)
        {
            _endScreenFrame.Width = finalWidth + EndScreenStepCells * frameLevel;
            _endScreenFrame.Height = finalHeight + EndScreenStepCells * frameLevel;
            _endScreenFrame.Col = Math.Floor((_viewportWidthCells - _endScreenFrame.Width) / 2);
            _endScreenFrame.Row = Math.Floor((_viewportHeightCells - _endScreenFrame.Height) / 2);
            UIRenderer.AddFrame(glyphs, _endScreenFrame, _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        }

        if (level > 0)
        {
            return;
        }

        // The label sits on the frame's single interior row; its black background is a solid panel behind the text.
        var interiorWidth = text.Length + 2;
        _endScreenLabel.Col = _endScreenFrame.Col + 1;
        _endScreenLabel.Row = _endScreenFrame.Row + 1;
        _endScreenLabel.Width = interiorWidth;
        _endScreenLabel.ForeColor = color;
        _endScreenLabel.Lines.Clear();
        _endScreenLabel.Lines.Add($" {text} ");
        UIRenderer.AddLabel(glyphs, _endScreenLabel, _renderer.CellWidthPixels, _renderer.CellHeightPixels);
    }

    // ---------------------------------------------------------------------------------------
    // Drawing
    // ---------------------------------------------------------------------------------------

    private Task DrawAsync(List<Glyph> glyphs) =>
        canvasBridge.DrawFrameAsync(_viewportWidthPixels, _viewportHeightPixels, _renderer.CellWidthPixels, _renderer.CellHeightPixels, glyphs);
}
