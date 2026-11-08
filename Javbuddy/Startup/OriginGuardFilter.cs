namespace Javbuddy.Startup;

/// <summary>
/// Not authentication — a guard against a web page in the user's browser calling the unauthenticated API cross-site (CSRF) or
/// via DNS rebinding (the MCP spec requires Origin validation on HTTP servers). A request without an <c>Origin</c> header (curl,
/// CLI tools, MCP clients) passes; one with an Origin passes only when it is the app's own origin or is listed in the
/// <c>Api:AllowedOrigins</c> setting (env <c>Api__AllowedOrigins</c>, comma-separated, empty by default).
/// </summary>
public sealed class OriginGuardFilter(IConfiguration configuration) : IEndpointFilter
{
    public const string AllowedOriginsKey = "Api:AllowedOrigins";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.Headers.TryGetValue("Origin", out var origin)
            && !IsAllowed(origin.ToString(), $"{request.Scheme}://{request.Host}", ParseAllowed(configuration[AllowedOriginsKey])))
        {
            return Results.Problem(
                title: "Origin not allowed",
                detail: $"Requests from the origin '{origin}' are not allowed. Add it to Api__AllowedOrigins to permit it.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }

    /// <summary>The rule: the origin is the request's own (<paramref name="ownOrigin"/>, "scheme://host[:port]") or listed.
    /// Comparison ignores case and a trailing slash; the literal "null" origin (sandboxed pages, file://) is never the app's own.</summary>
    public static bool IsAllowed(string origin, string ownOrigin, IReadOnlyCollection<string> allowedOrigins)
    {
        var candidate = Normalize(origin);
        if (candidate.Length == 0)
        {
            return false;
        }

        return string.Equals(candidate, Normalize(ownOrigin), StringComparison.OrdinalIgnoreCase)
            || allowedOrigins.Any(a => string.Equals(candidate, Normalize(a), StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyCollection<string> ParseAllowed(string? setting) =>
        (setting ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Normalize(string origin) => origin.Trim().TrimEnd('/');
}

public static class OriginGuardExtensions
{
    /// <summary>Applies <see cref="OriginGuardFilter"/> to every endpoint of the group, whatever the HTTP method.</summary>
    public static TBuilder RequireAllowedOrigin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter<TBuilder, OriginGuardFilter>();
}
