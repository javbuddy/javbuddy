using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Components.Shared;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.ActorEnrichment;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.MinnanoAv;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.R18Dev;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Warashi;
using Javbuddy.Tests.TestSupport;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class ActorDetailTests : BunitContext
{
    private readonly ISceneWallQueryService sceneWall = Substitute.For<ISceneWallQueryService>();

    public ActorDetailTests()
    {
        // The Scenes, Highlights and Apexes sections: empty unless a test gives them clips.
        sceneWall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([], 0));
        sceneWall.GetHighlightPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new HighlightWallPage([], 0));
        sceneWall.GetApexPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ApexWallPage([], 0));
        Services.AddSingleton(sceneWall);
        Services.AddSingleton(Substitute.For<ISceneMediaService>());
        Services.AddSingleton(Substitute.For<IHighlightMediaService>());
        Services.AddSingleton(Substitute.For<IApexMediaService>());
        Services.AddSingleton(new SceneMediaQueue(Substitute.For<IServiceScopeFactory>(), NullLogger<SceneMediaQueue>.Instance));
        // The sections' clip player.
        Services.AddSingleton(Substitute.For<IMovieHighlightService>());
        Services.AddSingleton(Substitute.For<IMovieApexService>());
        Services.AddSingleton(StreamServiceStubs.AllPlayable());
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Trickplay.ITrickplayService>());
        Services.AddSingleton(new Javbuddy.Services.Trickplay.TrickplayGenerationTracker());
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Trickplay.IHighlightTrickplayService>());
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton(Substitute.For<IJellyfinClient>());
        Services.AddSingleton(Substitute.For<IWarashiClient>());
        Services.AddSingleton(Substitute.For<IMinnanoAvClient>());
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Movies.IMovieService>());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task ActorDetail_RendersEditButton_AndHeroDetails()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 1,
            FirstName = "Yua",
            LastName = "Mikami",
            JapaneseNameKanji = "三上悠亜",
            JapaneseNameKana = "みかみ ゆあ",
            R18DevId = 1234,
            R18DevName = "Yua Mikami"
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
            await db.SaveChangesAsync();

            db.ActorPhotos.AddRange(
                new ActorPhoto { ActorId = actor.Id },
                new ActorPhoto { ActorId = actor.Id });
            await db.SaveChangesAsync();
        }

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        // Change Photo button present in toolbar
        var changePhotoBtn = cut.FindAll("button.actor-toolbar-btn")
            .FirstOrDefault(b => b.TextContent.Contains("Change Photo"));
        Assert.NotNull(changePhotoBtn);

        // Combined "Search" split-button present in toolbar, with a dropdown arrow for
        // provider-specific searches
        var searchBtn = cut.FindAll("button.actor-toolbar-btn-split-main")
            .FirstOrDefault(b => b.TextContent.Contains("Search"));
        Assert.NotNull(searchBtn);

        var searchArrowBtn = cut.FindAll("button.actor-toolbar-btn-split-arrow");
        Assert.Single(searchArrowBtn);

        // Merge button present in toolbar
        var mergeBtn = cut.FindAll("button.actor-toolbar-btn")
            .FirstOrDefault(b => b.TextContent.Contains("Merge..."));
        Assert.NotNull(mergeBtn);

        // Edit link present in toolbar
        var editLink = cut.Find("a[href='/actors/Mikami%20Yua/edit']");
        Assert.NotNull(editLink);
        Assert.Contains("Edit", editLink.TextContent);

        // Japanese name displayed in hero
        var japanese = cut.Find("span.actor-hero-japanese");
        Assert.Contains("三上悠亜", japanese.TextContent);
        Assert.Contains("みかみ ゆあ", japanese.TextContent);

        // Badges rendered
        var badges = cut.Find("div.actor-hero-badges");
        Assert.Contains("r18: Yua Mikami", badges.TextContent);
        Assert.DoesNotContain("r18.dev #1234", badges.TextContent);
        Assert.DoesNotContain("javinizer #567", badges.TextContent);

        Assert.Contains("0 movies · 2 images", cut.Find(".actor-hero-meta").TextContent);
    }

    [Fact]
    public async Task ActorDetail_NfoDriftFilterGroup_OrsSelectedDirections()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
            foreach (var (code, kind) in new[]
            {
                ("DRF-001", NfoDriftKind.JavbuddyChanged),
                ("DRF-002", NfoDriftKind.ExternalEdit),
                ("DRF-003", NfoDriftKind.BothChanged),
                ("DRF-004", NfoDriftKind.None),
            })
            {
                var movie = new Movie { Code = code, Status = MovieStatus.Got, NfoDriftKind = kind };
                movie.MovieActors.Add(new MovieActor { ActorId = actor.Id });
                db.Movies.Add(movie);
            }
            await db.SaveChangesAsync();
        }

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll(".poster-title").Count));

        void ClickNfoDrift(string label)
        {
            if (!cut.FindAll("button.sort-dropdown-subitem").Any(b => b.TextContent.Trim().StartsWith(label)))
            {
                cut.FindAll("button").First(b => b.TextContent.StartsWith("Filter")).Click();
                cut.FindAll("button").First(b => b.TextContent.StartsWith("NFO drift")).Click();
            }
            cut.FindAll("button.sort-dropdown-subitem").First(b => b.TextContent.Trim() == label).Click();
        }

        ClickNfoDrift("Javbuddy changed");
        ClickNfoDrift("External edit");

        cut.WaitForAssertion(() => Assert.Equal(
            ["DRF-001", "DRF-002"],
            cut.FindAll(".poster-title").Select(t => t.TextContent.Trim()).Order()));
    }

    [Fact]
    public async Task ActorDetail_TracksScrollPerActor_OnceTheFilmographyRenders()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var yua = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var eimi = new Actor { Id = 2, FirstName = "Eimi", LastName = "Fukada" };
        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(yua);
        actorService.GetByRouteNameAsync("Fukada Eimi", Arg.Any<CancellationToken>()).Returns(eimi);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        await using (var db = await factory.CreateDbContextAsync())
        {
            foreach (var (actor, code) in new[] { (yua, "AAA-001"), (eimi, "BBB-001") })
            {
                db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
                var movie = new Movie { Code = code, Status = MovieStatus.Got };
                movie.MovieActors.Add(new MovieActor { ActorId = actor.Id });
                db.Movies.Add(movie);
            }
            await db.SaveChangesAsync();
        }

        var module = JSInterop.SetupModule("./Components/Pages/ActorDetail.razor.js");
        List<string?> TrackedKeys() => module.Invocations
            .Where(i => i.Identifier == "trackScroll").Select(i => i.Arguments[0]?.ToString()).ToList();

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));
        cut.WaitForAssertion(() => Assert.Equal(["javbuddy-actor-detail-scroll-Mikami Yua"], TrackedKeys()));

        // Re-renders of the same actor don't restart tracking (which would restore the position again).
        cut.Render();
        Assert.Single(TrackedKeys());

        cut.Render(parameters => parameters.Add(p => p.RouteName, "Fukada Eimi"));
        cut.WaitForAssertion(() => Assert.Equal(
            ["javbuddy-actor-detail-scroll-Mikami Yua", "javbuddy-actor-detail-scroll-Fukada Eimi"], TrackedKeys()));
    }

    [Fact]
    public async Task ActorDetail_FilmographyCards_LinkToMovieDetailWithTheActorAsBackContext()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var yua = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(yua);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = yua.Id, FirstName = yua.FirstName, LastName = yua.LastName });
            var movie = new Movie { Code = "AAA-001", Status = MovieStatus.Got };
            movie.MovieActors.Add(new MovieActor { ActorId = yua.Id });
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        cut.WaitForAssertion(() => Assert.Equal(
            "/movies/AAA-001?actor=Mikami%20Yua",
            cut.Find(".poster-title").Closest("a")!.GetAttribute("href")));
    }

    // The same card as the Movies grid: Downloading status and a favorite toggle.
    [Fact]
    public async Task ActorDetail_FilmographyCards_ShowDownloadingStatusAndToggleFavorite()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));
        var movieService = Substitute.For<Javbuddy.Services.Movies.IMovieService>();
        Services.AddSingleton(movieService);

        var yua = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(yua);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = yua.Id, FirstName = yua.FirstName, LastName = yua.LastName });
            var movie = new Movie { Code = "AAA-001", Status = MovieStatus.Missing };
            movie.MovieActors.Add(new MovieActor { ActorId = yua.Id });
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            db.TorrentDownloads.Add(new TorrentDownload { MovieId = movie.Id, MovieCode = "AAA-001", Status = TorrentDownloadStatus.Downloading });
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }
        movieService.ToggleFavoriteAsync(movieId, Arg.Any<CancellationToken>()).Returns(true);

        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        cut.WaitForAssertion(() => Assert.Contains("status-downloading", cut.Find(".poster-image").ClassList));
        cut.Find(".poster-favorite").Click();
        cut.WaitForAssertion(() => Assert.Contains("active", cut.Find(".poster-favorite").ClassList));
        await movieService.Received(1).ToggleFavoriteAsync(movieId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Genre", "Play", "GEN-001,GEN-002")]
    [InlineData("Genre", "Rough", "GEN-001")]
    [InlineData("Features", "Has favorite scene", "GEN-002")]
    [InlineData("Features", "Favorites", "GEN-003")]
    public async Task ActorDetail_GenreAndFeatureFilters_MatchTheMoviesPage(string group, string option, string expectedCodes)
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
            var play = new Tag { Name = "Play" };
            var rough = new Tag { Name = "Rough", ParentTag = play };

            // GEN-001 has only the subtag, so picking the parent "Play" must roll up to it.
            var subtagOnly = new Movie { Code = "GEN-001", Status = MovieStatus.Got };
            subtagOnly.MovieTags.Add(new MovieTag { Tag = rough });
            var parentWithFavoriteScene = new Movie { Code = "GEN-002", Status = MovieStatus.Got };
            parentWithFavoriteScene.MovieTags.Add(new MovieTag { Tag = play });
            parentWithFavoriteScene.Scenes.Add(new Scene { StartSeconds = 0, IsFavorite = true });
            var favorite = new Movie { Code = "GEN-003", Status = MovieStatus.Got, IsFavorite = true };

            foreach (var movie in new[] { subtagOnly, parentWithFavoriteScene, favorite })
            {
                movie.MovieActors.Add(new MovieActor { ActorId = actor.Id });
                db.Movies.Add(movie);
            }
            await db.SaveChangesAsync();
        }

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".poster-title").Count));

        cut.FindAll("button").First(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(group)).Click();
        cut.FindAll("button.sort-dropdown-subitem").First(b => b.TextContent.Trim() == option).Click();

        cut.WaitForAssertion(() => Assert.Equal(
            expectedCodes.Split(',').Order(),
            cut.FindAll(".poster-title").Select(t => t.TextContent.Trim()).Order()));
    }

    [Fact]
    public void ActorDetail_RendersAliases_WhenPresent()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 1,
            FirstName = "Yua",
            LastName = "Mikami"
        };
        actor.Aliases.Add(new ActorAlias { Name = "Alternate Stage Name" });

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var aliases = cut.Find("div.actor-hero-aliases");
        Assert.Contains("Alternate Stage Name", aliases.TextContent);
    }

    [Fact]
    public async Task ActorDetail_ClickingMergeButton_OpensMergeModal()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 1,
            FirstName = "Yua",
            LastName = "Mikami"
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        actorService.GetMergeCandidatesAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ActorMergeCandidate>());
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        Assert.Empty(cut.FindAll(".merge-modal-backdrop"));

        var mergeBtn = cut.FindAll("button.actor-toolbar-btn")
            .First(b => b.TextContent.Contains("Merge..."));
        await cut.InvokeAsync(() => mergeBtn.Click());

        Assert.NotEmpty(cut.FindAll(".merge-modal-backdrop"));
    }

    [Fact]
    public async Task ActorDetail_ClickingSearchButton_OpensCombinedSearchModal()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var searchBtn = cut.Find("button.actor-toolbar-btn-split-main");
        await cut.InvokeAsync(() => searchBtn.Click());

        var title = cut.Find(".search-modal-title");
        Assert.Contains("Search All Providers", title.TextContent);
    }

    [Fact]
    public async Task ActorDetail_ClickingDropdownArrow_ShowsProviderMenu_AndSelectingWAPdBOpensItsOwnModal()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        Assert.Empty(cut.FindAll(".actor-toolbar-dropdown-menu"));

        var arrowBtn = cut.Find("button.actor-toolbar-btn-split-arrow");
        await cut.InvokeAsync(() => arrowBtn.Click());

        var menu = cut.Find(".actor-toolbar-dropdown-menu");
        var items = menu.QuerySelectorAll(".actor-toolbar-dropdown-item");
        Assert.Equal(2, items.Length);
        Assert.Equal("WAPdB", items[0].TextContent);
        Assert.Equal("minnano-av", items[1].TextContent);

        await cut.InvokeAsync(() => items[0].Click());

        // Dropdown menu closes, and the WAPdB-only modal (not the combined one) is now shown
        Assert.Empty(cut.FindAll(".actor-toolbar-dropdown-menu"));
        var title = cut.Find(".search-modal-title");
        Assert.Contains("Mikami Yua", title.TextContent);
        Assert.DoesNotContain("Search All Providers", title.TextContent);
    }

    [Fact]
    public async Task ActorDetail_WhenCombinedSearchPicksAWarashiCandidate_HandsOffToTheWarashiModal()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var warashiClient = Substitute.For<IWarashiClient>();
        var searchResult = new WarashiSearchResult(
            Name: "Yua MIKAMI", JapaneseName: "三上悠亜", PathOrUrl: "/en/actress-1",
            ImageUrl: null, CareerActivity: null, IsExactMatch: true, KnownAliases: Array.Empty<string>());
        warashiClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<WarashiSearchResult> { searchResult });
        warashiClient.GetPerformerDetailAsync("/en/actress-1", Arg.Any<CancellationToken>())
            .Returns(new WarashiPerformerDetail { Name = "Yua MIKAMI", JapaneseName = "三上悠亜", HeightCm = 159, Aliases = new List<string>() });
        Services.AddSingleton(warashiClient);

        var minnanoAvClient = Substitute.For<Javbuddy.Services.MinnanoAv.IMinnanoAvClient>();
        minnanoAvClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<Javbuddy.Services.MinnanoAv.MinnanoAvSearchResult>());
        Services.AddSingleton(minnanoAvClient);

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var searchBtn = cut.Find("button.actor-toolbar-btn-split-main");
        await cut.InvokeAsync(() => searchBtn.Click());

        var inspectBtn = cut.Find(".search-result-item button");
        await cut.InvokeAsync(() => inspectBtn.Click());

        // Combined modal is gone, the WAPdB modal is open and already shows this candidate's detail
        var title = cut.Find(".search-modal-title");
        Assert.Contains("Mikami Yua", title.TextContent);
        Assert.DoesNotContain("Search All Providers", title.TextContent);
        var detailCard = cut.Find(".performer-detail-card");
        Assert.Contains("Height: 159 cm", detailCard.TextContent);
    }

    [Fact]
    public async Task ActorDetail_DeleteButton_RequiresConfirmationBeforeDeleting()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 5,
            FirstName = "Remu",
            LastName = null
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Remu", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Remu"));

        var deleteBtn = cut.Find("button.actor-toolbar-btn-danger");
        await cut.InvokeAsync(() => deleteBtn.Click());

        var modal = cut.Find(".actor-delete-modal-backdrop");
        Assert.Contains("Delete Remu?", modal.TextContent);
        Assert.Contains("unlink the actor from linked movies", modal.TextContent);
        Assert.Contains("delete custom aliases", modal.TextContent);
        Assert.Contains("remove cached portraits", modal.TextContent);
        await actorService.DidNotReceive().DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());

        var confirmBtn = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Delete Actor"));
        await cut.InvokeAsync(() => confirmBtn.Click());

        await actorService.Received(1).DeleteAsync(5, Arg.Any<CancellationToken>());
        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/actors", nav.Uri);
    }

    [Fact]
    public async Task ActorDetail_DeleteConfirmation_CancelAndEscapeCloseWithoutDeleting()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Remu", Arg.Any<CancellationToken>())
            .Returns(new Actor { Id = 5, FirstName = "Remu" });
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Remu"));
        var deleteBtn = cut.Find("button.actor-toolbar-btn-danger");

        await cut.InvokeAsync(() => deleteBtn.Click());
        var cancelBtn = cut.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Cancel");
        Assert.True(cancelBtn.HasAttribute("autofocus"));
        await cut.InvokeAsync(() => cancelBtn.Click());
        Assert.Empty(cut.FindAll(".actor-delete-modal-backdrop"));

        await cut.InvokeAsync(() => deleteBtn.Click());
        var modal = cut.Find(".actor-delete-modal-backdrop");
        await cut.InvokeAsync(() => modal.KeyDown(new KeyboardEventArgs { Key = "Escape" }));
        Assert.Empty(cut.FindAll(".actor-delete-modal-backdrop"));

        await cut.InvokeAsync(() => cut.Find("button.actor-toolbar-btn-danger").Click());
        await cut.InvokeAsync(() => cut.Find(".actor-delete-modal-backdrop").Click());
        Assert.Empty(cut.FindAll(".actor-delete-modal-backdrop"));
        await actorService.DidNotReceive().DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ActorDetail_RendersFavoriteButton_InToolbar_AndUpdatesOnToggle()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 1,
            FirstName = "Yua",
            LastName = "Mikami",
            IsFavorite = false
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        actorService.ToggleFavoriteAsync(1, Arg.Any<CancellationToken>())
            .Returns(true);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        // Favorite button present in toolbar and not active initially
        var favBtn = cut.FindAll("button.actor-toolbar-btn")
            .FirstOrDefault(b => b.TextContent.Contains("Favorite"));
        Assert.NotNull(favBtn);
        Assert.DoesNotContain("active", favBtn.ClassList);

        // Hero does not show star
        Assert.Empty(cut.FindAll(".actor-hero-favorite"));

        // Click favorite button
        await cut.InvokeAsync(() => favBtn.Click());

        await actorService.Received(1).ToggleFavoriteAsync(1, Arg.Any<CancellationToken>());

        // Button is now active and labeled "Favorited"
        var updatedFavBtn = cut.FindAll("button.actor-toolbar-btn")
            .FirstOrDefault(b => b.TextContent.Contains("Favorited"));
        Assert.NotNull(updatedFavBtn);
        Assert.Contains("active", updatedFavBtn.ClassList);

        // Hero now shows star
        Assert.NotEmpty(cut.FindAll(".actor-hero-favorite"));
    }

    [Fact]
    public void ActorDetail_RendersFavoriteStar_InHero_WhenActorIsFavoriteInitially()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 2,
            FirstName = "Remu",
            LastName = null,
            IsFavorite = true
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Remu", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Remu"));

        var favBtn = cut.FindAll("button.actor-toolbar-btn")
            .FirstOrDefault(b => b.TextContent.Contains("Favorited"));
        Assert.NotNull(favBtn);
        Assert.Contains("active", favBtn.ClassList);

        var heroStar = cut.Find(".actor-hero-favorite");
        Assert.NotNull(heroStar);
        Assert.Equal("♥", heroStar.TextContent.Trim());
    }

    [Fact]
    public async Task ActorDetail_ClickingSyncNfoButton_OpensSyncModal()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 1,
            FirstName = "Yua",
            LastName = "Mikami"
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        var nfoSyncService = Substitute.For<INfoSyncService>();
        Services.AddSingleton(nfoSyncService);

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        Assert.Empty(cut.FindAll(".sync-modal-backdrop"));

        var syncBtn = cut.FindAll("button.actor-toolbar-btn")
            .First(b => b.TextContent.Contains("Sync .nfo"));
        await cut.InvokeAsync(() => syncBtn.Click());

        Assert.NotEmpty(cut.FindAll(".sync-modal-backdrop"));
    }

    [Fact]
    public async Task ActorDetail_RendersRefreshButton_AndCallsActorServiceRefreshMetadata()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 42,
            FirstName = "Yua",
            LastName = "Mikami"
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        actorService.RefreshMetadataAsync(42, Arg.Any<CancellationToken>())
            .Returns(ActorEnrichmentResult.Ok("Local", 1, ["JapaneseNameKanji"], [], false));
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var refreshBtn = cut.FindAll("button.actor-toolbar-btn")
            .FirstOrDefault(b => b.TextContent.Contains("Refresh"));
        Assert.NotNull(refreshBtn);

        await cut.InvokeAsync(() => refreshBtn.Click());

        await actorService.Received(1).RefreshMetadataAsync(42, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ActorDetail_DoesNotRenderMetadataSourceBadge()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 1,
            FirstName = "Yua",
            LastName = "Mikami",
            R18DevId = 123
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        Assert.Empty(cut.FindAll("div.actor-hero-badges"));
    }

    [Fact]
    public async Task ActorDetail_ClickingAvatarWithImage_OpensLightboxModal()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 1,
            FirstName = "Yua",
            LastName = "Mikami"
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        actorService.GetPortraitInfoAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ActorPortraitInfo(HasImage: true, HasCroppedImage: true, HasSourceImage: true));
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        // Avatar has "Click to view large photo" title
        var avatar = cut.Find(".actor-hero-avatar-photo");
        Assert.Equal("Click to view large photo", avatar.GetAttribute("title"));

        // Lightbox is closed initially
        Assert.Empty(cut.FindAll(".image-lightbox-backdrop"));

        // Clicking avatar opens lightbox
        await cut.InvokeAsync(() => avatar.Click());
        Assert.NotEmpty(cut.FindAll(".image-lightbox-backdrop"));
        Assert.NotEmpty(cut.FindAll(".image-lightbox-image"));

        // Closing lightbox dismisses it
        var closeBtn = cut.Find(".image-lightbox-close-btn");
        await cut.InvokeAsync(() => closeBtn.Click());
        Assert.Empty(cut.FindAll(".image-lightbox-backdrop"));
    }

    [Fact]
    public void ActorDetail_HeroAvatarImage_UsesImageVersionFromPortraitInfo()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 1,
            FirstName = "Yua",
            LastName = "Mikami"
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        actorService.GetPortraitInfoAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ActorPortraitInfo(HasImage: true, HasCroppedImage: true, HasSourceImage: true, ImageVersion: 123456789L));
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var img = cut.Find(".actor-hero-avatar-photo img");
        Assert.Equal("/actor-image/1/full?v=123456789", img.GetAttribute("src"));
    }

    [Fact]
    public async Task ActorDetail_ClickingAvatarWithoutImage_OpensUploadModal()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 1,
            FirstName = "Yua",
            LastName = "Mikami"
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        actorService.GetPortraitInfoAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ActorPortraitInfo(HasImage: false, HasCroppedImage: false, HasSourceImage: false));
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        // Avatar has "Click to upload photo" title
        var avatar = cut.Find(".actor-hero-avatar");
        Assert.Equal("Click to upload photo", avatar.GetAttribute("title"));

        // Upload modal closed initially
        Assert.Empty(cut.FindAll(".crop-modal-backdrop"));

        // Clicking avatar opens upload modal
        await cut.InvokeAsync(() => avatar.Click());
        Assert.NotEmpty(cut.FindAll(".crop-modal-backdrop"));
    }

    [Fact]
    public void ActorDetail_RendersOpenInJellyfinLink_WhenJellyfinPersonIdLinked_AndClientResolvesUrl()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 1,
            FirstName = "Yua",
            LastName = "Mikami",
            JellyfinPersonId = "jf-person-12345"
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.GetWebUrlAsync("jf-person-12345", null, Arg.Any<CancellationToken>())
            .Returns("https://jellyfin.local/web/index.html#!/details?id=jf-person-12345&serverId=server-999");
        Services.AddSingleton(jellyfinClient);

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var link = cut.Find("a.actor-badge.actor-badge-jellyfin");
        Assert.NotNull(link);
        Assert.Equal("https://jellyfin.local/web/index.html#!/details?id=jf-person-12345&serverId=server-999", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.Contains("Open in Jellyfin", link.TextContent);
        Assert.NotNull(link.QuerySelector("svg"));
    }

    [Fact]
    public void ActorDetail_RendersStaticJellyfinBadge_WhenJellyfinPersonIdLinked_ButClientReturnsNull()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 1,
            FirstName = "Yua",
            LastName = "Mikami",
            JellyfinPersonId = "jf-person-12345"
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.GetWebUrlAsync("jf-person-12345", null, Arg.Any<CancellationToken>())
            .Returns((string?)null);
        Services.AddSingleton(jellyfinClient);

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        // Clickable link is NOT rendered
        Assert.Empty(cut.FindAll("a.actor-badge-jellyfin"));

        // Static badge IS rendered
        var badge = cut.FindAll("span.actor-badge").FirstOrDefault(b => b.TextContent.Trim() == "Jellyfin");
        Assert.NotNull(badge);
        Assert.Equal("Jellyfin Person ID", badge.GetAttribute("title"));
    }

    [Fact]
    public void ActorDetail_DoesNotRenderJellyfinBadge_WhenActorHasNoJellyfinPersonId()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 1,
            FirstName = "Yua",
            LastName = "Mikami",
            JellyfinPersonId = null
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        Assert.Empty(cut.FindAll("a.actor-badge-jellyfin"));
        Assert.DoesNotContain("Open in Jellyfin", cut.Markup);
        Assert.DoesNotContain("Jellyfin Person ID", cut.Markup);
    }

    [Fact]
    public async Task ActorDetail_FiltersByStatus_UpdatesVisibleMovies()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var m1 = new Movie { Id = 10, Code = "AAA-001", Status = MovieStatus.Missing };
        var m2 = new Movie { Id = 11, Code = "AAA-002", Status = MovieStatus.Got };

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
            db.Movies.AddRange(m1, m2);
            db.MovieActors.AddRange(
                new MovieActor { ActorId = actor.Id, MovieId = m1.Id },
                new MovieActor { ActorId = actor.Id, MovieId = m2.Id });
            await db.SaveChangesAsync();
        }

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        Assert.Equal(2, cut.FindAll(".poster-card").Count);

        var missingBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Missing (1)"));
        missingBtn.Click();

        var cards = cut.FindAll(".poster-card");
        Assert.Single(cards);
        Assert.Contains("AAA-001", cards[0].TextContent);
    }

    [Fact]
    public async Task ActorDetail_VrMovie_ShowsAVrBadgeOnItsPoster()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var m1 = new Movie { Id = 10, Code = "SIVR-059", Status = MovieStatus.Got, VrType = "VR180 SBS" };
        var m2 = new Movie { Id = 11, Code = "AAA-002", Status = MovieStatus.Got };

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
            db.Movies.AddRange(m1, m2);
            db.MovieActors.AddRange(
                new MovieActor { ActorId = actor.Id, MovieId = m1.Id },
                new MovieActor { ActorId = actor.Id, MovieId = m2.Id });
            await db.SaveChangesAsync();
        }

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var badge = Assert.Single(cut.FindAll(".poster-corner-badge"));
        Assert.Equal("VR", badge.TextContent);
        Assert.Equal("VR180 SBS", badge.GetAttribute("title"));
        Assert.Contains("SIVR-059", badge.Closest(".poster-card")!.TextContent);
    }

    [Fact]
    public async Task ActorDetail_FiltersByText_AfterDebounce_UpdatesVisibleMovies()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var m1 = new Movie { Id = 10, Code = "IPX-001", Status = MovieStatus.Missing };
        var m2 = new Movie { Id = 11, Code = "SSNI-002", Status = MovieStatus.Got };

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
            db.Movies.AddRange(m1, m2);
            db.MovieActors.AddRange(
                new MovieActor { ActorId = actor.Id, MovieId = m1.Id },
                new MovieActor { ActorId = actor.Id, MovieId = m2.Id });
            await db.SaveChangesAsync();
        }

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));
        Assert.Equal(2, cut.FindAll(".poster-card").Count);

        cut.Find("input[type=search]").Input("SSNI");

        cut.WaitForAssertion(() =>
        {
            var cards = cut.FindAll(".poster-card");
            Assert.Single(cards);
            Assert.Contains("SSNI-002", cards[0].TextContent);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ActorDetail_LargeFilmography_RendersOnlyFirstBatchEager_RestAsJsLazy()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        const int movieCount = 32;
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var movies = Enumerable.Range(0, movieCount)
            .Select(i => new Movie
            {
                Id = 100 + i,
                Code = $"AAA-{i:D3}",
                MetaSourceName = "javinizer-go",
                Status = MovieStatus.Got,
                FileAddedAt = baseTime.AddSeconds(i)
            })
            .ToList();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
            db.Movies.AddRange(movies);
            db.MovieActors.AddRange(movies.Select(m => new MovieActor { ActorId = actor.Id, MovieId = m.Id }));
            await db.SaveChangesAsync();
        }

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var images = cut.FindAll(".poster-card img");
        Assert.Equal(movieCount, images.Count);

        for (var i = 0; i < movieCount; i++)
        {
            if (i < 30)
            {
                Assert.NotNull(images[i].GetAttribute("src"));
                Assert.Null(images[i].GetAttribute("data-src"));
            }
            else
            {
                Assert.Null(images[i].GetAttribute("src"));
                Assert.NotNull(images[i].GetAttribute("data-src"));
                Assert.Contains("js-lazy-poster-img", images[i].GetAttribute("class"));
            }
        }
    }

    [Fact]
    public async Task ActorDetail_SortingMoviesByTitle_OrdersCorrectly()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var m1 = new Movie { Id = 10, Code = "BBB-002", Title = "Bravo", Status = MovieStatus.Missing };
        var m2 = new Movie { Id = 11, Code = "AAA-001", Title = "Alpha", Status = MovieStatus.Got };

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
            db.Movies.AddRange(m1, m2);
            db.MovieActors.AddRange(
                new MovieActor { ActorId = actor.Id, MovieId = m1.Id },
                new MovieActor { ActorId = actor.Id, MovieId = m2.Id });
            await db.SaveChangesAsync();
        }

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var sortBtn = cut.FindAll("button").First(b => b.TextContent.StartsWith("Sort:"));
        sortBtn.Click();

        var titleBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Title"));
        titleBtn.Click();

        var renderedCodes = cut.FindAll(".poster-title").Select(e => e.TextContent).ToList();
        Assert.Equal(new[] { "AAA-001", "BBB-002" }, renderedCodes);
    }

    [Fact]
    public async Task ActorDetail_DefaultMovieSort_OrdersByReleaseDateDescending()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var mOld = new Movie
        {
            Id = 10,
            Code = "OLD-001",
            MetaReleaseDate = new DateTime(2022, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            Status = MovieStatus.Got
        };
        var mNew = new Movie
        {
            Id = 11,
            Code = "NEW-002",
            MetaReleaseDate = new DateTime(2024, 6, 20, 0, 0, 0, DateTimeKind.Utc),
            Status = MovieStatus.Got
        };
        var mNull = new Movie
        {
            Id = 12,
            Code = "NOREL-003",
            MetaReleaseDate = null,
            Status = MovieStatus.Missing
        };

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
            db.Movies.AddRange(mOld, mNew, mNull);
            db.MovieActors.AddRange(
                new MovieActor { ActorId = actor.Id, MovieId = mOld.Id },
                new MovieActor { ActorId = actor.Id, MovieId = mNew.Id },
                new MovieActor { ActorId = actor.Id, MovieId = mNull.Id });
            await db.SaveChangesAsync();
        }

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var sortBtn = cut.FindAll("button").First(b => b.TextContent.StartsWith("Sort:"));
        Assert.Contains("Sort: Release date", sortBtn.TextContent);
        Assert.Contains("▼", sortBtn.TextContent);

        var renderedCodes = cut.FindAll(".poster-title").Select(e => e.TextContent).ToList();
        Assert.Equal(new[] { "NEW-002", "OLD-001", "NOREL-003" }, renderedCodes);

        // Toggle sort direction on Release date to ascending
        sortBtn.Click();
        var releaseOption = cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains("Release date"));
        releaseOption.Click();

        var updatedSortBtn = cut.FindAll("button").First(b => b.TextContent.StartsWith("Sort:"));
        Assert.Contains("Sort: Release date", updatedSortBtn.TextContent);
        Assert.Contains("▲", updatedSortBtn.TextContent);

        var ascendingCodes = cut.FindAll(".poster-title").Select(e => e.TextContent).ToList();
        Assert.Equal(new[] { "OLD-001", "NEW-002", "NOREL-003" }, ascendingCodes);
    }

    /// <summary>Adds an already-Id-assigned movie linked to the given actor, resolved onto the
    /// real canonical Tags/MovieTags relation (reusing an already-created same-named Tag within
    /// this db instance) — ActorDetail's genre facets/filter read that relation, not MetaGenres.</summary>
    private static void AddMovieWithGenres(AppDbContext db, Movie movie, int actorId, params string[] genres)
    {
        db.Movies.Add(movie);
        db.MovieActors.Add(new MovieActor { ActorId = actorId, MovieId = movie.Id });
        db.SaveChanges();

        foreach (var name in genres)
        {
            var tag = db.Tags.FirstOrDefault(t => t.Name == name);
            if (tag is null)
            {
                tag = new Tag { Name = name };
                db.Tags.Add(tag);
                db.SaveChanges();
            }
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = tag.Id });
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task ActorDetail_ClickingGenreFilter_ShowsOnlyMoviesWithThatTag()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var m1 = new Movie { Id = 10, Code = "AAA-001", Status = MovieStatus.Got };
        var m2 = new Movie { Id = 11, Code = "AAA-002", Status = MovieStatus.Got };

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
            await db.SaveChangesAsync();
            AddMovieWithGenres(db, m1, actor.Id, "VR");
            AddMovieWithGenres(db, m2, actor.Id, "Solowork");
        }

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        cut.FindAll("button").First(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Genre")).Click();
        cut.FindAll("button").First(b => b.TextContent == "VR").Click();

        var renderedCodes = cut.FindAll(".poster-title").Select(e => e.TextContent).ToList();
        Assert.Equal(new[] { "AAA-001" }, renderedCodes);
    }

    [Fact]
    public async Task ActorDetail_GenreFacetsAndFilter_TagNameContainingAComma_ResolvesAsOneGenreNotTwo()
    {
        // A canonical Tag name that itself contains a comma (e.g. "Nasty, hardcore") must stay one
        // genre — both as a single facet in the dropdown and as a single exact match when clicked —
        // not get split into "Nasty" and "hardcore" pieces the way re-parsing MetaGenres's own
        // comma-joined text would.
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var m1 = new Movie { Id = 10, Code = "AAA-001", Status = MovieStatus.Got };
        var m2 = new Movie { Id = 11, Code = "AAA-002", Status = MovieStatus.Got };

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
            await db.SaveChangesAsync();
            AddMovieWithGenres(db, m1, actor.Id, "Nasty, hardcore", "VR");
            AddMovieWithGenres(db, m2, actor.Id, "Nasty", "hardcore");
        }

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        cut.FindAll("button").First(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Genre")).Click();

        var options = cut.FindAll(".sort-dropdown-subitem").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("Nasty, hardcore", options);
        Assert.Contains("Nasty", options);
        Assert.Contains("hardcore", options);

        cut.FindAll("button").First(b => b.TextContent == "Nasty, hardcore").Click();

        var renderedCodes = cut.FindAll(".poster-title").Select(e => e.TextContent).ToList();
        Assert.Equal(new[] { "AAA-001" }, renderedCodes);
    }

    [Fact]
    public async Task ActorDetail_ResetFilters_RestoresAllMovies()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var m1 = new Movie { Id = 10, Code = "AAA-001", Status = MovieStatus.Missing };
        var m2 = new Movie { Id = 11, Code = "AAA-002", Status = MovieStatus.Got };

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
            db.Movies.AddRange(m1, m2);
            db.MovieActors.AddRange(
                new MovieActor { ActorId = actor.Id, MovieId = m1.Id },
                new MovieActor { ActorId = actor.Id, MovieId = m2.Id });
            await db.SaveChangesAsync();
        }

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        cut.FindAll("button").First(b => b.TextContent.Contains("Missing (1)")).Click();
        Assert.Single(cut.FindAll(".poster-card"));

        cut.Find(".dropdown-clear-btn").Click();

        Assert.Equal(2, cut.FindAll(".poster-card").Count);
    }

    [Fact]
    public async Task ActorDetail_RendersPhysicalAttributes_WhenPopulated()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 1,
            FirstName = "Yua",
            LastName = "Mikami",
            HeightCm = 157,
            CupSize = "C",
            Bust = 89,
            Waist = 65,
            Hips = 94,
            BirthDate = new DateTime(1993, 8, 16),
            IsRetired = true
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
            await db.SaveChangesAsync();
        }

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var attrBlock = cut.Find(".actor-hero-attributes");
        Assert.NotNull(attrBlock);
        Assert.Contains("Birthdate:", attrBlock.TextContent);
        var expectedAge = ActorPhysicalAttributesHelper.CalculateAge(actor.BirthDate.Value);
        Assert.Contains($"16.08.1993 ({expectedAge})", attrBlock.TextContent);
        Assert.Contains("Measurements:", attrBlock.TextContent);
        Assert.Contains("89-65-94", attrBlock.TextContent);
        Assert.DoesNotContain("US", attrBlock.TextContent);
        Assert.Contains("Cup size:", attrBlock.TextContent);
        Assert.Contains("C", attrBlock.TextContent);
        Assert.Contains("Height:", attrBlock.TextContent);
        Assert.Contains("157 cm", attrBlock.TextContent);
        Assert.DoesNotContain("5'2\"", attrBlock.TextContent);

        // Retired badge check
        var retiredBadge = cut.Find(".actor-badge-retired");
        Assert.NotNull(retiredBadge);
        Assert.Equal("Retired", retiredBadge.TextContent.Trim());

        // Tooltip check
        var infoIcon = cut.Find(".actor-cup-info-icon");
        Assert.NotNull(infoIcon);
        Assert.Equal(ActorPhysicalAttributesHelper.JapaneseCupSizeExplanation, infoIcon.GetAttribute("title"));
    }

    private IRenderedComponent<ActorDetail> RenderActorWithCupHistory(TestDbContextFactory factory, Actor actor)
    {
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        return Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));
    }

    [Fact]
    public void ActorDetail_CupSizePeriods_RenderChainWithCurrentStepAndDateTooltips()
    {
        using var factory = new TestDbContextFactory();
        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami", CupSize = "C" };
        actor.CupSizePeriods.Add(new ActorCupSizePeriod { EffectiveFrom = new DateTime(2024, 1, 1), CupSize = "E" });

        var cut = RenderActorWithCupHistory(factory, actor);

        var steps = cut.FindAll(".actor-cup-step");
        Assert.Equal(["C", "E"], steps.Select(s => s.TextContent.Trim()));
        Assert.Equal("until 31.12.2023", steps[0].GetAttribute("title"));
        Assert.Equal("from 01.01.2024 (current)", steps[1].GetAttribute("title"));
        Assert.DoesNotContain("actor-cup-step-current", steps[0].ClassList);
        Assert.Contains("actor-cup-step-current", steps[1].ClassList);
        Assert.Single(cut.FindAll(".actor-cup-arrow"));
        Assert.Single(cut.FindAll(".actor-cup-info-icon"));
    }

    [Fact]
    public void ActorDetail_CupSizePeriodsWithoutBaseCup_StillShowCupAttribute()
    {
        using var factory = new TestDbContextFactory();
        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        actor.CupSizePeriods.Add(new ActorCupSizePeriod { EffectiveFrom = new DateTime(2024, 1, 1), CupSize = "E" });

        var cut = RenderActorWithCupHistory(factory, actor);

        Assert.Contains("Cup size:", cut.Find(".actor-hero-attributes").TextContent);
        var step = Assert.Single(cut.FindAll(".actor-cup-step"));
        Assert.Equal("E", step.TextContent.Trim());
        Assert.Empty(cut.FindAll(".actor-cup-arrow"));
    }

    [Fact]
    public void ActorDetail_NoCupSizePeriods_ShowsPlainCupWithoutChain()
    {
        using var factory = new TestDbContextFactory();
        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami", CupSize = "C" };

        var cut = RenderActorWithCupHistory(factory, actor);

        Assert.Empty(cut.FindAll(".actor-cup-arrow"));
        Assert.Empty(cut.FindAll(".actor-cup-step-current"));
        Assert.Null(Assert.Single(cut.FindAll(".actor-cup-step")).GetAttribute("title"));
    }

    [Fact]
    public async Task ActorDetail_OmitsPhysicalAttributes_WhenEmpty()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor
        {
            Id = 1,
            FirstName = "Remu",
            LastName = null
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Remu", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
            await db.SaveChangesAsync();
        }

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Remu"));

        Assert.Empty(cut.FindAll(".actor-hero-attributes"));
    }

    [Fact]
    public async Task WapdbEnrichment_RefreshesPhotoGalleryWithoutRequiringPageRefresh()
    {
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 9, FirstName = "Yua", LastName = "Mikami" };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        var photoService = Substitute.For<IActorPhotoService>();
        var photos = new List<ActorPhotoSummary>();
        photoService.GetGalleryAsync(9, Arg.Any<CancellationToken>())
            .Returns(_ => new ActorPhotoGalleryData([], photos.ToList()));
        Services.AddSingleton(photoService);

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));
        cut.WaitForAssertion(() => Assert.Contains("No photos in this selection yet.", cut.Markup));

        // Simulate a WAPdB enrichment importing a photo (ActorService.ImportOrEnrichFromWarashiAsync
        // would have written this row) and then raising the same OnEnriched callback EnrichActor fires.
        photos.Add(new ActorPhotoSummary(1, null, DateTime.UtcNow));
        var searchModal = cut.FindComponent<ActorMetadataSearchModal>();
        await cut.InvokeAsync(() => searchModal.Instance.OnEnriched.InvokeAsync());

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".actor-photo-tile")));
    }

    private async Task<IRenderedComponent<ActorDetail>> RenderWithMoviesAsync(params MovieStatus[] movieStatuses)
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = actor.Id, FirstName = actor.FirstName, LastName = actor.LastName });
            for (var i = 0; i < movieStatuses.Length; i++)
            {
                var movie = new Movie { Id = 10 + i, Code = $"AAA-{i:000}", Status = movieStatuses[i] };
                db.Movies.Add(movie);
                db.MovieActors.Add(new MovieActor { ActorId = actor.Id, MovieId = movie.Id });
            }
            await db.SaveChangesAsync();
        }

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());

        return Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));
    }

    [Fact]
    public async Task ActorDetail_WithGotMovies_ShowsReviewButtonLinkingToScopedReview()
    {
        var cut = await RenderWithMoviesAsync(MovieStatus.Missing, MovieStatus.Got);

        var review = cut.FindAll("a.actor-toolbar-btn").FirstOrDefault(a => a.TextContent.Contains("Review"));
        Assert.NotNull(review);
        Assert.Equal("/movies/review?actorId=1", review.GetAttribute("href"));
        Assert.DoesNotContain(cut.FindAll(".actor-toolbar-btn"), b => b.TextContent.Contains("Cleanup"));
    }

    [Fact]
    public async Task ActorDetail_WithoutGotMovies_HidesReviewButton()
    {
        var cut = await RenderWithMoviesAsync(MovieStatus.Missing);

        Assert.DoesNotContain(cut.FindAll(".actor-toolbar-btn"), b => b.TextContent.Contains("Review"));
    }

    [Fact]
    public async Task ActorDetail_MoviesSection_IsACardHeadedWithTheTotalMovieCount_EvenWhileFiltered()
    {
        var cut = await RenderWithMoviesAsync(MovieStatus.Missing, MovieStatus.Got);

        var section = cut.Find("section.actor-detail-movies.card");
        Assert.Equal("Movies (2)", section.QuerySelector("h2.card-title")!.TextContent);
        Assert.Equal(2, section.QuerySelectorAll(".card-body .poster-card").Length);

        cut.FindAll("button").First(b => b.TextContent.Contains("Missing (1)")).Click();

        section = cut.Find("section.actor-detail-movies.card");
        Assert.Equal("Movies (2)", section.QuerySelector("h2.card-title")!.TextContent);
        Assert.Single(section.QuerySelectorAll(".poster-card"));
    }

    [Fact]
    public async Task ActorDetail_MoviesSection_KeepsItsCardAndEmptyState_WhenTheActorHasNoMovies()
    {
        using var factory = new TestDbContextFactory();
        var cut = await RenderWithASceneAsync(factory);

        var section = cut.Find("section.actor-detail-movies.card");
        Assert.Equal("Movies (0)", section.QuerySelector("h2.card-title")!.TextContent);
        Assert.Contains("No movies in your library are matched to this actor yet.", section.TextContent);
    }

    private async Task<IRenderedComponent<ActorDetail>> RenderWithASceneAsync(TestDbContextFactory factory)
    {
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));
        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
        }

        sceneWall.GetPageAsync(Arg.Is<SceneWallFilter>(f => f.ActorIds!.SequenceEqual(new[] { 1 })), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([new SceneWallCard(5, 10, "ABC-123", "Title", "Scene 1", 0, 60, ["Yua Mikami"], [], false, null, null)], 1));
        return Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));
    }

    [Fact]
    public async Task ActorDetail_ShowsOnlyTheClipSectionsTheActorHas_AfterMoviesByDefault()
    {
        using var factory = new TestDbContextFactory();
        var cut = await RenderWithASceneAsync(factory);

        var section = cut.Find(".actor-clips");
        Assert.Equal("scene", section.GetAttribute("data-kind"));
        Assert.Contains("Scenes (1)", section.TextContent);
        Assert.Single(cut.FindAll(".actor-clips"));
        Assert.True(cut.Markup.IndexOf("actor-detail-movies", StringComparison.Ordinal) < cut.Markup.IndexOf("actor-clips card", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ActorDetail_OrdersSectionsAsTheBrowserRemembers()
    {
        JSInterop.Setup<string?>("lsActorDetailSections.get").SetResult("Scenes,Movies");
        using var factory = new TestDbContextFactory();
        var cut = await RenderWithASceneAsync(factory);

        cut.WaitForAssertion(() =>
            Assert.True(cut.Markup.IndexOf("actor-clips card", StringComparison.Ordinal) < cut.Markup.IndexOf("actor-detail-movies", StringComparison.Ordinal)));
    }

    // Poster options just changed on the Movies page reach a live circuit only through the browser's cookie.
    [Fact]
    public async Task ActorDetail_UsesThePosterOptionsTheBrowserRemembers()
    {
        JSInterop.Setup<string?>("lsPosterOptions.get").SetResult("""{"PosterSize":"small","ShowTitle":false}""");
        using var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorDetailQueryService>(new ActorDetailQueryService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));
        var yua = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(yua);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());
        Services.AddSingleton(Substitute.For<INfoSyncService>());
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { Id = yua.Id, FirstName = yua.FirstName, LastName = yua.LastName });
            var movie = new Movie { Code = "AAA-001", Status = MovieStatus.Got };
            movie.MovieActors.Add(new MovieActor { ActorId = yua.Id });
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        var cut = Render<ActorDetail>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        cut.WaitForAssertion(() =>
        {
            var classes = cut.Find(".movie-poster-grid").ClassList;
            Assert.Contains("movie-poster-size-small", classes);
            Assert.Contains("movie-poster-hide-title", classes);
        });
    }
}
