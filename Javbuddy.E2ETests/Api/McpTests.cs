using System.Net;
using System.Text;
using System.Text.Json;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Javbuddy.E2ETests.Api;

/// <summary>The MCP server at /mcp driven by the SDK's own client over real HTTP (stateless Streamable HTTP), no browser.</summary>
[Collection(E2ECollection.Name)]
public class McpTests(E2EFixture fixture)
{
    private static readonly string[] ToolNames =
    [
        "search_movies", "get_movie", "fetch_javinizer_metadata", "update_movie_metadata", "list_tags", "add_movie_tag",
        "remove_movie_tag", "search_actors", "add_movie_cast", "remove_movie_cast", "preview_nfo", "push_nfo",
    ];

    private Task<McpClient> ConnectAsync() =>
        McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(new Uri(fixture.App.ServerAddress), "/mcp"),
        }));

    private async Task<string> SeedAsync(string prefix)
    {
        var code = $"{prefix}-{Random.Shared.Next(100, 999)}";
        await DbSeeding.SeedMovieAsync(fixture.DbFactory, code);
        return code;
    }

    private static string TextOf(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    [Fact]
    public async Task Initialize_ReportsServerNameAndInstructions()
    {
        await using var client = await ConnectAsync();

        Assert.Equal("Javbuddy", client.ServerInfo!.Name);
        Assert.Contains("push_nfo", client.ServerInstructions);
        Assert.Contains("list_tags", client.ServerInstructions);
    }

    [Fact]
    public async Task ToolsList_HasExactlyTheTwelveToolsWithAnnotations()
    {
        await using var client = await ConnectAsync();

        var tools = await client.ListToolsAsync();

        Assert.Equal(ToolNames.Order(), tools.Select(t => t.Name).Order());
        var byName = tools.ToDictionary(t => t.Name);
        foreach (var readOnly in new[] { "search_movies", "get_movie", "fetch_javinizer_metadata", "list_tags", "search_actors", "preview_nfo" })
        {
            Assert.True(byName[readOnly].ProtocolTool.Annotations?.ReadOnlyHint, readOnly);
        }
        foreach (var writer in new[] { "update_movie_metadata", "add_movie_tag", "remove_movie_tag", "add_movie_cast", "remove_movie_cast", "push_nfo" })
        {
            var annotations = byName[writer].ProtocolTool.Annotations;
            Assert.False(annotations?.ReadOnlyHint, writer);
            Assert.True(annotations?.IdempotentHint, writer);
        }
        Assert.True(byName["fetch_javinizer_metadata"].ProtocolTool.Annotations?.OpenWorldHint);
        foreach (var destructive in new[] { "push_nfo", "remove_movie_tag", "remove_movie_cast" })
        {
            Assert.True(byName[destructive].ProtocolTool.Annotations?.DestructiveHint, destructive);
        }
        foreach (var additive in new[] { "update_movie_metadata", "add_movie_tag", "add_movie_cast" })
        {
            Assert.False(byName[additive].ProtocolTool.Annotations?.DestructiveHint, additive);
        }
        Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
    }

    [Fact]
    public async Task UpdateMovieMetadataSchema_DescribesTheSetFields()
    {
        await using var client = await ConnectAsync();

        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "update_movie_metadata");

        var set = tool.JsonSchema.GetProperty("properties").GetProperty("set").GetProperty("properties");
        foreach (var field in new[] { "title", "titleOverride", "originalTitle", "description", "releaseDate", "studio", "label", "series", "director", "contentType" })
        {
            Assert.True(set.TryGetProperty(field, out _), field);
        }
    }

    [Fact]
    public async Task GetMovie_ReturnsTheSeededMovieWithAbsoluteWebUrl()
    {
        var code = await SeedAsync("MCPG");
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("get_movie", new Dictionary<string, object?> { ["code"] = code.ToLowerInvariant() });

        Assert.NotEqual(true, result.IsError);
        var json = JsonDocument.Parse(TextOf(result)).RootElement;
        Assert.Equal(code, json.GetProperty("code").GetString());
        Assert.Equal("got", json.GetProperty("status").GetString());
        Assert.Equal($"{fixture.App.ServerAddress}movies/{code}", json.GetProperty("webUrl").GetString());
    }

    [Fact]
    public async Task UpdateMovieMetadata_RoundTripsThroughTheRestApi()
    {
        var code = await SeedAsync("MCPU");
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("update_movie_metadata", new Dictionary<string, object?>
        {
            ["code"] = code,
            ["set"] = new Dictionary<string, object?> { ["description"] = "From MCP", ["releaseDate"] = "2023-05-01" },
            ["clear"] = new[] { "studio" },
        });

        Assert.NotEqual(true, result.IsError);
        using var http = new HttpClient { BaseAddress = new Uri(fixture.App.ServerAddress) };
        var detail = await http.GetFromJsonAsync<JsonElement>($"/api/v1/movies/{code}");
        Assert.Equal("From MCP", detail.GetProperty("description").GetString());
        Assert.Equal("2023-05-01", detail.GetProperty("releaseDate").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("studio").ValueKind);
    }

    [Fact]
    public async Task UpdateMovieMetadata_WithNothingToChange_IsAToolError()
    {
        var code = await SeedAsync("MCPN");
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("update_movie_metadata", new Dictionary<string, object?> { ["code"] = code });

        Assert.True(result.IsError);
        Assert.Contains("at least one", TextOf(result));
    }

    [Fact]
    public async Task UnknownCode_IsAnErrorResultNotAProtocolFailure()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("get_movie", new Dictionary<string, object?> { ["code"] = "NOPE-99999" });

        Assert.True(result.IsError);
        Assert.Contains("No movie with code 'NOPE-99999'", TextOf(result));
    }

    [Fact]
    public async Task ForeignOrigin_Is403()
    {
        using var http = new HttpClient { BaseAddress = new Uri(fixture.App.ServerAddress) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Origin", "http://evil.example");
        request.Headers.Add("Accept", "application/json, text/event-stream");

        var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetOnMcp_IsAnEmpty405NotTheHtmlNotFoundPage()
    {
        using var http = new HttpClient { BaseAddress = new Uri(fixture.App.ServerAddress) };

        var response = await http.GetAsync("/mcp");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }
}
