namespace Javbuddy.Services.Api;

/// <summary>Why an API operation didn't (or did) succeed — the endpoint layer maps it to an HTTP status (200/404/409/400/410/502/503)
/// and the MCP tools to a tool error.</summary>
public enum ApiStatus
{
    Ok,
    NotFound,
    Conflict,
    Invalid,
    Upstream,
    NotConfigured,
    Gone,
}

/// <summary>A small discriminated result for <see cref="ILibraryApiService"/>. <see cref="Value"/> is set for Ok, and for
/// Conflict when there is an existing resource to show (a duplicate add carries the movie already in the library).</summary>
public sealed record ApiResult<T>
{
    public ApiStatus Status { get; internal init; }
    public T? Value { get; internal init; }
    public string? Message { get; internal init; }

    /// <summary>The individual validation problems of an Invalid result.</summary>
    public IReadOnlyList<string> Errors { get; internal init; } = [];

    public bool IsOk => Status == ApiStatus.Ok;

    public static ApiResult<T> Ok(T value) => new() { Status = ApiStatus.Ok, Value = value };
    public static ApiResult<T> NotFound(string message) => new() { Status = ApiStatus.NotFound, Message = message };
    public static ApiResult<T> Conflict(string message, T? existing = default) => new() { Status = ApiStatus.Conflict, Message = message, Value = existing };
    public static ApiResult<T> Invalid(params string[] errors) => Invalid((IReadOnlyList<string>)errors);
    public static ApiResult<T> Invalid(IReadOnlyList<string> errors) => new() { Status = ApiStatus.Invalid, Errors = errors, Message = string.Join(" ", errors) };
    public static ApiResult<T> Upstream(string message) => new() { Status = ApiStatus.Upstream, Message = message };
    public static ApiResult<T> Gone(string message) => new() { Status = ApiStatus.Gone, Message = message };
    public static ApiResult<T> NotConfigured(string message) => new() { Status = ApiStatus.NotConfigured, Message = message };

    /// <summary>This failure carrying a different payload type, for passing a failed lookup up through another result.</summary>
    public ApiResult<TOther> AsFailure<TOther>() => new() { Status = Status, Message = Message, Errors = Errors };
}
