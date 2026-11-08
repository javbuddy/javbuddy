using System.ComponentModel;
using Javbuddy.Services.Api;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Javbuddy.Mcp;

/// <summary>MCP tools for a movie's cast. Only actors already tracked in Javbuddy can be linked.</summary>
[McpServerToolType]
public sealed class CastTools(ILibraryApiService library, IHttpContextAccessor http)
{
    [McpServerTool(Name = "search_actors", ReadOnly = true, OpenWorld = false)]
    [Description("Searches Javbuddy's tracked actors by name or alternative name. Returns [{ id, name }]. add_movie_cast only accepts an id returned by this tool.")]
    public async Task<CallToolResult> SearchActors(
        [Description("Part of an actor's name.")] string query,
        CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(query)
            ? McpResults.Error("Invalid input: query is required.")
            : McpResults.Success(await library.SearchActorsAsync(query, ct));

    [McpServerTool(Name = "add_movie_cast", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Links a tracked actor to a movie's cast (no change when already linked). actor_id must come from search_actors. "
        + "Returns the updated movie. Database only; the .nfo file is not updated until push_nfo.")]
    public async Task<CallToolResult> AddMovieCast(
        [Description("Movie code, e.g. 'ABP-123'.")] string code,
        [Description("Actor id from search_actors.")] int actor_id,
        CancellationToken ct = default) =>
        McpResults.From(await library.AddCastAsync(code, actor_id, ct), d => ApiLinks.WithUrl(http.HttpContext?.Request, d));

    [McpServerTool(Name = "remove_movie_cast", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Removes one cast entry from a movie: either a tracked actor (give actor_id, as shown in the movie's cast) or a cast name that matches no tracked actor "
        + "(give name). Provide exactly one of the two. Returns the updated movie. Database only; the .nfo file is not updated until push_nfo.")]
    public async Task<CallToolResult> RemoveMovieCast(
        [Description("Movie code, e.g. 'ABP-123'.")] string code,
        [Description("Id of a tracked actor in the movie's cast.")] int? actor_id = null,
        [Description("Name of an unmatched cast entry (actorId null in the movie's cast).")] string? name = null,
        CancellationToken ct = default)
    {
        var hasName = !string.IsNullOrWhiteSpace(name);
        if (actor_id is null == !hasName)
        {
            return McpResults.Error("Invalid input: provide exactly one of actor_id or name.");
        }

        var result = actor_id is { } id
            ? await library.RemoveCastByActorAsync(code, id, ct)
            : await library.RemoveCastByNameAsync(code, name!, ct);
        return McpResults.From(result, d => ApiLinks.WithUrl(http.HttpContext?.Request, d));
    }
}
