using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Camera;
using ASCII_Hero.Client.Game.Input;
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
    // 728 = 26 x 28, an exact multiple of the fixed 28px cell height both fonts are
    // scaled to match (see TARGET_CELL_HEIGHT_PX in game-interop.js). Keeping this
    // an exact multiple avoids a partial, clipped row of cells at the bottom of
    // the canvas.
    private const int ViewportHeightPixels = 728;

    private readonly InputState _input = new();
    private readonly PhysicsSystem _physics = new();
    private readonly CollisionSystem _collision = new();
    private readonly Camera2D _camera = new();
    private readonly AsciiRenderer _renderer = new();
    private readonly AnimationSystem _animation = new();

    private World2D _world = null!;

    private double _viewportWidthCells;
    private double _viewportHeightCells;

    public async Task StartAsync(string canvasElementId, FontMode fontMode = FontMode.Authentic)
    {
        // Assets are loaded once, up front, over HTTP (see IAssetFileProvider) so gameplay never
        // stalls mid-frame waiting on a fetch; the loop itself only starts once this completes.
        _world = await World2D.LoadAsync(assetFileProvider, "LevelBallTest");

        var cellMetrics = await canvasBridge.InitializeAsync(canvasElementId, this, fontMode);
        ApplyCellMetrics(cellMetrics);

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
        // which would later throw an OverflowException when cast to int in AsciiRenderer.
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
        // Clamp delta to avoid huge jumps after tab switches, etc.
        deltaSeconds = Math.Clamp(deltaSeconds, 0, 0.1);

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

        var glyphs = _renderer.BuildFrame(_world, _camera);
        await canvasBridge.DrawFrameAsync(ViewportWidthPixels, ViewportHeightPixels, glyphs);
    }
}
