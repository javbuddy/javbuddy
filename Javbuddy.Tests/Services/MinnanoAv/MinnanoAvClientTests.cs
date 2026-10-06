using System.Net;
using Javbuddy.Models;
using Javbuddy.Services.MinnanoAv;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.MinnanoAv;

public class MinnanoAvClientTests
{
    private const string SampleListHtml = """
        <table class="tbllist actress">
            <tr>
                <td><a href="actress148426.html?澤口美帆"><img src="p_actress_125_125/006/148426.jpg" alt="澤口美帆" /></a></td>
                <td class="details">
                    <h2 class="ttl"><a href="actress148426.html?澤口美帆">澤口美帆</a></h2>
                    <p class="furi">さわぐちみほ / sawaguchi miho</p>
                </td>
                <td>1</td>
            </tr>
        </table>
        """;

    private const string SampleDetailHtml = """
        <div class="act-profile">
        <table>
            <tr><td><h2>通野未帆 （とおのみほ / Tohno Miho）</h2></td></tr>
            <tr><td><span>サイズ</span><p>T160 / B84(<a href="x">Eカップ</a>) / W59 / H85</p></td></tr>
        </table>
        </div>
        <meta property="og:image" content="https://www.minnano-av.com/p_actress_125_125/006/148426.jpg?new">
        """;

    [Fact]
    public async Task SearchPerformersAsync_WhenAmbiguous_ParsesTheListingPage()
    {
        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, SampleListHtml);
        var clientFactory = new FakeHttpClientFactory(handler);
        using var dbFactory = new TestDbContextFactory();
        var client = new MinnanoAvClient(clientFactory, dbFactory);

        var results = await client.SearchPerformersAsync("美帆");

        Assert.NotNull(handler.LastRequest);
        Assert.Contains("search_result.php", handler.LastRequest.RequestUri?.ToString());
        Assert.Contains("Chrome", handler.LastRequest.Headers.UserAgent.ToString());
        Assert.Single(results);
        Assert.Equal("澤口美帆", results[0].Name);
        Assert.False(results[0].IsExactMatch);
    }

    [Fact]
    public async Task SearchPerformersAsync_WhenRedirectedStraightToDetailPage_ReturnsOneExactMatch()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleDetailHtml),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://www.minnano-av.com/actress148426.html")
            };
            return Task.FromResult(response);
        });

        var clientFactory = new FakeHttpClientFactory(handler);
        using var dbFactory = new TestDbContextFactory();
        var client = new MinnanoAvClient(clientFactory, dbFactory);

        var results = await client.SearchPerformersAsync("通野未帆");

        Assert.Single(results);
        Assert.True(results[0].IsExactMatch);
        Assert.Equal("通野未帆", results[0].Name);
        Assert.Equal("actress148426.html", results[0].PathOrUrl);
        Assert.Contains("148426.jpg", results[0].ImageUrl);
    }

    [Fact]
    public async Task GetPerformerDetailAsync_FetchesAndParsesPerformer()
    {
        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, SampleDetailHtml);
        var clientFactory = new FakeHttpClientFactory(handler);
        using var dbFactory = new TestDbContextFactory();
        var client = new MinnanoAvClient(clientFactory, dbFactory);

        var detail = await client.GetPerformerDetailAsync("actress148426.html");

        Assert.NotNull(detail);
        Assert.Equal("通野未帆", detail.Name);
        Assert.Equal(160, detail.HeightCm);
        Assert.Equal("E", detail.CupSize);
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
                Content = new StringContent(SampleListHtml)
            });
        });
        var clientFactory = new FakeHttpClientFactory(handler);
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.MinnanoAvSettings.Add(new MinnanoAvSettings { RequestDelayMs = 150 });
            await db.SaveChangesAsync();
        }

        var client = new MinnanoAvClient(clientFactory, dbFactory);

        await client.SearchPerformersAsync("美帆");
        await client.SearchPerformersAsync("美帆");

        Assert.Equal(2, requestTimestamps.Count);
        var elapsedMs = (requestTimestamps[1] - requestTimestamps[0]).TotalMilliseconds;
        Assert.True(elapsedMs >= 100, $"Expected the second request to be throttled by ~150ms, but only {elapsedMs}ms elapsed between requests.");
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsTrueOn200()
    {
        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, "<html><body>minnano-av</body></html>");
        var clientFactory = new FakeHttpClientFactory(handler);
        using var dbFactory = new TestDbContextFactory();
        var client = new MinnanoAvClient(clientFactory, dbFactory);

        var connected = await client.TestConnectionAsync();
        Assert.True(connected);
    }

    private static FakeHttpMessageHandler HangingUntilCancelled() =>
        new(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        });

    public static TheoryData<string> Operations => new() { "test", "search", "detail" };

    private static Task InvokeAsync(MinnanoAvClient client, string operation, CancellationToken ct) => operation switch
    {
        "test" => client.TestConnectionAsync(ct),
        "search" => client.SearchPerformersAsync("Yua Mikami", ct),
        _ => client.GetPerformerDetailAsync("/performer/1", ct),
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task CallerCancellationDuringRequest_Propagates(string operation)
    {
        using var dbFactory = new TestDbContextFactory();
        var client = new MinnanoAvClient(new FakeHttpClientFactory(HangingUntilCancelled()), dbFactory);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InvokeAsync(client, operation, cts.Token));
    }

    [Fact]
    public async Task AlreadyCancelledToken_PropagatesFromSettingsLookup()
    {
        using var dbFactory = new TestDbContextFactory();
        var client = new MinnanoAvClient(new FakeHttpClientFactory(FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, "")), dbFactory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SearchPerformersAsync("Yua Mikami", cts.Token));
    }

    [Fact]
    public async Task OrdinaryProviderFailure_IsStillHandled()
    {
        using var dbFactory = new TestDbContextFactory();
        var client = new MinnanoAvClient(new FakeHttpClientFactory(FakeHttpMessageHandler.Throwing(new HttpRequestException("connection refused"))), dbFactory);

        Assert.False(await client.TestConnectionAsync());
        Assert.Empty(await client.SearchPerformersAsync("Yua Mikami"));
        Assert.Null(await client.GetPerformerDetailAsync("/performer/1"));
    }
}
