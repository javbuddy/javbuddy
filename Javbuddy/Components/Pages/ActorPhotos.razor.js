// Owns the proximity image loading for the Actor Photos justified gallery.
//
// Images load only within an overscan margin (~800px) of the viewport, to avoid hundreds of
// concurrent requests. A tile that leaves the margin before its images finish has the unfinished
// requests aborted, so a fast scroll doesn't queue every tile it passed. Tiles are appended in
// batches before the grid's end reaches the viewport; the justified layout and lightbox stay
// owned by Blazor.

let observer = null;
let state = null;

export function init(container, dotNetRef) {
    dispose();
    if (!container || typeof container.querySelectorAll !== 'function') return;
    state = { container, dotNetRef, moreObserver: null, layoutObserver: null, pending: false };
    setUpLayoutObserver(state);

    if (!('IntersectionObserver' in window)) {
        loadAll(container);
        return;
    }

    const imageObserver = new IntersectionObserver((entries) => {
        for (const entry of entries) {
            if (!entry.target.isConnected) continue;
            if (entry.isIntersecting) {
                loadImage(entry.target);
            } else {
                cancelImage(entry.target);
            }
            if (isTileSettled(entry.target)) imageObserver.unobserve(entry.target);
        }
    }, {
        // ~800px margin loads images slightly before they scroll into the viewport
        rootMargin: '800px 0px 800px 0px'
    });
    observer = imageObserver;

    const current = state;
    current.moreObserver = new IntersectionObserver(async entries => {
        if (state !== current || current.pending) return;
        // The placeholder always spans from the end of the rendered batch to the true end of the
        // collection, so it - not just the button - must also be able to trigger loading: jumping
        // straight to the bottom of the scrollbar (drag, End key) lands inside it, well past the
        // button's own rootMargin.
        if (!entries.some(entry => entry.isIntersecting && entry.target.isConnected)) return;
        current.pending = true;
        current.moreObserver.disconnect();
        let completed = false;
        try {
            const generation = Number(current.container.dataset.generation);
            await current.dotNetRef.invokeMethodAsync('LoadMorePhotos', generation);
            completed = true;
        } catch {
            // A disconnected circuit can reconnect; the button also remains a manual fallback.
        } finally {
            current.pending = false;
            if (completed && state === current) observeMore(current);
        }
    }, { rootMargin: '1600px 0px 1600px 0px' });

    observe(container);
}

export function observe(container) {
    observer?.disconnect();
    state?.moreObserver?.disconnect();
    if (!state) return;
    if (state.container !== container) {
        state.layoutObserver?.disconnect();
        state.layoutObserver = null;
    }
    state.container = container;
    if (!container || !container.isConnected || typeof container.querySelectorAll !== 'function') return;
    if (!state.layoutObserver) setUpLayoutObserver(state);
    reportLayoutMetrics(state);
    if (!observer) {
        loadAll(container);
        return;
    }

    const tiles = container.querySelectorAll('.actor-photo-tile');
    for (const tile of tiles) {
        if (!isTileSettled(tile)) observer.observe(tile);
    }
    if (!state.pending) observeMore(state);
}

// Called after a layout-metrics-driven re-render may have added the placeholder to the DOM for
// the first time (or changed its size) - re-observing an already-observed element is a no-op, so
// this just needs to make sure both possible trigger elements are covered.
export function refreshLoadMoreObserver() {
    if (state?.moreObserver && !state.pending) observeMore(state);
}

function observeMore(current) {
    const button = current.container?.querySelector('.actor-photos-load-more');
    if (button?.isConnected) current.moreObserver.observe(button);
    const placeholder = current.container?.querySelector('.actor-photos-placeholder');
    if (placeholder?.isConnected) current.moreObserver.observe(placeholder);
}

// Reports the grid's real pixel width and the per-section heading overhead (height contributed by
// a section's heading + margins, measured from an already-rendered section) so Blazor can compute
// the exact placeholder height for photos that aren't rendered yet, instead of estimating it.
function setUpLayoutObserver(current) {
    if ('ResizeObserver' in window) {
        current.layoutObserver = new ResizeObserver(() => reportLayoutMetrics(current));
        current.layoutObserver.observe(current.container);
    } else {
        reportLayoutMetrics(current);
    }
}

function reportLayoutMetrics(current) {
    if (!current.container || !current.container.isConnected) return;
    const grid = current.container.querySelector('.actor-photos-justified-grid');
    const width = (grid ?? current.container).getBoundingClientRect().width;

    let sectionOverhead = 0;
    const section = current.container.querySelector('.actor-photos-timeline-section');
    if (section) {
        const sectionRect = section.getBoundingClientRect();
        const gridRect = (section.querySelector('.actor-photos-justified-grid') ?? section).getBoundingClientRect();
        const marginBottom = parseFloat(window.getComputedStyle(section).marginBottom) || 0;
        sectionOverhead = (gridRect.top - sectionRect.top) + marginBottom;
    }

    current.dotNetRef.invokeMethodAsync('OnLayoutMetricsChanged', width, sectionOverhead).catch(() => {
        // A disconnected circuit can reconnect; the next resize/observe call will retry.
    });
}

const tileImageSelectors = ['.actor-photo-tile-img', '.actor-photo-tile-avatar'];

function loadImage(tile) {
    for (const selector of tileImageSelectors) {
        const img = tile.querySelector(selector);
        if (!img || !img.dataset.src || img.hasAttribute('src')) continue;
        img.src = img.dataset.src;
        if (img.complete) {
            onImageSettled(tile, img);
        } else {
            img.addEventListener('load', () => onImageSettled(tile, img), { once: true });
            img.addEventListener('error', () => onImageSettled(tile, img), { once: true });
        }
    }
}

// Removing the src attribute (rather than setting it to '') aborts the in-flight request without
// firing an error event, so the avatar's onerror handler doesn't hide it; data-src is kept so the
// tile loads again if it comes back into range.
function cancelImage(tile) {
    for (const selector of tileImageSelectors) {
        const img = tile.querySelector(selector);
        if (img && img.hasAttribute('src') && !img.complete) {
            img.removeAttribute('src');
        }
    }
}

function onImageSettled(tile, img) {
    // A load/error event from a request cancelImage already aborted must not count.
    if (!img.hasAttribute('src') || !img.complete) return;
    img.classList.add('loaded');
    if (observer && isTileSettled(tile)) observer.unobserve(tile);
}

function isTileSettled(tile) {
    return tileImageSelectors.every(selector => {
        const img = tile.querySelector(selector);
        return !img || !img.dataset.src || (img.hasAttribute('src') && img.complete);
    });
}

function loadAll(container) {
    if (!container || typeof container.querySelectorAll !== 'function') return;
    const tiles = container.querySelectorAll('.actor-photo-tile');
    for (const tile of tiles) {
        loadImage(tile);
    }
}

export function dispose() {
    state?.moreObserver?.disconnect();
    state?.layoutObserver?.disconnect();
    state = null;
    if (observer) {
        observer.disconnect();
        observer = null;
    }
}
