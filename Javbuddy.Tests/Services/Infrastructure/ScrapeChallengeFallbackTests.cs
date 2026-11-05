using System.Net;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.MinnanoAv;
using Javbuddy.Services.Warashi;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Javbuddy.Tests.Services.Infrastructure;

/// <summary>The plain-first / solver-on-challenge fetch shared by the scraped actor sources, exercised
/// through the minnano-av.com and Warashi clients with a fake site and a substitute solver.</summary>
public class ScrapeChallengeFallbackTests
{
    private const string ListHtml = """
        <table class="tbllist actress"><tr>
            <td><a href="actress148426.html?x"><img src="p/148426.jpg" alt="澤口美帆" /></a></td>
            <td class="details"><h2 class="ttl"><a href="actress148426.html?x">澤口美帆</a></h2>
            <p class="furi">さわぐちみほ / sawaguchi miho</p></td><td>1</td></tr></table>
        """;

    private const string DetailHtml = """
        <div class="act-profile"><table>
            <tr><td><h2>通野未帆 （とおのみほ / Tohno Miho）</h2></td></tr>
            <tr><td><span>サイズ</span><p>T160 / B84(<a href="x">Eカップ</a>) / W59 / H85</p></td></tr>
        </table></div>
        <meta property="og:image" content="https://www.minnano-av.com/p/148426.jpg?new">
        """;

    private static FakeHttpMessageHandler Challenge(HttpStatusCode status = HttpStatusCode.Forbidden) =>
        FakeHttpMessageHandler.ReturningStatus(status, "<html><title>Just a moment...</title></html>");

    private static IChallengeSolverClient Solver(SolverResult? get = null, bool configured = true)
    {
        var solver = Substitute.For<IChallengeSolverClient>();
        solver.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(configured);
        solver.GetAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(get ?? SolverResult.Fail("unused"));
        return solver;
    }

    private static SolverResult Solved(string html, string finalUrl, int status = 200) =>
        SolverResult.Ok(new SolvedResponse(html, status, finalUrl, "UA", []));

    private static IConfiguration Env(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    private static async Task SaveMinnanoAsync(TestDbContextFactory dbFactory, bool useSolver)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.MinnanoAvSettings.Add(new MinnanoAvSettings { UseChallengeSolver = useSolver, RequestDelayMs = 0 });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task WhenThePlainRequestSucceeds_TheSolverIsNeverCalled()
    {
        using var dbFactory = new TestDbContextFactory();
        await SaveMinnanoAsync(dbFactory, useSolver: true);
        var solver = Solver();
        var client = new MinnanoAvClient(new FakeHttpClientFactory(FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.OK, ListHtml)), dbFactory, challengeSolver: solver);

        var results = await client.SearchPerformersAsync("美帆");

        Assert.Single(results);
        await solver.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default);
    }

    [Fact]
    public async Task WhenChallengedAndEnabled_TheSameRequestIsRepeatedThroughTheSolver()
    {
        using var dbFactory = new TestDbContextFactory();
        await SaveMinnanoAsync(dbFactory, useSolver: true);
        var solver = Solver(Solved(ListHtml, "https://www.minnano-av.com/search_result.php?search_scope=actress&search_word=x"));
        var client = new MinnanoAvClient(new FakeHttpClientFactory(Challenge()), dbFactory, challengeSolver: solver);

        var results = await client.SearchPerformersAsync("美帆");

        Assert.Equal("澤口美帆", Assert.Single(results).Name);
        await solver.Received(1).GetAsync(
            Arg.Is<Uri>(u => u.AbsolutePath == "/search_result.php" && u.Query.Contains("search_scope=actress")),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SolverFinalUrl_DrivesTheSingleMatchRedirectDetection()
    {
        using var dbFactory = new TestDbContextFactory();
        await SaveMinnanoAsync(dbFactory, useSolver: true);
        var solver = Solver(Solved(DetailHtml, "https://www.minnano-av.com/actress148426.html"));
        var client = new MinnanoAvClient(new FakeHttpClientFactory(Challenge()), dbFactory, challengeSolver: solver);

        var result = Assert.Single(await client.SearchPerformersAsync("通野未帆"));

        Assert.True(result.IsExactMatch);
        Assert.Equal("通野未帆", result.Name);
        Assert.Equal("actress148426.html", result.PathOrUrl);
    }

    [Fact]
    public async Task WhenTheSolversFirstAnswerIsStillAChallenge_ItIsRetriedOnce()
    {
        using var dbFactory = new TestDbContextFactory();
        await SaveMinnanoAsync(dbFactory, useSolver: true);
        var solver = Solver();
        solver.GetAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(
            Solved("<html><title>Just a moment...</title></html>", "https://www.minnano-av.com/search_result.php", 403),
            Solved(ListHtml, "https://www.minnano-av.com/search_result.php"));
        var client = new MinnanoAvClient(new FakeHttpClientFactory(Challenge()), dbFactory, challengeSolver: solver);

        var results = await client.SearchPerformersAsync("美帆");

        Assert.NotEmpty(results);
        await solver.Received(2).GetAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenTheSolverStaysChallenged_ItGivesUpAfterOneRetry()
    {
        using var dbFactory = new TestDbContextFactory();
        await SaveMinnanoAsync(dbFactory, useSolver: true);
        var solver = Solver(Solved("<html><title>Just a moment...</title></html>", "https://www.minnano-av.com/x", 403));
        var client = new MinnanoAvClient(new FakeHttpClientFactory(Challenge()), dbFactory, challengeSolver: solver);

        await Assert.ThrowsAsync<ScrapeBlockedException>(() => client.SearchPerformersAsync("美帆"));
        await solver.Received(2).GetAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DetailFetch_GoesThroughTheSolverWhenChallenged()
    {
        using var dbFactory = new TestDbContextFactory();
        await SaveMinnanoAsync(dbFactory, useSolver: true);
        var solver = Solver(Solved(DetailHtml, "https://www.minnano-av.com/actress148426.html"));
        var client = new MinnanoAvClient(new FakeHttpClientFactory(Challenge()), dbFactory, challengeSolver: solver);

        var detail = await client.GetPerformerDetailAsync("actress148426.html");

        Assert.Equal(160, detail?.HeightCm);
    }

    [Fact]
    public async Task WhenChallengedAndTheSourceSwitchedTheSolverOff_ThrowsBlockedAndSkipsTheSolver()
    {
        using var dbFactory = new TestDbContextFactory();
        await SaveMinnanoAsync(dbFactory, useSolver: false);
        var solver = Solver();
        var client = new MinnanoAvClient(new FakeHttpClientFactory(Challenge()), dbFactory, challengeSolver: solver);

        var ex = await Assert.ThrowsAsync<ScrapeBlockedException>(() => client.SearchPerformersAsync("美帆"));
        Assert.Contains("minnano-av.com", ex.Message);
        Assert.Contains("switched off", ex.Message);
        await Assert.ThrowsAsync<ScrapeBlockedException>(() => client.GetPerformerDetailAsync("actress1.html"));
        await solver.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default);
    }

    [Fact]
    public async Task WhenChallengedAndNoSolverIsConfigured_ThrowsBlockedSayingSo()
    {
        using var dbFactory = new TestDbContextFactory();
        await SaveMinnanoAsync(dbFactory, useSolver: true);
        var client = new MinnanoAvClient(new FakeHttpClientFactory(Challenge()), dbFactory, challengeSolver: Solver(configured: false));

        var ex = await Assert.ThrowsAsync<ScrapeBlockedException>(() => client.SearchPerformersAsync("美帆"));

        Assert.Contains("Configure a challenge solver", ex.Message);
    }

    [Fact]
    public async Task WhenTheSolverFails_ThrowsBlockedWithItsReason()
    {
        using var dbFactory = new TestDbContextFactory();
        await SaveMinnanoAsync(dbFactory, useSolver: true);
        var client = new MinnanoAvClient(new FakeHttpClientFactory(Challenge()), dbFactory,
            challengeSolver: Solver(SolverResult.Fail("Timeout after 60 seconds")));

        var ex = await Assert.ThrowsAsync<ScrapeBlockedException>(() => client.SearchPerformersAsync("美帆"));

        Assert.Contains("Timeout after 60 seconds", ex.Message);
    }

    [Fact]
    public async Task WhenTheSolverStillReturnsAChallenge_ThrowsBlocked()
    {
        using var dbFactory = new TestDbContextFactory();
        await SaveMinnanoAsync(dbFactory, useSolver: true);
        var client = new MinnanoAvClient(new FakeHttpClientFactory(Challenge()), dbFactory,
            challengeSolver: Solver(Solved("<html><title>Just a moment...</title></html>", "https://www.minnano-av.com/", 403)));

        await Assert.ThrowsAsync<ScrapeBlockedException>(() => client.SearchPerformersAsync("美帆"));
    }

    [Fact]
    public async Task OrdinaryHttpErrors_AreNotTreatedAsAChallenge()
    {
        using var dbFactory = new TestDbContextFactory();
        await SaveMinnanoAsync(dbFactory, useSolver: true);
        var solver = Solver();
        var client = new MinnanoAvClient(new FakeHttpClientFactory(FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.NotFound, "gone")), dbFactory, challengeSolver: solver);

        Assert.Empty(await client.SearchPerformersAsync("美帆"));
        Assert.Null(await client.GetPerformerDetailAsync("actress1.html"));
        await solver.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default);
    }

    [Fact]
    public async Task TestConnectionAsync_PassesTheChallengeThroughTheSolver()
    {
        using var dbFactory = new TestDbContextFactory();
        await SaveMinnanoAsync(dbFactory, useSolver: true);
        var client = new MinnanoAvClient(new FakeHttpClientFactory(Challenge()), dbFactory,
            challengeSolver: Solver(Solved("<html>home</html>", "https://www.minnano-av.com/")));

        Assert.True(await client.TestConnectionAsync());
    }

    [Fact]
    public async Task TestConnectionAsync_WhenBlocked_ReportsDown()
    {
        using var dbFactory = new TestDbContextFactory();
        var client = new MinnanoAvClient(new FakeHttpClientFactory(Challenge()), dbFactory);

        Assert.False(await client.TestConnectionAsync());
    }

    [Fact]
    public async Task WithNoSavedSettingsAtAll_ASolverIsUsedByDefault()
    {
        using var dbFactory = new TestDbContextFactory();
        var solver = Solver(Solved(ListHtml, "https://www.minnano-av.com/search_result.php"));
        var client = new MinnanoAvClient(new FakeHttpClientFactory(Challenge()), dbFactory, challengeSolver: solver);

        Assert.Single(await client.SearchPerformersAsync("美帆"));
    }

    [Fact]
    public async Task EnvironmentOverride_SwitchesTheSolverOffOverTheSavedSetting()
    {
        using var dbFactory = new TestDbContextFactory();
        await SaveMinnanoAsync(dbFactory, useSolver: true);
        var solver = Solver();
        var client = new MinnanoAvClient(new FakeHttpClientFactory(Challenge()), dbFactory, challengeSolver: solver,
            configuration: Env(("MinnanoAv:UseChallengeSolver", "false")));

        await Assert.ThrowsAsync<ScrapeBlockedException>(() => client.SearchPerformersAsync("美帆"));
        await solver.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default);
    }

    [Fact]
    public async Task EnvironmentOverride_SwitchesTheSolverOnOverTheSavedSetting()
    {
        using var dbFactory = new TestDbContextFactory();
        await SaveMinnanoAsync(dbFactory, useSolver: false);
        var client = new MinnanoAvClient(new FakeHttpClientFactory(Challenge()), dbFactory,
            challengeSolver: Solver(Solved(ListHtml, "https://www.minnano-av.com/search_result.php")),
            configuration: Env(("MinnanoAv:UseChallengeSolver", "true")));

        Assert.Single(await client.SearchPerformersAsync("美帆"));
    }

    [Fact]
    public async Task EnvironmentOverride_IsPerSource()
    {
        using var dbFactory = new TestDbContextFactory();
        var solver = Solver();
        var client = new WarashiClient(new FakeHttpClientFactory(Challenge()), dbFactory, challengeSolver: solver,
            configuration: Env(("MinnanoAv:UseChallengeSolver", "true"), ("Warashi:UseChallengeSolver", "false")));

        await Assert.ThrowsAsync<ScrapeBlockedException>(() => client.SearchPerformersAsync("Yua Mikami"));
        await solver.DidNotReceiveWithAnyArgs().PostFormAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task WarashiSearch_PostsTheFormThroughTheSolverWhenChallenged()
    {
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.WarashiSettings.Add(new WarashiSettings { UseChallengeSolver = true, RequestDelayMs = 0 });
            await db.SaveChangesAsync();
        }

        var solver = Substitute.For<IChallengeSolverClient>();
        solver.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        solver.PostFormAsync(Arg.Any<Uri>(), Arg.Any<IReadOnlyList<KeyValuePair<string, string>>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Solved("<html></html>", "https://warashi-asian-pornstars.fr/en/s-12/search"));
        var client = new WarashiClient(new FakeHttpClientFactory(Challenge()), dbFactory, challengeSolver: solver);

        await client.SearchPerformersAsync("Yua Mikami");

        await solver.Received(1).PostFormAsync(
            Arg.Is<Uri>(u => u.AbsolutePath == "/en/s-12/search"),
            Arg.Is<IReadOnlyList<KeyValuePair<string, string>>>(f => f.Any(p => p.Key == "recherche_valeur" && p.Value == "Yua Mikami")),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
