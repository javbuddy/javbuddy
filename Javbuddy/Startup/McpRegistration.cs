using System.Reflection;
using Javbuddy.Mcp;
using ModelContextProtocol.Protocol;

namespace Javbuddy.Startup;

/// <summary>The MCP server at <c>/mcp</c> (see docs/api.md): stateless Streamable HTTP, so short tool calls need no session and nothing
/// is lost across restarts. Unauthenticated like the rest of the app; guarded against cross-site browser calls by <see cref="OriginGuardFilter"/>.</summary>
public static class McpRegistration
{
    public const string Route = "/mcp";

    public const string Instructions =
        "Javbuddy tracks wanted and owned JAV movies; movies are addressed by code (e.g. ABP-123). "
        + "Edits change Javbuddy's database only. Call preview_nfo, then push_nfo, to write the movie's .nfo. "
        + "Tags must come from list_tags.";

    public static IServiceCollection AddJavbuddyMcp(this IServiceCollection services)
    {
        var version = typeof(McpRegistration).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(McpRegistration).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        // The informational version carries a "+<commit sha>" suffix that means nothing to a client.
        var plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];

        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "Javbuddy", Version = version };
                options.ServerInstructions = Instructions;
            })
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools<MovieTools>()
            .WithTools<TagTools>()
            .WithTools<CastTools>()
            .WithTools<NfoTools>();
        return services;
    }

    public static void MapJavbuddyMcp(this WebApplication app) =>
        app.MapMcp(Route).RequireAllowedOrigin().DisableAntiforgery();
}
