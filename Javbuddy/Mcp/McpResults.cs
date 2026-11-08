using System.Text.Json;
using Javbuddy.Services.Api;
using ModelContextProtocol.Protocol;

namespace Javbuddy.Mcp;

/// <summary>Turns the facade's <see cref="ApiResult{T}"/> into an MCP tool result: the DTO as JSON text (the same camelCase,
/// string-enum shape as the REST API) on success, or an <c>isError</c> result carrying a message the model can act on.</summary>
public static class McpResults
{
    public static CallToolResult Success(object? value) => new()
    {
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(value, ApiJson.Options) }],
    };

    public static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }],
    };

    /// <summary>The success result with <paramref name="map"/> applied to the value, or the error result for a failed one.</summary>
    public static CallToolResult From<T, TOut>(ApiResult<T> result, Func<T, TOut> map) =>
        result.IsOk ? Success(map(result.Value!)) : Error(ErrorMessage(result));

    public static string ErrorMessage<T>(ApiResult<T> result)
    {
        var message = string.IsNullOrWhiteSpace(result.Message) ? "The request failed." : result.Message;
        return result.Status switch
        {
            ApiStatus.NotFound => message,
            ApiStatus.Invalid => $"Invalid input: {message}",
            ApiStatus.Conflict => $"Conflict: {message}",
            ApiStatus.Upstream => $"An integration failed: {message}",
            ApiStatus.NotConfigured => $"Not available: {message}",
            ApiStatus.Gone => $"Gone: {message}",
            _ => message,
        };
    }
}
