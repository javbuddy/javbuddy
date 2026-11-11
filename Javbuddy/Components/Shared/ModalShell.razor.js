// Moves focus onto a freshly opened modal's backdrop so its Escape handler works without a click first —
// unless a control inside already has focus (e.g. an autofocus input), which must keep it.
export function focusUnlessInside(el) {
    if (!(el instanceof HTMLElement) || !el.isConnected) return;
    if (el.contains(document.activeElement)) return;
    el.focus({ preventScroll: true });
}
