using Microsoft.JSInterop;

namespace ASCII_Hero.Client.Game.Browser;

/// <summary>
/// The real, measured pixel size of one font glyph cell, reported by the browser after the
/// active rendering font has finished loading. See game-interop.js for how this is measured.
/// </summary>
public readonly record struct CellMetrics(double CellWidthPixels, double CellHeightPixels);

/// <summary>
/// The two selectable rendering fonts, matching the presets defined in game-interop.js.
/// </summary>
public enum FontMode
{
    /// <summary>The bundled CP437 bitmap font (Web437 IBM VGA 8x14).</summary>
    Authentic,

    /// <summary>A conventional anti-aliased coding font (JetBrains Mono).</summary>
    Modern,
}

/// <summary>
/// Thin, isolated interop boundary between C# and the browser Canvas/keyboard APIs.
/// This is the only place in the game that talks to JavaScript.
/// </summary>
public class CanvasBridge(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private const string ModulePath = "./js/game-interop.js";

    private IJSObjectReference? _module;
    private DotNetObjectReference<GameLoop>? _dotNetRef;

    public async Task<CellMetrics> InitializeAsync(string canvasElementId, GameLoop gameLoop, FontMode fontMode)
    {
        _module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
        _dotNetRef = DotNetObjectReference.Create(gameLoop);
        return await _module.InvokeAsync<CellMetrics>("initialize", canvasElementId, _dotNetRef, ToJsFontMode(fontMode));
    }

    /// <summary>
    /// Switches the active rendering font at runtime (used by the Authentic/Modern toggle in
    /// Home.razor) and returns the newly measured cell size for the switched-to font.
    /// </summary>
    public async Task<CellMetrics> SetFontAsync(FontMode fontMode)
    {
        if (_module is null)
        {
            return default;
        }

        return await _module.InvokeAsync<CellMetrics>("setFont", ToJsFontMode(fontMode));
    }

    private static string ToJsFontMode(FontMode fontMode) => fontMode switch
    {
        FontMode.Modern => "modern",
        _ => "authentic",
    };

    public async Task DrawFrameAsync(int width, int height, IReadOnlyList<Rendering.Glyph> glyphs)
    {
        if (_module is null)
        {
            return;
        }

        var characters = new string(glyphs.Select(g => g.Character).ToArray());
        var xs = glyphs.Select(g => g.PixelX).ToArray();
        var ys = glyphs.Select(g => g.PixelY).ToArray();

        await _module.InvokeVoidAsync("drawFrame", width, height, characters, xs, ys);
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
