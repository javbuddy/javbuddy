// The apex countdown's timer: while the video plays, every TICK_MS, shows ceil(next apex - currentTime) in `el`
// when the next apex is within the length set in Settings > UI (window.lsApexCountdown; 0 = off), and
// hides it otherwise. Mirrors ApexRanges.CountdownFor, which holds the rule's tests. The paused video
// keeps its number, so a countdown interrupted mid-way is still true.
const TICK_MS = 100;
const states = new Map();

const countdownFor = (apexes, current, length) => {
    if (!(length > 0)) return null;
    let next = null;
    for (const a of apexes) {
        if (a > current) { next = a; break; }
    }
    if (next === null || next - current > length) return null;
    return Math.ceil(next - current);
};

// The 100 ms timer only runs while the matching video is playing. Everything else that can change
// the number (a seek, a source swap, a new apex list) is a media event or an update() call, handled by
// one tick. Media events don't bubble, so they're caught in the capture phase on the document, which
// also covers the <video> being created or replaced after init.
const MEDIA_EVENTS = ['play', 'playing', 'pause', 'ended', 'seeking', 'seeked', 'loadedmetadata', 'emptied', 'ratechange'];

export function init(key, el, selector, apexes) {
    dispose(key);
    if (!el) return;

    const state = { apexes };
    let timer = null;
    const tick = () => {
        const video = document.querySelector(selector);
        const length = window.lsApexCountdown ? window.lsApexCountdown.get() : 0;
        const n = video && state.apexes.length > 0 ? countdownFor(state.apexes, video.currentTime, length) : null;
        if (n === null) {
            if (!el.hidden) el.hidden = true;
        } else {
            if (el.hidden) el.hidden = false;
            const text = String(n);
            if (el.textContent !== text) el.textContent = text;
        }
    };
    // Shows the current number, then starts or stops the timer to match whether the video is playing.
    const sync = () => {
        tick();
        const video = document.querySelector(selector);
        const playing = !!video && !video.paused && !video.ended;
        if (playing && timer === null) {
            timer = setInterval(tick, TICK_MS);
        } else if (!playing && timer !== null) {
            clearInterval(timer);
            timer = null;
        }
    };
    const onMedia = e => {
        if (e.target instanceof Element && e.target.matches(selector)) sync();
    };
    for (const name of MEDIA_EVENTS) document.addEventListener(name, onMedia, true);

    states.set(key, {
        state,
        sync,
        stop: () => {
            for (const name of MEDIA_EVENTS) document.removeEventListener(name, onMedia, true);
            if (timer !== null) clearInterval(timer);
            timer = null;
        }
    });
    sync();
}

// apexes: ascending seconds.
export function update(key, apexes) {
    const entry = states.get(key);
    if (!entry) return;
    entry.state.apexes = apexes;
    entry.sync();
}

export function dispose(key) {
    const entry = states.get(key);
    if (!entry) return;
    entry.stop();
    states.delete(key);
}
