// Minimal Canvas + keyboard interop for the ASCII game.
// This module intentionally contains no game logic: it only forwards keyboard events
// to C#, drives the requestAnimationFrame loop, and draws glyphs onto the canvas.

let ctx = null;
let dotNetRef = null;
let rafHandle = null;
let lastTimestamp = null;
let keydownHandler = null;
let keyupHandler = null;

// Fixed on-screen cell size the bundled bitmap font is scaled to fit.
const TARGET_CELL_WIDTH_PX = 16;
const TARGET_CELL_HEIGHT_PX = 28;

// The bundled retro CP437 bitmap font (see wwwroot/fonts and its @font-face
// declaration in app.css/standalone.css).
const FONT_FAMILY = '"Web437IbmVga8x14", monospace';

let currentHorizontalScale = 1;

export async function initialize(canvasElementId, dotNetObjectRef) {
    const canvas = document.getElementById(canvasElementId);
    ctx = canvas.getContext('2d');
    ctx.textBaseline = 'top';
    ctx.fillStyle = '#00ff00';

    dotNetRef = dotNetObjectRef;

    keydownHandler = (e) => dotNetRef.invokeMethodAsync('OnKeyDown', e.code);
    keyupHandler = (e) => dotNetRef.invokeMethodAsync('OnKeyUp', e.code);
    window.addEventListener('keydown', keydownHandler);
    window.addEventListener('keyup', keyupHandler);

    // The keyboard listeners above are on window, which only receives key events
    // once the window/document has focus. The canvas is focusable (tabIndex = -1
    // keeps it out of normal tab order) and the pointerdown listener re-focuses it
    // whenever the player clicks anywhere on the page.
    canvas.tabIndex = -1;
    canvas.focus();
    window.addEventListener('pointerdown', () => canvas.focus());

    lastTimestamp = null;
    rafHandle = window.requestAnimationFrame(onAnimationFrame);

    // Report the measured cell size back to C# (camelCase property names match
    // System.Text.Json's default naming policy, mapping onto
    // CellMetrics.CellWidthPixels/CellHeightPixels).
    return await loadFont();
}

// Computes the font-size and horizontal scale needed to make the bundled bitmap
// font fill exactly TARGET_CELL_WIDTH_PX x TARGET_CELL_HEIGHT_PX pixel cells,
// and returns those fixed target dimensions.
async function loadFont() {
    // Ensure the font is loaded before measuring it, otherwise the measurement
    // could use a fallback system font with different metrics.
    await document.fonts.load(`16px ${FONT_FAMILY}`);

    // Probe the font's aspect ratio at a large size to minimize rounding error.
    const probeSizePx = 100;
    ctx.font = `${probeSizePx}px ${FONT_FAMILY}`;
    const probeMetrics = ctx.measureText('#');
    const probeAscent = probeMetrics.fontBoundingBoxAscent ?? probeMetrics.actualBoundingBoxAscent;
    const probeDescent = probeMetrics.fontBoundingBoxDescent ?? probeMetrics.actualBoundingBoxDescent;
    const probeHeight = probeAscent + probeDescent;

    // Scale the font-size so the measured cell height matches the fixed target,
    // then re-measure at that size to get the resulting cell width.
    const sizePx = TARGET_CELL_HEIGHT_PX * (probeSizePx / probeHeight);
    ctx.font = `${sizePx}px ${FONT_FAMILY}`;
    ctx.textBaseline = 'top';
    ctx.fillStyle = '#00ff00';

    const measuredCellWidth = ctx.measureText('#').width;

    // Guard against a zero/invalid measurement (would otherwise produce an
    // Infinity scale factor) by defaulting to a scale of 1.
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

    // Apply the horizontal scale computed in setFont() so glyphs fill
    // TARGET_CELL_WIDTH_PX cells. ctx.scale affects x coordinates too, so each x
    // position is counter-scaled to keep glyph positions correct on screen.
    ctx.save();
    ctx.scale(currentHorizontalScale, 1);
    for (let i = 0; i < characters.length; i++) {
        const x = xs[i] / currentHorizontalScale;
        const y = ys[i];

        // Background fill (if any) is drawn as a rect sized to the fixed target
        // cell dimensions, behind the glyph.
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
