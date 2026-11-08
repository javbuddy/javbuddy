using System.ComponentModel;
using Javbuddy.Services.Api;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Javbuddy.Mcp;

/// <summary>MCP tools for the movie's .nfo file, the only place an edit leaves Javbuddy's database.</summary>
[McpServerToolType]
public sealed class NfoTools(ILibraryApiService library)
{
    [McpServerTool(Name = "preview_nfo", ReadOnly = true, OpenWorld = false)]
    [Description("Shows a movie's .nfo state without writing anything: driftKind (none, javbuddyChanged = database edits not yet written, "
        + "externalEdit = the file was edited outside Javbuddy, bothChanged, unreadable), details, and proposedNfo, the XML push_nfo would write. "
        + "Call this before push_nfo.")]
    public async Task<CallToolResult> PreviewNfo(
        [Description("Movie code, e.g. 'ABP-123'.")] string code,
        CancellationToken ct = default) =>
        McpResults.From(await library.GetNfoAsync(code, ct), nfo => nfo);

    [McpServerTool(Name = "push_nfo", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Writes the movie's .nfo file in the media library from Javbuddy's database, so edits made with update_movie_metadata, tags and cast reach the file. "
        + "This overwrites a file on disk. By default a .nfo that was edited outside Javbuddy (externalEdit or bothChanged) is skipped and counted in skippedExternal; "
        + "set include_external to true only if the user wants those external edits overwritten. Returns { written, skippedExternal, failed, stillDrifting }. "
        + "Use preview_nfo first.")]
    public async Task<CallToolResult> PushNfo(
        [Description("Movie code, e.g. 'ABP-123'.")] string code,
        [Description("Also overwrite a .nfo edited outside Javbuddy. Default false.")] bool include_external = false,
        CancellationToken ct = default) =>
        McpResults.From(await library.PushNfoAsync(code, include_external, ct), r => r);
}
