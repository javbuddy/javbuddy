using System.ComponentModel;
using Javbuddy.Services.Api;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Javbuddy.Mcp;

/// <summary>MCP tools for the tag library. Tags are a curated set: movies can only get existing tags, and tags can't be created here.</summary>
[McpServerToolType]
public sealed class TagTools(ILibraryApiService library, IHttpContextAccessor http)
{
    [McpServerTool(Name = "list_tags", ReadOnly = true, OpenWorld = false)]
    [Description("Searches Javbuddy's tag library by name (or lists tags without a query). Returns [{ id, name, parent }]. "
        + "Tags are curated: add_movie_tag only accepts an id returned by this tool, and new tags can't be created through MCP.")]
    public async Task<CallToolResult> ListTags(
        [Description("Part of a tag name. Omit to list tags.")] string? query = null,
        CancellationToken ct = default) =>
        McpResults.Success(await library.SearchTagsAsync(query, ct));

    [McpServerTool(Name = "add_movie_tag", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Adds an existing tag to a movie (no change when it already has it). tag_id must come from list_tags. "
        + "Returns the updated movie. Database only; the .nfo file is not updated until push_nfo.")]
    public async Task<CallToolResult> AddMovieTag(
        [Description("Movie code, e.g. 'ABP-123'.")] string code,
        [Description("Tag id from list_tags.")] int tag_id,
        CancellationToken ct = default) =>
        McpResults.From(await library.AddTagAsync(code, tag_id, ct), d => ApiLinks.WithUrl(http.HttpContext?.Request, d));

    [McpServerTool(Name = "remove_movie_tag", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Removes a tag that was added to a movie directly (no change when it hasn't it). A tag the movie only carries through its scenes or highlights "
        + "(fromClips) stays. Returns the updated movie. Database only; the .nfo file is not updated until push_nfo.")]
    public async Task<CallToolResult> RemoveMovieTag(
        [Description("Movie code, e.g. 'ABP-123'.")] string code,
        [Description("Tag id from list_tags or the movie's tags.")] int tag_id,
        CancellationToken ct = default) =>
        McpResults.From(await library.RemoveTagAsync(code, tag_id, ct), d => ApiLinks.WithUrl(http.HttpContext?.Request, d));
}
