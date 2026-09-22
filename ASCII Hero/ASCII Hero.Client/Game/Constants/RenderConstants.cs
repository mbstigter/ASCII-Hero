namespace ASCII_Hero.Client.Game.Constants;

/// <summary>
/// Centralized rendering tuning values shared across the render pipeline
/// (<see cref="Rendering.WorldRenderer"/>, <see cref="Rendering.WorldSelectRenderer"/>,
/// <see cref="Rendering.GlyphBuilder"/>, <see cref="GameLoop"/>). Only values genuinely shared or
/// reused across multiple renderers live here - purely local layout constants (padding, single-use
/// widths/heights, etc.) stay defined in whichever renderer owns them.
/// </summary>
public static class RenderConstants
{
    /// <summary>
    /// Default font family used to render the ASCII grid, until overridden by the
    /// <c>[Render] FontFamily</c> key in <c>Global/Settings.ini</c>. Must name a font already
    /// available to the browser (e.g. via an <c>@font-face</c> declaration in app.css/standalone.css,
    /// or a system font) - selecting a font here does not itself load any new font asset.
    /// </summary>
    public const string DefaultFontFamily = "\"Web437IbmVga8x14\", monospace";

    /// <summary>
    /// Default width of the viewport, in whole character columns - the canvas/viewport pixel
    /// width is always this many columns times the active cell width (see
    /// <see cref="GameLoop.ApplyCellMetrics"/>), never a fixed pixel value, so it adapts to
    /// whatever font/scale is active. Overridable via the <c>[Render] ViewportColumns</c> key in
    /// <c>Global/Settings.ini</c>.
    /// </summary>
    public const int DefaultViewportColumns = 80;

    /// <summary>
    /// Default height of the viewport, in whole character rows - see
    /// <see cref="DefaultViewportColumns"/>'s own doc comment for the same rationale applied to
    /// rows. Overridable via the <c>[Render] ViewportRows</c> key in <c>Global/Settings.ini</c>.
    /// </summary>
    public const int DefaultViewportRows = 25;

    /// <summary>
    /// No default: the <c>[Render] FontWidthPixels</c>/<c>FontHeightPixels</c> keys in
    /// <c>Global/Settings.ini</c> are mandatory - <see cref="GameLoop.StartAsync"/> throws if
    /// either is missing. A browser's text-measuring API cannot reliably report a true bitmap
    /// font's real native pixel size (a browser may only rasterize/measure such a font precisely
    /// at specific sizes), so rather than an unreliable auto-detect, the font's actual documented
    /// native size must always be supplied explicitly. These are also the final on-screen cell
    /// size directly (no separate scale factor) - e.g. the bundled 8x14 font at 2x zoom is simply
    /// configured as 16/28 - so a deliberately non-uniform pair (e.g. 15/28) can squeeze/stretch
    /// the font in one direction if ever desired.
    /// </summary>

    /// <summary>
    /// Default width (in pixels) of one world cell, used until the browser reports its own
    /// measured font-cell metrics (see <see cref="Browser.CanvasBridge.CellMetrics"/>) and as the
    /// fallback if that measurement is ever invalid. Not overridable via any ini file.
    /// </summary>
    public const double DefaultCellWidthPixels = 16;

    /// <summary>
    /// Default height (in pixels) of one world cell, used until the browser reports its own
    /// measured font-cell metrics (see <see cref="Browser.CanvasBridge.CellMetrics"/>) and as the
    /// fallback if that measurement is ever invalid. Not overridable via any ini file.
    /// </summary>
    public const double DefaultCellHeightPixels = 28;

    /// <summary>
    /// Default foreground color used by any renderer (<see cref="Rendering.WorldRenderer"/>,
    /// <see cref="Rendering.WorldSelectRenderer"/>, <see cref="Rendering.UIRenderer"/>) when
    /// nothing more specific is resolved (no palette match, or no explicit color set) - the single
    /// app-wide place this hex value is defined, so it isn't hardcoded independently in each
    /// renderer. Not overridable via any ini file.
    /// </summary>
    public const string DefaultForeColor = "#00ff00";

    /// <summary>
    /// Default background color used by any renderer when nothing more specific is resolved -
    /// null means no fill (fully transparent, letting the canvas show through). The single
    /// app-wide place this default is defined, so it isn't hardcoded independently in each
    /// renderer. Not overridable via any ini file.
    /// </summary>
    public const string? DefaultBackColor = null;
}
