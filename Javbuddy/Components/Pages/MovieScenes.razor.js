// The scene wall's remembered filters. Read here rather than from the request: the
// page isn't prerendered, and a live circuit's HttpContext can hold a stale cookie.
// setJsonCookie is this page's own copy of the helper Movies/Actors/Missing each keep, since a
// shared non-collocated JS module isn't served (see Movies.razor.js).
export function getCookie(name) {
    const prefix = `${name}=`;
    const entry = document.cookie.split('; ').find(c => c.startsWith(prefix));
    return entry ? decodeURIComponent(entry.substring(prefix.length)) : null;
}

export function setJsonCookie(name, json, maxAgeSeconds) {
    document.cookie = `${name}=${encodeURIComponent(json)};path=/;max-age=${maxAgeSeconds};samesite=lax`;
}
