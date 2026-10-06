using System.Net;
using Javbuddy.Data;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Javbuddy.Tests.Services.Infrastructure;

file sealed class TestSettings : IHasConnectionUrls
{
    public string? BaseUrl { get; set; }
    public string? ExternalUrl { get; set; }
}

/// <summary>Minimal concrete client exposing ApiClientBase's protected members for direct testing,
/// independent of any real integration's DTOs/auth.</summary>
file sealed class TestApiClient(IHttpClientFactory httpClientFactory, TestSettings? settings) : ApiClientBase<TestSettings>(httpClientFactory, Substitute.For<IDbContextFactory<AppDbContext>>(), Substitute.For<IConfiguration>())
{
    private readonly TestSettings? settings = settings;

    protected override string ServiceName => "TestService";

    protected override Task<TestSettings?> GetSettingsAsync(CancellationToken ct) => Task.FromResult(settings);

    public static string CombineUrlPublic(string baseUrl, string path) => CombineUrl(baseUrl, path);

    public Task<string> ExecuteTestAsync(
        TimeSpan timeout,
        Func<HttpClient, CancellationToken, Task<string>> action,
        Func<string, string> onFailure,
        CancellationToken ct = default) =>
        ExecuteAsync(timeout, ct, action, onFailure);

    public static Task<string> ReadJsonErrorPublic(HttpResponseMessage response, CancellationToken ct) =>
        ReadJsonErrorAsync<TestError>(response, e => e.Message, ct);
}

file sealed record TestError(string? Message);

public class ApiClientBaseTests
{
    [Theory]
    [InlineData("http://example.test", "/api/v1/x", "http://example.test/api/v1/x")]
    [InlineData("http://example.test/", "/api/v1/x", "http://example.test/api/v1/x")]
    [InlineData("http://example.test///", "/api/v1/x", "http://example.test/api/v1/x")]
    public void CombineUrl_JoinsBaseAndPath(string baseUrl, string path, string expected)
    {
        Assert.Equal(expected, TestApiClient.CombineUrlPublic(baseUrl, path));
    }

    [Fact]
    public async Task GetBaseUrlAsync_TrimsTrailingSlash()
    {
        var client = new TestApiClient(Substitute.For<IHttpClientFactory>(), new TestSettings { BaseUrl = "http://example.test/" });

        Assert.Equal("http://example.test", await client.GetBaseUrlAsync());
    }

    [Fact]
    public async Task GetExternalUrlAsync_FallsBackToBaseUrl_WhenExternalNotSet()
    {
        var client = new TestApiClient(Substitute.For<IHttpClientFactory>(), new TestSettings { BaseUrl = "http://internal.test" });

        Assert.Equal("http://internal.test", await client.GetExternalUrlAsync());
    }

    [Fact]
    public async Task GetExternalUrlAsync_PrefersExternalUrl_WhenSet()
    {
        var client = new TestApiClient(Substitute.For<IHttpClientFactory>(),
            new TestSettings { BaseUrl = "http://internal.test", ExternalUrl = "http://external.test/" });

        Assert.Equal("http://external.test", await client.GetExternalUrlAsync());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsActionResult_OnSuccess()
    {
        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, "ok");
        var client = new TestApiClient(new FakeHttpClientFactory(handler), settings: null);

        var result = await client.ExecuteTestAsync(
            TimeSpan.FromSeconds(5),
            async (http, ct) =>
            {
                using var response = await http.GetAsync("http://example.test/", ct);
                return await response.Content.ReadAsStringAsync(ct);
            },
            failure => $"FAIL: {failure}");

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task ExecuteAsync_MapsHttpRequestException_ToCouldNotReachMessage()
    {
        var handler = FakeHttpMessageHandler.Throwing(new HttpRequestException("connection refused"));
        var client = new TestApiClient(new FakeHttpClientFactory(handler), settings: null);

        var result = await client.ExecuteTestAsync(
            TimeSpan.FromSeconds(5),
            async (http, ct) => await http.GetStringAsync("http://example.test/", ct),
            failure => failure);

        Assert.Equal("Could not reach TestService: connection refused", result);
    }

    [Fact]
    public async Task ExecuteAsync_MapsTimeoutToTimedOutMessage_WithoutCancellingCallersToken()
    {
        var client = new TestApiClient(Substitute.For<IHttpClientFactory>(), settings: null);
        using var callerCts = new CancellationTokenSource();

        var result = await client.ExecuteTestAsync(
            TimeSpan.FromMilliseconds(10),
            async (http, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return "should not get here";
            },
            failure => failure,
            callerCts.Token);

        Assert.Equal("Request to TestService timed out.", result);
        Assert.False(callerCts.IsCancellationRequested);
    }

    private static HttpResponseMessage ErrorResponse(HttpContent content) =>
        new(HttpStatusCode.BadRequest) { ReasonPhrase = "Bad Request", Content = content };

    [Fact]
    public async Task ReadJsonErrorAsync_ReturnsMessageFromJsonBody()
    {
        using var response = ErrorResponse(new StringContent("""{"message":"token expired"}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal("token expired", await TestApiClient.ReadJsonErrorPublic(response, CancellationToken.None));
    }

    [Fact]
    public async Task ReadJsonErrorAsync_MalformedJson_FallsBackToReasonPhrase()
    {
        using var response = ErrorResponse(new StringContent("<html>oops</html>", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal("Bad Request", await TestApiClient.ReadJsonErrorPublic(response, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_OrdinaryHttpFailure_ReturnsErrorBodyMessage()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(ErrorResponse(
            new StringContent("""{"message":"not found"}""", System.Text.Encoding.UTF8, "application/json"))));
        var client = new TestApiClient(new FakeHttpClientFactory(handler), settings: null);

        var result = await client.ExecuteTestAsync(
            TimeSpan.FromSeconds(5),
            async (http, ct) =>
            {
                using var response = await http.GetAsync("http://example.test/", ct);
                return await TestApiClient.ReadJsonErrorPublic(response, ct);
            },
            failure => $"FAIL: {failure}");

        Assert.Equal("not found", result);
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutDuringErrorBodyRead_ReportsTimedOut()
    {
        var client = new TestApiClient(Substitute.For<IHttpClientFactory>(), settings: null);

        var result = await client.ExecuteTestAsync(
            TimeSpan.FromMilliseconds(50),
            async (_, ct) =>
            {
                using var response = ErrorResponse(new StreamContent(ControlledStream.Blocking()));
                return await TestApiClient.ReadJsonErrorPublic(response, ct);
            },
            failure => failure);

        Assert.Equal("Request to TestService timed out.", result);
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancelledDuringErrorBodyRead_Propagates()
    {
        var client = new TestApiClient(Substitute.For<IHttpClientFactory>(), settings: null);
        using var callerCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ExecuteTestAsync(
            TimeSpan.FromSeconds(30),
            async (_, ct) =>
            {
                using var response = ErrorResponse(new StreamContent(ControlledStream.Blocking()));
                return await TestApiClient.ReadJsonErrorPublic(response, ct);
            },
            failure => failure,
            callerCts.Token));
    }
}
