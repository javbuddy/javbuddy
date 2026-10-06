// Owns viewport-proximity image loading for the actor detail page's photo gallery preview grid.
//
// Native `loading="lazy"` on these <img> tags only defers correctly on the page's initial static
// parse. Once Blazor Server's interactive circuit connects, the whole grid is re-inserted as
// fresh DOM nodes in one batch, and Chromium's lazy-loading distance heuristic fails open (loads
// everything at once) for nodes it can't yet measure at insertion time. Deferring via our own
// IntersectionObserver instead sidesteps that.

let observer = null;

export function init(container) {
    dispose();
    if (!container || typeof container.querySelectorAll !== 'function') return;

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
    if (!container || typeof container.querySelectorAll !== 'function') return;
    if (!observer) {
        init(container);
        return;
    }

    const isExpanded = container.closest('.actor-photo-gallery')?.classList.contains('actor-photo-gallery-expanded') ?? true;
    const grid = container.querySelector('.actor-photos-justified-grid') || container;
    const maxHeight = isExpanded ? Number.POSITIVE_INFINITY : (grid.offsetHeight || 1120);
    const margin = 800;
    const tiles = container.querySelectorAll('.actor-photo-tile');

    for (const tile of tiles) {
        const photoImg = tile.querySelector('.actor-photo-tile-img');
        if (!photoImg || photoImg.src) continue;

        if (tile.offsetTop < maxHeight) {
            const rect = tile.getBoundingClientRect();
            if (rect.bottom >= -margin && rect.top <= (window.innerHeight || 800) + margin) {
                loadImage(tile);
                continue;
            }
        }

        observer.unobserve(tile);
        observer.observe(tile);
    }
}

function loadImage(tile) {
    const photoImg = tile.querySelector('.actor-photo-tile-img');
    if (photoImg && photoImg.dataset.src && !photoImg.src) {
        photoImg.src = photoImg.dataset.src;
        if (photoImg.complete) {
            photoImg.classList.add('loaded');
        } else {
            photoImg.addEventListener('load', () => photoImg.classList.add('loaded'), { once: true });
        }
    }
}

function loadAll(container) {
    if (!container || typeof container.querySelectorAll !== 'function') return;
    const tiles = container.querySelectorAll('.actor-photo-tile');
    for (const tile of tiles) {
        loadImage(tile);
    }
}

export function dispose() {
    if (observer) {
        observer.disconnect();
        observer = null;
    }
}
