// Player access for ClipEditor and the scene timeline: reading the video's
// current time for "Set from player", seeking to a scene, playing just a highlight's clip
//, and the editor's playhead shortcuts (Review mode's scene keys, extended to
// highlights and apexes in). Also the video's own length, for the player's scrub bar
// when the movie has no stored duration.

export function currentTime(selector) {
    const video = document.querySelector(selector);
    if (!video || !(video.readyState > 0)) return null;
    return video.currentTime;
}

export function duration(selector) {
    const video = document.querySelector(selector);
    return video && Number.isFinite(video.duration) && video.duration > 0 ? video.duration : null;
}

export function seek(selector, seconds) {
    const video = document.querySelector(selector);
    if (!video) return false;
    video.currentTime = seconds;
    if (video.paused) {
        video.play().catch(() => { });
    }
    return true;
}

// Plays [start, end) once and pauses at the end. Seeking outside the clip (scrub bar, a scene, the
// native controls) or starting another clip cancels the pause, so it never fires unexpectedly later.
// `timeupdate` fires a few times a second, so the pause can land a fraction of a second past `end`.
const activeClips = new WeakMap();

export function playClip(selector, start, end) {
    const video = document.querySelector(selector);
    if (!video) return false;
    activeClips.get(video)?.();

    const stop = () => {
        video.removeEventListener('timeupdate', onTimeUpdate);
        video.removeEventListener('seeking', onSeeking);
        activeClips.delete(video);
    };
    const onTimeUpdate = () => {
        if (video.currentTime >= end) {
            stop();
            video.pause();
        }
    };
    const onSeeking = () => {
        if (video.currentTime < start - 0.05 || video.currentTime >= end) stop();
    };
    video.addEventListener('timeupdate', onTimeUpdate);
    video.addEventListener('seeking', onSeeking);
    activeClips.set(video, stop);

    video.currentTime = start;
    if (video.paused) {
        video.play().catch(() => { });
    }
    return true;
}

// Playhead shortcuts for the scene, highlight and apex editors: each editor registers
// its own `bindings`, mapping a key ("m", or "shift+m" with Shift) to its [JSInvokable] method that
// takes the current time — m / Shift+M for scenes, h / Shift+H for highlights, a for apexes. Guarded
// like MovieCleanup.razor.js's Review shortcuts: ignored while typing or with a modal/lightbox open.
const shortcutHandlers = new Map();

function isEditable(element) {
    return element instanceof Element
        && (element.closest('input, textarea, select') !== null || element.isContentEditable);
}

export function initShortcuts(key, dotNetRef, selector, bindings) {
    disposeShortcuts(key);
    const onKeyDown = (event) => {
        if (event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) return;
        const method = event.key.length === 1 ? bindings[(event.shiftKey ? 'shift+' : '') + event.key.toLowerCase()] : undefined;
        if (!method) return;
        if (document.querySelector('.image-lightbox-backdrop, .cleanup-modal-overlay, .delete-confirm-backdrop')) return;
        if (isEditable(event.target) || isEditable(document.activeElement)) return;

        const time = currentTime(selector);
        if (time === null) return;

        event.preventDefault();
        if (event.repeat) return;
        dotNetRef.invokeMethodAsync(method, time).catch(() => { });
    };
    window.addEventListener('keydown', onKeyDown, true);
    shortcutHandlers.set(key, onKeyDown);
}

export function disposeShortcuts(key) {
    const handler = shortcutHandlers.get(key);
    if (!handler) return;
    window.removeEventListener('keydown', handler, true);
    shortcutHandlers.delete(key);
}

// The player's side panel pins the editor's top block (header, add buttons, open add forms) while the
// clip list scrolls. Taller than the panel, a pinned block would hide its own bottom (an
// add form's Save), so it gets .clip-editor-top-unpinned and scrolls with the list until it fits again.
// Only where it's sticky: Review mode's card never pins it.
const topFitObservers = new WeakMap();

export function watchTopFit(top) {
    unwatchTopFit(top);
    if (!top || !('ResizeObserver' in window) || getComputedStyle(top).position !== 'sticky') return;
    const scroller = scrollParent(top);
    if (!scroller) return;

    const check = () => {
        const tooTall = top.offsetHeight > scroller.clientHeight;
        if (tooTall === top.classList.contains('clip-editor-top-unpinned')) return;
        top.classList.toggle('clip-editor-top-unpinned', tooTall);
        // Unpinned, it falls back to the top of the list: bring it into view rather than leave the form off-screen.
        if (tooTall) scroller.scrollTop += top.getBoundingClientRect().top - scroller.getBoundingClientRect().top;
    };
    const observer = new ResizeObserver(check);
    observer.observe(top);
    observer.observe(scroller);
    topFitObservers.set(top, observer);
}

export function unwatchTopFit(top) {
    topFitObservers.get(top)?.disconnect();
    topFitObservers.delete(top);
}

function scrollParent(element) {
    for (let parent = element.parentElement; parent; parent = parent.parentElement) {
        if (/^(auto|scroll)$/.test(getComputedStyle(parent).overflowY)) return parent;
    }
    return null;
}
