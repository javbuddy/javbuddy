using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Components.Shared;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class MissingTests : BunitContext
{
    private TestDbContextFactory SetUpServices()
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IMovieGridQueryService>(new MovieGridQueryService(factory));
        var prowlarr = Substitute.For<IProwlarrClient>();
        prowlarr.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ProwlarrSearchResult(true, [], null));
        Services.AddSingleton(prowlarr);
        Services.AddSingleton(Substitute.For<ITorrentGrabService>());
        Services.AddSingleton(Substitute.For<IMovieAddService>());
        JSInterop.Mode = JSRuntimeMode.Loose;
        return factory;
    }

    [Fact]
    public void SortDescendingByDefault_ShowsNewestReleaseFirst()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Missing, MetaReleaseDate = new DateTime(2020, 1, 1) });
            db.Movies.Add(new Movie { Code = "AAA-002", Status = MovieStatus.Missing, MetaReleaseDate = new DateTime(2023, 1, 1) });
            db.SaveChanges();
        }

        var cut = Render<Missing>();

        var codes = Codes(cut);
        Assert.Equal(new[] { "AAA-002", "AAA-001" }, codes);
    }

    [Fact]
    public void ViewStateCookie_RestoresAscendingSortOnFreshLoad()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Missing, MetaReleaseDate = new DateTime(2020, 1, 1) });
            db.Movies.Add(new Movie { Code = "AAA-002", Status = MovieStatus.Missing, MetaReleaseDate = new DateTime(2023, 1, 1) });
            db.SaveChanges();
        }

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "javbuddy-missing-view=" + Uri.EscapeDataString("""{"SortDescending":false}""");
        httpContextAccessor.HttpContext.Returns(httpContext);
        Services.AddSingleton(httpContextAccessor);

        var cut = Render<Missing>();

        var codes = Codes(cut);
        Assert.Equal(new[] { "AAA-001", "AAA-002" }, codes);
    }

    private static List<string> Codes(IRenderedComponent<Missing> cut) =>
        cut.FindAll(".missing-row-item > .missing-code").Select(e => e.TextContent).ToList();

    private static void AddDatedMissingMovies(TestDbContextFactory factory, int count)
    {
        using var db = factory.CreateDbContext();
        for (var i = 1; i <= count; i++)
        {
            db.Movies.Add(new Movie { Code = $"MIS-{i:000}", Status = MovieStatus.Missing, MetaReleaseDate = new DateTime(2000, 1, 1).AddDays(i) });
        }
        db.SaveChanges();
    }

    [Fact]
    public void LongList_RendersOnlyTheFirstWindow_WithTheTotalForTheSpacers()
    {
        using var factory = SetUpServices();
        AddDatedMissingMovies(factory, 120);

        var cut = Render<Missing>();

        var codes = Codes(cut);
        Assert.Equal(48, codes.Count);
        Assert.Equal("MIS-120", codes[0]);
        Assert.Equal("120", cut.Find(".virtualized-grid-spacer").GetAttribute("data-total-count"));
        Assert.Equal("0", cut.Find(".virtualized-grid-spacer").GetAttribute("data-window-start"));
    }

    [Fact]
    public async Task ScrollingToTheEnd_LoadsTheLastWindow_SoEveryMovieStaysReachable()
    {
        using var factory = SetUpServices();
        AddDatedMissingMovies(factory, 120);
        var cut = Render<Missing>();

        await cut.InvokeAsync(() => cut.FindComponent<VirtualizedGrid>().Instance.SetVisibleRange(100, 40, 1));

        cut.WaitForAssertion(() =>
        {
            var codes = Codes(cut);
            Assert.Equal(20, codes.Count);
            Assert.Equal("MIS-020", codes[0]);
            Assert.Equal("MIS-001", codes[^1]);
            Assert.Equal("100", cut.Find(".virtualized-grid-spacer").GetAttribute("data-window-start"));
        });
    }

    [Fact]
    public async Task TogglingSort_ReloadsFromTheTopInTheOtherOrder()
    {
        using var factory = SetUpServices();
        AddDatedMissingMovies(factory, 120);
        var cut = Render<Missing>();
        await cut.InvokeAsync(() => cut.FindComponent<VirtualizedGrid>().Instance.SetVisibleRange(100, 40, 1));

        cut.Find(".missing-sortable").Click();

        cut.WaitForAssertion(() =>
        {
            var codes = Codes(cut);
            Assert.Equal("MIS-001", codes[0]);
            Assert.Equal("0", cut.Find(".virtualized-grid-spacer").GetAttribute("data-window-start"));
        });
    }

    [Fact]
    public void NoMissingMovies_ShowsTheEmptyMessage()
    {
        using var factory = SetUpServices();

        var cut = Render<Missing>();

        Assert.Contains("Nothing missing", cut.Markup);
        Assert.Empty(Codes(cut));
    }

    [Fact]
    public void SearchButton_OpensTheSearchModalForThatMovie()
    {
        using var factory = SetUpServices();
        AddDatedMissingMovies(factory, 3);
        var cut = Render<Missing>();

        cut.FindAll(".missing-row-item .missing-icon-btn")[0].Click();

        var modal = cut.FindComponent<ProwlarrSearchModal>();
        Assert.True(modal.Instance.Show);
        Assert.Equal("MIS-003", modal.Instance.Movie?.Code);
    }

    [Fact]
    public void TogglingSort_WritesViewStateCookie()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Missing });
            db.SaveChanges();
        }

        var cut = Render<Missing>();
        cut.Find(".missing-sortable").Click();

        cut.WaitForAssertion(
            () => Assert.Contains(JSInterop.Invocations, inv => inv.Identifier == "setJsonCookie" && inv.Arguments.Any(a => a?.ToString() == "javbuddy-missing-view")),
            TimeSpan.FromSeconds(2));
    }

    // VirtualizedGrid imports its own module through the same IJSRuntime, so it gets a module of its
    // own; only the page's is under test.
    private static bool PageModule(object?[] args) => args is [string path, ..] && path.EndsWith("/Missing.razor.js", StringComparison.Ordinal);

    // bUnit's JSInterop.SetupModule() always resolves an IJSObjectReference import synchronously
    // with no way to defer or fail it, so these tests replace IJSRuntime outright with an
    // NSubstitute mock to control exactly when/how the "import" call completes.

    [Fact]
    public async Task DisposeAsync_BeforeModuleImportResolves_DisposesModuleOnceItResolves()
    {
        using var factory = SetUpServices();
        var importTcs = new TaskCompletionSource<IJSObjectReference>();
        var jsRuntime = Substitute.For<IJSRuntime>();
        var module = Substitute.For<IJSObjectReference>();
#pragma warning disable BL0016 // NSubstitute mock setup, not a real unguarded JS interop call
        jsRuntime.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]>())
            .Returns(new ValueTask<IJSObjectReference>(Substitute.For<IJSObjectReference>()));
        jsRuntime.InvokeAsync<IJSObjectReference>("import", Arg.Is<object?[]>(a => PageModule(a)))
            .Returns(new ValueTask<IJSObjectReference>(importTcs.Task));
#pragma warning restore BL0016
        Services.AddSingleton(jsRuntime);

        Render<Missing>();

        var disposeTask = DisposeComponentsAsync();
        Assert.False(disposeTask.IsCompleted);

        importTcs.SetResult(module);

        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2));
        _ = module.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_AfterModuleImportResolves_DisposesModule()
    {
        using var factory = SetUpServices();
        var module = Substitute.For<IJSObjectReference>();
        var jsRuntime = Substitute.For<IJSRuntime>();
#pragma warning disable BL0016 // NSubstitute mock setup, not a real unguarded JS interop call
        jsRuntime.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]>())
            .Returns(new ValueTask<IJSObjectReference>(Substitute.For<IJSObjectReference>()));
        jsRuntime.InvokeAsync<IJSObjectReference>("import", Arg.Is<object?[]>(a => PageModule(a)))
            .Returns(new ValueTask<IJSObjectReference>(Task.FromResult(module)));
#pragma warning restore BL0016
        Services.AddSingleton(jsRuntime);

        Render<Missing>();

        await DisposeComponentsAsync().WaitAsync(TimeSpan.FromSeconds(2));
        _ = module.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WhenModuleImportFailsBecauseCircuitDisconnected_DoesNotThrow()
    {
        using var factory = SetUpServices();
        var jsRuntime = Substitute.For<IJSRuntime>();
#pragma warning disable BL0016 // NSubstitute mock setup, not a real unguarded JS interop call
        jsRuntime.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]>())
            .Returns(new ValueTask<IJSObjectReference>(Task.FromException<IJSObjectReference>(new JSDisconnectedException("Circuit disconnected."))));
#pragma warning restore BL0016
        Services.AddSingleton(jsRuntime);

        Render<Missing>();

        await DisposeComponentsAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }
}
