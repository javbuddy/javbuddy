// Zoom and pan for a lightbox image. Everything runs client-side — wheel, drag and
// pinch fire far too often for a SignalR round-trip each — and .NET only hears when the image goes
// from fit-to-screen to zoomed and back (SetZoomed), so the host can disable swipe navigation.
//
// The image keeps its fit-to-screen layout box and is moved with a CSS transform
// (translate + scale about its centre), so the host's layout is untouched while zoomed.
const MAX_SCALE = 8;
const TAP_SLOP = 6;
const DOUBLE_TAP_MS = 300;
const DOUBLE_TAP_DISTANCE = 30;

const zoomers = new Map();

export function init(key, anchor, dotNetRef) {
    dispose(key);
    const container = anchor?.closest('[data-zoom-container]');
    if (!container) return;
    zoomers.set(key, createZoomer(container, dotNetRef));
}

export function zoomBy(key, factor) {
    zoomers.get(key)?.zoomBy(factor);
}

export function reset(key) {
    zoomers.get(key)?.reset();
}

export function dispose(key) {
    const zoomer = zoomers.get(key);
    if (!zoomer) return;
    zoomer.destroy();
    zoomers.delete(key);
}

function createZoomer(container, dotNetRef) {
    let scale = 1;
    let tx = 0;
    let ty = 0;
    let zoomed = false;
    const pointers = new Map();
    let gesture = null;
    let lastTap = null;
    let lastTouchTapTime = 0;

    const image = () => container.querySelector('[data-zoom-image]');
    const clamp = (value, min, max) => Math.min(max, Math.max(min, value));

    // The image's untransformed centre and size in container coordinates, plus the container size.
    function layout(img) {
        let x = 0;
        let y = 0;
        for (let el = img; el && el !== container; el = el.offsetParent) {
            x += el.offsetLeft;
            y += el.offsetTop;
        }
        return {
            cx: x + img.offsetWidth / 2,
            cy: y + img.offsetHeight / 2,
            w: img.offsetWidth,
            h: img.offsetHeight,
            vw: container.clientWidth,
            vh: container.clientHeight,
        };
    }

    function toContainer(clientX, clientY) {
        const rect = container.getBoundingClientRect();
        return { x: clientX - rect.left, y: clientY - rect.top };
    }

    // An image smaller than the container on an axis stays where the layout put it; a bigger one
    // can be panned only until its edge reaches the container's edge.
    function clampAxis(t, centre, size, view) {
        if (size <= view) return 0;
        return clamp(t, view - centre - size / 2, size / 2 - centre);
    }

    function apply(img, animate) {
        const l = layout(img);
        tx = clampAxis(tx, l.cx, l.w * scale, l.vw);
        ty = clampAxis(ty, l.cy, l.h * scale, l.vh);
        img.style.transition = animate ? 'transform 0.15s ease-out' : '';
        img.style.transform = scale > 1 ? `translate(${tx}px, ${ty}px) scale(${scale})` : '';
        img.classList.toggle('is-zoomed', scale > 1);

        if ((scale > 1) !== zoomed) {
            zoomed = scale > 1;
            dotNetRef.invokeMethodAsync('SetZoomed', zoomed).catch(() => { });
        }
    }

    // Scales to newScale keeping the image point under (px, py) — container coordinates — fixed.
    function zoomTo(newScale, px, py, animate) {
        const img = image();
        if (!img) return;
        const l = layout(img);
        newScale = clamp(newScale, 1, MAX_SCALE);
        const k = newScale / scale;
        tx = px - l.cx - k * (px - l.cx - tx);
        ty = py - l.cy - k * (py - l.cy - ty);
        scale = newScale;
        if (scale === 1) {
            tx = 0;
            ty = 0;
        }
        apply(img, animate);
    }

    // Double-click/tap: fit-to-screen ↔ 1:1 (the image's own pixels), or 2x when fitting already
    // shows it near full size.
    function toggleZoom(px, py) {
        const img = image();
        if (!img) return;
        if (scale > 1) {
            zoomTo(1, px, py, true);
            return;
        }
        const native = img.naturalWidth && img.offsetWidth ? img.naturalWidth / img.offsetWidth : 0;
        zoomTo(native >= 1.5 ? native : 2, px, py, true);
    }

    // A drag or pinch ends with a click on whatever is under the pointer; the lightbox backdrop
    // closes on click, so swallow that one click before Blazor's document-level handler sees it.
    function suppressNextClick() {
        const swallow = (e) => {
            e.stopPropagation();
            e.preventDefault();
            window.removeEventListener('click', swallow, true);
        };
        window.addEventListener('click', swallow, true);
        setTimeout(() => window.removeEventListener('click', swallow, true), 300);
    }

    function startGesture() {
        const points = [...pointers.values()];
        if (points.length === 1) {
            gesture = { kind: 'pan', startX: points[0].x, startY: points[0].y, startTx: tx, startTy: ty, moved: false, multi: gesture?.multi ?? false };
        } else if (points.length === 2) {
            const mid = toContainer((points[0].x + points[1].x) / 2, (points[0].y + points[1].y) / 2);
            gesture = {
                kind: 'pinch',
                startDistance: Math.hypot(points[1].x - points[0].x, points[1].y - points[0].y) || 1,
                startMid: mid,
                startScale: scale,
                startTx: tx,
                startTy: ty,
                moved: true,
                multi: true,
            };
        }
    }

    function onPointerDown(e) {
        const img = image();
        if (!img || (e.pointerType === 'mouse' && e.button !== 0)) return;
        const onImage = e.target === img;
        if (e.pointerType === 'mouse') {
            // Nothing to pan at fit-to-screen, so leave mouse clicks alone.
            if (!onImage || scale === 1) return;
        } else if (!onImage && pointers.size === 0) {
            // A touch has to start on the image; a second finger anywhere joins it as a pinch.
            return;
        }
        if (pointers.size >= 2) return;

        pointers.set(e.pointerId, { x: e.clientX, y: e.clientY, type: e.pointerType, onImage });
        try {
            img.setPointerCapture(e.pointerId);
        } catch {
            // The pointer may already be gone.
        }
        if (pointers.size === 1) gesture = null;
        startGesture();
        if (scale > 1) img.classList.add('is-panning');
    }

    function onPointerMove(e) {
        const pointer = pointers.get(e.pointerId);
        if (!pointer || !gesture) return;
        pointer.x = e.clientX;
        pointer.y = e.clientY;
        const img = image();
        if (!img) return;

        if (gesture.kind === 'pan') {
            const dx = e.clientX - gesture.startX;
            const dy = e.clientY - gesture.startY;
            if (Math.hypot(dx, dy) > TAP_SLOP) gesture.moved = true;
            if (scale === 1) return;
            tx = gesture.startTx + dx;
            ty = gesture.startTy + dy;
            apply(img, false);
        } else if (pointers.size === 2) {
            const [a, b] = [...pointers.values()];
            const distance = Math.hypot(b.x - a.x, b.y - a.y);
            const mid = toContainer((a.x + b.x) / 2, (a.y + b.y) / 2);
            // Zoom about the pinch's starting midpoint, then follow the midpoint as it moves.
            scale = gesture.startScale;
            tx = gesture.startTx;
            ty = gesture.startTy;
            zoomTo(gesture.startScale * distance / gesture.startDistance, gesture.startMid.x, gesture.startMid.y, false);
            if (scale > 1) {
                tx += mid.x - gesture.startMid.x;
                ty += mid.y - gesture.startMid.y;
                apply(img, false);
            }
        }
    }

    function onPointerUp(e) {
        const pointer = pointers.get(e.pointerId);
        if (!pointer) return;
        pointers.delete(e.pointerId);
        const ended = gesture;

        if (pointers.size > 0) {
            // One finger of a pinch lifted: carry on panning with the other.
            startGesture();
            return;
        }

        gesture = null;
        image()?.classList.remove('is-panning');
        if (!ended) return;
        if (ended.moved) {
            suppressNextClick();
            return;
        }

        if (e.type === 'pointerup' && pointer.type !== 'mouse' && pointer.onImage && !ended.multi) {
            lastTouchTapTime = Date.now();
            const now = e.timeStamp;
            if (lastTap && now - lastTap.time < DOUBLE_TAP_MS && Math.hypot(e.clientX - lastTap.x, e.clientY - lastTap.y) < DOUBLE_TAP_DISTANCE) {
                lastTap = null;
                const p = toContainer(e.clientX, e.clientY);
                toggleZoom(p.x, p.y);
            } else {
                lastTap = { time: now, x: e.clientX, y: e.clientY };
            }
        }
    }

    function onDoubleClick(e) {
        // Touch double-taps are handled in onPointerUp; some browsers also fire dblclick for them.
        if (e.target !== image() || Date.now() - lastTouchTapTime < 600) return;
        const p = toContainer(e.clientX, e.clientY);
        toggleZoom(p.x, p.y);
    }

    function onWheel(e) {
        e.preventDefault();
        // Trackpad pinches arrive as ctrl+wheel with much smaller deltas than a mouse wheel notch.
        const delta = e.deltaMode === 1 ? e.deltaY * 16 : e.deltaY;
        const factor = Math.exp(-delta * (e.ctrlKey ? 0.01 : 0.002));
        const p = toContainer(e.clientX, e.clientY);
        zoomTo(scale * factor, p.x, p.y, false);
    }

    function onResize() {
        const img = image();
        if (img && scale > 1) apply(img, false);
    }

    const onDragStart = (e) => e.preventDefault();

    container.addEventListener('pointerdown', onPointerDown);
    container.addEventListener('pointermove', onPointerMove);
    container.addEventListener('pointerup', onPointerUp);
    container.addEventListener('pointercancel', onPointerUp);
    container.addEventListener('dblclick', onDoubleClick);
    container.addEventListener('wheel', onWheel, { passive: false });
    container.addEventListener('dragstart', onDragStart);
    window.addEventListener('resize', onResize);

    return {
        zoomBy(factor) {
            zoomTo(scale * factor, container.clientWidth / 2, container.clientHeight / 2, true);
        },
        reset() {
            pointers.clear();
            gesture = null;
            lastTap = null;
            scale = 1;
            tx = 0;
            ty = 0;
            const img = image();
            if (img) {
                img.classList.remove('is-panning');
                apply(img, false);
            }
        },
        destroy() {
            container.removeEventListener('pointerdown', onPointerDown);
            container.removeEventListener('pointermove', onPointerMove);
            container.removeEventListener('pointerup', onPointerUp);
            container.removeEventListener('pointercancel', onPointerUp);
            container.removeEventListener('dblclick', onDoubleClick);
            container.removeEventListener('wheel', onWheel);
            container.removeEventListener('dragstart', onDragStart);
            window.removeEventListener('resize', onResize);
        },
    };
}
