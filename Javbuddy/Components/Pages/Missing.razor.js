// Persists the selected sort order across refresh/navigation — same generic
// name/json pair pattern as Movies.razor.js's setPosterOptionsCookie/setJsonCookie.
export function setJsonCookie(name, json, maxAgeSeconds) {
    document.cookie = `${name}=${encodeURIComponent(json)};path=/;max-age=${maxAgeSeconds};samesite=lax`;
}
