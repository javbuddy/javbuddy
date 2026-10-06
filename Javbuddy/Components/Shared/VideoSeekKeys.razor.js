// Arrow-key seeking: ←/→ skip 5 s, Shift+←/→ skip 60 s; ,/. step one frame. Clamped to the video's
// length, or to [min, max] when given (a clip's range). Guarded like the other player shortcuts (MovieCleanup.razor.js, ClipEditor.razor.js):
// ignored while typing, with a modifier other than Shift, or with a lightbox/Review modal open.
// H / Shift+H jump to the next / previous highlight, A / Shift+A to the next / previous apex,
// given as sorted targets (a highlight's start, or an apex's lead-in before it; MarkerJumps.cs).
const STEP_SECONDS = 5;
const SHIFT_STEP_SECONDS = 60;
const FALLBACK_FRAME_SECONDS = 1 / 30;
// A target this close ahead of the playhead is where it already is, not the next one.
const NEXT_EPSILON_SECONDS = 0.05;
// Previous skips a target the playhead passed less than this ago, so pressing it right after a jump
// (or while held) carries on back instead of landing on the same marker again.
const PREVIOUS_GRACE_SECONDS = 1;
const handlers = new Map();
// Per-<video> frame duration, measured from consecutive presented frames while it plays.
const frameDurations = new WeakMap();
const samplers = new WeakSet();

function sampleFrameDuration(video) {
    if (samplers.has(video) || !video.requestVideoFrameCallback) return;
    samplers.add(video);
    let last = null;
    const onFrame = (_now, metadata) => {
        if (last && metadata.presentedFrames === last.presentedFrames + 1) {
            const delta = metadata.mediaTime - last.mediaTime;
            if (delta > 0.001 && delta < 0.2) {
                frameDurations.set(video, Math.min(frameDurations.get(video) ?? Infinity, delta));
            }
        }
        last = metadata;
        video.requestVideoFrameCallback(onFrame);
    };
    video.requestVideoFrameCallback(onFrame);
}

function isEditable(element) {
    return element instanceof Element
        && (element.closest('input, textarea, select') !== null || element.isContentEditable);
}

// The first target ahead of `at`, or the last one behind it.
function jumpTarget(targets, at, forward) {
    if (forward) return targets.find(t => t > at + NEXT_EPSILON_SECONDS);
    return targets.findLast(t => t < at - PREVIOUS_GRACE_SECONDS);
}

export function init(key, selector, min, max, highlights, apexes) {
    dispose(key);
    const onKeyDown = (event) => {
        if (event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) return;
        const isFrameStep = event.key === ',' || event.key === '.';
        const letter = event.key.length === 1 ? event.key.toLowerCase() : null;
        const isJump = letter === 'h' || letter === 'a';
        const jumpTargets = isJump ? (letter === 'a' ? apexes : highlights) ?? [] : null;
        if (isJump && jumpTargets.length === 0) return;
        if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight' && !isFrameStep && !isJump) return;
        if (isFrameStep && event.shiftKey) return;
        if (document.querySelector('.image-lightbox-backdrop, .cleanup-modal-overlay, .delete-confirm-backdrop')) return;
        if (isEditable(event.target) || isEditable(document.activeElement)) return;

        const video = document.querySelector(selector);
        if (!video || !(video.readyState > 0)) return;

        // Claimed so a focused <video>'s native controls don't seek a second time. A held key
        // repeats the skip.
        event.preventDefault();
        const end = Math.min(Number.isFinite(video.duration) ? video.duration : Infinity, max ?? Infinity);
        if (isJump) {
            const target = jumpTarget(jumpTargets, video.currentTime, !event.shiftKey);
            if (target !== undefined) video.currentTime = Math.min(Math.max(target, min ?? 0), end);
            return;
        }
        if (isFrameStep) {
            // Frame-by-frame: pause, then land mid-frame on the neighbouring frame
            // so repeated presses don't stall on float error.
            video.pause();
            const frame = frameDurations.get(video) ?? FALLBACK_FRAME_SECONDS;
            const index = Math.floor(video.currentTime / frame + 1e-3) + (event.key === ',' ? -1 : 1);
            video.currentTime = Math.min(Math.max((index + 0.5) * frame, min ?? 0), end);
            return;
        }
        const step = (event.shiftKey ? SHIFT_STEP_SECONDS : STEP_SECONDS) * (event.key === 'ArrowLeft' ? -1 : 1);
        video.currentTime = Math.min(Math.max(video.currentTime + step, min ?? 0), end);
    };
    // Media events don't bubble, so the capture phase is the only way to see the video start playing.
    const onPlaying = (event) => {
        if (event.target instanceof HTMLVideoElement && event.target.matches(selector)) sampleFrameDuration(event.target);
    };
    // Capture phase, so the page sees the key before a focused element's own handling.
    window.addEventListener('keydown', onKeyDown, true);
    document.addEventListener('playing', onPlaying, true);
    handlers.set(key, { onKeyDown, onPlaying });
}

export function dispose(key) {
    const handler = handlers.get(key);
    if (!handler) return;
    window.removeEventListener('keydown', handler.onKeyDown, true);
    document.removeEventListener('playing', handler.onPlaying, true);
    handlers.delete(key);
}
