// The Movies page's cookie helpers. The grid's virtualization lives in the shared VirtualizedGrid
// component.

// Persists the Poster Options modal's choices across sessions — same "cookie, not localStorage"
// approach App.razor's theme toggle uses, so the value's readable server-side (via
// IHttpContextAccessor) during Movies.razor's own OnInitializedAsync, on both the prerendered
// request and the interactive circuit's reconnect, without waiting on a JS-interop round trip.
export function setPosterOptionsCookie(json) {
    document.cookie = 'poster-options=' + encodeURIComponent(json) + ';path=/;max-age=31536000;samesite=lax';
}

// Persists selected filters/sort the same way as setPosterOptionsCookie above —
// a generic name/json pair rather than a hardcoded cookie name, since Actors.razor and
// Missing.razor each keep their own identical copy of this function for their own view-state
// cookie rather than sharing one: a standalone (non-collocated) JS module isn't picked up by
// Blazor's static-web-asset pipeline the way a Component.razor.js file is, so a shared module
// under Components/Shared/ 404s at runtime with no visible error — confirmed live.
export function setJsonCookie(name, json, maxAgeSeconds) {
    document.cookie = `${name}=${encodeURIComponent(json)};path=/;max-age=${maxAgeSeconds};samesite=lax`;
}
