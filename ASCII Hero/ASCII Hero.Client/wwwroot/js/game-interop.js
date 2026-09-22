// Minimal Canvas + keyboard interop for the ASCII game.
// This module intentionally contains no game logic: it only forwards keyboard events
// to C#, drives the requestAnimationFrame loop, and draws glyphs onto the canvas.

// Fallback glyph color applied when the canvas context is (re)configured, before the first real
// frame is drawn (every actual glyph draw sets its own color from the C#-supplied per-glyph data -
// see drawFrame - so this value is never otherwise visible).
const FALLBACK_FORE_COLOR = '#00ff00';

let ctx = null;
let dotNetRef = null;
let rafHandle = null;
let lastTimestamp = null;
// Keydown/keyup listeners are stored (rather than passed as inline arrow functions) so dispose()
// can remove the exact same function instances that were added - removeEventListener is a no-op
// unless given a reference equal to the one originally passed to addEventListener.
let keydownHandler = null;
let keyupHandler = null;

export async function initialize(canvasElementId, dotNetObjectRef, fontFamily, viewportColumns, viewportRows, fontWidthPixels, fontHeightPixels) {
    const canvas = document.getElementById(canvasElementId);
    ctx = canvas.getContext('2d');

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

    // fontWidthPixels/fontHeightPixels (Global/Settings.ini's [Render] FontWidthPixels/
    // FontHeightPixels) are already the final on-screen cell size (see RenderConstants.cs's own
    // doc comment), so they're used directly to resize the canvas element to fit the viewport
    // (columns/rows times that cell size), and returned as-is to C# (camelCase property names
    // match System.Text.Json's default naming policy, mapping onto
    // CellMetrics.CellWidthPixels/CellHeightPixels).
    //
    // Resizing a canvas element resets its 2D context state (font/fillStyle/
    // textBaseline all revert to defaults), so the canvas must be resized
    // *before* the font is applied to the context - otherwise the canvas ends
    // up drawing with the tiny default font instead.
    canvas.width = fontWidthPixels * viewportColumns;
    canvas.height = fontHeightPixels * viewportRows;
    applyFontToContext(fontFamily, fontHeightPixels);
    return { cellWidthPixels: fontWidthPixels, cellHeightPixels: fontHeightPixels };
}

// Applies the configured font (and the fillStyle/textBaseline drawing glyphs rely on) to the
// canvas context. Must run *after* the canvas element's width/height are set, since resizing a
// canvas resets its 2D context state.
function applyFontToContext(fontFamily, cellHeightPixels) {
    // A font-size in px directly corresponds to one cell's height in px, so the configured cell
    // height is used as the font-size verbatim - no further scaling/derivation needed.
    ctx.font = `${cellHeightPixels}px ${fontFamily}`;
    ctx.textBaseline = 'top';
    ctx.fillStyle = FALLBACK_FORE_COLOR;
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

export function drawFrame(width, height, cellWidthPixels, cellHeightPixels, characters, xs, ys, foreColors, backColors) {
    if (!ctx) {
        return;
    }

    ctx.clearRect(0, 0, width, height);

    for (let i = 0; i < characters.length; i++) {
        const x = xs[i];
        const y = ys[i];

        // Background fill (if any) is drawn as a rect sized to one cell, behind
        // the glyph.
        if (backColors[i]) {
            ctx.fillStyle = backColors[i];
            ctx.fillRect(x, y, cellWidthPixels, cellHeightPixels);
        }

        ctx.fillStyle = foreColors[i];
        ctx.fillText(characters[i], x, y);
    }
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

