using System.Text.Json;

namespace Javbuddy.Startup;

/// <summary>Request-body handling for the write routes: JSON only, unknown properties rejected, and a malformed body answered as a 400 problem.</summary>
public static class ApiBody
{
    private static readonly JsonSerializerOptions Strict = new(ApiResults.Json)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Whether the request carries a body (a Content-Length above zero, or a chunked transfer).</summary>
    public static bool HasBody(HttpRequest request) =>
        request.ContentLength > 0 || (request.ContentLength is null && request.Headers.ContainsKey("Transfer-Encoding"));

    public static bool IsJson(string? contentType) =>
        contentType is not null
        && Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var parsed)
        && (parsed.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || (parsed.MediaType.HasValue && parsed.MediaType.Value.EndsWith("+json", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Reads the JSON body as <typeparamref name="T"/>. An absent body gives <c>default</c> with no error; a bad one gives a 400 problem.</summary>
    public static async Task<(T? Value, IResult? Error)> ReadAsync<T>(HttpRequest request, CancellationToken ct) where T : class
    {
        if (!HasBody(request))
        {
            return (null, null);
        }

        try
        {
            return (await JsonSerializer.DeserializeAsync<T>(request.Body, Strict, ct), null);
        }
        catch (JsonException ex)
        {
            return (null, ApiResults.BadRequest($"The request body is not valid: {ex.Message}"));
        }
    }
}

/// <summary>Rejects any mutating request that has a body or a Content-Type other than JSON with 415. Together with the Origin
/// guard this stops a cross-site "simple" request (text/plain or form-encoded POST) from reaching a write route.</summary>
public sealed class RequireJsonBodyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        var mutating = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method);
        if (mutating
            && (ApiBody.HasBody(request) || !string.IsNullOrEmpty(request.ContentType))
            && !ApiBody.IsJson(request.ContentType))
        {
            return Results.Problem(
                title: "Unsupported media type",
                detail: "Send request bodies as application/json.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        return await next(context);
    }
}
