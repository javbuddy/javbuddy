// The Actors page's cookie helper. The grid's virtualization lives in the shared VirtualizedGrid
// component.

// Persists selected filters/sort — a generic name/json pair, same pattern as
// Movies.razor.js's setPosterOptionsCookie. Not shared across pages as a standalone module: a
// non-collocated JS file under Components/Shared/ isn't picked up by Blazor's static-web-asset
// pipeline the way a Component.razor.js file is, so it 404s at runtime with no visible error —
// confirmed live.
export function setJsonCookie(name, json, maxAgeSeconds) {
    document.cookie = `${name}=${encodeURIComponent(json)};path=/;max-age=${maxAgeSeconds};samesite=lax`;
}
