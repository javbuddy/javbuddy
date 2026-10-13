using System.Text.Json;
using Microsoft.JSInterop;

namespace Javbuddy.Services.Infrastructure;

/// <summary>Reads a per-browser preference JSON-encoded in a cookie, tolerant of a missing or
/// stale cookie (no cookie, or one written before a property existed/was renamed) by falling
/// back to null rather than throwing — callers keep their own field defaults in that case. Mirrors
/// the pattern every page that persists a UI preference this way (poster options, grid scroll
/// window) already hand-rolled individually before this was extracted.</summary>
public static class JsonCookieState
{
    public static T? TryLoad<T>(IHttpContextAccessor? httpContextAccessor, string cookieName) where T : class =>
        TryParse<T>(httpContextAccessor?.HttpContext?.Request.Cookies[cookieName]);

    /// <summary>The same, for a cookie value read from the browser instead of the request.</summary>
    public static T? TryParse<T>(string? raw) where T : class
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private const int OneYearSeconds = 31536000;

    /// <summary>Writes <paramref name="state"/> to the cookie through the global
    /// <c>lsJsonCookie.set</c> (App.razor), so a page needs no JS module of its own for it. A circuit
    /// already gone is ignored: the preference is best-effort.</summary>
    public static async Task SaveAsync<T>(IJSRuntime js, string cookieName, T state)
    {
        try
        {
            await js.InvokeVoidAsync("lsJsonCookie.set", cookieName, JsonSerializer.Serialize(state), OneYearSeconds);
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
