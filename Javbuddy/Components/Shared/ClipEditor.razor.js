import { isShortcutBlocked } from './ScrubBar.razor.js';

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

// Shows the video's index-th subtitle track and hides the rest; null hides them all, clearing any
// cue still on screen (Blink/WebKit can leave the last one frozen over the next video).
export function setSubtitleTrack(selector, index) {
    const video = document.querySelector(selector);
    if (!video) return;
    Array.from(video.textTracks).forEach((track, i) => {
        if (i === index) {
            track.mode = "showing";
            return;
        }
        // A disabled track exposes no cues, so remove them while it is merely hidden.
        if (track.mode === "showing") {
            track.mode = "hidden";
            Array.from(track.activeCues ?? []).forEach(cue => track.removeCue(cue));
        }
        track.mode = "disabled";
    });
}

const SUBTITLE_PREFS_KEY = "javbuddy-subtitles";
const SUBTITLE_DEFAULTS = { enabled: true, size: "medium", background: "transparent" };
const SUBTITLE_SIZES = { small: "75%", medium: "100%", large: "150%", xlarge: "200%" };
const SUBTITLE_BACKGROUNDS = { transparent: "transparent", translucent: "rgba(0, 0, 0, 0.55)", solid: "#000" };

// The remembered subtitle preferences; localStorage may be unavailable (private mode, blocked).
export function subtitlePrefs() {
    try {
        const saved = JSON.parse(localStorage.getItem(SUBTITLE_PREFS_KEY) ?? "{}");
        return {
            enabled: typeof saved.enabled === "boolean" ? saved.enabled : SUBTITLE_DEFAULTS.enabled,
            size: Object.hasOwn(SUBTITLE_SIZES, saved.size) ? saved.size : SUBTITLE_DEFAULTS.size,
            background: Object.hasOwn(SUBTITLE_BACKGROUNDS, saved.background) ? saved.background : SUBTITLE_DEFAULTS.background,
        };
    } catch {
        return { ...SUBTITLE_DEFAULTS };
    }
}

export function saveSubtitlePrefs(prefs) {
    try {
        localStorage.setItem(SUBTITLE_PREFS_KEY, JSON.stringify(prefs));
    } catch {
        // Not remembered, but the choice still applies to this player.
    }
}

// Sets the custom properties the video's ::cue rule reads.
export function applySubtitleStyle(selector, size, background) {
    const video = document.querySelector(selector);
    if (!video) return;
    video.style.setProperty("--video-subtitle-font-size", SUBTITLE_SIZES[size] ?? SUBTITLE_SIZES.medium);
    video.style.setProperty("--video-subtitle-background", SUBTITLE_BACKGROUNDS[background] ?? "transparent");
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

export function initShortcuts(key, dotNetRef, selector, bindings) {
    disposeShortcuts(key);
    const onKeyDown = (event) => {
        const method = event.key.length === 1 ? bindings[(event.shiftKey ? 'shift+' : '') + event.key.toLowerCase()] : undefined;
        if (!method) return;
        if (isShortcutBlocked(event)) return;

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
