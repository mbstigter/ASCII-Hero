using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Camera;
using ASCII_Hero.Client.Game.Input;
using ASCII_Hero.Client.Game.Menu;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.Rendering;
using ASCII_Hero.Client.Game.World;
using ASCII_Hero.Client.Game.Animation;
using Microsoft.JSInterop;

namespace ASCII_Hero.Client.Game;

/// <summary>
/// Ties together world state, physics, collision, camera and rendering, and drives them once
/// per animation frame. This is the game loop; it is invoked from JS via requestAnimationFrame,
/// never via Blazor's StateHasChanged.
/// </summary>
public class GameLoop(CanvasBridge canvasBridge, IAssetFileProvider assetFileProvider)
{
    private const int ViewportWidthPixels = 1280;
    // 700 = 25 x 28, an exact multiple of the fixed 28px cell height both fonts are
    // scaled to match (see TARGET_CELL_HEIGHT_PX in game-interop.js). Keeping this
    // an exact multiple avoids a partial, clipped row of cells at the bottom of
    // the canvas.
    private const int ViewportHeightPixels = 700;

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

        /// <summary>A world was confirmed and <see cref="World2D.LoadAsync"/> is in flight; frames are dropped until it completes.</summary>
        LoadingWorld,

        /// <summary>Driving the normal per-frame Physics/Collision/Camera/Render tick against a loaded <see cref="World2D"/>.</summary>
        Playing,
    }

    private readonly InputState _input = new();
    private readonly PhysicsSystem _physics = new();
    private readonly CollisionSystem _collision = new();
    private readonly Camera2D _camera = new();
    private readonly WorldRenderer _renderer = new();
    private readonly AnimationSystem _animation = new();

    private World2D _world = null!;
    private WorldSelectScreen _worldSelect = null!;
    private GameMode _mode = GameMode.WorldSelecting;

    private const string HudForeColor = "#00ff00";

    /// <summary>
    /// Test HUD overlay shown in the top-left corner while playing, using the independent
    /// <see cref="UIFrame"/>/<see cref="UILabel"/> screen-space primitives directly - eventually
    /// meant for a real score/collectable-count readout, not just this one placeholder line.
    /// </summary>
    private readonly UILabel _hudText = new(col: 2, row: 2, width: 20, height: 1, foreColor: HudForeColor);

    private readonly UIFrame _hudBox = new(col: 1, row: 1, width: 22, height: 3, foreColor: HudForeColor);

    /// <summary>
    /// Test horizontal gauge shown below the HUD frame while playing, using the independent
    /// <see cref="UIBar"/> screen-space primitive - eventually meant for a health/stamina style
    /// readout, not just this placeholder value.
    /// </summary>
    private readonly UIBar _hudBar = new(col: 2, row: 4, width: 20, height: 1, minValue: 0, maxValue: 100, foreColor: HudForeColor) { CurrentValue = 75 };

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

    public async Task StartAsync(string canvasElementId, FontMode fontMode = FontMode.Authentic)
    {
        // The world catalog's titles/thumbnails are cheap to load - unlike a full World2D - so
        // they're loaded up front alongside everything else the selection screen needs.
        var worlds = await WorldCatalog.LoadAllAsync(assetFileProvider);

        var cellMetrics = await canvasBridge.InitializeAsync(canvasElementId, this, fontMode);
        ApplyCellMetrics(cellMetrics);

        var visibleSlotCount = WorldSelectRenderer.ComputeVisibleSlotCount(_viewportWidthCells, worlds.Count);
        _worldSelect = new WorldSelectScreen(worlds, visibleSlotCount);
        _mode = GameMode.WorldSelecting;
    }

    private async Task LoadWorldAsync(string worldName)
    {
        // Assets are loaded once, up front, over HTTP (see IAssetFileProvider) so gameplay never
        // stalls mid-frame waiting on a fetch; the frame loop only starts driving Physics/etc.
        // once this completes.
        _world = await World2D.LoadAsync(assetFileProvider, worldName);

        // Placeholder readout - no actual points/rings tracking exists yet; this just shows
        // what the HUD text line is eventually meant to display (see _hudText's own doc comment).
        _hudText.Lines.Clear();
        _hudText.Lines.Add("Points: 0   Rings: 0");

        _camera.SnapTo(
            _world.CameraTarget.Position,
            _world.CameraTarget.Size,
            _world.WidthCells,
            _world.HeightCells,
            _viewportWidthCells,
            _viewportHeightCells);
    }


    /// <summary>
    /// Switches the rendering font at runtime (used by the Authentic/Modern toggle) and
    /// re-applies the newly measured cell metrics so the world keeps rendering correctly.
    /// </summary>
    public async Task SetFontModeAsync(FontMode fontMode)
    {
        var cellMetrics = await canvasBridge.SetFontAsync(fontMode);
        ApplyCellMetrics(cellMetrics);
    }

    private void ApplyCellMetrics(CellMetrics cellMetrics)
    {
        // Defensive guard: if the browser ever reports a non-finite or non-positive cell size
        // (e.g. a font measurement taken before layout/font-load settled), fall back to the
        // previous/default cell size instead of propagating NaN/Infinity into the renderer,
        // which would later throw an OverflowException when cast to int in WorldRenderer.
        var width = cellMetrics.CellWidthPixels;
        var height = cellMetrics.CellHeightPixels;

        if (!double.IsFinite(width) || width <= 0)
        {
            width = _renderer.CellWidthPixels > 0 ? _renderer.CellWidthPixels : 16;
        }
        if (!double.IsFinite(height) || height <= 0)
        {
            height = _renderer.CellHeightPixels > 0 ? _renderer.CellHeightPixels : 24;
        }

        _renderer.CellWidthPixels = width;
        _renderer.CellHeightPixels = height;

        _viewportWidthCells = ViewportWidthPixels / _renderer.CellWidthPixels;
        _viewportHeightCells = ViewportHeightPixels / _renderer.CellHeightPixels;
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
                    // Nothing to do - a confirmed world's World2D is already being loaded by an
                    // earlier call to OnWorldSelectingFrameAsync; that call itself will switch
                    // _mode to Playing once it completes.
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
        _physics.Step(_world, _input, deltaSeconds);
        _collision.Resolve(_world);
        _world.ApplyPendingRemovals();
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
        UIRenderer.AddLabel(glyphs, _hudText, _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        UIRenderer.AddBar(glyphs, _hudBar, _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        await canvasBridge.DrawFrameAsync(ViewportWidthPixels, ViewportHeightPixels, glyphs);
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
            // Switch modes synchronously, before the genuine async gap below (World2D.LoadAsync
            // does real HTTP fetches) - _isProcessingFrame already blocks a literally-overlapping
            // OnFrame call, but flipping _mode here too keeps the three states honest even if
            // that guard is ever loosened, and documents the transition explicitly.
            _mode = GameMode.LoadingWorld;

            var worldName = _worldSelect.SelectedWorld.WorldName;
            await LoadWorldAsync(worldName);

            _mode = GameMode.Playing;
            return;
        }

        var glyphs = WorldSelectRenderer.BuildFrame(
            _worldSelect, _viewportWidthCells, _viewportHeightCells,
            _renderer.CellWidthPixels, _renderer.CellHeightPixels);
        await canvasBridge.DrawFrameAsync(ViewportWidthPixels, ViewportHeightPixels, glyphs);
    }
}
