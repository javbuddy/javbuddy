import { isShortcutBlocked, releaseVideo as releaseMedia } from '../Shared/ScrubBar.razor.js';

// Stops the card's video and aborts its stream before the movie's folder is deleted.
// Just removing the <video> from the DOM doesn't reliably end the download, and while
// the stream still has the file open a network share keeps it around after the delete (NFS .nfsXXXX,
// SMB delete-pending), failing the folder delete with "Directory not empty". Emptying the element —
// dropping src and calling load() — is the spec'd way to release a media resource.
export function releaseVideo(selector) {
    const video = document.querySelector(selector);
    if (!video) return;
    releaseMedia(video);
}

// Review mode's keyboard shortcuts: Space toggles the card's video, N clicks Next
// (it was Right arrow until the arrows became seek keys, / VideoSeekKeys.razor.js).
// Handled here so play/pause needs no SignalR round-trip; Next goes through the
// button's own handler, which also keeps it inert while an action is running (disabled). The
// listener lives for the page's lifetime and checks the DOM on each key: the Next button only
// exists on a Review card, so Cleanup mode, the summary and the empty state are left alone.
let onReviewKeyDown = null;

export function initReviewShortcuts() {
    disposeReviewShortcuts();
    onReviewKeyDown = (event) => {
        // Unlike the other shortcuts, Shift is not allowed here (isShortcutBlocked ignores it).
        if (event.shiftKey) return;
        if (event.key !== ' ' && event.key !== 'n' && event.key !== 'N') return;

        const next = document.querySelector('.cleanup-next-btn');
        if (!next) return;
        if (isShortcutBlocked(event)) return;

        // Claimed even on key repeat or without a video: Space would otherwise scroll the page or
        // activate a focused button, and a focused <video>'s native controls would toggle it a
        // second time.
        event.preventDefault();
        if (event.repeat) return;

        if (event.key === ' ') {
            const video = document.querySelector('video.cleanup-video');
            if (!video) return;
            if (video.paused) {
                video.play().catch(() => { });
            } else {
                video.pause();
            }
        } else if (!next.disabled) {
            next.click();
        }
    };
    // Capture phase, so the page sees the key before a focused element's own handling.
    window.addEventListener('keydown', onReviewKeyDown, true);
}

export function disposeReviewShortcuts() {
    if (!onReviewKeyDown) return;
    window.removeEventListener('keydown', onReviewKeyDown, true);
    onReviewKeyDown = null;
}
