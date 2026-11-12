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
        // Scroll speed in px/ms (positive = down), smoothed, and how long a window round trip takes in ms (smoothed).
        // Together they say how far ahead of the viewport the window has to reach to be there when it arrives.
        lastY: null,
        lastTime: 0,
        velocity: 0,
        roundTrip: 120,
        onViewportChanged: null,
        onPageHide: null,
        // What evaluate() last saw worth persisting, and the timer that writes it; see schedulePersist.
        persist: null,
        persistTimer: 0,
        resizeObserver: null,
        windowObserver: null,
    };
    state.onViewportChanged = () => schedule(state);
    grids.set(id, state);

    window.addEventListener('scroll', state.onViewportChanged, { passive: true });
    window.addEventListener('resize', state.onViewportChanged, { passive: true });
    state.onPageHide = () => flushPersist(state);
    window.addEventListener('pagehide', state.onPageHide);

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
    // Explicitly instant, so this can never animate all the way up from wherever the user was
    // (pulling a window fetch along at every position it passes through) even if a `scroll-behavior:
    // smooth` rule ever returns; app.css currently sets `auto` on <html>.
    window.scrollTo({ top: 0, behavior: 'instant' });
    const state = grids.get(id);
    if (state) schedule(state);
}

export function dispose(id) {
    const state = grids.get(id);
    if (!state) return;
    grids.delete(id);

    // Last position out before the listeners go: a throttled write may still be waiting, and enhanced
    // navigation has usually already moved window.scrollY on to the next page, so the remembered
    // values are the only correct ones.
    flushPersist(state);

    window.removeEventListener('scroll', state.onViewportChanged);
    window.removeEventListener('resize', state.onViewportChanged);
    window.removeEventListener('pagehide', state.onPageHide);
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
    const columnGap = parseFloat(gridStyle.columnGap) || 0;
    const cardRect = card.getBoundingClientRect();
    const rowHeight = cardRect.height + rowGap;

    return rowHeight > 0
        ? { columns, rowHeight, cardWidth: cardRect.width, cardHeight: cardRect.height, columnGap, cardLeft: cardRect.left }
        : null;
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
    paintSkeleton(state.leadingSpacer, geometry, false, 0);
    // The list's last row is usually partial, and it's the trailing spacer's last row.
    paintSkeleton(state.trailingSpacer, geometry, true, trailingItems % columns);

    return geometry;
}

const PERSIST_INTERVAL_MS = 250;

// Scrolling runs evaluate() every frame, but a sessionStorage write, a JSON.stringify and a cookie
// write per frame are wasted work: only the value at rest matters. A trailing timer writes the latest
// remembered values at most every PERSIST_INTERVAL_MS; dispose() and 'pagehide' flush the rest so the
// last position is never lost.
function schedulePersist(state) {
    if (state.persistTimer) return;
    state.persistTimer = setTimeout(() => flushPersist(state), PERSIST_INTERVAL_MS);
}

function flushPersist(state) {
    if (state.persistTimer) {
        clearTimeout(state.persistTimer);
        state.persistTimer = 0;
    }
    const persist = state.persist;
    if (!persist) return;
    state.persist = null;

    const { scrollStorageKey, windowStartCookie } = state.options;
    try {
        if (scrollStorageKey) sessionStorage.setItem(scrollStorageKey, String(persist.scrollY));
        if (windowStartCookie) document.cookie = `${windowStartCookie}=${persist.windowStart};path=/;samesite=lax`;
        if (scrollStorageKey && persist.spacers) {
            sessionStorage.setItem(`${scrollStorageKey}:spacers`, JSON.stringify(persist.spacers));
        }
    } catch {
        // Storage disabled/full — restoreScrollPosition() and App.razor's early restore just find
        // nothing next time, same as a first-ever visit.
    }
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
    // never touches the server. Remembered here every frame (cheap), written to storage and the
    // cookie by schedulePersist at most every PERSIST_INTERVAL_MS.
    state.persist = { ...state.persist, scrollY: window.scrollY, windowStart: state.windowStart };

    const geometry = resizeSpacers(state);
    if (!geometry) {
        schedulePersist(state);
        return;
    }

    // What App.razor's enhancedload script needs to size the spacers (and so make the document tall
    // enough to scroll) the moment a back-navigation's markup lands, before this module has loaded.
    state.persist.spacers = {
        windowStart: state.windowStart,
        leading: state.leadingSpacer.style.height,
        trailing: state.trailingSpacer.style.height,
    };
    schedulePersist(state);

    const { columns, rowHeight } = geometry;
    if (state.totalCount <= 0) return;

    const contentTop = state.leadingSpacer.getBoundingClientRect().top + window.scrollY;
    const firstVisibleRow = Math.floor((window.scrollY - contentTop) / rowHeight);
    const viewportRows = Math.max(1, Math.ceil(window.innerHeight / rowHeight));
    trackScrollSpeed(state);

    // Two viewports of overscan either side, at rest or scrolling slowly. One was measurably not enough: the
    // round-trip to fetch a window is itself time the user keeps scrolling through, so a card was landing in the
    // DOM a median of ~400px from the viewport edge — about 120ms of lead at a normal scroll speed, against a
    // ~40ms poster fetch. Doubling the overscan bought ~4x the lead time while the card count stays bounded and
    // proportional to the viewport.
    //
    // A fling outruns any fixed overscan once the round trip is longer than a LAN's (the app is often reached
    // over a VPN or a tunnel): at 6000px/s and a 150ms round trip the viewport is already 900px further on by
    // the time the window it asked for lands, and a harder fling never catches up. So in the direction of travel
    // the window reaches as far as the viewport is expected to move in a round trip (plus margin), capped so the
    // DOM stays bounded.
    const baseRows = 2 * viewportRows;
    const pxPerRow = rowHeight;
    const expectedTravelPx = Math.abs(state.velocity) * (state.roundTrip + 80);
    const aheadRows = Math.min(8 * viewportRows, Math.max(baseRows, Math.ceil(expectedTravelPx / pxPerRow) + viewportRows));
    const movingDown = state.velocity > 0.05;
    const movingUp = state.velocity < -0.05;

    // What each side asks for: a little more than is needed (so the next request isn't due right away), within
    // what one request may hold. In a dense grid (many columns, so few rows per request) that budget can be
    // smaller than the wish; the viewport itself always comes first, and the shortfall is taken from the side
    // the user is moving away from, never from the side they are heading towards.
    const { behind, forward } = allocateRows(
        Math.ceil((movingUp ? aheadRows : baseRows) * 1.25),
        Math.ceil((movingDown ? aheadRows : baseRows) * 1.25),
        Math.floor(MAX_REQUEST_ITEMS / columns) - viewportRows,
        movingDown, movingUp);

    // Whether the loaded window already reaches far enough each way. A side that runs into the list's end needs
    // nothing more. Asking before the margin is gone (at 60% of the wish, so 48% of what a request holds) keeps
    // the next window on its way while the viewport is still inside the current one. Measured against what
    // allocateRows can actually deliver, so a side the budget has squeezed doesn't ask on every row.
    const totalRows = Math.ceil(state.totalCount / columns);
    const windowFirstRow = Math.floor(state.windowStart / columns);
    const windowEndRow = Math.ceil((state.windowStart + state.windowCount) / columns);
    const behindMargin = windowFirstRow <= 0 ? Infinity : firstVisibleRow - windowFirstRow;
    const forwardMargin = windowEndRow >= totalRows ? Infinity : windowEndRow - (firstVisibleRow + viewportRows);
    if (behindMargin >= Math.floor(behind * 0.48) && forwardMargin >= Math.floor(forward * 0.48) && state.windowCount > 0) return;

    const startRow = Math.max(0, firstVisibleRow - behind);
    const endRow = firstVisibleRow + viewportRows + forward;

    const start = startRow * columns;
    const count = Math.min(MAX_REQUEST_ITEMS, (endRow - startRow + 1) * columns);

    // Keyed on both the request and the window it was made against, so a request the server can't
    // satisfy exactly (a range clamped to its own maximum window size, say) is asked for once
    // rather than re-sent forever — while any real change to the loaded window frees it to ask again.
    requestWindow(state, start, count, columns, `${start}:${count}:${state.windowStart}:${state.windowCount}:${state.totalCount}`);
}

// Splits the spare rows of one request (the budget minus the viewport) between behind and forward. Each side gets
// what it wished for if both fit; otherwise the side being moved away from gives way first, but keeps at least a
// quarter of the spare rows, and at rest the two share it evenly.
function allocateRows(wishBehind, wishForward, spare, movingDown, movingUp) {
    spare = Math.max(0, spare);
    if (wishBehind + wishForward <= spare) return { behind: wishBehind, forward: wishForward };

    const floor = Math.floor(spare / 4);
    if (movingDown) {
        const forward = Math.min(wishForward, spare - Math.min(wishBehind, floor));
        return { behind: Math.min(wishBehind, spare - forward), forward };
    }
    if (movingUp) {
        const behind = Math.min(wishBehind, spare - Math.min(wishForward, floor));
        return { behind, forward: Math.min(wishForward, spare - behind) };
    }
    const half = Math.floor(spare / 2);
    const behind = Math.min(wishBehind, Math.max(half, spare - wishForward));
    return { behind, forward: Math.min(wishForward, spare - behind) };
}

// A card-count ceiling for one request, below every grid page's own cap (500 on Actors and Discover, 1000 elsewhere) so one that
// stays under it is never clamped — a clamped one would be re-sent forever (see the request key below).
const MAX_REQUEST_ITEMS = 480;

// Scroll speed from the change since the last evaluation, smoothed so one odd frame doesn't swing it. A jump of
// several viewports in one step (a scroll restore, "back to top") is a teleport, not a speed, and is ignored.
function trackScrollSpeed(state) {
    const now = performance.now();
    const y = window.scrollY;
    if (state.lastY !== null && now > state.lastTime) {
        const dy = y - state.lastY;
        const speed = Math.abs(dy) > 3 * window.innerHeight ? 0 : dy / (now - state.lastTime);
        // After a pause the old speed is stale; weight the new sample by how long it has been.
        const weight = now - state.lastTime > 150 ? 1 : 0.5;
        state.velocity = state.velocity * (1 - weight) + speed * weight;
    }
    state.lastY = y;
    state.lastTime = now;
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
    const sentAt = performance.now();
    state.dotNetRef.invokeMethodAsync('SetVisibleRange', start, count, columns)
        .then(() => {
            // Smoothed, and bounded so one stalled request can't make every window enormous.
            state.roundTrip = 0.6 * state.roundTrip + 0.4 * Math.min(1500, performance.now() - sentAt);
        })
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

// Scrolling past the loaded window lands on a spacer until the server returns the next one, which
// used to be a blank page. The spacers instead tile a neutral card-shaped placeholder, one per
// card slot, sized and positioned from a real card, so a fast scroll shows the grid's shape right
// away and the real cards replace it in place. Drawn as a repeating background, so it costs no DOM
// nodes however tall the spacer is.
//
// A grid whose cards touch (one column, no row gap: the Missing list, the Actors table) is not a grid of
// card shapes, so tiling it would only paint one grey band; it gets no skeleton.
//
// partialColumns: when the spacer's last row holds fewer cards than the grid has columns (the end of the
// list), only that many columns of it are painted, so the placeholders don't promise cards that won't exist.
function paintSkeleton(spacer, { columns, cardWidth, cardHeight, columnGap, rowHeight, cardLeft }, isTrailing, partialColumns) {
    const rowGap = rowHeight - cardHeight;
    const flat = columns === 1 && rowGap < 0.5;
    const tileWidth = cardWidth + columnGap;
    const key = flat ? 'flat' : `${tileWidth}|${rowHeight}|${cardWidth}|${cardHeight}|${cardLeft}|${isTrailing}|${partialColumns}`;
    if (spacer.dataset.skeleton === key) return;
    spacer.dataset.skeleton = key;
    if (flat) {
        spacer.style.backgroundImage = '';
        setMask(spacer, '');
        return;
    }

    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${tileWidth}" height="${rowHeight}">`
        + `<rect width="${cardWidth}" height="${cardHeight}" rx="4" fill="rgba(128,128,128,0.14)"/></svg>`;
    spacer.style.backgroundImage = `url("data:image/svg+xml,${encodeURIComponent(svg)}")`;
    spacer.style.backgroundSize = `${tileWidth}px ${rowHeight}px`;
    spacer.style.backgroundRepeat = 'repeat';
    // The grid's left edge relative to the spacer, so the tiles line up with the cards that replace them.
    // The leading spacer ends with the gap after its last row, so its tiles start flush at the top. The trailing
    // one starts at the last real row's bottom edge, which has no gap below it yet, so its first row of tiles
    // is pushed down by one gap to land where the real row will.
    const left = cardLeft - spacer.getBoundingClientRect().left;
    spacer.style.backgroundPosition = `${left}px ${isTrailing ? rowGap : 0}px`;

    // Everything but the last row stays; the last row is kept only as far as its real cards reach.
    setMask(spacer, partialColumns > 0
        ? `linear-gradient(#000,#000) 0 0 / 100% calc(100% - ${rowHeight}px) no-repeat, `
            + `linear-gradient(#000,#000) 0 100% / ${left + partialColumns * tileWidth}px ${rowHeight}px no-repeat`
        : '');
}

function setMask(element, value) {
    element.style.mask = value;
    element.style.webkitMask = value;
}

// Not rounded to whole pixels: a row measured at a fractional height (the Missing page's 49.78px
// rows) made the rounded spacers disagree with the rows they stand in for by up to a
// pixel, so the list's height wobbled as the window slid. The browser lays out in 1/64 px units
// and the measured row height already is one, so the exact product is what the rows would occupy.
function setHeight(element, px) {
    const value = Math.max(0, px) + 'px';
    if (element.style.height !== value) element.style.height = value;
}
