using System.ComponentModel;
using Javbuddy.Services.Api;
using Javbuddy.Services.Api.Contracts;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Javbuddy.Mcp;

/// <summary>MCP tools to find, read and edit movies in the Javbuddy library. Movies are addressed by their code (e.g. "ABP-123").</summary>
[McpServerToolType]
public sealed class MovieTools(ILibraryApiService library, IHttpContextAccessor http)
{
    private HttpRequest? Request => http.HttpContext?.Request;

    [McpServerTool(Name = "search_movies", ReadOnly = true, OpenWorld = false)]
    [Description("Lists or searches movies in the Javbuddy library, newest added first. Every filter is optional and filters combine. "
        + "Returns { total, items } where total counts all matches and items is one page of summaries (id, code, title, status, studio, "
        + "nfoDriftKind, webUrl, ...). Use limit/offset to page.")]
    public async Task<CallToolResult> SearchMovies(
        [Description("Free text matched against code, title, studio and cast.")] string? query = null,
        [Description("'missing' (wanted, not in the library files) or 'got' (owned).")] string? status = null,
        [Description("Exact studio name.")] string? studio = null,
        [Description("Tag name; use list_tags to find names.")] string? tag = null,
        [Description("Filter by .nfo drift state: none, javbuddyChanged, externalEdit, bothChanged or unreadable. 'javbuddyChanged' means database edits not yet written to the .nfo.")] string? nfo_drift = null,
        [Description("Page size, 1 to 200.")] int limit = 25,
        [Description("Number of matches to skip.")] int offset = 0,
        CancellationToken ct = default)
    {
        if (limit is < 1 or > LibraryApiService.MaxPageSize) return McpResults.Error($"Invalid input: limit must be between 1 and {LibraryApiService.MaxPageSize}.");
        if (offset < 0) return McpResults.Error("Invalid input: offset must not be negative.");

        var result = await library.ListAsync(new MovieListQuery(query, status, studio, tag, null, nfo_drift, offset, limit), ct);
        return McpResults.From(result, page => ApiLinks.WithUrl(Request, page));
    }

    [McpServerTool(Name = "get_movie", ReadOnly = true, OpenWorld = false)]
    [Description("Gets one movie's full detail by code: metadata fields (title, titleOverride, originalTitle, description, releaseDate, studio, label, series, "
        + "director, contentType), status, tags (id, name), cast (name, actorId), files, .nfo drift state and an absolute webUrl to open it in Javbuddy. "
        + "The code is matched case-insensitively and tolerates zero-padding (abp123, ABP-00123 and abp-123 all find ABP-123).")]
    public async Task<CallToolResult> GetMovie(
        [Description("Movie code, e.g. 'ABP-123'.")] string code,
        CancellationToken ct = default) =>
        McpResults.From(await library.GetDetailAsync(code, ct), detail => ApiLinks.WithUrl(Request, detail));

    [McpServerTool(Name = "fetch_javinizer_metadata", ReadOnly = true, OpenWorld = true)]
    [Description("Scrapes a movie's metadata through javinizer-go (an external service) for comparison and returns it: title, originalTitle, description, "
        + "releaseDate, studio, label, series, director, runtimeMinutes, cast and genres. Nothing is stored; read the result and apply the fields you "
        + "want with update_movie_metadata. Fails if javinizer-go is not configured.")]
    public async Task<CallToolResult> FetchJavinizerMetadata(
        [Description("Movie code, e.g. 'ABP-123'. The movie must be in the library.")] string code,
        CancellationToken ct = default) =>
        McpResults.From(await library.FetchJavinizerAsync(code, ct), preview => preview);

    [McpServerTool(Name = "update_movie_metadata", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Edits some of a movie's metadata fields and leaves the rest untouched. Values in 'set' replace the field; names in 'clear' empty it. "
        + "Provide at least one of them (an empty call is rejected), and never the same field in both. "
        + "Editable fields: title (the fetched title), titleOverride (a manual title that wins over title), originalTitle, description, "
        + "releaseDate (format YYYY-MM-DD), studio, label, series, director, and contentType (censored, uncensored or decensored; it can be set but not cleared). "
        + "Text values must not be blank; to empty a field list it in 'clear'. "
        + "This changes Javbuddy's database only: the movie's .nfo file on disk is NOT updated until you call push_nfo (check with preview_nfo first). "
        + "Returns { movie, warning? } with the updated movie.")]
    public async Task<CallToolResult> UpdateMovieMetadata(
        [Description("Movie code, e.g. 'ABP-123'.")] string code,
        [Description("Fields to set, e.g. { \"description\": \"...\", \"releaseDate\": \"2023-05-01\" }. Only include the fields you want to change.")] MetadataFields? set = null,
        [Description("Names of fields to empty, from: title, titleOverride, originalTitle, description, releaseDate, studio, label, series, director.")] string[]? clear = null,
        CancellationToken ct = default) =>
        McpResults.From(await library.PatchMetadataAsync(code, new MetadataPatchRequest(set, clear), ct), r => ApiLinks.WithUrl(Request, r));
}
