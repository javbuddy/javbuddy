// Makes everything outside a modal inert while it's open: its siblings, and its
// ancestors' siblings up to <body>, so Tab / Shift+Tab can't move focus onto the page behind it
// (where Space or Enter would press a button such as Movie Detail's Play) and nothing there can be
// clicked. Children added next to the modal's ancestors while it's open (the page re-rendering
// behind it) are made inert too. The reconnect dialog and error bar stay usable.
const KEEP = 'script, #components-reconnect-modal, #blazor-error-ui';
const states = new Map();

export function init(key, root) {
    dispose(key);
    if (!(root instanceof Element) || !root.isConnected) return;

    // Only what this call made inert is restored, so an element already inert stays that way.
    const made = [];
    const makeInert = (element) => {
        if (!(element instanceof Element) || element.inert || element.matches(KEEP)) return;
        element.inert = true;
        made.push(element);
    };

    const observers = [];
    for (let node = root; node !== document.body && node.parentElement; node = node.parentElement) {
        const keep = node;
        const parent = node.parentElement;
        for (const sibling of parent.children) {
            if (sibling !== keep) makeInert(sibling);
        }
        const observer = new MutationObserver((records) => {
            for (const record of records) {
                for (const added of record.addedNodes) {
                    if (added !== keep) makeInert(added);
                }
            }
        });
        observer.observe(parent, { childList: true });
        observers.push(observer);
    }
    // Whatever on the page still has focus (e.g. the button that opened the modal) gives it up, so a
    // key can't reach it before the modal takes focus.
    const active = document.activeElement;
    if (active instanceof HTMLElement && active !== document.body && !root.contains(active)) active.blur();
    states.set(key, { made, observers });
}

export function dispose(key) {
    const state = states.get(key);
    if (!state) return;
    for (const observer of state.observers) observer.disconnect();
    for (const element of state.made) element.inert = false;
    states.delete(key);
}
