using System.Net;
using Javbuddy.Models;
using Javbuddy.Services.Warashi;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Warashi;

public class WarashiClientTests
{
    private const string SamplePerformerHtml = """
        <div id="main">
            <span itemprop="name">Yua MIKAMI</span>
            <span itemprop="additionalName">三上悠亜</span>
            <p itemprop="height">height: <span itemprop="value">159</span> cm</p>
            <p>cup size: F</p>
            <p>measurements: JP 83-57-88</p>
            <time itemprop="birthDate" content="1993-08-16">August 16, 1993</time>
        </div>
        """;

    [Fact]
    public async Task SearchPerformersAsync_SendsPostAndReturnsResults()
    {
        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, """
            <div class="resultat-pornostar correspondance_exacte">
                <a href="/en/s-2-0/yua-mikami/asian-female-pornstar/2922">
                    <span class="correspondance-lien">Yua MIKAMI</span> - 三上悠亜
                </a>
            </div>
            """);

        var clientFactory = new FakeHttpClientFactory(handler);
        using var dbFactory = new TestDbContextFactory();
        var client = new WarashiClient(clientFactory, dbFactory);

        var results = await client.SearchPerformersAsync("Yua Mikami");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.EndsWith("/en/s-12/search", handler.LastRequest.RequestUri?.ToString());
        Assert.Contains("Chrome", handler.LastRequest.Headers.UserAgent.ToString());
        Assert.Single(results);
        Assert.Equal("Yua MIKAMI", results[0].Name);
    }

    [Fact]
    public async Task GetPerformerDetailAsync_FetchesAndParsesPerformer()
    {
        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, SamplePerformerHtml);
        var clientFactory = new FakeHttpClientFactory(handler);
        using var dbFactory = new TestDbContextFactory();
        var client = new WarashiClient(clientFactory, dbFactory);

        var detail = await client.GetPerformerDetailAsync("/en/s-2-0/yua-mikami/asian-female-pornstar/2922");

        Assert.NotNull(detail);
        Assert.Equal("Yua MIKAMI", detail.Name);
        Assert.Equal(159, detail.HeightCm);
        Assert.Equal("F", detail.CupSize);
        Assert.Equal(83, detail.Bust);
    }

    [Fact]
    public async Task SearchPerformersAsync_ThrottlesConsecutiveRequestsPerConfiguredDelay()
    {
        var requestTimestamps = new List<DateTime>();
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            requestTimestamps.Add(DateTime.UtcNow);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    <div class="resultat-pornostar">
                        <a href="/en/s-2-0/yua-mikami/asian-female-pornstar/2922">
                            <span class="correspondance-lien">Yua MIKAMI</span> - 三上悠亜
                        </a>
                    </div>
                    """)
            });
        });

        var clientFactory = new FakeHttpClientFactory(handler);
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.WarashiSettings.Add(new WarashiSettings { RequestDelayMs = 150 });
            await db.SaveChangesAsync();
        }

        var client = new WarashiClient(clientFactory, dbFactory);

        // The throttle spaces out the moments each request claims its slot, just before sending, so
        // measure from before the first call: a slow first send would otherwise eat into the gap.
        var firstCallStarted = DateTime.UtcNow;
        await client.SearchPerformersAsync("Yua Mikami");
        await client.SearchPerformersAsync("Yua Mikami");

        Assert.Equal(2, requestTimestamps.Count);
        var elapsedMs = (requestTimestamps[1] - firstCallStarted).TotalMilliseconds;
        Assert.True(elapsedMs >= 140, $"Expected the second request to be throttled by ~150ms, but only {elapsedMs}ms elapsed since the first call started.");
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsTrueOn200()
    {
        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, "<html><body>WAPdB</body></html>");
        var clientFactory = new FakeHttpClientFactory(handler);
        using var dbFactory = new TestDbContextFactory();
        var client = new WarashiClient(clientFactory, dbFactory);

        var connected = await client.TestConnectionAsync();
        Assert.True(connected);
    }

    private static FakeHttpMessageHandler HangingUntilCancelled() =>
        new(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        });

    public static TheoryData<string> Operations => new() { "test", "search", "detail", "image" };

    private static Task InvokeAsync(WarashiClient client, string operation, CancellationToken ct) => operation switch
    {
        "test" => client.TestConnectionAsync(ct),
        "search" => client.SearchPerformersAsync("Yua Mikami", ct),
        "image" => client.DownloadImageAsync("/img/1.jpg", ct),
        _ => client.GetPerformerDetailAsync("/performer/1", ct),
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task CallerCancellationDuringRequest_Propagates(string operation)
    {
        using var dbFactory = new TestDbContextFactory();
        var client = new WarashiClient(new FakeHttpClientFactory(HangingUntilCancelled()), dbFactory);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InvokeAsync(client, operation, cts.Token));
    }

    [Fact]
    public async Task AlreadyCancelledToken_PropagatesFromSettingsLookup()
    {
        using var dbFactory = new TestDbContextFactory();
        var client = new WarashiClient(new FakeHttpClientFactory(FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, "")), dbFactory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SearchPerformersAsync("Yua Mikami", cts.Token));
    }

    [Fact]
    public async Task OrdinaryProviderFailure_IsStillHandled()
    {
        using var dbFactory = new TestDbContextFactory();
        var client = new WarashiClient(new FakeHttpClientFactory(FakeHttpMessageHandler.Throwing(new HttpRequestException("connection refused"))), dbFactory);

        Assert.False(await client.TestConnectionAsync());
        Assert.Empty(await client.SearchPerformersAsync("Yua Mikami"));
        Assert.Null(await client.GetPerformerDetailAsync("/performer/1"));
    }
}
