// Minimal Canvas + keyboard interop for the ASCII game.
// This module intentionally contains no game logic: it only forwards keyboard events
// to C#, drives the requestAnimationFrame loop, and draws glyphs onto the canvas.

// Fallback glyph color applied when the canvas context is (re)configured, before the first real
// frame is drawn (every actual glyph draw sets its own color from the C#-supplied per-glyph data -
// see drawFrame - so this value is never otherwise visible). Interface colors themselves live in
// Assets/Global/InterfaceColors.ini.
const FALLBACK_FORE_COLOR = '#00ff00';

let ctx = null;
let font = null;
// Pre-rendered glyph images: color -> (character -> canvas). Rendering text is far more expensive
// than copying an image, and a frame repeats the same few character/color combinations thousands
// of times, so each combination is rendered once and then copied. Cleared whenever the font or
// cell size changes (see applyFontToContext).
const glyphCache = new Map();
let cacheCellWidth = 0;
let cacheCellHeight = 0;
let dotNetRef = null;
let rafHandle = null;
let lastTimestamp = null;
// Window listeners are stored (rather than passed as inline arrow functions) so dispose()
// can remove the exact same function instances that were added - removeEventListener is a no-op
// unless given a reference equal to the one originally passed to addEventListener.
let keydownHandler = null;
let keyupHandler = null;
let pointerdownHandler = null;
// Glyphs cached before the web font finished loading were rendered in a fallback font.
const fontsLoadedHandler = () => glyphCache.clear();

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
    pointerdownHandler = () => canvas.focus();
    window.addEventListener('pointerdown', pointerdownHandler);
    document.fonts.addEventListener('loadingdone', fontsLoadedHandler);

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
    font = ctx.font;
    glyphCache.clear();
}

// Returns the cached image of one character in one color, rendering it on first use.
function getGlyphImage(character, color, cellWidthPixels, cellHeightPixels) {
    if (cellWidthPixels !== cacheCellWidth || cellHeightPixels !== cacheCellHeight) {
        glyphCache.clear();
        cacheCellWidth = cellWidthPixels;
        cacheCellHeight = cellHeightPixels;
    }

    let images = glyphCache.get(color);
    if (!images) {
        images = new Map();
        glyphCache.set(color, images);
    }

    let image = images.get(character);
    if (!image) {
        image = document.createElement('canvas');
        image.width = Math.ceil(cellWidthPixels);
        image.height = Math.ceil(cellHeightPixels);
        const imageCtx = image.getContext('2d');
        imageCtx.font = font;
        imageCtx.textBaseline = 'top';
        imageCtx.fillStyle = color;
        imageCtx.fillText(character, 0, 0);
        images.set(character, image);
    }

    return image;
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

// Each glyph has a foreground color and an optional background color (null = none). A background
// is a plain cell-sized rectangle; a character is copied from its cached image (see getGlyphImage).
export function drawFrame(width, height, cellWidthPixels, cellHeightPixels, characters, xs, ys, foreColors, backColors) {
    if (!ctx) {
        return;
    }

    ctx.clearRect(0, 0, width, height);

    for (let i = 0; i < characters.length; i++) {
        const x = xs[i];
        const y = ys[i];

        const backColor = backColors[i];
        if (backColor) {
            ctx.fillStyle = backColor;
            ctx.fillRect(x, y, cellWidthPixels, cellHeightPixels);
        }

        const image = getGlyphImage(characters[i], foreColors[i], cellWidthPixels, cellHeightPixels);
        ctx.drawImage(image, x, y);
    }
}

export function dispose() {
    if (rafHandle !== null) {
        window.cancelAnimationFrame(rafHandle);
        rafHandle = null;
    }
    if (keydownHandler) {
        window.removeEventListener('keydown', keydownHandler);
        keydownHandler = null;
    }
    if (keyupHandler) {
        window.removeEventListener('keyup', keyupHandler);
        keyupHandler = null;
    }
    if (pointerdownHandler) {
        window.removeEventListener('pointerdown', pointerdownHandler);
        pointerdownHandler = null;
    }
    document.fonts.removeEventListener('loadingdone', fontsLoadedHandler);
    ctx = null;
    glyphCache.clear();
    dotNetRef = null;
}

