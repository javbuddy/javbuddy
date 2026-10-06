// Escape closes the lightbox no matter where focus is. The backdrop only receives
// focus after a server round-trip, so an Escape pressed right after opening would otherwise go
// to <body> and be ignored. Registered once per component; the DOM check keeps it inert while closed.
const handlers = new Map();

export function init(key, dotNetRef) {
    dispose(key);
    const onKeyDown = (event) => {
        if (event.key !== 'Escape' || event.defaultPrevented) return;
        if (!document.querySelector('.image-lightbox-backdrop')) return;
        event.preventDefault();
        dotNetRef.invokeMethodAsync('HandleEscape');
    };
    window.addEventListener('keydown', onKeyDown);
    handlers.set(key, onKeyDown);
}

export function dispose(key) {
    const handler = handlers.get(key);
    if (!handler) return;
    window.removeEventListener('keydown', handler);
    handlers.delete(key);
}
