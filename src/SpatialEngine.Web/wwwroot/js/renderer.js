// ZeroQuad renderer.
//
// JavaScript owns the animation loop; .NET owns all of the drawing. Each frame .NET hands over a memory
// view onto the WebAssembly heap and this module performs exactly one putImageData for the agent layer and,
// only when the tree topology actually changed, one Path2D stroke for the wireframe overlay.

let agentsCanvas = null;
let agentsCtx = null;
let overlayCanvas = null;
let overlayCtx = null;
let imageData = null;
let scratch = null;
let rectScratch = null;
let wireframePath = null;
let rafHandle = 0;
let lastTimestamp = 0;
let onFrameCallback = null;
let onPointerMoveCallback = null;
let onPointerActiveCallback = null;
let onResizeCallback = null;

// Pointer state lives here so the overlay can draw the search circle without a round trip to .NET.
const pointer = { x: 0, y: 0, radius: 90, active: false, dragging: false };

// MemoryView exposes copyTo(targetTypedArray); older shapes only offer slice(). Probed once.
let useCopyTo = true;

function readView(view, expectedLength) {
    if (!scratch || scratch.length < expectedLength) {
        scratch = new Uint8Array(expectedLength);
    }

    if (useCopyTo) {
        try {
            view.copyTo(scratch.subarray(0, expectedLength));
            return scratch;
        } catch {
            useCopyTo = false;
        }
    }

    const copy = view.slice();
    scratch.set(copy.subarray(0, expectedLength));
    return scratch;
}

export function initialize(width, height, onPointerMove, onPointerActive, onResize) {
    agentsCanvas = document.getElementById('agents');
    overlayCanvas = document.getElementById('overlay');
    if (!agentsCanvas || !overlayCanvas) {
        throw new Error('ZeroQuad: canvas elements #agents and #overlay were not found.');
    }

    onPointerMoveCallback = onPointerMove;
    onPointerActiveCallback = onPointerActive;
    onResizeCallback = onResize;

    agentsCanvas.width = width;
    agentsCanvas.height = height;
    overlayCanvas.width = width;
    overlayCanvas.height = height;

    // willReadFrequently is wrong here: we only ever write.
    agentsCtx = agentsCanvas.getContext('2d', { alpha: false });
    overlayCtx = overlayCanvas.getContext('2d');
    imageData = agentsCtx.createImageData(width, height);

    attachPointerHandlers();
    attachResizeObserver();
}

function attachPointerHandlers() {
    const toCanvas = (event) => {
        const rect = overlayCanvas.getBoundingClientRect();
        return {
            x: (event.clientX - rect.left) * (overlayCanvas.width / rect.width),
            y: (event.clientY - rect.top) * (overlayCanvas.height / rect.height),
        };
    };

    const push = () => {
        onPointerMoveCallback?.(pointer.x, pointer.y, pointer.radius);
        onPointerActiveCallback?.(pointer.active);
    };

    overlayCanvas.addEventListener('pointerdown', (event) => {
        const p = toCanvas(event);
        pointer.x = p.x;
        pointer.y = p.y;
        pointer.active = true;
        pointer.dragging = true;
        overlayCanvas.setPointerCapture(event.pointerId);
        push();
    });

    overlayCanvas.addEventListener('pointermove', (event) => {
        if (!pointer.dragging) {
            return;
        }

        const p = toCanvas(event);
        pointer.x = p.x;
        pointer.y = p.y;
        push();
    });

    const release = (event) => {
        pointer.dragging = false;
        if (event.pointerId !== undefined && overlayCanvas.hasPointerCapture?.(event.pointerId)) {
            overlayCanvas.releasePointerCapture(event.pointerId);
        }
    };

    overlayCanvas.addEventListener('pointerup', release);
    overlayCanvas.addEventListener('pointercancel', release);

    // The wheel resizes the search radius, which is the most natural way to explore query cost.
    overlayCanvas.addEventListener('wheel', (event) => {
        event.preventDefault();
        const factor = event.deltaY < 0 ? 1.1 : 1 / 1.1;
        pointer.radius = Math.max(4, Math.min(overlayCanvas.width, pointer.radius * factor));
        push();
    }, { passive: false });

    overlayCanvas.addEventListener('dblclick', () => {
        pointer.active = false;
        push();
    });
}

function attachResizeObserver() {
    if (typeof ResizeObserver === 'undefined') {
        return;
    }

    const observer = new ResizeObserver((entries) => {
        for (const entry of entries) {
            // Keep the backing store square so the world's aspect ratio is preserved.
            const size = Math.max(256, Math.floor(Math.min(entry.contentRect.width, entry.contentRect.height)));
            if (size === agentsCanvas.width) {
                continue;
            }

            agentsCanvas.width = size;
            agentsCanvas.height = size;
            overlayCanvas.width = size;
            overlayCanvas.height = size;
            imageData = agentsCtx.createImageData(size, size);
            wireframePath = null;
            onResizeCallback?.(size, size);
        }
    });

    observer.observe(agentsCanvas.parentElement ?? agentsCanvas);
}

export function start(onFrame) {
    onFrameCallback = onFrame;
    lastTimestamp = 0;

    const loop = (timestamp) => {
        const delta = lastTimestamp === 0 ? 16.6667 : timestamp - lastTimestamp;
        lastTimestamp = timestamp;
        onFrameCallback?.(delta);
        rafHandle = requestAnimationFrame(loop);
    };

    rafHandle = requestAnimationFrame(loop);
}

export function stop() {
    if (rafHandle) {
        cancelAnimationFrame(rafHandle);
        rafHandle = 0;
    }
}

export function renderFrame(rgba, rects, rectCount, wireframeChanged, showGrid, stats) {
    if (!agentsCtx || !imageData) {
        return;
    }

    // Agent layer: one bulk copy from the WASM heap, then one putImageData, regardless of agent count.
    const expected = imageData.data.length;
    const bytes = readView(rgba, expected);
    imageData.data.set(bytes.subarray(0, expected));
    agentsCtx.putImageData(imageData, 0, 0);

    // Overlay: wireframe plus the interactive search circle.
    overlayCtx.clearRect(0, 0, overlayCanvas.width, overlayCanvas.height);

    if (showGrid && rectCount > 0) {
        if (wireframeChanged || !wireframePath) {
            wireframePath = buildWireframePath(rects, rectCount);
        }

        overlayCtx.strokeStyle = 'rgba(120, 220, 170, 0.28)';
        overlayCtx.lineWidth = 1;
        overlayCtx.stroke(wireframePath);
    } else if (!showGrid) {
        wireframePath = null;
    }

    if (pointer.active) {
        overlayCtx.beginPath();
        overlayCtx.arc(pointer.x, pointer.y, pointer.radius, 0, Math.PI * 2);
        overlayCtx.strokeStyle = 'rgba(255, 196, 64, 0.95)';
        overlayCtx.lineWidth = 2;
        overlayCtx.stroke();
    }

    publishStats(stats);
}

function buildWireframePath(rects, rectCount) {
    const floatCount = rectCount * 4;
    const byteLength = floatCount * 4;

    if (!rectScratch || rectScratch.byteLength < byteLength) {
        rectScratch = new ArrayBuffer(byteLength);
    }

    const bytes = new Uint8Array(rectScratch, 0, byteLength);
    let source;
    if (useCopyTo) {
        try {
            rects.copyTo(bytes);
            source = bytes;
        } catch {
            useCopyTo = false;
        }
    }

    if (!source) {
        source = rects.slice().subarray(0, byteLength);
        bytes.set(source);
    }

    const quads = new Float32Array(rectScratch, 0, floatCount);
    const path = new Path2D();
    for (let i = 0; i < floatCount; i += 4) {
        path.rect(quads[i], quads[i + 1], quads[i + 2], quads[i + 3]);
    }

    return path;
}

// Telemetry is written into a DOM node directly rather than through Blazor, so a 60 FPS frame never
// triggers a component re-render.
let statsScratch = null;
let lastStatsUpdate = 0;

function publishStats(stats) {
    const now = performance.now();
    if (now - lastStatsUpdate < 250) {
        return;
    }

    lastStatsUpdate = now;

    const byteLength = 12 * 4;
    if (!statsScratch) {
        statsScratch = new ArrayBuffer(byteLength);
    }

    const bytes = new Uint8Array(statsScratch);
    if (useCopyTo) {
        try {
            stats.copyTo(bytes);
        } catch {
            useCopyTo = false;
            bytes.set(stats.slice().subarray(0, byteLength));
        }
    } else {
        bytes.set(stats.slice().subarray(0, byteLength));
    }

    const f = new Float32Array(statsScratch);
    setText('stat-fps', f[0].toFixed(0));
    setText('stat-move', formatMicros(f[1]));
    setText('stat-update', formatMicros(f[2]));
    setText('stat-query', formatMicros(f[3]));
    setText('stat-tick', formatMicros(f[4]));
    setText('stat-agents', f[5].toLocaleString());
    setText('stat-nodes', f[6].toLocaleString());
    setText('stat-depth', f[7].toFixed(0));
    setText('stat-hits', f[8].toLocaleString());
    setText('stat-dirty', `${(f[9] * 100).toFixed(1)} %`);
}

function formatMicros(value) {
    if (value >= 1000) {
        return `${(value / 1000).toFixed(2)} ms`;
    }

    return `${value.toFixed(1)} \u00B5s`;
}

function setText(id, text) {
    const element = document.getElementById(id);
    if (element && element.textContent !== text) {
        element.textContent = text;
    }
}
