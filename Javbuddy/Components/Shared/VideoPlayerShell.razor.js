// The player backdrop's keys, handled in the browser so only the ones that need .NET reach it: Esc
// closes the player, and in a movie playlist N / P go to the next / previous movie. Every other
// key (held arrow-key seeking, typing in the scene editor's fields) stays here instead of costing a
// SignalR round-trip and a re-render of the player. The listener sits on the backdrop, so it hears
// the same keys the Blazor handler did, fullscreen included (entering fullscreen moves focus to
// the column inside the backdrop).
import { isShortcutBlocked } from './ScrubBar.razor.js';

const instances = new WeakMap();

// `options`: { playlist (N / P are live), hasPrevious (P is live) }. Called again from `update`
// when either changes.
export function init(backdrop, dotnet, options) {
    dispose(backdrop);
    if (!backdrop) return;
    const state = { options, dotnet };
    const send = (method) => dotnet.invokeMethodAsync(method).catch(() => { });
    const onKeyDown = (event) => {
        // The delete confirmation keeps its keys to itself (Esc cancels it, not the player).
        if (event.target instanceof Element && event.target.closest('.delete-confirm-backdrop')) return;
        if (event.key === 'Escape') {
            send('CloseFromKeyAsync');
            return;
        }
        if (!state.options.playlist || event.shiftKey) return;
        const key = event.key.toLowerCase();
        if (key !== 'n' && key !== 'p') return;
        if (key === 'p' && !state.options.hasPrevious) return;
        if (isShortcutBlocked(event)) return;
        send(key === 'n' ? 'NextFromKeyAsync' : 'PreviousFromKeyAsync');
    };
    backdrop.addEventListener('keydown', onKeyDown);
    // An attribute Blazor never renders, so it survives its re-renders: the keys work once it's there,
    // a moment after the player opens (the module import and this call), which tests wait for.
    backdrop.dataset.keysBound = '';
    state.teardown = () => {
        backdrop.removeEventListener('keydown', onKeyDown);
        delete backdrop.dataset.keysBound;
    };
    instances.set(backdrop, state);
}

export function update(backdrop, options) {
    const state = backdrop && instances.get(backdrop);
    if (state) state.options = options;
}

export function dispose(backdrop) {
    const state = backdrop && instances.get(backdrop);
    if (state) {
        state.teardown();
        instances.delete(backdrop);
    }
}
