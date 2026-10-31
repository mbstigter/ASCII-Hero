using Microsoft.JSInterop;

namespace ASCII_Hero.Client.Game.Browser;

/// <summary>
/// The pixel size of one font glyph cell, as returned by the browser initializer. These are the
/// configured <c>FontWidthPixels</c>/<c>FontHeightPixels</c> echoed back; the initializer does not
/// wait for the font to load or measure it. See game-interop.js.
/// </summary>
public readonly record struct CellMetrics(double CellWidthPixels, double CellHeightPixels);

/// <summary>
/// Thin, isolated interop boundary between C# and the browser Canvas/keyboard APIs.
/// Audio has its own separate adapter; no other game code talks to JavaScript.
/// </summary>
public class CanvasBridge(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private const string ModulePath = "./js/game-interop.js";

    private IJSObjectReference? _module;
    private DotNetObjectReference<GameLoop>? _dotNetRef;

    public async Task<CellMetrics> InitializeAsync(string canvasElementId, GameLoop gameLoop, string fontFamily, int viewportColumns, int viewportRows, int fontWidthPixels, int fontHeightPixels)
    {
        _module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
        _dotNetRef = DotNetObjectReference.Create(gameLoop);
        return await _module.InvokeAsync<CellMetrics>("initialize", canvasElementId, _dotNetRef, fontFamily, viewportColumns, viewportRows, fontWidthPixels, fontHeightPixels);
    }

    public async Task DrawFrameAsync(int width, int height, double cellWidthPixels, double cellHeightPixels, IReadOnlyList<Rendering.Glyph> glyphs)
    {
        if (_module is null)
        {
            return;
        }

        var characters = new string(glyphs.Select(g => g.Character).ToArray());
        var xs = glyphs.Select(g => g.PixelX).ToArray();
        var ys = glyphs.Select(g => g.PixelY).ToArray();
        var foreColors = glyphs.Select(g => g.ForeColor).ToArray();
        var backColors = glyphs.Select(g => g.BackColor).ToArray();

        await _module.InvokeVoidAsync("drawFrame", width, height, cellWidthPixels, cellHeightPixels, characters, xs, ys, foreColors, backColors);
    }

    public async ValueTask DisposeAsync()
    {
        // Stop the JS frame loop and listeners first, so no callback can reach the .NET reference
        // after it is released.
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("dispose");
            await _module.DisposeAsync();
        }

        _dotNetRef?.Dispose();
    }
}
