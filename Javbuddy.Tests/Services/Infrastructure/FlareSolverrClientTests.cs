using System.Net;
using System.Text.Json;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.Infrastructure;

public class FlareSolverrClientTests
{
    private const string OkBody = """
        {"status":"ok","message":"","version":"3.3.21","solution":{"url":"https://site.example/actress1.html","status":200,
         "response":"<html>page</html>","userAgent":"UA/1.0","headers":{},
         "cookies":[{"name":"cf_clearance","value":"abc","domain":".site.example","path":"/"}]}}
        """;

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    private static async Task<FlareSolverrClient> CreateAsync(FakeHttpMessageHandler handler, TestDbContextFactory dbFactory, IConfiguration? config = null, string? baseUrl = "http://solver:8191/")
    {
        if (baseUrl is not null)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            db.FlareSolverrSettings.Add(new FlareSolverrSettings { BaseUrl = baseUrl, MaxTimeoutMs = 45000 });
            await db.SaveChangesAsync();
        }

        return new FlareSolverrClient(new FakeHttpClientFactory(handler), dbFactory, config ?? Config());
    }

    private static FakeHttpMessageHandler Capturing(List<(string Url, JsonElement Body)> sink, string responseBody, HttpStatusCode status = HttpStatusCode.OK) =>
        new(async (request, ct) =>
        {
            var json = await request.Content!.ReadAsStringAsync(ct);
            sink.Add((request.RequestUri!.ToString(), JsonDocument.Parse(json).RootElement.Clone()));
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        });

    [Fact]
    public async Task GetAsync_SendsRequestGetToV1AndMapsTheSolution()
    {
        var sent = new List<(string Url, JsonElement Body)>();
        using var dbFactory = new TestDbContextFactory();
        var client = await CreateAsync(Capturing(sent, OkBody), dbFactory);

        var result = await client.GetAsync(new Uri("https://site.example/search?q=a"), "minnano");

        var (url, body) = Assert.Single(sent);
        Assert.Equal("http://solver:8191/v1", url);
        Assert.Equal("request.get", body.GetProperty("cmd").GetString());
        Assert.Equal("https://site.example/search?q=a", body.GetProperty("url").GetString());
        Assert.Equal("javbuddy-minnano", body.GetProperty("session").GetString());
        Assert.Equal(45000, body.GetProperty("maxTimeout").GetInt32());
        Assert.False(body.TryGetProperty("postData", out _));

        var response = Assert.IsType<SolvedResponse>(result.Response);
        Assert.Equal("<html>page</html>", response.Html);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("https://site.example/actress1.html", response.FinalUrl);
        Assert.Equal("UA/1.0", response.UserAgent);
        var cookie = Assert.Single(response.Cookies);
        Assert.Equal(new SolverCookie("cf_clearance", "abc", ".site.example", "/"), cookie);
    }

    [Theory]
    [InlineData("minnano-av.com", "javbuddy-minnano-av-com")]
    [InlineData("Some Source Name", "javbuddy-Some-Source-Name")]
    [InlineData("a_b-C9", "javbuddy-a_b-C9")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void SessionName_UsesOnlyCharactersSolversAccept(string? key, string? expected) =>
        Assert.Equal(expected, FlareSolverrClient.SessionName(key));

    [Fact]
    public async Task GetAsync_WhenTheSolverRejectsTheRequest_IncludesItsReason()
    {
        using var dbFactory = new TestDbContextFactory();
        var client = await CreateAsync(
            Capturing([], """{"status":"error","message":"session ID must contain 1-128 letters, numbers, underscores or hyphens"}""", HttpStatusCode.BadRequest), dbFactory);

        var result = await client.GetAsync(new Uri("https://site.example/"), null);

        Assert.Contains("400", result.Error);
        Assert.Contains("session ID must contain", result.Error);
    }

    [Fact]
    public async Task PostFormAsync_SendsRequestPostWithUrlEncodedPostData()
    {
        var sent = new List<(string Url, JsonElement Body)>();
        using var dbFactory = new TestDbContextFactory();
        var client = await CreateAsync(Capturing(sent, OkBody), dbFactory);

        await client.PostFormAsync(new Uri("https://site.example/search"),
            [new("recherche_critere", "f"), new("recherche_valeur", "a b&c")], sessionKey: null);

        var body = Assert.Single(sent).Body;
        Assert.Equal("request.post", body.GetProperty("cmd").GetString());
        Assert.Equal("recherche_critere=f&recherche_valeur=a%20b%26c", body.GetProperty("postData").GetString());
        Assert.False(body.TryGetProperty("session", out _));
    }

    [Fact]
    public async Task GetAsync_WhenSolverReportsAnError_ReturnsItsMessage()
    {
        using var dbFactory = new TestDbContextFactory();
        var client = await CreateAsync(
            Capturing([], """{"status":"error","message":"Error solving the challenge. Timeout after 60.0 seconds."}"""), dbFactory);

        var result = await client.GetAsync(new Uri("https://site.example/"), null);

        Assert.Null(result.Response);
        Assert.Contains("Timeout after 60.0 seconds", result.Error);
    }

    [Fact]
    public async Task GetAsync_WhenSolverReturnsHttpError_Fails()
    {
        using var dbFactory = new TestDbContextFactory();
        var client = await CreateAsync(Capturing([], "boom", HttpStatusCode.InternalServerError), dbFactory);

        var result = await client.GetAsync(new Uri("https://site.example/"), null);

        Assert.Null(result.Response);
        Assert.Contains("500", result.Error);
    }

    [Fact]
    public async Task GetAsync_WhenSolverUnreachable_FailsInsteadOfThrowing()
    {
        using var dbFactory = new TestDbContextFactory();
        var client = await CreateAsync(FakeHttpMessageHandler.Throwing(new HttpRequestException("connection refused")), dbFactory);

        var result = await client.GetAsync(new Uri("https://site.example/"), null);

        Assert.Null(result.Response);
        Assert.Contains("connection refused", result.Error);
    }

    [Fact]
    public async Task GetAsync_WhenNotConfigured_FailsWithoutRequest()
    {
        var sent = new List<(string Url, JsonElement Body)>();
        using var dbFactory = new TestDbContextFactory();
        var client = await CreateAsync(Capturing(sent, OkBody), dbFactory, baseUrl: null);

        var result = await client.GetAsync(new Uri("https://site.example/"), null);

        Assert.False(await client.IsConfiguredAsync());
        Assert.Contains("not configured", result.Error);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task EnvironmentVariables_TakePrecedenceOverTheSavedRow()
    {
        var sent = new List<(string Url, JsonElement Body)>();
        using var dbFactory = new TestDbContextFactory();
        var client = await CreateAsync(Capturing(sent, OkBody), dbFactory,
            Config(("FlareSolverr:BaseUrl", "http://env-solver:8191"), ("FlareSolverr:MaxTimeoutMs", "20000")));

        await client.GetAsync(new Uri("https://site.example/"), null);

        var (url, body) = Assert.Single(sent);
        Assert.Equal("http://env-solver:8191/v1", url);
        Assert.Equal(20000, body.GetProperty("maxTimeout").GetInt32());
    }

    [Fact]
    public async Task EnvironmentTimeoutAlone_OverridesTheSavedRowsTimeout()
    {
        var sent = new List<(string Url, JsonElement Body)>();
        using var dbFactory = new TestDbContextFactory();
        var client = await CreateAsync(Capturing(sent, OkBody), dbFactory, Config(("FlareSolverr:MaxTimeoutMs", "20000")));

        await client.GetAsync(new Uri("https://site.example/"), null);

        var (url, body) = Assert.Single(sent);
        Assert.Equal("http://solver:8191/v1", url);
        Assert.Equal(20000, body.GetProperty("maxTimeout").GetInt32());
    }

    [Fact]
    public async Task GetAsync_DoesNotCapASlowSolveAtTheHttpClientDefaultTimeout()
    {
        HttpClient? seen = null;
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.FlareSolverrSettings.Add(new FlareSolverrSettings { BaseUrl = "http://solver:8191", MaxTimeoutMs = 200000 });
            await db.SaveChangesAsync();
        }
        var factory = new CapturingClientFactory(new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(OkBody) })), c => seen = c);
        var client = new FlareSolverrClient(factory, dbFactory, Config());

        await client.GetAsync(new Uri("https://site.example/"), null);

        Assert.Equal(Timeout.InfiniteTimeSpan, seen!.Timeout);
    }

    private sealed class CapturingClientFactory(HttpMessageHandler handler, Action<HttpClient> onCreate) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(handler, disposeHandler: false);
            onCreate(client);
            return client;
        }
    }

    [Fact]
    public async Task TestConnectionAsync_ListsSessionsAndReportsTheVersion()
    {
        var sent = new List<(string Url, JsonElement Body)>();
        using var dbFactory = new TestDbContextFactory();
        var client = await CreateAsync(Capturing(sent, """{"status":"ok","message":"","version":"2.0.0","sessions":[]}"""), dbFactory);

        var result = await client.TestConnectionAsync();

        Assert.True(result.Success);
        Assert.Equal("2.0.0", result.Version);
        Assert.Equal("sessions.list", Assert.Single(sent).Body.GetProperty("cmd").GetString());
    }

    [Fact]
    public async Task TestConnectionAsync_WhenAnswerIsNotTheV1Api_Fails()
    {
        using var dbFactory = new TestDbContextFactory();
        var client = await CreateAsync(Capturing([], "<html>not a solver</html>"), dbFactory);

        var result = await client.TestConnectionAsync();

        Assert.False(result.Success);
    }
}
