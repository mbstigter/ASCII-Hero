// Minimal Canvas + keyboard interop for the ASCII game.
// This module intentionally contains no game logic: it only forwards keyboard events
// to C#, drives the requestAnimationFrame loop, and draws glyphs onto the canvas.

let ctx = null;
let dotNetRef = null;
let rafHandle = null;
let lastTimestamp = null;
let keydownHandler = null;
let keyupHandler = null;

// Fixed on-screen cell size that BOTH rendering fonts are made to match, so the
// world grid (column/row count) is identical no matter which font is active. This
// is exactly 2x the bundled bitmap font's native 8x14 pixel glyph size, which keeps
// that font pixel-perfect (see the horizontal-scale note below for how the
// "Modern" font is forced to the same cell size even though it has no native
// pixel grid of its own).
const TARGET_CELL_WIDTH_PX = 16;
const TARGET_CELL_HEIGHT_PX = 28;

// Two selectable rendering fonts (see the Authentic/Modern toggle in Home.razor).
// Neither preset hardcodes a font-size anymore: setFont() below computes, for
// whichever font is active, the exact font-size that makes its real (measured)
// cell height equal TARGET_CELL_HEIGHT_PX, then computes a small horizontal
// scale factor so its real cell width also equals TARGET_CELL_WIDTH_PX. This
// means both fonts always render into identical 16x28 pixel cells:
//  - "authentic": the bundled CP437 bitmap font (Web437 IBM VGA 8x14). Its
//    natural aspect ratio already matches 16x28 almost exactly, so the computed
//    horizontal scale ends up very close to 1.
//  - "modern": a conventional anti-aliased coding font (JetBrains Mono, loaded
//    via Google Fonts in App.razor). Its natural aspect ratio differs from the
//    bitmap font, so the horizontal scale correction does more work here.
const FONT_PRESETS = {
    authentic: { family: '"Web437IbmVga8x14", monospace' },
    modern: { family: '"JetBrains Mono", monospace' },
};

let currentFontMode = 'authentic';
let currentHorizontalScale = 1;

export async function initialize(canvasElementId, dotNetObjectRef, fontMode) {
    const canvas = document.getElementById(canvasElementId);
    ctx = canvas.getContext('2d');
    ctx.textBaseline = 'top';
    ctx.fillStyle = '#00ff00';

    dotNetRef = dotNetObjectRef;

    keydownHandler = (e) => dotNetRef.invokeMethodAsync('OnKeyDown', e.code);
    keyupHandler = (e) => dotNetRef.invokeMethodAsync('OnKeyUp', e.code);
    window.addEventListener('keydown', keydownHandler);
    window.addEventListener('keyup', keyupHandler);

    lastTimestamp = null;
    rafHandle = window.requestAnimationFrame(onAnimationFrame);

    // Report the measured cell size back to C# so AsciiRenderer/GameLoop can use
    // real, font-accurate dimensions instead of a hardcoded guess. Property names
    // are camelCase to match System.Text.Json's default JS interop naming policy,
    // which maps them onto CellMetrics.CellWidthPixels/CellHeightPixels in C#.
    return await setFont(fontMode);
}

// Switches the active rendering font, computes the font-size and horizontal scale
// needed to make it fill exactly TARGET_CELL_WIDTH_PX x TARGET_CELL_HEIGHT_PX
// pixel cells, and returns those fixed target dimensions. Called both from
// initialize() and whenever the user flips the Authentic/Modern toggle in
// Home.razor.
export async function setFont(fontMode) {
    const preset = FONT_PRESETS[fontMode] ?? FONT_PRESETS.authentic;
    currentFontMode = FONT_PRESETS[fontMode] ? fontMode : 'authentic';

    // Ensure the font is actually loaded before measuring it; any size triggers
    // the load/parse of the whole font file. Without this, the very first
    // measurement could silently use a fallback system font instead (different
    // metrics), which would throw off every calculation below.
    await document.fonts.load(`16px ${preset.family}`);

    // Probe the font's real aspect ratio at a large reference size, so rounding
    // error from measuring at small pixel sizes doesn't skew the result.
    const probeSizePx = 100;
    ctx.font = `${probeSizePx}px ${preset.family}`;
    const probeMetrics = ctx.measureText('#');
    const probeAscent = probeMetrics.fontBoundingBoxAscent ?? probeMetrics.actualBoundingBoxAscent;
    const probeDescent = probeMetrics.fontBoundingBoxDescent ?? probeMetrics.actualBoundingBoxDescent;
    const probeHeight = probeAscent + probeDescent;

    // Scale the font-size so this font's real cell height lands exactly on our
    // fixed target, then re-measure at that final size to get the real cell
    // width, and compute the horizontal stretch/squeeze factor (applied at draw
    // time, see drawFrame) needed to also hit the fixed target width.
    const sizePx = TARGET_CELL_HEIGHT_PX * (probeSizePx / probeHeight);
    ctx.font = `${sizePx}px ${preset.family}`;
    ctx.textBaseline = 'top';
    ctx.fillStyle = '#00ff00';

    const measuredCellWidth = ctx.measureText('#').width;

    // Guard against a zero (or otherwise invalid) measurement, which can happen if the
    // font hasn't actually finished loading/laying out yet (e.g. right after a page
    // layout change). Falling through with measuredCellWidth === 0 would produce an
    // Infinity scale factor that eventually crashes the .NET side (OverflowException
    // when casting Infinity/NaN to int in AsciiRenderer). Default to a scale of 1 instead.
    currentHorizontalScale = measuredCellWidth > 0
        ? TARGET_CELL_WIDTH_PX / measuredCellWidth
        : 1;

    return { cellWidthPixels: TARGET_CELL_WIDTH_PX, cellHeightPixels: TARGET_CELL_HEIGHT_PX };
}

function onAnimationFrame(timestamp) {
    if (lastTimestamp === null) {
        lastTimestamp = timestamp;
    }
    const deltaSeconds = (timestamp - lastTimestamp) / 1000;
    lastTimestamp = timestamp;

    dotNetRef.invokeMethodAsync('OnFrame', deltaSeconds);

    rafHandle = window.requestAnimationFrame(onAnimationFrame);
}

export function drawFrame(width, height, characters, xs, ys, foreColors, backColors) {
    if (!ctx) {
        return;
    }

    ctx.clearRect(0, 0, width, height);

    // Apply the horizontal scale correction computed in setFont() so this font's
    // glyphs land exactly within TARGET_CELL_WIDTH_PX, regardless of its natural
    // aspect ratio. ctx.scale affects all subsequent drawing (including x
    // coordinates), so we counter-scale each x position by the inverse factor to
    // keep every glyph's cell position correct on screen.
    ctx.save();
    ctx.scale(currentHorizontalScale, 1);
    for (let i = 0; i < characters.length; i++) {
        const x = xs[i] / currentHorizontalScale;
        const y = ys[i];

        // Background fill (if any) is drawn as a plain rect behind the glyph, sized
        // to the fixed target cell dimensions so it lines up with the glyph grid
        // regardless of the active font's own natural metrics.
        if (backColors[i]) {
            ctx.fillStyle = backColors[i];
            ctx.fillRect(x, y, TARGET_CELL_WIDTH_PX / currentHorizontalScale, TARGET_CELL_HEIGHT_PX);
        }

        ctx.fillStyle = foreColors[i];
        ctx.fillText(characters[i], x, y);
    }
    ctx.restore();
}

export function dispose() {
    if (rafHandle !== null) {
        window.cancelAnimationFrame(rafHandle);
        rafHandle = null;
    }
    if (keydownHandler) {
        window.removeEventListener('keydown', keydownHandler);
    }
    if (keyupHandler) {
        window.removeEventListener('keyup', keyupHandler);
    }
    ctx = null;
    dotNetRef = null;
}
