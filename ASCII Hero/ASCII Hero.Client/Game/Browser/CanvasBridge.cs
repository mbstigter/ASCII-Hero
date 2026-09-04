using Microsoft.JSInterop;

namespace ASCII_Hero.Client.Game.Browser;

/// <summary>
/// The real, measured pixel size of one font glyph cell, reported by the browser after the
/// active rendering font has finished loading. See game-interop.js for how this is measured.
/// </summary>
public readonly record struct CellMetrics(double CellWidthPixels, double CellHeightPixels);

/// <summary>
/// Thin, isolated interop boundary between C# and the browser Canvas/keyboard APIs.
/// This is the only place in the game that talks to JavaScript.
/// </summary>
public class CanvasBridge(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private const string ModulePath = "./js/game-interop.js";

    private IJSObjectReference? _module;
    private DotNetObjectReference<GameLoop>? _dotNetRef;

    public async Task<CellMetrics> InitializeAsync(string canvasElementId, GameLoop gameLoop)
    {
        _module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
        _dotNetRef = DotNetObjectReference.Create(gameLoop);
        return await _module.InvokeAsync<CellMetrics>("initialize", canvasElementId, _dotNetRef);
    }

    public async Task DrawFrameAsync(int width, int height, IReadOnlyList<Rendering.Glyph> glyphs)
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

        await _module.InvokeVoidAsync("drawFrame", width, height, characters, xs, ys, foreColors, backColors);
    }

    public async ValueTask DisposeAsync()
    {
        _dotNetRef?.Dispose();

        if (_module is not null)
        {
            await _module.InvokeVoidAsync("dispose");
            await _module.DisposeAsync();
        }
    }
}
