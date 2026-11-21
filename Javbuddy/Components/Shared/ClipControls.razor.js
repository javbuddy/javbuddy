// The clip player's controls. The track, the time readout and seeking cover only the
// clip, [start, end] of the video, shown as 0:00 to its length. Reaching the end restarts the clip
// when Loop is on, and otherwise pauses and tells .NET (ClipEndedAsync), which closes the player or,
// in a playlist, moves to the next clip. Previous/Next and P/N also go through .NET.
// Runs in the browser like ScrubBar.razor.js, whose trickplay, preview and fullscreen helpers it
// reuses. Loop is remembered in localStorage, which may be unavailable (private mode, blocked).
import { bindFullscreen, bindTouchScrub, bindTransport, formatTime, placePreview, trickplayThumbnails } from './ScrubBar.razor.js';

const LOOP_KEY = 'javbuddy-clip-loop';
const instances = new WeakMap();

function readLoop() {
    try {
        return localStorage.getItem(LOOP_KEY) === '1';
    } catch {
        return false;
    }
}

// Plays the video; when the browser blocks sound without a fresh tap (a phone, after .NET moves a
// playlist on), plays it muted instead of leaving it paused on its first frame. The mute button then
// shows muted, and a tap on it brings the sound back.
function playOrMute(video) {
    video.play().catch((error) => {
        if (error?.name !== 'NotAllowedError' || video.muted) return;
        video.muted = true;
        video.play().catch(() => { });
    });
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

// `options`: { videoSelector, startSeconds, endSeconds (null = the video's end), trickplay,
// playlist, hasPrevious }. `newClip`: another clip than the last init's (or the first), rather than the
// same clip with a changed range or place. Returns the remembered Loop choice. Called again for each clip.
export function init(root, dotnet, options, newClip) {
    dispose(root);
    // `done` once this clip hands over to .NET (its end, Next, Previous): the next clip loads into the
    // same <video> before .NET re-inits, and this clip's range mustn't pull it back meanwhile.
    const state = { loop: readLoop(), done: false, showThumbnail: null };
    const video = root && document.querySelector(options.videoSelector);
    if (!video) return state.loop;

    const q = (selector) => root.querySelector(selector);
    const trackWrap = q('.clip-controls-track-wrap');
    const track = q('.clip-controls-track');
    const progress = q('.clip-controls-progress');
    const preview = q('.clip-controls-preview');
    const thumb = q('.clip-controls-thumb');
    const previewTime = q('.clip-controls-preview-time');
    const time = q('.clip-controls-time');
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

    // The time readout and progress; the transport's render adds the playing/muted state and volume.
    const renderClip = () => {
        const len = length();
        const at = Math.min(Math.max(video.currentTime - start, 0), len);
        progress.style.width = len > 0 ? `${(at / len) * 100}%` : '0%';
        time.textContent = `${formatTime(at)} / ${len > 0 ? formatTime(len) : '-:--'}`;
    };

    const handOver = (method) => {
        state.done = true;
        dotnet.invokeMethodAsync(method).catch(() => { });
    };

    const reachEnd = () => {
        if (state.done) return;
        if (state.loop) {
            video.currentTime = start;
            playOrMute(video);
            return;
        }
        video.pause();
        handOver('ClipEndedAsync');
    };

    const restart = () => {
        video.currentTime = start;
        playOrMute(video);
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

    // While playing, keeps the playhead inside the clip. Not before the video's metadata is in or while a
    // seek is under way: the playhead then still reads 0 (or the last clip's spot), and setting it every
    // frame would restart the seek's range request each time and stall playback.
    const checkRange = () => {
        if (video.readyState < 1 || video.seeking) return;
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
        if (video.paused) playOrMute(video);
    };

    // Space plays/pauses like the native controls did, and in a playlist N / P go to the next or
    // previous clip, unless a field or button has focus (a focused button's own Space already clicks
    // it) or a dialog is open over the player.
    const transport = bindTransport({
        root,
        video,
        classPrefix: 'clip-controls',
        playBtn: q('.clip-controls-play'),
        muteBtn: q('.clip-controls-mute'),
        volume: q('.clip-controls-volume'),
        renderEvents: ['pause', 'volumechange', 'loadedmetadata', 'durationchange', 'seeked'],
        renderExtra: renderClip,
        extraKeys: options.playlist ? { n: next, p: previous } : {},
    });
    const render = transport.render;

    const listeners = [
        [video, 'play', onPlay],
        [video, 'timeupdate', onTimeUpdate],
        [video, 'ended', reachEnd],
        ...(previousBtn ? [[previousBtn, 'click', previous]] : []),
        ...(nextBtn ? [[nextBtn, 'click', next]] : []),
        [trackWrap, 'pointerenter', preloadSheets],
        [trackWrap, 'pointermove', showPreview],
        [trackWrap, 'pointerleave', hidePreview],
        [trackWrap, 'click', seek],
    ];
    for (const [target, type, handler] of listeners) target.addEventListener(type, handler);
    // A drag only moves the playhead: unlike a tap, letting go doesn't start a paused clip.
    const seekOnly = (event) => {
        video.currentTime = secondsFor(event);
    };
    const unbindTouchScrub = bindTouchScrub(trackWrap, { onMove: showPreview, onSeek: seekOnly, onEnd: hidePreview });

    const container = root.closest('.video-player-column') || root.parentElement;
    const unbindFullscreen = bindFullscreen(root, container, video, fullscreenBtn, 'clip-controls-is-fullscreen', 0);

    // Starts a new clip. A clip of the same movie keeps the <video>, and its src changes only in the #t=
    // fragment, or not at all when both clips start at the same point, so it neither reliably seeks nor
    // plays again (the last clip paused it at its end): seek to the start and play explicitly. A video
    // that is still loading gets its #t= and autoplay, so it's only checked once the metadata is in.
    // The same clip re-inited (its range or place changed) is left as it is, paused or not.
    const startClip = (seek) => {
        if (state.done) return;
        if (seek) video.currentTime = start;
        if (video.paused) playOrMute(video);
    };
    if (newClip && video.readyState >= 1) {
        startClip(true);
    } else if (newClip) {
        const onMetadata = () => {
            const e = end();
            startClip(video.currentTime < start - 0.25 || (e != null && video.currentTime >= e - 0.05));
        };
        listeners.push([video, 'loadedmetadata', onMetadata]);
        video.addEventListener('loadedmetadata', onMetadata, { once: true });
    }

    render();
    if (!video.paused) onPlay();

    state.teardown = () => {
        for (const [target, type, handler] of listeners) target.removeEventListener(type, handler);
        unbindTouchScrub();
        transport.unbind();
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
