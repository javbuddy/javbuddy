// The clip player's controls. The track, the time readout and seeking cover only the
// clip, [start, end] of the video, shown as 0:00 to its length. Reaching the end restarts the clip
// when Loop is on, and otherwise pauses and tells .NET (ClipEndedAsync), which closes the player or,
// in a playlist, moves to the next clip. Previous/Next and P/N also go through .NET.
// Runs in the browser like ScrubBar.razor.js, whose trickplay, preview and fullscreen helpers it
// reuses. Loop is remembered in localStorage, which may be unavailable (private mode, blocked).
import { bindFullscreen, formatTime, placePreview, trickplayThumbnails } from './ScrubBar.razor.js';

const LOOP_KEY = 'javbuddy-clip-loop';
const instances = new WeakMap();

function readLoop() {
    try {
        return localStorage.getItem(LOOP_KEY) === '1';
    } catch {
        return false;
    }
}

function writeLoop(loop) {
    try {
        if (loop) {
            localStorage.setItem(LOOP_KEY, '1');
        } else {
            localStorage.removeItem(LOOP_KEY);
        }
    } catch {
        // Not remembered, but the toggle still works for this clip.
    }
}

function isEditable(element) {
    return element instanceof Element
        && (element.closest('input, textarea, select, button') !== null || element.isContentEditable);
}

// `options`: { videoSelector, startSeconds, endSeconds (null = the video's end), trickplay,
// playlist, hasPrevious }. Returns the remembered Loop choice. Called again for each clip.
export function init(root, dotnet, options) {
    dispose(root);
    // `done` once this clip hands over to .NET (its end, Next, Previous): the next clip loads into the
    // same <video> before .NET re-inits, and this clip's range mustn't pull it back meanwhile.
    const state = { loop: readLoop(), done: false, showThumbnail: null };
    const video = root && document.querySelector(options.videoSelector);
    if (!video) return state.loop;

    const q = (selector) => root.querySelector(selector);
    const playBtn = q('.clip-controls-play');
    const trackWrap = q('.clip-controls-track-wrap');
    const track = q('.clip-controls-track');
    const progress = q('.clip-controls-progress');
    const preview = q('.clip-controls-preview');
    const thumb = q('.clip-controls-thumb');
    const previewTime = q('.clip-controls-preview-time');
    const time = q('.clip-controls-time');
    const muteBtn = q('.clip-controls-mute');
    const volume = q('.clip-controls-volume');
    const fullscreenBtn = q('.clip-controls-fullscreen');
    const previousBtn = q('.clip-controls-prev');
    const nextBtn = q('.clip-controls-next');

    const start = options.startSeconds;
    const end = () => options.endSeconds ?? (Number.isFinite(video.duration) ? video.duration : null);
    const length = () => {
        const e = end();
        return e == null ? 0 : Math.max(e - start, 0);
    };

    state.setTrickplay = (config) => {
        state.showThumbnail = config && config.width > 0 ? trickplayThumbnails(thumb, config) : null;
        thumb.hidden = !state.showThumbnail;
    };
    state.setTrickplay(options.trickplay);

    const render = () => {
        const len = length();
        const at = Math.min(Math.max(video.currentTime - start, 0), len);
        progress.style.width = len > 0 ? `${(at / len) * 100}%` : '0%';
        time.textContent = `${formatTime(at)} / ${len > 0 ? formatTime(len) : '-:--'}`;
        root.classList.toggle('clip-controls-playing', !video.paused);
        root.classList.toggle('clip-controls-muted', video.muted || video.volume === 0);
        volume.value = video.muted ? 0 : video.volume;
    };

    const handOver = (method) => {
        state.done = true;
        dotnet.invokeMethodAsync(method).catch(() => { });
    };

    const reachEnd = () => {
        if (state.done) return;
        if (state.loop) {
            video.currentTime = start;
            video.play().catch(() => { });
            return;
        }
        video.pause();
        handOver('ClipEndedAsync');
    };

    const restart = () => {
        video.currentTime = start;
        video.play().catch(() => { });
    };

    // More than 3 s in, Previous restarts this clip, as in most players; otherwise it goes back one.
    const previous = () => {
        if (!options.playlist || state.done) return;
        if (video.currentTime - start > 3 || !options.hasPrevious) {
            restart();
        } else {
            handOver('PreviousAsync');
        }
    };

    const next = () => {
        if (options.playlist && !state.done) handOver('NextAsync');
    };

    // While playing, keeps the playhead inside the clip.
    const checkRange = () => {
        const e = end();
        if (e != null && video.currentTime >= e) {
            reachEnd();
        } else if (video.currentTime < start - 0.25) {
            video.currentTime = start;
        }
    };

    // Checked every frame while playing, so the clip stops close to its end and the progress moves
    // smoothly. timeupdate (about 4 per second) checks too, because a background tab pauses
    // animation frames while the video plays on.
    let frame = 0;
    const tick = () => {
        frame = 0;
        if (state.done) return;
        // A frame still pending after a pause mustn't end the clip, e.g. when paused and then seeked
        // to its end.
        if (!video.paused) checkRange();
        render();
        if (!video.paused) frame = requestAnimationFrame(tick);
    };
    const onTimeUpdate = () => {
        if (state.done) return;
        if (!video.paused) checkRange();
        render();
    };
    // Playing from the clip's end (seeked there while paused) restarts the clip, as a native player
    // restarts a finished video, rather than ending it straight away.
    const onPlay = () => {
        if (state.done) return;
        const e = end();
        if (e != null && video.currentTime >= e - 0.05) video.currentTime = start;
        if (!frame) frame = requestAnimationFrame(tick);
        render();
    };

    const togglePlay = () => {
        if (video.paused) {
            video.play().catch(() => { });
        } else {
            video.pause();
        }
    };

    const secondsAt = (clientX) => {
        const rect = track.getBoundingClientRect();
        const fraction = rect.width > 0 ? (clientX - rect.left) / rect.width : 0;
        return start + Math.min(Math.max(fraction, 0), 1) * length();
    };

    // A highlight span under the pointer: hovering previews, and clicking seeks to, the
    // highlight's start rather than the pointer's position. Its data-start is from the clip's start.
    const highlightAt = (event) => event.target instanceof Element ? event.target.closest('.highlight-segment[data-start], .apex-marker[data-start]') : null;
    const secondsFor = (event) => {
        const highlight = highlightAt(event);
        return highlight ? start + Number(highlight.dataset.start) : secondsAt(event.clientX);
    };

    const showPreview = (event) => {
        const seconds = secondsFor(event);
        state.showThumbnail?.(seconds);
        const name = highlightAt(event)?.dataset.highlightName;
        previewTime.textContent = name ? `${formatTime(seconds - start)} · ${name}` : formatTime(seconds - start);
        placePreview(preview, track, event.clientX);
        preview.classList.add('clip-controls-preview-visible');
    };
    const hidePreview = () => preview.classList.remove('clip-controls-preview-visible');
    const preloadSheets = () => state.showThumbnail?.preload(start, end() ?? undefined);

    const seek = (event) => {
        video.currentTime = secondsFor(event);
        if (video.paused) video.play().catch(() => { });
    };

    const onMute = () => {
        video.muted = !video.muted;
        if (!video.muted && video.volume === 0) video.volume = 1;
    };
    const onVolume = () => {
        video.volume = Number(volume.value);
        video.muted = video.volume === 0;
    };

    // Space plays/pauses like the native controls did, and in a playlist N / P go to the next or
    // previous clip, unless a field or button has focus (a focused button's own Space already clicks
    // it) or a dialog is open over the player.
    const keys = { ' ': togglePlay, n: next, p: previous };
    const onKeyDown = (event) => {
        const action = keys[event.key.length === 1 ? event.key.toLowerCase() : event.key];
        if (!action || event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) return;
        if (action !== togglePlay && !options.playlist) return;
        if (isEditable(event.target) || isEditable(document.activeElement)) return;
        if (document.querySelector('.image-lightbox-backdrop, .delete-confirm-backdrop')) return;
        event.preventDefault();
        action();
    };

    // A mouse click leaves focus where it was (the player), so Space keeps playing/pausing instead of
    // pressing the last clicked button again, e.g. flipping Loop. Keyboard focus still reaches them.
    const keepFocus = (event) => {
        if (event.target instanceof Element && event.target.closest('button')) event.preventDefault();
    };

    const listeners = [
        [video, 'play', onPlay],
        [video, 'timeupdate', onTimeUpdate],
        [root, 'mousedown', keepFocus],
        [video, 'ended', reachEnd],
        [video, 'click', togglePlay],
        ...['pause', 'volumechange', 'loadedmetadata', 'durationchange', 'seeked'].map(type => [video, type, render]),
        [playBtn, 'click', togglePlay],
        ...(previousBtn ? [[previousBtn, 'click', previous]] : []),
        ...(nextBtn ? [[nextBtn, 'click', next]] : []),
        [trackWrap, 'pointerenter', preloadSheets],
        [trackWrap, 'pointermove', showPreview],
        [trackWrap, 'pointerleave', hidePreview],
        [trackWrap, 'click', seek],
        [muteBtn, 'click', onMute],
        [volume, 'input', onVolume],
        [window, 'keydown', onKeyDown],
    ];
    for (const [target, type, handler] of listeners) target.addEventListener(type, handler);

    const container = root.closest('.video-player-column') || root.parentElement;
    const unbindFullscreen = bindFullscreen(root, container, video, fullscreenBtn, 'clip-controls-is-fullscreen', 0);

    render();
    if (!video.paused) onPlay();

    state.teardown = () => {
        for (const [target, type, handler] of listeners) target.removeEventListener(type, handler);
        unbindFullscreen();
        if (frame) cancelAnimationFrame(frame);
    };
    instances.set(root, state);
    return state.loop;
}

export function setLoop(root, loop) {
    writeLoop(loop);
    const state = instances.get(root);
    if (state) state.loop = loop;
}

export function setTrickplay(root, config) {
    instances.get(root)?.setTrickplay?.(config);
}

export function dispose(root) {
    const state = root && instances.get(root);
    if (state) {
        state.teardown?.();
        instances.delete(root);
    }
}
