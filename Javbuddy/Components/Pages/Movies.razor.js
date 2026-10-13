// The Movies page's cookie helpers. The grid's virtualization lives in the shared VirtualizedGrid
// component.

// Persists the Poster Options modal's choices across sessions — same "cookie, not localStorage"
// approach App.razor's theme toggle uses, so the value's readable server-side (via
// IHttpContextAccessor) during Movies.razor's own OnInitializedAsync, on both the prerendered
// request and the interactive circuit's reconnect, without waiting on a JS-interop round trip.
export function setPosterOptionsCookie(json) {
    document.cookie = 'poster-options=' + encodeURIComponent(json) + ';path=/;max-age=31536000;samesite=lax';
}
