// Actor-tag popovers (ActorTagHover): a top-layer popover, so no card's overflow or a modal clips it. Shown under the
// actor on hover or focus, pulled back inside the window, kept under its actor while anything scrolls, and hidden
// when the pointer or focus that opened it leaves, or on Escape. Loaded once by App.razor; the listeners are on the
// document, so popovers Blazor renders later need nothing more.
let open = null;
let openHost = null;
let openedByFocus = false;

function hide() {
    if (!open) return;
    try { open.hidePopover(); } catch { /* removed by a re-render meanwhile */ }
    open = null;
    openHost = null;
    openedByFocus = false;
}

function place(host, pop) {
    const r = host.getBoundingClientRect();
    const w = pop.offsetWidth, h = pop.offsetHeight;
    const left = Math.max(8, Math.min(r.left, window.innerWidth - w - 8));
    let top = r.bottom + 4;
    if (top + h > window.innerHeight - 8 && r.top - h - 4 > 8) top = r.top - h - 4;
    pop.style.left = `${left}px`;
    pop.style.top = `${top}px`;
}

function show(host, byFocus) {
    const pop = host.querySelector(':scope > .actor-hover-popover');
    if (!pop || !pop.showPopover) return;
    if (open === pop) return;
    hide();
    pop.showPopover();
    place(host, pop);
    open = pop;
    openHost = host;
    openedByFocus = byFocus;
}

const hostOf = target => target.closest?.('.actor-hover');

document.addEventListener('mouseover', e => {
    const host = hostOf(e.target);
    if (host) show(host, false);
    else if (!openedByFocus) hide();
});
document.addEventListener('focusin', e => {
    const host = hostOf(e.target);
    if (host) show(host, true);
    else hide();
});
document.addEventListener('focusout', e => {
    if (openedByFocus && !hostOf(e.relatedTarget ?? document.body)) hide();
});
document.addEventListener('keydown', e => { if (e.key === 'Escape') hide(); });
window.addEventListener('scroll', () => { if (open && openHost.isConnected) place(openHost, open); }, { capture: true, passive: true });
