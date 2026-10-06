using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.R18Dev;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

/// <summary>Smoke coverage for ActorMissing.razor post-Phase 3 (StatusFilterButtonGroup/
/// DropdownMenuButton extraction). Doesn't exercise the full r18.dev-filmography-loaded branch
/// (would need seeding R18DevDumpStore's sidecar data) — sticks to the branches reachable with a
/// plain DB seed, which already covers the extracted components' wiring.</summary>
public class ActorMissingTests : BunitContext
{
    private TestDbContextFactory SetUpServices(IR18DevDumpStore? dumpStore = null)
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorService>(new ActorService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));
        Services.AddSingleton(dumpStore ?? Substitute.For<IR18DevDumpStore>());
        Services.AddSingleton(Substitute.For<IProwlarrClient>());
        Services.AddSingleton(Substitute.For<ITorrentGrabService>());
        Services.AddSingleton(Substitute.For<IMovieAddService>());
        Services.AddSingleton<IActorFilmographyService, ActorFilmographyService>();
        return factory;
    }

    [Fact]
    public void UnknownActor_ShowsNotFoundMessage()
    {
        using var factory = SetUpServices();

        var cut = Render<ActorMissing>(p => p.Add(x => x.RouteName, "Nobody"));

        Assert.Contains("Actor not found", cut.Markup);
    }

    [Fact]
    public void KnownActor_R18DevDisabled_ShowsDisabledMessage()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Actors.Add(new Actor { FirstName = "Hatano", LastName = "Yui" });
            db.SaveChanges();
        }

        var cut = Render<ActorMissing>(p => p.Add(x => x.RouteName, "Yui Hatano"));

        Assert.Contains("Yui Hatano", cut.Markup);
        Assert.Contains("r18.dev metadata source is disabled", cut.Markup);
    }

    [Fact]
    public void KnownActor_R18DevEnabledButNotImported_ShowsImportPrompt()
    {
        var dumpStore = Substitute.For<IR18DevDumpStore>();
        dumpStore.GetFilmographyForActorAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new R18DevFilmographyResult(false, []));
        using var factory = SetUpServices(dumpStore);
        using (var db = factory.CreateDbContext())
        {
            db.Actors.Add(new Actor { FirstName = "Hatano", LastName = "Yui" });
            db.R18DevSettings.Add(new R18DevSettings { Enabled = true });
            db.SaveChanges();
        }

        var cut = Render<ActorMissing>(p => p.Add(x => x.RouteName, "Yui Hatano"));

        Assert.Contains("hasn't been imported yet", cut.Find("p.text-muted").TextContent);
    }

    [Fact]
    public void Filmography_ShowsProwlarrSearchOnlyForUntrackedCards()
    {
        var dumpStore = Substitute.For<IR18DevDumpStore>();
        dumpStore.GetFilmographyForActorAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new R18DevFilmographyResult(true,
            [
                new R18DevFilmographyEntry("ABC-123", "Tracked", null, null, null),
                new R18DevFilmographyEntry("DEF-456", "Untracked", null, null, null),
            ]));
        using var factory = SetUpServices(dumpStore);
        using (var db = factory.CreateDbContext())
        {
            db.Actors.Add(new Actor { FirstName = "Hatano", LastName = "Yui" });
            db.Movies.Add(new Movie { Code = "ABC-123", Status = MovieStatus.Missing });
            db.R18DevSettings.Add(new R18DevSettings { Enabled = true });
            db.SaveChanges();
        }

        var cut = Render<ActorMissing>(p => p.Add(x => x.RouteName, "Yui Hatano"));

        var searchButton = Assert.Single(cut.FindAll("button.missing-poster-search"));
        Assert.Equal("Search Prowlarr for DEF-456", searchButton.GetAttribute("aria-label"));
    }

    [Fact]
    public void Prerendering_ShowsLoadingIndicatorImmediately()
    {
        var dumpStore = Substitute.For<IR18DevDumpStore>();
        using var factory = SetUpServices(dumpStore);
        using (var db = factory.CreateDbContext())
        {
            db.Actors.Add(new Actor { FirstName = "Hatano", LastName = "Yui" });
            db.R18DevSettings.Add(new R18DevSettings { Enabled = true });
            db.SaveChanges();
        }

        var cut = Render<ActorMissing>(p => p
            .Add(x => x.RouteName, "Yui Hatano")
            .Add(x => x.HttpContext, new Microsoft.AspNetCore.Http.DefaultHttpContext()));

        Assert.Contains("Yui Hatano", cut.Markup);
        Assert.Contains("missing-loading-state", cut.Markup);
        Assert.NotNull(cut.Find(".skeleton-wrapper"));
        Assert.Equal(24, cut.FindAll(".skeleton-card").Count);
        dumpStore.DidNotReceive().GetFilmographyForActorAsync(
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void LoadingFilmography_ShowsSkeletonShimmerLoadingAnimation()
    {
        var tcs = new TaskCompletionSource<R18DevFilmographyResult>();
        var dumpStore = Substitute.For<IR18DevDumpStore>();
        dumpStore.GetFilmographyForActorAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        using var factory = SetUpServices(dumpStore);
        using (var db = factory.CreateDbContext())
        {
            db.Actors.Add(new Actor { FirstName = "Hatano", LastName = "Yui" });
            db.R18DevSettings.Add(new R18DevSettings { Enabled = true });
            db.SaveChanges();
        }

        var cut = Render<ActorMissing>(p => p.Add(x => x.RouteName, "Yui Hatano"));

        Assert.Contains("missing-loading-state", cut.Markup);
        Assert.NotNull(cut.Find(".skeleton-wrapper"));
        Assert.Equal(24, cut.FindAll(".skeleton-card").Count);

        tcs.SetResult(new R18DevFilmographyResult(false, []));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".missing-loading-state")));
    }
}
