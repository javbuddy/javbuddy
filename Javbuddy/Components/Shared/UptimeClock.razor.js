// The uptime clock's timer: every second, writes the elapsed time into `el`, counted from the
// server's elapsed value at render time plus the browser's own monotonic clock (so the browser's
// wall clock and time zone don't matter). format mirrors DurationFormatter.Format, which holds the tests.
const TICK_MS = 1000;
const states = new Map();

const pad = n => String(n).padStart(2, '0');

const format = ms => {
    const total = Math.max(0, Math.floor(ms / 1000));
    const days = Math.floor(total / 86400);
    const hms = `${pad(Math.floor(total / 3600) % 24)}:${pad(Math.floor(total / 60) % 60)}:${pad(total % 60)}`;
    return days > 0 ? `${days}d ${hms}` : hms;
};

export function start(key, el, elapsedMs) {
    dispose(key);
    if (!el) return;

    const origin = performance.now() - elapsedMs;
    const tick = () => {
        const text = format(performance.now() - origin);
        // Edits Blazor's own text node rather than replacing it, so a later server render still diffs cleanly.
        const node = el.firstChild;
        if (node && node.nodeType === Node.TEXT_NODE) {
            if (node.nodeValue !== text) node.nodeValue = text;
        } else {
            el.textContent = text;
        }
    };
    const timer = setInterval(tick, TICK_MS);
    states.set(key, () => clearInterval(timer));
    tick();
}

export function dispose(key) {
    const stop = states.get(key);
    if (!stop) return;
    stop();
    states.delete(key);
}
