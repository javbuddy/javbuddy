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
    let revealTimer = null;
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

    // Hidden chrome that a key, click or clip end revealed just before a new clip loads (Next, Previous,
    // auto-advance) goes back to hidden: the clip change isn't a reason to show it.
    let idleStamp = 0;
    const noteIdle = () => {
        const now = Date.now();
        if (root.classList.contains(IDLE_CLASS)) idleStamp = now;
    };
    const onActivity = () => {
        noteIdle();
        root.classList.remove(IDLE_CLASS);
        schedule();
    };
    const onLoadStart = () => {
        clearTimeout(revealTimer);
        if (Date.now() - idleStamp < 2000) root.classList.add(IDLE_CLASS);
        schedule();
    };
    // Media events don't bubble: captured on root. Starting playback (a new clip loading included)
    // must not reveal hidden chrome, so play/playing/seeking only restart the timer and cancel a pending reveal. Pause/ended reveal it, but
    // after a beat and only if the video is still stopped, so a clip change doesn't flash it back.
    const onStarted = () => {
        clearTimeout(revealTimer);
        schedule();
    };
    const onStopped = () => {
        noteIdle();
        clearTimeout(revealTimer);
        revealTimer = setTimeout(() => {
            const v = video();
            if (v && (v.paused || v.ended) && v.readyState > 0) onActivity();
        }, 600);
    };

    const events = ['pointermove', 'pointerdown', 'touchstart', 'wheel', 'keydown', 'click'];
    for (const type of events) root.addEventListener(type, onActivity, { passive: true, capture: true });
    for (const type of ['play', 'playing', 'seeking']) root.addEventListener(type, onStarted, true);
    for (const type of ['pause', 'ended']) root.addEventListener(type, onStopped, true);
    root.addEventListener('loadstart', onLoadStart, true);
    // Fullscreen moves the column, not root, so the class stays; hidden chrome stays hidden across it.


    schedule();
    states.set(key, () => {
        clearTimeout(timer);
        clearTimeout(revealTimer);
        root.classList.remove(IDLE_CLASS);
        for (const type of events) root.removeEventListener(type, onActivity, true);
        for (const type of ['play', 'playing', 'seeking']) root.removeEventListener(type, onStarted, true);
        for (const type of ['pause', 'ended']) root.removeEventListener(type, onStopped, true);
        root.removeEventListener('loadstart', onLoadStart, true);
    });
}

export function dispose(key) {
    const teardown = states.get(key);
    if (!teardown) return;
    teardown();
    states.delete(key);
}
