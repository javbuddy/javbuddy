// Owns viewport-proximity image loading for the ActorDetail movie grid.
//
// Native `loading="lazy"` only defers correctly on the initial static parse; once the interactive
// circuit connects, the grid is re-inserted in one batch and Chromium loads everything at once.
// Our own IntersectionObserver avoids that. Also remembers the scroll position per actor in
// sessionStorage, so going back from a movie lands where the user left the filmography.

let observer = null;
let scrollTracking = null;

export function init(container) {
    dispose();
    if (!container) return;

    if (!('IntersectionObserver' in window)) {
        loadAll(container);
        return;
    }

    observer = new IntersectionObserver((entries) => {
        for (const entry of entries) {
            if (entry.isIntersecting) {
                loadImage(entry.target);
                observer.unobserve(entry.target);
            }
        }
    }, {
        rootMargin: '800px 0px 800px 0px'
    });

    observe(container);
}

export function observe(container) {
    if (!container) return;
    if (!observer) {
        loadAll(container);
        return;
    }

    const images = container.querySelectorAll('.js-lazy-poster-img[data-src]');
    for (const img of images) {
        observer.observe(img);
    }
}

function loadImage(img) {
    if (img.dataset.src) {
        img.src = img.dataset.src;
        img.removeAttribute('data-src');
    }
}

function loadAll(container) {
    const images = container.querySelectorAll('.js-lazy-poster-img[data-src]');
    for (const img of images) {
        loadImage(img);
    }
}

// Restores the saved position for this actor, then records every later scroll under the same key.
export function trackScroll(storageKey) {
    disposeScrollTracking();
    if (!storageKey) return;

    // A scroll event that arrives after enhanced navigation has already moved to another page
    // (DisposeAsync round-trips back here later) would otherwise overwrite the saved position with
    // the new page's scrollY; the route updates atomically, so it is what guards against that (see
    // VirtualizedGrid.razor.js's evaluate()).
    const activePath = location.pathname;
    let pending = false;
    const onScroll = () => {
        if (pending) return;
        pending = true;
        requestAnimationFrame(() => {
            pending = false;
            if (location.pathname !== activePath) return;
            try {
                sessionStorage.setItem(storageKey, String(window.scrollY));
            } catch {
                // Storage disabled/full: the next visit just starts at the top.
            }
        });
    };

    scrollTracking = { onScroll, restoreFrame: 0 };
    restoreScrollPosition(storageKey, scrollTracking);
    window.addEventListener('scroll', onScroll, { passive: true });
}

// The filmography may still be growing (images and late layout) when this runs, and a scrollTo past
// the document's current height just clamps, so retry each frame until the page is tall enough, for
// up to two seconds.
function restoreScrollPosition(storageKey, tracking) {
    let saved;
    try {
        saved = Number(sessionStorage.getItem(storageKey));
    } catch {
        return;
    }
    if (!saved) return;

    const deadline = performance.now() + 2000;
    function attempt() {
        if (scrollTracking !== tracking) return;
        const maxScroll = document.documentElement.scrollHeight - window.innerHeight;
        // Explicitly instant: the app sets `scroll-behavior: smooth` on <html>.
        window.scrollTo({ top: Math.min(saved, maxScroll), behavior: 'instant' });
        if (maxScroll < saved && performance.now() < deadline) {
            tracking.restoreFrame = requestAnimationFrame(attempt);
        }
    }
    attempt();
}

function disposeScrollTracking() {
    if (scrollTracking) {
        window.removeEventListener('scroll', scrollTracking.onScroll);
        cancelAnimationFrame(scrollTracking.restoreFrame);
        scrollTracking = null;
    }
}

export function dispose() {
    if (observer) {
        observer.disconnect();
        observer = null;
    }
    disposeScrollTracking();
}
