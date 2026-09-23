using ASCII_Hero.Client.Game.Assets;
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
/// per animation frame. This is the game loop; it is invoked from JS via requestAnimationFrame,
/// never via Blazor's StateHasChanged.
/// </summary>
public class GameLoop(CanvasBridge canvasBridge, IAssetFileProvider assetFileProvider)
{


    /// <summary>
    /// The three strictly-separate states <see cref="OnFrame"/> can be in. Exactly one of these
    /// is ever "live" at a time - see the guards in <see cref="OnFrame"/> and
    /// <see cref="OnWorldSelectingFrameAsync"/> for how a transition between them is made atomic
    /// even though <see cref="OnFrame"/> is invoked by JS in a fire-and-forget fashion (the next
    /// requestAnimationFrame call is scheduled without waiting for this one's Task to finish - see
    /// game-interop.js) and both loading a world and world-selection input can span a genuine
    /// async gap (HTTP fetches).
    /// </summary>
    private enum GameMode
    {
        /// <summary>Driving <see cref="WorldSelectScreen"/>/<see cref="WorldSelectRenderer"/>; no <see cref="World2D"/> exists yet.</summary>
        WorldSelecting,

        /// <summary>A world was confirmed and <see cref="World2D.LoadAsync"/> is running in the background via <see cref="_loadWorldTask"/>; each frame redraws the selection screen plus the filling loading bar until it completes.</summary>
        LoadingWorld,

        /// <summary>Driving the normal per-frame Physics/Collision/Camera/Render tick against a loaded <see cref="World2D"/>.</summary>
        Playing,

        /// <summary>
        /// The player reached a <see cref="World.CollectableType.LevelEnd"/> collectable (see
        /// <see cref="World2D.LevelCompleted"/>). Freezes gameplay, shows a brief message, then
        /// returns to <see cref="WorldSelecting"/> after <see cref="LevelCompleteDisplaySeconds"/>.
        /// </summary>
        LevelComplete,
    }

    /// <summary>How long <see cref="GameMode.LevelComplete"/> is shown before returning to the world-select screen.</summary>
    private const double LevelCompleteDisplaySeconds = 2.0;

    /// <summary>Elapsed time since entering <see cref="GameMode.LevelComplete"/>, driving its return-to-selection timer.</summary>
    private double _levelCompleteElapsedSeconds;

    /// <summary>Message shown centered on screen while <see cref="GameMode.LevelComplete"/> is active.</summary>
    private readonly UILabel _levelCompleteLabel = new(col: 0, row: 0, width: 20, height: 1, foreColor: RenderConstants.DefaultForeColor) { Lines = { "Level Complete!" } };

    private readonly InputState _input = new();
    private readonly PhysicsSystem _physics = new();
    private readonly CollisionSystem _collision = new();
    private readonly Camera _camera = new();
    private readonly WorldRenderer _renderer = new();
    private readonly AnimationSystem _animation = new();

    private World2D _world = null!;
    private WorldSelectScreen _worldSelect = null!;
    private GameMode _mode = GameMode.WorldSelecting;

    /// <summary>
    /// The "Loading [World]" progress bar shown under the thumbnail row while <see cref="GameMode.LoadingWorld"/>
    /// is active - created fresh each time a world is confirmed (see <see cref="OnWorldSelectingFrameAsync"/>)
    /// and filled in by the <see cref="IProgress{T}"/> callback passed to <see cref="World2D.LoadAsync"/>
    /// as loading proceeds, so <see cref="OnFrame"/> can keep redrawing it instead of freezing the
    /// last selection-screen frame.
    /// </summary>
    private UIBar? _loadingBar;

    /// <summary>
    /// The in-flight <see cref="LoadWorldAsync"/> call while <see cref="GameMode.LoadingWorld"/> is
    /// active. Deliberately *not* awaited from within <see cref="OnWorldSelectingFrameAsync"/> -
    /// doing so would hold <see cref="OnFrame"/>'s <see cref="_isProcessingFrame"/> guard for the
    /// entire load, causing every intervening requestAnimationFrame tick (and so every intervening
    /// redraw of <see cref="_loadingBar"/>) to be dropped, which is why the bar used to appear but
    /// never visibly fill. Instead, each <see cref="OnFrame"/> tick while <see cref="GameMode.LoadingWorld"/>
    /// is active redraws the bar and polls this task for completion.
    /// </summary>
    private Task? _loadWorldTask;

    /// <summary>
    /// HUD overlay shown in the top-left corner while playing, using the independent
    /// <see cref="UIFrame"/>/<see cref="UILabel"/> screen-space primitives directly. Refreshed
    /// every <see cref="OnPlayingFrameAsync"/> tick from <see cref="World.Player2D.Health"/>/
    /// <see cref="World.Player2D.Score"/>. Sized to fit 3-digit values for both numbers (the
    /// widest either is ever expected to get) so the frame never needs to resize as they grow -
    /// each number is also right-aligned to a fixed 3-character field so the surrounding text
    /// doesn't shift/jitter as a value's own digit count changes.
    /// </summary>
    private readonly UILabel _hudText = new(col: 2, row: 2, width: 24, height: 1, foreColor: RenderConstants.DefaultForeColor);

    private readonly UIFrame _hudBox = new(col: 1, row: 1, width: 26, height: 3, foreColor: RenderConstants.DefaultForeColor);

    /// <summary>
    /// Test horizontal gauge shown below the HUD frame while playing, using the independent
    /// <see cref="UIBar"/> screen-space primitive - eventually meant for a health/stamina style
    /// readout, not just this placeholder value.
    /// </summary>
    private readonly UIBar _hudBar = new(col: 2, row: 4, width: 20, height: 1, minValue: 0, maxValue: 100, foreColor: RenderConstants.DefaultForeColor) { CurrentValue = 75 };

    /// <summary>
    /// Dev/testing FPS overlay, toggled by <see cref="InputState.IsFpsToggleKeyPressed"/> (see
    /// <see cref="OnFrame"/>) - off by default so it never appears for an ordinary player, but one
    /// keypress away when diagnosing frame-timing-dependent issues (e.g. the sub-step/pose-jitter
    /// bug this was added for - see docs/Decisions.md). Positioned in the top-right corner, in its
    /// own independent <see cref="UILabel"/> rather than reusing <see cref="_hudText"/>, so it
    /// never competes with real HUD content for the same screen space.
    /// </summary>
    private readonly UILabel _fpsLabel = new(col: 0, row: 0, width: 12, height: 1, foreColor: RenderConstants.DefaultForeColor);

    private bool _showFpsOverlay;

    /// <summary>Edge-detection state for <see cref="InputState.IsFpsToggleKeyPressed"/>, mirroring
    /// the pattern <see cref="PhysicsSystem"/> uses for its own latched keys - without this, holding
    /// the key down would toggle the overlay on/off every single frame instead of once per press.</summary>
    private bool _wasFpsToggleKeyDown;

    /// <summary>Edge-detection state for <see cref="InputState.IsRespawnDebugKeyPressed"/> - without
    /// this, holding the key down would call <see cref="World.World2D.Respawn"/> every single frame
    /// instead of once per press.</summary>
    private bool _wasRespawnDebugKeyDown;

    /// <summary>
    /// Smoothed frames-per-second reading shown by <see cref="_fpsLabel"/>, updated once per real
    /// animation frame in <see cref="OnFrame"/> from that frame's own *unclamped* deltaSeconds
    /// (before the 0.1s simulation clamp below) - deliberately the raw browser-reported frame time,
    /// not the sub-stepped simulation time, since the whole point of this overlay is to show actual
    /// real-world frame pacing, including hitches the simulation clamp/sub-stepping otherwise hides
    /// from gameplay. A simple exponential moving average smooths out the frame-to-frame noise an
    /// instantaneous 1/deltaSeconds reading would otherwise show, while still reacting quickly
    /// enough to be useful for spotting a real, sustained frame-rate change.
    /// </summary>
    private double _smoothedFps;

    private const double FpsSmoothingFactor = 0.1;

    /// <summary>
    /// Dev/testing world object-count overlay, toggled by
    /// <see cref="InputState.IsObjectCounterToggleKeyPressed"/> (see <see cref="OnFrame"/>) - off
    /// by default, shown directly under <see cref="_fpsLabel"/> (row 1) so the two overlays stack
    /// in the top-right corner without competing for the same row. Lets a level author watch
    /// <see cref="World.World2D.Objects"/>'s count live, e.g. to see it drop as collectables
    /// (such as the new points stars) are picked up.
    /// </summary>
    private readonly UILabel _objectCounterLabel = new(col: 0, row: 1, width: 12, height: 1, foreColor: RenderConstants.DefaultForeColor);

    private bool _showObjectCounterOverlay;

    /// <summary>Edge-detection state for <see cref="InputState.IsObjectCounterToggleKeyPressed"/>,
    /// mirroring <see cref="_wasFpsToggleKeyDown"/>'s own pattern/rationale.</summary>
    private bool _wasObjectCounterToggleKeyDown;

    /// <summary>
    /// Unspent real elapsed time carried forward between frames for the fixed-timestep Physics/
    /// Collision accumulator in <see cref="OnPlayingFrameAsync"/> (the standard "fix your
    /// timestep" pattern) - each frame adds that frame's <c>deltaSeconds</c> here, then drains
    /// whatever whole multiple of <see cref="PhysicsConstants.FixedPhysicsStepSeconds"/> is currently
    /// available, leaving any remainder (always strictly less than one fixed step) sitting here
    /// for the next frame to pick up, rather than folding it into an odd-sized partial step this
    /// frame. This is what keeps every Physics/Collision step identically sized regardless of how
    /// the real frame rate happens to fluctuate frame to frame - see
    /// <see cref="PhysicsConstants.FixedPhysicsStepSeconds"/>'s own doc comment for why an
    /// inconsistent step size was itself enough to visibly perturb collision/pose resolution,
    /// independent of the earlier oversized-single-step tunneling concern.
    /// </summary>
    private double _physicsAccumulatorSeconds;

    /// <summary>
    /// Blanket re-entrancy guard for <see cref="OnFrame"/> itself, on top of (not instead of) the
    /// explicit <see cref="GameMode"/> transition guard below: since a fire-and-forget-scheduled
    /// frame can take arbitrarily long (in particular, one that does real async I/O), a dropped
    /// frame here is harmless - the next requestAnimationFrame call, moments later, simply picks
    /// up from whatever state this call left behind - but re-entering this method's logic
    /// mid-flight never is.
    /// </summary>
    private bool _isProcessingFrame;

    private double _viewportWidthCells;
    private double _viewportHeightCells;
    private int _viewportWidthPixels;
    private int _viewportHeightPixels;

    public async Task StartAsync(string canvasElementId)
    {
        // The world catalog's titles/thumbnails are cheap to load - unlike a full World2D - so
        // they're loaded up front alongside everything else the selection screen needs.
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

        // The font's own true native pixel size has no default: a browser's text-measuring API
        // cannot reliably report it for a true bitmap font (see docs/Decisions.md's Rendering &
        // Camera section), so it must always be supplied explicitly. There is no separate scale
        // factor either - these values are the final, already-scaled on-screen cell size (e.g. an
        // 8x14 font at 2x zoom is simply configured as 16/28), so a deliberately non-uniform value
        // (e.g. 15x28) can squeeze/stretch the font in one direction if ever desired.
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

        var visibleSlotCount = WorldSelectRenderer.ComputeVisibleSlotCount(_viewportWidthCells, worlds.Count);
        _worldSelect = new WorldSelectScreen(worlds, visibleSlotCount);
        _mode = GameMode.WorldSelecting;
    }

    private async Task LoadWorldAsync(string worldName)
    {
        // Assets are loaded once, up front, over HTTP (see IAssetFileProvider) so gameplay never
        // stalls mid-frame waiting on a fetch; the frame loop only starts driving Physics/etc.
        // once this completes. _loadingBar is filled in as each logical loading step completes
        // (see World2D.LoadStepCount), read back by OnFrame's GameMode.LoadingWorld case so the
        // bar visibly fills instead of the canvas freezing on the last selection-screen frame.
        var progress = new Progress<double>(stepsCompleted =>
        {
            if (_loadingBar is not null)
            {
                _loadingBar.CurrentValue = stepsCompleted;
            }
        });
        _world = await World2D.LoadAsync(assetFileProvider, worldName, progress);

        // Discard any leftover accumulated time from a previous world (or the time genuinely
        // spent loading this one, which was never real gameplay time) - starting the new world
        // with a stale/inflated _physicsAccumulatorSeconds would otherwise immediately burn
        // through several fixed steps in the very first Playing frame.
        _physicsAccumulatorSeconds = 0;

        // Placeholder readout - no actual points/rings tracking exists yet; this just shows
        // what the HUD text line is eventually meant to display (see _hudText's own doc comment).
        _hudText.Lines.Clear();
        _hudText.Lines.Add($"Health: {_world.Player.Health,3}   Score: {_world.Player.Score,3}");

        _camera.SnapTo(
            _world.CameraTarget.Position,
            _world.CameraTarget.Size,
            _world.WidthCells,
            _world.HeightCells,
            _viewportWidthCells,
            _viewportHeightCells);
    }

    private void ApplyCellMetrics(CellMetrics cellMetrics, int viewportColumns, int viewportRows)
    {
        // Defensive guard: if the browser ever reports a non-finite or non-positive cell size
        // (e.g. a font measurement taken before layout/font-load settled), fall back to the
        // previous/default cell size instead of propagating NaN/Infinity into the renderer,
        // which would later throw an OverflowException when cast to int in WorldRenderer.
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

        // The viewport is always exactly viewportColumns/Rows cells - never a division of a fixed
        // pixel size by the cell size - so it's an exact whole number of cells regardless of which
        // font/scale is active (see RenderConstants.DefaultViewportColumns/Rows's own doc comment).
        _viewportWidthCells = viewportColumns;
        _viewportHeightCells = viewportRows;
        _viewportWidthPixels = (int)Math.Round(viewportColumns * _renderer.CellWidthPixels);
        _viewportHeightPixels = (int)Math.Round(viewportRows * _renderer.CellHeightPixels);
    }

    [JSInvokable]
    public void OnKeyDown(string code) => _input.KeyDown(code);

    [JSInvokable]
    public void OnKeyUp(string code) => _input.KeyUp(code);

    [JSInvokable]
    public async Task OnFrame(double deltaSeconds)
    {
        // See _isProcessingFrame's doc comment - dropping an overlapping frame outright here is
        // simpler and safer than trying to make every state's frame logic itself re-entrant-safe.
        if (_isProcessingFrame)
        {
            return;
        }

        _isProcessingFrame = true;
        try
        {
            // Edge-triggered so holding "F" down doesn't flicker the overlay on/off every frame -
            // see _wasFpsToggleKeyDown's own doc comment.
            var fpsToggleKeyDown = _input.IsFpsToggleKeyPressed;
            if (fpsToggleKeyDown && !_wasFpsToggleKeyDown)
            {
                _showFpsOverlay = !_showFpsOverlay;
            }
            _wasFpsToggleKeyDown = fpsToggleKeyDown;

            // Same edge-triggered pattern as the FPS toggle above - see _wasObjectCounterToggleKeyDown's
            // own doc comment.
            var objectCounterToggleKeyDown = _input.IsObjectCounterToggleKeyPressed;
            if (objectCounterToggleKeyDown && !_wasObjectCounterToggleKeyDown)
            {
                _showObjectCounterOverlay = !_showObjectCounterOverlay;
            }
            _wasObjectCounterToggleKeyDown = objectCounterToggleKeyDown;

            // Uses this frame's raw, unclamped deltaSeconds - see _smoothedFps's own doc comment
            // for why the simulation clamp below must not affect this reading. Guarded against a
            // zero/negative delta (e.g. the very first frame, or an unexpected browser timestamp
            // glitch) which would otherwise divide by zero or report an infinite/negative rate.
            if (deltaSeconds > 0)
            {
                var instantaneousFps = 1.0 / deltaSeconds;
                _smoothedFps = _smoothedFps <= 0
                    ? instantaneousFps
                    : _smoothedFps + (instantaneousFps - _smoothedFps) * FpsSmoothingFactor;
            }

            // Clamp delta to avoid huge jumps after tab switches, etc.
            deltaSeconds = Math.Clamp(deltaSeconds, 0, 0.1);

            switch (_mode)
            {
                case GameMode.WorldSelecting:
                    await OnWorldSelectingFrameAsync(deltaSeconds);
                    break;
                case GameMode.Playing:
                    await OnPlayingFrameAsync(deltaSeconds);
                    break;
                case GameMode.LoadingWorld:
                    // A confirmed world's World2D is loading in the background via _loadWorldTask
                    // (started, not awaited, by OnWorldSelectingFrameAsync) - keep redrawing the
                    // selection screen plus the filling _loadingBar each frame, and switch to
                    // Playing once that task completes.
                    await OnLoadingWorldFrameAsync();
                    break;
                case GameMode.LevelComplete:
                    await OnLevelCompleteFrameAsync(deltaSeconds);
                    break;
            }
        }
        finally
        {
            _isProcessingFrame = false;
        }
    }

    private async Task OnPlayingFrameAsync(double deltaSeconds)
    {
        // Dev/testing shortcut: abandon the current world and return to the world-select screen -
        // see InputState.IsEscapePressed's doc comment. Also the same _mode reset a future
        // level-complete/death flow will use once those exist (see docs/Decisions.md).
        if (_input.IsEscapePressed)
        {
            _worldSelect.ResetConfirmation();
            _mode = GameMode.WorldSelecting;
            return;
        }

        // Dev/testing shortcut: instantly respawn the player (see World2D.Respawn) regardless of
        // current health - edge-triggered the same way the FPS/object-counter overlay toggles are,
        // so holding the key down doesn't respawn every single frame.
        var respawnDebugKeyDown = _input.IsRespawnDebugKeyPressed;
        if (respawnDebugKeyDown && !_wasRespawnDebugKeyDown)
        {
            _world.Respawn();
        }
        _wasRespawnDebugKeyDown = respawnDebugKeyDown;

        // A single real animation frame's deltaSeconds can vary - from ordinary frame-to-frame
        // jitter, or occasionally far more than ordinary after a real hitch (a GC pause, a slow
        // JS interop round-trip, a dropped/re-entrant frame - see _isProcessingFrame's own doc
        // comment). Physics/Collision are instead run in a fixed-timestep accumulator (the
        // standard "fix your timestep" pattern): this frame's deltaSeconds is added to
        // _physicsAccumulatorSeconds, then drained in however many whole
        // PhysicsConstants.FixedPhysicsStepSeconds-sized steps are currently available, leaving any
        // remainder for next frame rather than folding it into an odd-sized partial step this
        // frame - see PhysicsConstants.FixedPhysicsStepSeconds's own doc comment for why every step
        // must be identically sized, not just capped, to avoid perturbing collision/pose
        // resolution right at a grounded/airborne boundary. Collision is resolved once per fixed
        // step (not once for the whole frame) so a large catch-up delta can't let a body integrate
        // several times before ever being collision-checked, which would risk tunneling through a
        // wall (neither Physics nor Collision uses continuous/swept detection). Pending removals
        // are likewise applied after every fixed step, not just once at the end, so a later step's
        // collision pass never sees a body that should already be gone. Animation/camera/render
        // remain once per real frame using the full original deltaSeconds - both are purely
        // presentational and already tolerant of a larger delta.
        _physicsAccumulatorSeconds += deltaSeconds;
        while (_physicsAccumulatorSeconds >= PhysicsConstants.FixedPhysicsStepSeconds)
        {
            _physics.Step(_world, _input, PhysicsConstants.FixedPhysicsStepSeconds);
            _collision.Resolve(_world);
            _world.ApplyPendingRemovals();
            _physicsAccumulatorSeconds -= PhysicsConstants.FixedPhysicsStepSeconds;
        }

        if (_world.LevelCompleted)
        {
            _mode = GameMode.LevelComplete;
            _levelCompleteElapsedSeconds = 0;
            return;
        }

        _animation.Update(_world, deltaSeconds);

        _camera.Follow(
            _world.CameraTarget.Position,
            _world.CameraTarget.Size,
            _world.WidthCells,
            _world.HeightCells,
            _viewportWidthCells,
            _viewportHeightCells,
            deltaSeconds);

        var glyphs = _renderer.BuildFrame(_world, _camera, _viewportWidthCells, _viewportHeightCells);
        UIRenderer.AddFrame(glyphs, _hudBox, _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        _hudText.Lines.Clear();
        _hudText.Lines.Add($"Health: {_world.Player.Health,3}   Score: {_world.Player.Score,3}");
        UIRenderer.AddLabel(glyphs, _hudText, _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        UIRenderer.AddBar(glyphs, _hudBar, _renderer.CellWidthPixels, _renderer.CellHeightPixels);

        if (_showFpsOverlay)
        {
            // Right-aligned against the current viewport width (itself dependent on the browser's
            // reported cell metrics - see ApplyCellMetrics) so the label stays flush with the
            // top-right corner rather than a fixed column that would only be correct for one
            // particular viewport/cell size. The number leads (e.g. "144 FPS", not "FPS: 144") so
            // Col is recomputed from this frame's actual rendered text length, not _fpsLabel.Width
            // (which is only an upper-bound truncation cap) - otherwise the string would grow
            // rightward off the corner, or leave a gap, as the digit count changes.
            var fpsText = $"{_smoothedFps:F0} FPS";
            _fpsLabel.Col = _viewportWidthCells - fpsText.Length;
            _fpsLabel.Lines.Clear();
            _fpsLabel.Lines.Add(fpsText);
            UIRenderer.AddLabel(glyphs, _fpsLabel, _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        }

        if (_showObjectCounterOverlay)
        {
            // Same right-aligned-by-actual-text-length approach as _fpsLabel above, stacked
            // directly underneath it (row 1).
            var objectCountText = $"{_world.Objects.Count} Obj";
            _objectCounterLabel.Col = _viewportWidthCells - objectCountText.Length;
            _objectCounterLabel.Lines.Clear();
            _objectCounterLabel.Lines.Add(objectCountText);
            UIRenderer.AddLabel(glyphs, _objectCounterLabel, _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        }

        await canvasBridge.DrawFrameAsync(_viewportWidthPixels, _viewportHeightPixels, _renderer.CellWidthPixels, _renderer.CellHeightPixels, glyphs);
    }

    /// <summary>
    /// Drives one frame of <see cref="GameMode.LevelComplete"/>: keeps drawing the last playing
    /// frame's glyphs (gameplay is frozen - no Physics/Collision/Animation/Camera update) with a
    /// centered "Level Complete!" message on top, then returns to <see cref="GameMode.WorldSelecting"/>
    /// once <see cref="LevelCompleteDisplaySeconds"/> has elapsed.
    /// </summary>
    private async Task OnLevelCompleteFrameAsync(double deltaSeconds)
    {
        _levelCompleteElapsedSeconds += deltaSeconds;
        if (_levelCompleteElapsedSeconds >= LevelCompleteDisplaySeconds)
        {
            _worldSelect.ResetConfirmation();
            _mode = GameMode.WorldSelecting;
            return;
        }

        var glyphs = _renderer.BuildFrame(_world, _camera, _viewportWidthCells, _viewportHeightCells);
        _levelCompleteLabel.Col = (_viewportWidthCells - _levelCompleteLabel.Lines[0].Length) / 2;
        _levelCompleteLabel.Row = _viewportHeightCells / 2;
        UIRenderer.AddLabel(glyphs, _levelCompleteLabel, _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        await canvasBridge.DrawFrameAsync(_viewportWidthPixels, _viewportHeightPixels, _renderer.CellWidthPixels, _renderer.CellHeightPixels, glyphs);
    }

    /// <summary>
    /// Drives one frame of the pre-game world-selection screen: Left/Right (or A/D) moves the
    /// selection, the jump/action key confirms it, at which point the chosen world's actual
    /// World2D is loaded and gameplay begins once that completes. See docs/AssetFormat.md §3.2.
    /// </summary>
    private async Task OnWorldSelectingFrameAsync(double deltaSeconds)
    {
        _worldSelect.Update(_input, deltaSeconds);

        if (_worldSelect.Confirmed)
        {
            // Switch modes synchronously, then kick off the load WITHOUT awaiting it here - see
            // _loadWorldTask's doc comment for why: awaiting it inline would hold this OnFrame
            // call's _isProcessingFrame guard for the whole load, starving every intervening
            // requestAnimationFrame tick (and so _loadingBar's redraw) until it's already done.
            _mode = GameMode.LoadingWorld;
            _loadingBar = WorldLoadingRenderer.CreateLoadingBar(_worldSelect, _viewportWidthCells, _viewportHeightCells, World2D.LoadStepCount);

            var worldName = _worldSelect.SelectedWorld.WorldName;
            _loadWorldTask = LoadWorldAsync(worldName);

            await OnLoadingWorldFrameAsync();
            return;
        }

        var glyphs = WorldSelectRenderer.BuildFrame(
            _worldSelect, _viewportWidthCells, _viewportHeightCells,
            _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        await canvasBridge.DrawFrameAsync(_viewportWidthPixels, _viewportHeightPixels, _renderer.CellWidthPixels, _renderer.CellHeightPixels, glyphs);
    }

    /// <summary>
    /// Draws the frozen world-selection layout plus the current <see cref="_loadingBar"/> fill
    /// level, and switches to <see cref="GameMode.Playing"/> once <see cref="_loadWorldTask"/>
    /// completes. Called once right as loading starts (so the bar appears at 0 immediately) and
    /// from every subsequent <see cref="OnFrame"/> tick that lands while <see cref="GameMode.LoadingWorld"/>
    /// is still active.
    /// </summary>
    private async Task OnLoadingWorldFrameAsync()
    {
        if (_loadingBar is null || _loadWorldTask is null)
        {
            return;
        }

        var glyphs = WorldLoadingRenderer.BuildLoadingFrame(
            _worldSelect, _loadingBar, _viewportWidthCells, _viewportHeightCells,
            _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        await canvasBridge.DrawFrameAsync(_viewportWidthPixels, _viewportHeightPixels, _renderer.CellWidthPixels, _renderer.CellHeightPixels, glyphs);

        if (_loadWorldTask.IsCompleted)
        {
            try
            {
                // Propagate any load failure instead of silently swallowing it.
                await _loadWorldTask;

                _mode = GameMode.Playing;
            }
            catch (Exception ex)
            {
                // Without this, a faulted _loadWorldTask stays IsCompleted forever, so every
                // subsequent OnFrame tick would re-await (and re-throw from) it here - which is
                // exactly why the bar previously appeared to get permanently "stuck" at whatever
                // percentage it last reached instead of surfacing the actual failure. Logged via
                // Console (visible in the browser dev tools console for a WASM app) since there's
                // no dedicated error-screen UI yet; falling back to WorldSelecting lets the player
                // at least try again or pick a different world instead of a dead loading screen.
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
}
