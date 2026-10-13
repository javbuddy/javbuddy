// The scene wall's remembered filters. Read here rather than from the request: the
// page isn't prerendered, and a live circuit's HttpContext can hold a stale cookie.
export function getCookie(name) {
    const prefix = `${name}=`;
    const entry = document.cookie.split('; ').find(c => c.startsWith(prefix));
    return entry ? decodeURIComponent(entry.substring(prefix.length)) : null;
}
