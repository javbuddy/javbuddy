using System.Text.Json;
using System.Text.Json.Serialization;
using Javbuddy.Services.Api;

namespace Javbuddy.Startup;

/// <summary>Turns the facade's <see cref="ApiResult{T}"/> into the REST response: JSON for success, an RFC 7807 problem otherwise.</summary>
public static class ApiResults
{
    /// <summary>camelCase with enums as strings. Scoped to the API's own responses (via <see cref="Results.Json(object?, JsonSerializerOptions?, string?, int?)"/>)
    /// rather than the app-wide HTTP JSON options, so the other endpoints can't change.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static int StatusCodeOf(ApiStatus status) => status switch
    {
        ApiStatus.Ok => StatusCodes.Status200OK,
        ApiStatus.NotFound => StatusCodes.Status404NotFound,
        ApiStatus.Conflict => StatusCodes.Status409Conflict,
        ApiStatus.Invalid => StatusCodes.Status400BadRequest,
        ApiStatus.Upstream => StatusCodes.Status502BadGateway,
        ApiStatus.NotConfigured => StatusCodes.Status503ServiceUnavailable,
        ApiStatus.Gone => StatusCodes.Status410Gone,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static IResult JsonOk(object? value, int statusCode = StatusCodes.Status200OK) =>
        Results.Json(value, Json, statusCode: statusCode);

    /// <summary>200 with <paramref name="map"/> applied to the value, or the matching problem response.</summary>
    public static IResult ToHttp<T, TOut>(this ApiResult<T> result, Func<T, TOut> map) =>
        result.IsOk ? JsonOk(map(result.Value!)) : Problem(result);

    public static IResult Problem<T>(ApiResult<T> result) => result.Status switch
    {
        ApiStatus.Invalid => Results.ValidationProblem(
            new Dictionary<string, string[]> { ["request"] = [.. result.Errors] },
            title: "The request is not valid.",
            statusCode: StatusCodes.Status400BadRequest),
        _ => Results.Problem(
            detail: result.Message,
            statusCode: StatusCodeOf(result.Status),
            title: result.Status switch
            {
                ApiStatus.NotFound => "Not found",
                ApiStatus.Conflict => "Conflict",
                ApiStatus.Upstream => "Upstream service error",
                ApiStatus.NotConfigured => "Integration not configured",
                ApiStatus.Gone => "Gone",
                _ => "Request failed",
            }),
    };

    public static IResult BadRequest(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [message] }, title: "The request is not valid.");
}
