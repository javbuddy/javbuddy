// The apex countdown's timer: every TICK_MS, shows ceil(next apex - currentTime) in `el`
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

export function init(key, el, selector, apexes) {
    dispose(key);
    if (!el) return;

    const state = { apexes };
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
    const timer = setInterval(tick, TICK_MS);
    states.set(key, { state, stop: () => clearInterval(timer) });
    tick();
}

// apexes: ascending seconds.
export function update(key, apexes) {
    const entry = states.get(key);
    if (entry) entry.state.apexes = apexes;
}

export function dispose(key) {
    const entry = states.get(key);
    if (!entry) return;
    entry.stop();
    states.delete(key);
}
