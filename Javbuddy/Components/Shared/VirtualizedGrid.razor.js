// Owns the geometry half of a virtualized card grid (the Movies grid, the scene wall and the
// Actors grid).
//
// The server holds a *window*: a contiguous slice of the filtered list, rendered between a leading
// and a trailing spacer that reserve the height of the rows outside it. The document's scroll height
// is the same wherever the window sits, so sliding it never moves the content under the cursor. That
// only holds while every row is the same height, so cards must have a fixed height.
//
// Everything that needs real layout (column count, row height, viewport position) is measured here,
// and only the resulting item range is sent to .NET, so an ordinary scroll inside the loaded window
// costs no circuit traffic.
//
// The window (windowStart/windowCount/totalCount) arrives as the leading spacer's data-window-*
// attributes, rendered in the same diff as the card list. A separate follow-up call would land in a
// different browser task, with a paint in between where the spacer is the old height while the cards
// show the new window, so the scroll position would read a row off. A MutationObserver resizes the
// spacers in a microtask off the same DOM patch instead.
//
// State is kept per grid, keyed by an id the component generates, not in one module-level slot: this
// module is shared by every page using the component, and enhanced navigation can bring up the next
// page's grid before the previous page's DisposeAsync runs, which would tear down the new grid's
// listeners. An id rather than the element, because a disposed grid's ElementReference resolves to
// null.

const grids = new Map();

// Returns whether the grid was actually attached. If enhanced navigation swapped the page out before
// this call arrived, the ElementReferences resolve to null, and ResizeObserver.observe(null) would
// throw into OnAfterRenderAsync and kill the circuit.
//
// options: { activePath, gridSelector, cardSelector, scrollStorageKey?, windowStartCookie? }.
// The last two are optional: only a page that wants its scroll position back after a
// back-navigation (Movies) sets them.
export function init(id, dotNetRef, gridWrapper, leadingSpacer, trailingSpacer, options) {
    dispose(id);

    if (![gridWrapper, leadingSpacer, trailingSpacer].every(el => el instanceof Element && el.isConnected)) {
        return false;
    }

    const state = {
        id,
        dotNetRef,
        gridWrapper,
        leadingSpacer,
        trailingSpacer,
        options,
        // Kept in sync with the spacer's data-window-* attributes by syncWindowFromDom below.
        // Everything here is in *item* units, not rows.
        windowStart: 0,
        windowCount: 0,
        totalCount: 0,
        columns: 1,
        inFlight: false,
        pending: false,
        frame: 0,
        lastRequestKey: null,
        onViewportChanged: null,
        resizeObserver: null,
        windowObserver: null,
    };
    state.onViewportChanged = () => schedule(state);
    grids.set(id, state);

    window.addEventListener('scroll', state.onViewportChanged, { passive: true });
    window.addEventListener('resize', state.onViewportChanged, { passive: true });

    // The card height (and so the row height) is driven by the grid's own width — a sidebar
    // collapsing or a window resize changes both the column count and the row height, and the
    // spacers have to be recomputed from the new geometry or the scrollbar stops matching the list.
    if ('ResizeObserver' in window) {
        state.resizeObserver = new ResizeObserver(() => schedule(state));
        state.resizeObserver.observe(gridWrapper);
    }

    state.windowObserver = new MutationObserver(() => syncWindowFromDom(state));
    state.windowObserver.observe(leadingSpacer, {
        attributes: true,
        attributeFilter: ['data-window-start', 'data-window-count', 'data-total-count'],
    });

    // Reads whatever window the current markup already carries — the prerendered response's
    // window, or a persisted-state restore's — rather than assuming zero and waiting for a change.
    syncWindowFromDom(state);
    restoreScrollPosition(state);
    return true;
}

// Needs the spacers already sized to the full filtered-list height (see resizeSpacers), or the
// saved position can't land. syncWindowFromDom() already tries that once, but resizeSpacers() can
// find no rendered card yet; a scrollTo against a still-short document clamps and never scrolls
// again, so this retries each frame until resizeSpacers() confirms real geometry.
function restoreScrollPosition(state) {
    const key = state.options.scrollStorageKey;
    if (!key) return;

    let saved;
    try {
        saved = Number(sessionStorage.getItem(key));
    } catch {
        return;
    }
    if (!saved) return;

    function attempt() {
        if (!isLive(state)) return;
        if (!resizeSpacers(state)) {
            requestAnimationFrame(attempt);
            return;
        }
        window.scrollTo({ top: saved, behavior: 'instant' });
        schedule(state);
    }
    attempt();
}

export function scrollToTop(id) {
    // Explicitly instant: the app sets `scroll-behavior: smooth` on <html>, which would otherwise
    // animate this all the way up from wherever the user was — pulling a window fetch along at
    // every position it passes through on the way.
    window.scrollTo({ top: 0, behavior: 'instant' });
    const state = grids.get(id);
    if (state) schedule(state);
}

export function dispose(id) {
    const state = grids.get(id);
    if (!state) return;
    grids.delete(id);

    window.removeEventListener('scroll', state.onViewportChanged);
    window.removeEventListener('resize', state.onViewportChanged);
    if (state.resizeObserver) state.resizeObserver.disconnect();
    if (state.windowObserver) state.windowObserver.disconnect();
    if (state.frame) cancelAnimationFrame(state.frame);
}

function isLive(state) {
    return grids.get(state.id) === state;
}

// Fires synchronously (a MutationObserver callback runs as a microtask, before the next paint)
// whenever the page's render changes the loaded window — see this file's header comment for why
// that timing, not a separate JS-invokable call, is what keeps the spacer resize and the card swap
// visually atomic.
function syncWindowFromDom(state) {
    if (!isLive(state)) return;

    const data = state.leadingSpacer.dataset;
    state.windowStart = Number(data.windowStart) || 0;
    state.windowCount = Number(data.windowCount) || 0;
    state.totalCount = Number(data.totalCount) || 0;

    resizeSpacers(state);
    schedule(state);
}

// Scroll fires far faster than layout can usefully be re-read, and every evaluate() does a
// synchronous measure — coalesce to one per animation frame.
function schedule(state) {
    if (!isLive(state) || state.frame) return;
    state.frame = requestAnimationFrame(() => {
        state.frame = 0;
        if (!isLive(state)) return;
        evaluate(state);
    });
}

/// Real geometry, read from the rendered grid rather than assumed: the column count the
/// responsive `auto-fill` track list actually resolved to, and the height one row occupies
/// (a card plus the grid's row gap).
function measure(state) {
    const grid = state.gridWrapper.querySelector(state.options.gridSelector);
    const card = grid ? grid.querySelector(state.options.cardSelector) : null;
    if (!grid || !card) return null;

    const gridStyle = getComputedStyle(grid);
    const columns = Math.max(1, gridStyle.gridTemplateColumns.split(' ').filter(s => s.length > 0).length);
    const rowGap = parseFloat(gridStyle.rowGap) || 0;
    const rowHeight = card.getBoundingClientRect().height + rowGap;

    return rowHeight > 0 ? { columns, rowHeight } : null;
}

// Spacers first — they're what makes the scrollbar reflect the whole filtered list, and the
// leading one is the anchor everything below is measured from. Shared between evaluate() (a plain
// scroll, nothing about the window changed) and syncWindowFromDom() (the window just changed and
// this has to run before the next paint, not on the next animation frame).
function resizeSpacers(state) {
    const geometry = measure(state);
    if (!geometry) return null;

    const { columns, rowHeight } = geometry;
    state.columns = columns;

    const leadingRows = Math.floor(state.windowStart / columns);
    const trailingItems = Math.max(0, state.totalCount - state.windowStart - state.windowCount);
    setHeight(state.leadingSpacer, leadingRows * rowHeight);
    setHeight(state.trailingSpacer, Math.ceil(trailingItems / columns) * rowHeight);

    return geometry;
}

function evaluate(state) {
    // Enhanced navigation patches the DOM to the destination page well before the page's async
    // DisposeAsync detaches the listeners above, so a 'scroll'/'resize' event for the *new* page can
    // still reach this stale state and clobber the saved position with the new page's scrollY (usually
    // 0) just before the back-navigation restore needs it.
    //
    // location.pathname, not gridWrapper.isConnected, guards against that: DOM removal can lag the URL
    // update under a fast round trip, while the route updates atomically with the History API call.
    if (location.pathname !== state.options.activePath) return;

    // The grid is hidden (e.g. Movies.razor's "All releases (r18.dev)" browse mode): there is
    // nothing to measure — a display:none card reads as 0px tall and the row gap alone would pass
    // for a real row height — and the page's scroll position belongs to whatever replaced it.
    if (state.gridWrapper.offsetParent === null) return;

    // Recorded unconditionally (whenever we ARE still on this page), not just when the window
    // changes — this is what makes the value read by restoreScrollPosition() reflect wherever the
    // user actually left the grid, including a plain scroll within an already-loaded window that
    // never touches the server.
    const { scrollStorageKey, windowStartCookie } = state.options;
    try {
        if (scrollStorageKey) sessionStorage.setItem(scrollStorageKey, String(window.scrollY));
        if (windowStartCookie) document.cookie = `${windowStartCookie}=${state.windowStart};path=/;samesite=lax`;
    } catch {
        // Storage disabled/full — restoreScrollPosition() just finds nothing next time, same as
        // a first-ever visit.
    }

    const geometry = resizeSpacers(state);
    if (!geometry) return;

    // What App.razor's enhancedload script needs to size the spacers (and so make the document tall
    // enough to scroll) the moment a back-navigation's markup lands, before this module has loaded.
    try {
        if (scrollStorageKey) {
            sessionStorage.setItem(`${scrollStorageKey}:spacers`, JSON.stringify({
                windowStart: state.windowStart,
                leading: state.leadingSpacer.style.height,
                trailing: state.trailingSpacer.style.height,
            }));
        }
    } catch {
        // Same as above: the early restore just doesn't happen.
    }

    const { columns, rowHeight } = geometry;
    if (state.totalCount <= 0) return;

    const contentTop = state.leadingSpacer.getBoundingClientRect().top + window.scrollY;
    const firstVisibleRow = Math.floor((window.scrollY - contentTop) / rowHeight);
    const viewportRows = Math.max(1, Math.ceil(window.innerHeight / rowHeight));

    // Two viewports of overscan either side. One was measurably not enough: the round-trip to
    // fetch a window is itself time the user keeps scrolling through, so a card was landing in the
    // DOM a median of ~400px from the viewport edge — about 120ms of lead at a normal scroll speed,
    // against a ~40ms poster fetch. That left a few percent of cards becoming visible a frame or
    // two before their poster had painted, which is the flash you can just catch. Doubling the
    // overscan buys ~4x the lead time; the card count stays bounded and proportional to the
    // viewport, which is the property that matters for keeping the DOM small.
    const overscanRows = 2 * viewportRows;
    const startRow = Math.max(0, firstVisibleRow - overscanRows);
    const endRow = firstVisibleRow + viewportRows + overscanRows;

    const start = startRow * columns;
    const count = (endRow - startRow + 1) * columns;
    const desiredEnd = Math.min(start + count, state.totalCount);

    if (start >= state.windowStart && desiredEnd <= state.windowStart + state.windowCount) return;

    // Keyed on both the request and the window it was made against, so a request the server can't
    // satisfy exactly (a range clamped to its own maximum window size, say) is asked for once
    // rather than re-sent forever — while any real change to the loaded window frees it to ask again.
    requestWindow(state, start, count, columns, `${start}:${count}:${state.windowStart}:${state.windowCount}:${state.totalCount}`);
}

// Only one range request is ever in flight. A scrollbar drag produces a continuous stream of
// positions and the server can't keep up with all of them — but dropping the ones that arrive
// while a load is running is exactly how the grid used to end up blank after a drag: the last
// position, the one the user actually let go at, was the one dropped, and nothing re-asked for it
// once the scrolling stopped. So the newest request is remembered instead of dropped, and is
// re-evaluated (against the window that has meanwhile been loaded) as soon as the current one lands.
function requestWindow(state, start, count, columns, requestKey) {
    if (state.inFlight) {
        // Deliberately *don't* record the key here: it's only ever set for a request that was
        // really sent, so the re-evaluation after the in-flight one lands is free to send this
        // position for real.
        state.pending = true;
        return;
    }
    if (requestKey === state.lastRequestKey) return;
    state.lastRequestKey = requestKey;

    state.inFlight = true;
    state.dotNetRef.invokeMethodAsync('SetVisibleRange', start, count, columns)
        .catch(() => { })
        .finally(() => {
            if (!isLive(state)) return;
            state.inFlight = false;
            if (state.pending) {
                state.pending = false;
                schedule(state);
            }
        });
}

// Not rounded to whole pixels: a row measured at a fractional height (the Missing page's 49.78px
// rows) made the rounded spacers disagree with the rows they stand in for by up to a
// pixel, so the list's height wobbled as the window slid. The browser lays out in 1/64 px units
// and the measured row height already is one, so the exact product is what the rows would occupy.
function setHeight(element, px) {
    const value = Math.max(0, px) + 'px';
    if (element.style.height !== value) element.style.height = value;
}
