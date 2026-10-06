// A plain left-click on a clip card opens the clip player instead of following the card's
// link to Movie Detail; ctrl/meta/shift/alt- and middle-clicks keep the browser's own behavior.
// Blazor's link interception skips a click whose default is already prevented. The host binds it to
// an element around its cards that outlives them (the Scenes wall's root, an Actor Detail clip
// section), which runs before Blazor's document listener.
export function preventPlainCardClicks(container) {
    container.addEventListener('click', e => {
        if (e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
        if (e.target.closest('.scene-wall-card')) e.preventDefault();
    });
}

// A card's tag row stays on one line: the chips that don't fit, with room left for a
// "+N" badge, are marked data-cut, and the row data-overflow, which ClipCard.razor.css hides. Hovering
// the badge marks the row data-open, revealing them, until the pointer leaves the row. Only attributes
// Blazor never renders are set, so its re-renders keep them. Bound like preventPlainCardClicks: rows the
// container gains later (Show more, the wall's virtualization) or whose tags change are measured too,
// and every row again when its card is resized.
export function measureTagOverflow(container) {
    container.addEventListener('mouseover', e => {
        e.target.closest?.('.scene-wall-tags-more')?.parentElement.setAttribute('data-open', '');
    });
    container.addEventListener('mouseout', e => {
        const row = e.target.closest?.('.scene-wall-tags[data-open]');
        if (!row || row.contains(e.relatedTarget)) return;
        row.removeAttribute('data-open');
        if (stale.delete(row)) measureTags(row.parentElement);
    });

    const resizes = new ResizeObserver(entries => entries.forEach(e => measureTags(e.target)));
    const observe = root => root.querySelectorAll?.('.scene-wall-tags-slot').forEach(slot => resizes.observe(slot));
    new MutationObserver(mutations => {
        const changed = new Set();
        for (const m of mutations) {
            m.removedNodes.forEach(node => node.querySelectorAll?.('.scene-wall-tags-slot').forEach(slot => resizes.unobserve(slot)));
            m.addedNodes.forEach(observe);
            const slot = (m.target.nodeType === Node.ELEMENT_NODE ? m.target : m.target.parentElement)?.closest('.scene-wall-tags-slot');
            if (slot) changed.add(slot);
        }
        changed.forEach(measureTags);
    }).observe(container, { childList: true, subtree: true, characterData: true });
    observe(container);
}

// Open rows whose measuring waits for them to close.
const stale = new WeakSet();

function measureTags(slot) {
    if (!slot.isConnected) return;
    const row = slot.querySelector('.scene-wall-tags');
    const badge = row?.querySelector('.scene-wall-tags-more');
    if (!badge) return;
    // An open tray wraps its chips, so it's measured once it closes.
    if (row.hasAttribute('data-open')) {
        stale.add(row);
        return;
    }
    const chips = [...row.querySelectorAll('.scene-wall-tag')];
    chips.forEach(chip => chip.removeAttribute('data-cut'));
    row.removeAttribute('data-overflow');
    if (row.scrollWidth <= row.clientWidth) return;

    // Measured at its widest, so whatever count it ends up with still fits.
    badge.dataset.count = `+${chips.length}`;
    row.setAttribute('data-overflow', '');
    const limit = row.getBoundingClientRect().right - badge.offsetWidth - parseFloat(getComputedStyle(row).columnGap || '0');
    let shown = 0;
    while (shown < chips.length && chips[shown].getBoundingClientRect().right <= limit) shown++;
    chips.slice(shown).forEach(chip => chip.setAttribute('data-cut', ''));
    badge.dataset.count = `+${chips.length - shown}`;
}
