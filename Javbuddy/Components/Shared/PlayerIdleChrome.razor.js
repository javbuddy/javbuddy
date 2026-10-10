// Idle-hides a player's chrome: after IDLE_MS without mouse, touch or key activity while
// the video plays, `root` gets the `player-idle` class; any activity removes it and restarts the timer.
// It stays off while the video is paused or ended, while the pointer is over the header, scrub bar,
// clip controls, VR projection picker or side panel, and while focus is inside the header or on the
// picker (an open dropdown).
const IDLE_MS = 3000;
const KEEP_VISIBLE = '.video-player-modal-header, .scrub-bar, .clip-controls, .video-player-side-panel, .vr-viewer-projection';
// :hover has to follow each selector, not the list.
const KEEP_VISIBLE_HOVERED = KEEP_VISIBLE.split(', ').map((s) => s + ':hover').join(', ');
const IDLE_CLASS = 'player-idle';
const states = new Map();

export function init(key, root, selector) {
    dispose(key);
    if (!root) return;

    let timer = null;
    const video = () => document.querySelector(selector);

    const shouldStayVisible = () => {
        const v = video();
        if (!v || v.paused || v.ended) return true;
        if (root.querySelector(KEEP_VISIBLE_HOVERED)) return true;
        const header = root.querySelector('.video-player-modal-header');
        const active = document.activeElement;
        if (!(active instanceof Element) || !active.matches('select, input, textarea')) return false;
        return (header !== null && header.contains(active)) || active.matches('.vr-viewer-projection');
    };

    const schedule = () => {
        clearTimeout(timer);
        timer = setTimeout(() => {
            if (shouldStayVisible()) return;
            root.classList.add(IDLE_CLASS);
        }, IDLE_MS);
    };

    const onActivity = () => {
        root.classList.remove(IDLE_CLASS);
        schedule();
    };
    // Play/pause are media events, which don't bubble: captured on root.
    const onPlayState = () => onActivity();

    const events = ['pointermove', 'pointerdown', 'touchstart', 'wheel', 'keydown', 'click'];
    for (const type of events) root.addEventListener(type, onActivity, { passive: true, capture: true });
    for (const type of ['play', 'playing', 'pause', 'ended']) root.addEventListener(type, onPlayState, true);
    // Fullscreen moves the column, not root, so the class stays; a change is a new context to reveal in.
    document.addEventListener('fullscreenchange', onActivity);

    schedule();
    states.set(key, () => {
        clearTimeout(timer);
        root.classList.remove(IDLE_CLASS);
        for (const type of events) root.removeEventListener(type, onActivity, true);
        for (const type of ['play', 'playing', 'pause', 'ended']) root.removeEventListener(type, onPlayState, true);
        document.removeEventListener('fullscreenchange', onActivity);
    });
}

export function dispose(key) {
    const teardown = states.get(key);
    if (!teardown) return;
    teardown();
    states.delete(key);
}
