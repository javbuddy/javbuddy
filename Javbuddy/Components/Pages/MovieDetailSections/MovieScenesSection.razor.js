import { formatTime, placePreview, trickplayThumbnails } from '../../Shared/ScrubBar.razor.js';

// Pages the highlights shelf by most of its visible width.
export function scrollShelf(shelf, direction) {
    shelf?.scrollBy({ left: direction * shelf.clientWidth * 0.8, behavior: "smooth" });
}

// How long the pointer rests before the timeline preview starts playing, and how long each
// trickplay frame then shows.
const PLAY_AFTER_MS = 300;
const FRAME_MS = 500;

const timelines = new WeakMap();

// Hovering the scenes timeline previews the movie at that moment: the bar previews the time under
// the pointer; a highlight pin scrubs across the whole highlight. Once the pointer rests, the preview
// plays on through the trickplay frames to the end of the hovered scene or highlight (the movie's end
// between scenes) and loops. It runs in the browser for the same reason ScrubBar does. `trickplay` is
// the movie's TrickplayLayout, or null (then only the time and name show); `highlightTrickplay` maps a
// highlight's id to its own denser set. Clicking the bar outside any scene asks `dotNetRef` to play
// from there.
export function initTimeline(root, trickplay, highlightTrickplay, durationSeconds, dotNetRef) {
    disposeTimeline(root);
    if (!root || !(durationSeconds > 0)) return;

    const bar = root.querySelector('.movie-scenes-timeline-bar');
    const preview = root.querySelector('.movie-scenes-timeline-preview');
    const thumb = root.querySelector('.movie-scenes-timeline-thumb');
    const label = root.querySelector('.movie-scenes-timeline-time');
    if (!bar || !preview || !label) return;

    // The set being previewed and the function showing its thumbnails. Switching between the
    // movie's set and a highlight's re-sizes the thumb for the other layout.
    let layout = null;
    let showThumbnail = null;
    const useLayout = (next) => {
        if (!thumb) return;
        next = next?.tileUrlTemplate ? next : null;
        if (next === layout && showThumbnail) return;
        layout = next;
        showThumbnail = layout ? trickplayThumbnails(thumb, layout) : null;
        // Without a set the preview is just the time and name, not the last set's frame.
        thumb.hidden = !layout;
    };

    const secondsAt = (clientX) => {
        const rect = bar.getBoundingClientRect();
        const fraction = rect.width > 0 ? (clientX - rect.left) / rect.width : 0;
        return Math.min(Math.max(fraction, 0), 1) * durationSeconds;
    };

    const clientXAt = (seconds) => {
        const rect = bar.getBoundingClientRect();
        return rect.left + (seconds / durationSeconds) * rect.width;
    };

    // Segments' data-* attributes are read on each hover, so edits show without re-initialising.
    const sceneAt = (seconds) => {
        for (const segment of bar.querySelectorAll('.movie-scenes-timeline-segment[data-start]')) {
            if (seconds >= Number(segment.dataset.start) && seconds < Number(segment.dataset.end)) return segment;
        }
        return null;
    };

    // What the pointer is over: a highlight pin or a moment on the bar; null anywhere else.
    const hoverAt = (event) => {
        if (!(event.target instanceof Element)) return null;
        // An apex marker shows its own clip in a popover instead.
        if (event.target.closest('.movie-scenes-timeline-apex')) return null;
        const pin = event.target.closest('.movie-scenes-timeline-pin[data-start]');
        if (pin) {
            const from = Number(pin.dataset.start);
            const end = Number(pin.dataset.end);
            const rect = pin.getBoundingClientRect();
            const fraction = rect.width > 0 ? Math.min(Math.max((event.clientX - rect.left) / rect.width, 0), 1) : 0;
            // The preview stays over the highlight's start on the bar while the pin is scrubbed.
            return {
                start: from + fraction * (end - from), end, name: pin.dataset.highlightName, clientX: clientXAt(from),
                layout: highlightTrickplay?.[pin.dataset.highlightId] ?? trickplay,
            };
        }
        if (!bar.contains(event.target)) return null;
        const start = secondsAt(event.clientX);
        const scene = sceneAt(start);
        return { start, end: scene ? Number(scene.dataset.end) : durationSeconds, name: scene?.dataset.sceneName, clientX: event.clientX, layout: trickplay };
    };

    let hovered = null;
    let waitTimer = 0;
    let frameTimer = 0;

    const stop = () => {
        clearTimeout(waitTimer);
        clearInterval(frameTimer);
        waitTimer = frameTimer = 0;
    };

    const paint = (seconds) => {
        showThumbnail?.(seconds);
        label.textContent = hovered.name ? `${formatTime(seconds)} · ${hovered.name}` : formatTime(seconds);
    };

    const play = () => {
        const frameSeconds = layout.intervalMs / 1000;
        let seconds = hovered.start;
        frameTimer = setInterval(() => {
            // Removed from the page (e.g. the scenes were all deleted) without a pointerleave.
            if (!root.isConnected) return stop();
            seconds += frameSeconds;
            if (seconds >= hovered.end) seconds = hovered.start;
            paint(seconds);
        }, FRAME_MS);
    };

    const hide = () => {
        stop();
        hovered = null;
        preview.classList.remove('movie-scenes-timeline-preview-visible');
    };

    const onMove = (event) => {
        const hover = hoverAt(event);
        if (!hover) return hide();
        // A move that lands on the same moment (e.g. vertically) keeps the preview playing.
        if (hovered && hovered.start === hover.start && hovered.end === hover.end) return;
        stop();
        hovered = hover;
        useLayout(hover.layout);
        paint(hover.start);
        placePreview(preview, bar, hover.clientX);
        preview.classList.add('movie-scenes-timeline-preview-visible');
        if (layout && hover.end - hover.start > layout.intervalMs / 1000) waitTimer = setTimeout(play, PLAY_AFTER_MS);
    };

    // Only the movie's sheets: a highlight's set is a sheet or two, fetched when its pin is hovered.
    const preloadSheets = () => {
        useLayout(trickplay);
        showThumbnail?.preload();
    };

    // Segments, pins and apexes are buttons with their own click; only the bare bar plays from here.
    const onBarClick = (event) => {
        if (event.target !== bar) return;
        dotNetRef?.invokeMethodAsync('PlayFromTimelineAsync', secondsAt(event.clientX));
    };

    root.addEventListener('pointerenter', preloadSheets);
    root.addEventListener('pointermove', onMove);
    root.addEventListener('pointerleave', hide);
    // Clicking opens the clip player over the timeline.
    root.addEventListener('click', hide);
    bar.addEventListener('click', onBarClick);

    timelines.set(root, () => {
        hide();
        root.removeEventListener('pointerenter', preloadSheets);
        root.removeEventListener('pointermove', onMove);
        root.removeEventListener('pointerleave', hide);
        root.removeEventListener('click', hide);
        bar.removeEventListener('click', onBarClick);
    });
}

export function disposeTimeline(root) {
    const teardown = root && timelines.get(root);
    if (teardown) {
        teardown();
        timelines.delete(root);
    }
}
