using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.R18Dev;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

/// <summary>Smoke coverage for Movies.razor post-Phase 3 (PosterGrid/PosterCard/
/// StatusFilterButtonGroup/DropdownMenuButton extraction) — renders correctly and the status
/// filter buttons still drive the underlying EF query.</summary>
public class MoviesTests : BunitContext
{
    private TestDbContextFactory SetUpServices()
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        RegisterCommonServices();
        return factory;
    }

    /// <summary>Same setup as <see cref="SetUpServices"/>, but with the registered
    /// <see cref="IDbContextFactory{TContext}"/> wrapped in a call-counting decorator — for tests
    /// that need to assert *how many times* the grid reloaded (e.g. the text filter's debounce),
    /// not just what it ends up showing. Still backed by the same single <see
    /// cref="TestDbContextFactory"/>, so this isn't a second DB test double, just instrumentation
    /// around the one the project already uses.</summary>
    private (TestDbContextFactory Db, CountingDbContextFactory Counting) SetUpServicesWithCallCounting()
    {
        // Own connection per context: the initial load can still be running when the debounced reload
        // starts, and a shared SQLite connection isn't safe for concurrent queries.
        var factory = TestDbContextFactory.WithConnectionPerContext();
        var counting = new CountingDbContextFactory(factory);
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(counting);
        RegisterCommonServices();
        return (factory, counting);
    }

    private void RegisterCommonServices()
    {
        Services.AddSingleton(TimeProvider.System);
        // Replaced by SetUpReleaseBrowsing for the tests that open "All releases (r18.dev)".
        Services.AddSingleton(Substitute.For<IR18DevReleaseBrowseService>());
        Services.AddSingleton(new MovieChangeNotifier());
        Services.AddSingleton<IMovieService>(sp => new MovieService(
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            sp.GetRequiredService<MovieChangeNotifier>()));
        Services.AddSingleton<IMovieGridQueryService>(sp => new MovieGridQueryService(
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));
        Services.AddSingleton(new TorrentChangeNotifier());
        Services.AddSingleton(new MovieFilterNavigationState());
        Services.AddSingleton(Substitute.For<ILogger<Movies>>());
        Services.AddSingleton(sp => new NfoDriftPushLauncher(
            new BackgroundJobRunner(sp.GetRequiredService<IServiceScopeFactory>(), Substitute.For<ILogger<BackgroundJobRunner>>()),
            new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System),
            sp.GetRequiredService<MovieChangeNotifier>()));
        // Movies.razor resolves PersistentComponentState optionally (via IServiceProvider) so it
        // degrades gracefully — normal DB load, no persist/restore — when it's not registered,
        // exactly like here: bUnit's DI container doesn't wire up ASP.NET Core's real hosting
        // registrations, and PersistentComponentState has no accessible public constructor to
        // stand one up manually.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private sealed class CountingDbContextFactory(IDbContextFactory<AppDbContext> inner) : IDbContextFactory<AppDbContext>
    {
        public int CreateCount { get; private set; }

        public AppDbContext CreateDbContext()
        {
            CreateCount++;
            return inner.CreateDbContext();
        }

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            CreateCount++;
            return inner.CreateDbContextAsync(cancellationToken);
        }
    }

    [Fact]
    public void EmptyLibrary_ShowsNoMoviesMessageAndZeroCounts()
    {
        using var factory = SetUpServices();

        var cut = Render<Movies>();

        Assert.Contains("No movies here yet", cut.Markup);
        Assert.Contains("All (0)", cut.Markup);
    }

    [Fact]
    public void MoviesPresent_RendersOnePosterCardPerMovie()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Missing });
            db.Movies.Add(new Movie { Code = "AAA-002", Status = MovieStatus.Got });
            db.SaveChanges();
        }

        var cut = Render<Movies>();

        Assert.Equal(2, cut.FindAll(".poster-card").Count);
        Assert.Contains("All (2)", cut.Markup);
        Assert.Contains("Missing (1)", cut.Markup);
        Assert.Contains("Got (1)", cut.Markup);
    }

    /// <summary>Seeds <paramref name="count"/> movies whose "added" sort order (CreatedAt
    /// descending, the page's default) is exactly M000, M001, M002, … so a rendered window can be
    /// asserted against the indices it was asked for.</summary>
    private static void SeedOrderedMovies(TestDbContextFactory factory, int count)
    {
        var newest = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using var db = factory.CreateDbContext();
        for (var i = 0; i < count; i++)
        {
            db.Movies.Add(new Movie { Code = $"M{i:D3}", Status = MovieStatus.Missing, CreatedAt = newest.AddSeconds(-i) });
        }
        db.SaveChanges();
    }

    private static List<string> RenderedCodes(IRenderedComponent<Movies> cut) =>
        cut.FindAll(".poster-title").Select(e => e.TextContent).ToList();

    [Fact]
    public void InitialRender_ShowsOnlyTheFirstWindowNotTheWholeLibrary()
    {
        using var factory = SetUpServices();
        SeedOrderedMovies(factory, 200);

        var cut = Render<Movies>();

        var codes = RenderedCodes(cut);
        Assert.Equal(60, codes.Count);
        Assert.Equal("M000", codes[0]);
        Assert.Equal("M059", codes[^1]);
    }

    [Fact]
    public async Task SetVisibleRange_JumpingClearPastTheLoadedWindow_RendersTheRequestedRange()
    {
        using var factory = SetUpServices();
        SeedOrderedMovies(factory, 200);
        var cut = Render<Movies>();

        // The scrollbar-drag case: a range that shares nothing with what's loaded.
        await cut.InvokeAsync(() => cut.Instance.SetVisibleRange(120, 36, 6));

        var codes = RenderedCodes(cut);
        Assert.Equal(36, codes.Count);
        Assert.Equal("M120", codes[0]);
        Assert.Equal("M155", codes[^1]);
    }

    [Fact]
    public async Task SetVisibleRange_OverlappingTheLoadedWindow_SlidesToTheRequestedRange()
    {
        using var factory = SetUpServices();
        SeedOrderedMovies(factory, 200);
        var cut = Render<Movies>();

        // The ordinary scroll case: the new window shares its first half with the old one. (That
        // the shared half is *reused* rather than re-fetched — which is what keeps the grid from
        // flashing — isn't assertable here: bUnit re-parses its DOM from scratch on every render,
        // so element identity never survives a re-render regardless of what Blazor's diff did.)
        await cut.InvokeAsync(() => cut.Instance.SetVisibleRange(30, 60, 6));

        var codes = RenderedCodes(cut);
        Assert.Equal(60, codes.Count);
        Assert.Equal("M030", codes[0]);
        Assert.Equal("M089", codes[^1]);
    }

    [Fact]
    public async Task SetVisibleRange_PastTheEndOfTheList_ClampsInsteadOfRenderingBlanks()
    {
        using var factory = SetUpServices();
        SeedOrderedMovies(factory, 100);
        var cut = Render<Movies>();

        await cut.InvokeAsync(() => cut.Instance.SetVisibleRange(90, 60, 6));

        var codes = RenderedCodes(cut);
        Assert.Equal("M090", codes[0]);
        Assert.Equal("M099", codes[^1]);
    }

    [Fact]
    public void ClickingStatusSort_OrdersMissingBeforeGot()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Got });
            db.Movies.Add(new Movie { Code = "AAA-002", Status = MovieStatus.Missing });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Sort:")).Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Status")).Click();

        Assert.Equal(new[] { "AAA-002", "AAA-001" }, RenderedCodes(cut));
    }

    [Fact]
    public void ClickingReleaseDateSort_OrdersNewestFirstWithUndatedMoviesLast()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MetaReleaseDate = new DateTime(2025, 1, 1) });
            db.Movies.Add(new Movie { Code = "AAA-002", MetaReleaseDate = null });
            db.Movies.Add(new Movie { Code = "AAA-003", MetaReleaseDate = new DateTime(2026, 1, 1) });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Sort:")).Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Release date")).Click();

        Assert.Equal(new[] { "AAA-003", "AAA-001", "AAA-002" }, RenderedCodes(cut));
    }

    [Fact]
    public void ClickingRuntimeSort_OrdersLongestFirstWithUntimedMoviesLast()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MetaRuntimeMinutes = 90 });
            db.Movies.Add(new Movie { Code = "AAA-002", MetaRuntimeMinutes = null });
            db.Movies.Add(new Movie { Code = "AAA-003", MetaRuntimeMinutes = 180 });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Sort:")).Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Runtime")).Click();

        Assert.Equal(new[] { "AAA-003", "AAA-001", "AAA-002" }, RenderedCodes(cut));
    }

    [Fact]
    public void ClickingRandomSort_ShowsEveryMovieExactlyOnceWithNoArrow()
    {
        using var factory = SetUpServices();
        SeedOrderedMovies(factory, 30);

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Sort:")).Click();
        cut.FindAll(".sort-dropdown-item").Single(b => b.TextContent.Contains("Random")).Click();

        // Order itself is seed-dependent (not asserted here), but every movie must still appear
        // exactly once, and the direction arrow (meaningless for a shuffle) must be gone.
        var codes = RenderedCodes(cut);
        Assert.Equal(30, codes.Count);
        Assert.Equal(codes.Distinct().Count(), codes.Count);
        Assert.StartsWith("Sort: Random", cut.FindAll("button").Single(b => b.TextContent.StartsWith("Sort:")).TextContent);
        Assert.DoesNotContain("▼", cut.FindAll("button").Single(b => b.TextContent.StartsWith("Sort:")).TextContent);
        Assert.DoesNotContain("▲", cut.FindAll("button").Single(b => b.TextContent.StartsWith("Sort:")).TextContent);
    }

    [Fact]
    public void ClickingRandomSortAgain_ReshufflesInsteadOfTogglingDirection()
    {
        using var factory = SetUpServices();
        SeedOrderedMovies(factory, 30);

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Sort:")).Click();
        cut.FindAll(".sort-dropdown-item").Single(b => b.TextContent.Contains("Random")).Click();
        var firstOrder = RenderedCodes(cut);

        // Re-picking Random a bunch of times should never surface duplicate/missing movies, even
        // though each click regenerates the shuffle seed.
        var sawDifferentOrder = false;
        for (var i = 0; i < 10 && !sawDifferentOrder; i++)
        {
            cut.FindAll("button").Single(b => b.TextContent.StartsWith("Sort:")).Click();
            cut.FindAll(".sort-dropdown-item").Single(b => b.TextContent.Contains("Random")).Click();

            var codes = RenderedCodes(cut);
            Assert.Equal(30, codes.Count);
            Assert.Equal(codes.Distinct().Count(), codes.Count);
            sawDifferentOrder = !codes.SequenceEqual(firstOrder);
        }
        Assert.True(sawDifferentOrder, "Expected at least one reshuffle to change the order across 10 attempts.");
    }

    [Fact]
    public void ClickingMissingFilter_NarrowsTheGridToMissingMoviesOnly()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Missing });
            db.Movies.Add(new Movie { Code = "AAA-002", Status = MovieStatus.Got });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        var missingButton = cut.FindAll("button").Single(b => b.TextContent.Contains("Missing ("));
        missingButton.Click();

        var titles = cut.FindAll(".poster-title").Select(e => e.TextContent).ToList();
        Assert.Single(titles);
        Assert.Contains("AAA-001", titles);
    }

    [Fact]
    public void ResetFilters_ClearsTheStatusFilterWithoutChangingSort()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "BBB-002", Status = MovieStatus.Missing });
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Got });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Sort:")).Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Title")).Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Missing (")).Click();

        Assert.Equal("×", cut.Find(".dropdown-clear-btn").TextContent.Trim());
        cut.Find(".dropdown-clear-btn").Click();

        Assert.Equal(new[] { "AAA-001", "BBB-002" }, RenderedCodes(cut));
        Assert.StartsWith("Sort: Title", cut.FindAll("button").Single(b => b.TextContent.StartsWith("Sort:")).TextContent);
        Assert.Empty(cut.FindAll(".dropdown-clear-btn"));
    }

    [Fact]
    public void ResetFilters_FromEmptyResultRestoresTheGrid()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Got });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Missing (")).Click();

        Assert.Contains("No movies match the active filters.", cut.Markup);
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Reset filters").Click();

        Assert.Equal(new[] { "AAA-001" }, RenderedCodes(cut));
        Assert.DoesNotContain("No movies match the active filters.", cut.Markup);
    }

    [Fact]
    public void ResetFilters_ClearsSearchQueryAndPendingNavigationFilter()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MetaStudio = "Studio A" });
            db.Movies.Add(new Movie { Code = "BBB-002", MetaStudio = "Studio B" });
            db.SaveChanges();
        }

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("?q=AAA");
        var cut = Render<Movies>();
        var filterState = Services.GetRequiredService<MovieFilterNavigationState>();
        filterState.SetPendingFilter(studio: "Studio A");

        Assert.Equal(new[] { "AAA-001" }, RenderedCodes(cut));
        cut.Find(".dropdown-clear-btn").Click();

        Assert.Equal(new[] { "AAA-001", "BBB-002" }, RenderedCodes(cut).OrderBy(code => code));
        Assert.Equal("http://localhost/", nav.Uri);
        Assert.Null(filterState.PeekPendingFilter());
        Assert.Empty(cut.FindAll(".dropdown-clear-btn"));
    }

    [Fact]
    public void ResetFilters_FromDropdownFilter_ClearsMultiSelectAndCountWithoutChangingSort()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MediaWidth = 1920, MediaHeight = 1080 });
            db.Movies.Add(new Movie { Code = "BBB-002", MediaWidth = 720, MediaHeight = 480 });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Resolution")).Click();
        cut.FindAll(".sort-dropdown-subitem").Single(b => b.TextContent.Contains("HD")).Click();

        Assert.Equal(new[] { "AAA-001" }, RenderedCodes(cut));
        Assert.Equal("Filter (1)", cut.Find(".dropdown-main-btn").TextContent.Trim());
        Assert.Equal("×", cut.Find(".dropdown-clear-btn").TextContent.Trim());

        cut.Find(".dropdown-clear-btn").Click();

        Assert.StartsWith("Filter", cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).TextContent);
        Assert.DoesNotContain("Filter (", cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).TextContent);
        Assert.Empty(cut.FindAll(".dropdown-clear-btn"));
    }

    [Fact]
    public void ResolutionFilter_NotActiveByDefault_ShowsAllMovies()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MediaWidth = 720 });
            db.Movies.Add(new Movie { Code = "AAA-002", MediaWidth = 1920 });
            db.Movies.Add(new Movie { Code = "AAA-003", MediaWidth = null });
            db.SaveChanges();
        }

        var cut = Render<Movies>();

        Assert.Equal(3, cut.FindAll(".poster-card").Count);
        Assert.DoesNotContain("Filter (1)", cut.Markup);
    }

    [Fact]
    public void ScanTypeFilter_AlwaysOffersProgressiveAndInterlaced()
    {
        using var factory = SetUpServices();

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Scan Type")).Click();

        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Progressive");
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Interlaced");
    }

    [Fact]
    public void ScanTypeFilter_MatchesProgressiveOrEveryOtherProbedScanType_AndCombinesWithResolution()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.AddRange(
                new Movie { Code = "AAA-001", MediaWidth = 1920, MediaScanType = " progressive " },
                new Movie { Code = "AAA-002", MediaWidth = 1920, MediaScanType = "Interlaced" },
                new Movie { Code = "AAA-003", MediaWidth = 720, MediaScanType = "MBAFF" },
                new Movie { Code = "AAA-004", MediaWidth = 720, MediaScanType = "Mixed" },
                new Movie { Code = "AAA-005", MediaWidth = 720, MediaScanType = null });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Scan Type")).Click();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Interlaced").Click();

        Assert.Equal(new[] { "AAA-002", "AAA-003", "AAA-004" }, RenderedCodes(cut).OrderBy(code => code));
        Assert.Contains("Filter (1)", cut.Markup);

        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Resolution")).Click();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "SD").Click();

        Assert.Equal(new[] { "AAA-003", "AAA-004" }, RenderedCodes(cut).OrderBy(code => code));

        var progressiveCut = Render<Movies>();
        progressiveCut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        progressiveCut.FindAll("button").Single(b => b.TextContent.StartsWith("Scan Type")).Click();
        progressiveCut.FindAll("button").Single(b => b.TextContent.Trim() == "Progressive").Click();

        Assert.Equal(new[] { "AAA-001" }, RenderedCodes(progressiveCut));
    }

    [Fact]
    public void InterlacedMovie_ShowsHeightWithInterlacedSuffixOnPoster()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MediaWidth = 1920, MediaHeight = 1080, MediaScanType = "Interlaced" });
            db.SaveChanges();
        }

        var cut = Render<Movies>();

        Assert.Contains(cut.FindAll(".poster-meta-sub"), element => element.TextContent.Trim() == "1080i");
    }

    [Fact]
    public void ClickingHdFilter_NarrowsToHdOnly_AndClickingAgainClears()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MediaWidth = 720 });   // SD
            db.Movies.Add(new Movie { Code = "AAA-002", MediaWidth = 1920 });  // HD (1080p)
            db.Movies.Add(new Movie { Code = "AAA-003", MediaWidth = 1280 }); // HD (720p)
            db.Movies.Add(new Movie { Code = "AAA-004", MediaWidth = 3840 }); // 4K
            db.Movies.Add(new Movie { Code = "AAA-005", MediaWidth = null }); // never probed
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Resolution")).Click();
        // The dropdown (and the expanded Resolution group) deliberately stay open after a toggle
        // (see ToggleResolutionFilter) so several buckets can be picked without re-opening either —
        // no need to re-click "Filter"/"Resolution" here.
        cut.FindAll("button").Single(b => b.TextContent.Contains("HD (720p & 1080p)")).Click();

        var codes = RenderedCodes(cut);
        Assert.Equal(new[] { "AAA-002", "AAA-003" }, codes.OrderBy(c => c));
        Assert.Contains("Filter (1)", cut.Markup);

        cut.FindAll("button").Single(b => b.TextContent.Contains("HD (720p & 1080p)")).Click();

        Assert.Equal(5, cut.FindAll(".poster-card").Count);
        Assert.DoesNotContain("Filter (1)", cut.Markup);
    }

    [Fact]
    public void SelectingTwoResolutionBuckets_ShowsMoviesMatchingEither()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MediaWidth = 720 });   // SD
            db.Movies.Add(new Movie { Code = "AAA-002", MediaWidth = 1920 });  // HD
            db.Movies.Add(new Movie { Code = "AAA-003", MediaWidth = 3840 }); // 4K
            db.Movies.Add(new Movie { Code = "AAA-004", MediaWidth = 7680 }); // 8K
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Resolution")).Click();
        cut.FindAll("button").Single(b => b.TextContent == "SD").Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("4K")).Click();

        var codes = RenderedCodes(cut);
        Assert.Equal(new[] { "AAA-001", "AAA-003" }, codes.OrderBy(c => c));
        Assert.Contains("Filter (2)", cut.Markup);
    }

    [Fact]
    public void ActivatingResolutionFilter_UpdatesAllMissingGotCountsToMatch()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Missing, MediaWidth = 720 });   // SD, Missing
            db.Movies.Add(new Movie { Code = "AAA-002", Status = MovieStatus.Got, MediaWidth = 720 });      // SD, Got
            db.Movies.Add(new Movie { Code = "AAA-003", Status = MovieStatus.Missing, MediaWidth = 3840 });  // 4K, Missing — excluded once filtered
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        Assert.Contains("All (3)", cut.Markup);
        Assert.Contains("Missing (2)", cut.Markup);
        Assert.Contains("Got (1)", cut.Markup);

        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Resolution")).Click();
        cut.FindAll("button").Single(b => b.TextContent == "SD").Click();

        // With the resolution filter narrowing to the two SD movies, the status counts should
        // reflect that — not stay frozen at the library's unfiltered totals.
        Assert.Contains("All (2)", cut.Markup);
        Assert.Contains("Missing (1)", cut.Markup);
        Assert.Contains("Got (1)", cut.Markup);
    }

    [Fact]
    public void ClickingCodecFilter_NarrowsToThatCodecOnly()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MediaVideoCodec = "AVC" });
            db.Movies.Add(new Movie { Code = "AAA-002", MediaVideoCodec = "VP9" });
            db.Movies.Add(new Movie { Code = "AAA-003", MediaVideoCodec = null });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Codec")).Click();
        cut.FindAll("button").Single(b => b.TextContent == "AVC").Click();

        Assert.Equal(new[] { "AAA-001" }, RenderedCodes(cut));
    }

    [Fact]
    public void ClickingActorCupSizeFilter_NarrowsToMoviesWithThatCupSize_AndResetClearsIt()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            var e = new Movie { Code = "AAA-001" };
            e.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Eve", CupSize = "E" } });
            var b = new Movie { Code = "AAA-002" };
            b.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Bea", CupSize = "B" } });
            db.Movies.AddRange(e, b, new Movie { Code = "AAA-003" });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Actor")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Cup Size")).Click();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "E").Click();

        Assert.Equal(new[] { "AAA-001" }, RenderedCodes(cut));

        cut.FindAll("button").Single(b => b.GetAttribute("title") == "Reset all filters").Click();

        Assert.Equal(new[] { "AAA-001", "AAA-002", "AAA-003" }, RenderedCodes(cut).OrderBy(c => c));
    }

    private void RememberedMoviesState(string json)
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "javbuddy-movies-view=" + Uri.EscapeDataString(json);
        httpContextAccessor.HttpContext.Returns(httpContext);
        Services.AddSingleton(httpContextAccessor);
    }

    private static void SeedBustMovies(AppDbContext db)
    {
        var big = new Movie { Code = "AAA-001", MetaReleaseDate = new DateTime(2021, 6, 15) };
        big.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Big", CupSize = "E", Bust = 95, BirthDate = new DateTime(2000, 6, 15) } });
        var small = new Movie { Code = "AAA-002", MetaReleaseDate = new DateTime(2021, 6, 15) };
        small.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Small", CupSize = "B", Bust = 80, BirthDate = new DateTime(1990, 6, 15) } });
        db.Movies.AddRange(big, small, new Movie { Code = "AAA-003" });
        db.SaveChanges();
    }

    [Fact]
    public void ActorAttributes_OfferTheMeasurementSliders_AndARememberedRangeNarrowsTheGrid()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext()) SeedBustMovies(db);
        RememberedMoviesState("""{"ActorAttributes":{"Bust":{"Min":90}}}""");

        var cut = Render<Movies>();

        Assert.Equal(new[] { "AAA-001" }, RenderedCodes(cut));
        Assert.Contains("Filter (1)", cut.Markup);
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Actor")).Click();
        Assert.Contains("Bust (90–95 cm)", cut.Markup);
        Assert.Contains("Age", cut.Markup);
    }

    [Fact]
    public void ActorAttributes_ARememberedSelectionTheLibraryNoLongerOffers_IsDroppedNotApplied()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext()) SeedBustMovies(db);
        // Cup K (nobody has it) and a bust floor below every known bust (the end stop: no limit): both stale.
        RememberedMoviesState("""{"ActorAttributes":{"CupSizes":["K"],"Bust":{"Min":40}}}""");

        var cut = Render<Movies>();

        Assert.Equal(new[] { "AAA-001", "AAA-002", "AAA-003" }, RenderedCodes(cut).OrderBy(c => c));
        Assert.DoesNotContain("Filter (", cut.Markup);
    }

    [Fact]
    public void ActorAttributes_ARememberedFloorAboveEveryKnownValue_IsPulledToTheTopAndShownAsActive()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext()) SeedBustMovies(db);
        RememberedMoviesState("""{"ActorAttributes":{"Bust":{"Min":120}}}""");

        var cut = Render<Movies>();

        Assert.Equal(new[] { "AAA-001" }, RenderedCodes(cut));
        Assert.Contains("Filter (1)", cut.Markup);
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Actor")).Click();
        Assert.Contains("Bust (95 cm)", cut.Markup);
    }

    [Fact]
    public void ActorAttributes_AgeRangeUsesTheReleaseDate_AndOldFlatCookieKeysAreIgnored()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext()) SeedBustMovies(db);
        // Big was 21 on AAA-001's release, Small was 31 on AAA-002's. The old flat keys must not break loading.
        RememberedMoviesState("""{"ActorCupSizeFilter":["E"],"ActorHeightMin":150,"ActorAttributes":{"Age":{"Min":30,"Max":35}}}""");

        var cut = Render<Movies>();

        Assert.Equal(new[] { "AAA-002" }, RenderedCodes(cut));
    }

    /// <summary>Creates a movie with the given genres resolved onto the real canonical Tags/
    /// MovieTags relation (reusing an already-created same-named Tag within this db instance) —
    /// the Movies grid's genre facets/filter read that relation, not MetaGenres.
    /// MetaGenres is still set (comma-joined) to match what TagNormalization would leave behind,
    /// even though the grid itself no longer reads it for genre purposes.</summary>
    private static void AddMovieWithGenres(AppDbContext db, string code, params string[] genres)
    {
        var movie = new Movie { Code = code, MetaGenres = genres.Length > 0 ? string.Join(", ", genres) : null };
        db.Movies.Add(movie);
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

    private static (int Amy, int Bea) SeedActorMovies(AppDbContext db)
    {
        var amy = new Actor { FirstName = "Amy" };
        var bea = new Actor { FirstName = "Bea" };
        var first = new Movie { Code = "AAA-001" };
        var second = new Movie { Code = "AAA-002" };
        first.MovieActors.Add(new MovieActor { Actor = amy });
        second.MovieActors.Add(new MovieActor { Actor = bea });
        db.Movies.AddRange(first, second, new Movie { Code = "AAA-003" });
        db.SaveChanges();
        return (amy.Id, bea.Id);
    }

    [Fact]
    public void ActorNameFilter_NarrowsTheGrid_AndIsRemembered()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext()) SeedActorMovies(db);

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Actor")).Click();
        cut.FindAll("button").Single(b => b.TextContent.Trim().StartsWith("Name")).Click();
        Assert.Equal(["Amy", "Bea"], cut.FindAll(".sort-dropdown-subitem").Select(b => b.TextContent.Trim()));
        cut.FindAll(".sort-dropdown-subitem").Single(b => b.TextContent.Trim() == "Amy").Click();

        Assert.Equal(new[] { "AAA-001" }, RenderedCodes(cut));
        Assert.Contains("Filter (1)", cut.Markup);
        var saved = JSInterop.Invocations.Last(inv => inv.Identifier == "setJsonCookie").Arguments.Select(a => a?.ToString() ?? "").ToList();
        Assert.Contains(saved, a => a.Contains("\"ActorFilter\":[", StringComparison.Ordinal));
    }

    [Fact]
    public void ActorNameFilter_ARememberedSelectionIsApplied_AndAnActorNoLongerLinkedIsDropped()
    {
        using var factory = SetUpServices();
        int bea;
        using (var db = factory.CreateDbContext()) (_, bea) = SeedActorMovies(db);
        RememberedMoviesState($$"""{"ActorFilter":[{{bea}},99999]}""");

        var cut = Render<Movies>();

        Assert.Equal(new[] { "AAA-002" }, RenderedCodes(cut));
        Assert.Contains("Filter (1)", cut.Markup);
    }

    [Fact]
    public void VrMovie_ShowsAVrBadgeOnItsPoster_AndItsFormatBesideTheResolution()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "SIVR-059", Status = MovieStatus.Got, MediaWidth = 3840, MediaHeight = 1920, MediaScanType = "Progressive", VrType = "VR180 SBS" });
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Got, MediaWidth = 1920, MediaHeight = 1080, MediaScanType = "Progressive", VrType = "3D HSBS" });
            db.Movies.Add(new Movie { Code = "AAA-002", Status = MovieStatus.Got, MediaWidth = 1920, MediaHeight = 1080, MediaScanType = "Progressive" });
            db.SaveChanges();
        }

        var cut = Render<Movies>();

        var badges = cut.FindAll(".poster-corner-badge").Select(b => (b.TextContent, b.GetAttribute("title"))).OrderBy(b => b.TextContent).ToList();
        Assert.Equal([("3D", "3D HSBS"), ("VR", "VR180 SBS")], badges);
        var subLines = cut.FindAll(".poster-meta-sub").Select(e => e.TextContent.Trim()).ToList();
        Assert.Contains(subLines, l => l.EndsWith(" · VR180 SBS", StringComparison.Ordinal));
        Assert.Contains("HD · 3D HSBS", subLines);
        Assert.Contains("HD", subLines);
    }

    [Fact]
    public void UncheckingShowVrBadge_HidesTheVrBadge_ButKeepsTheFormatBesideTheResolution()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Got, MediaWidth = 1920, MediaHeight = 1080, MediaScanType = "Progressive", VrType = "3D HSBS" });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        Assert.Single(cut.FindAll(".poster-corner-badge")); // Show VR Badge defaults to on

        cut.FindAll("button").Single(b => b.TextContent == "Options").Click();
        cut.Find("#show-vr-check").Change(false);

        Assert.Empty(cut.FindAll(".poster-corner-badge"));
        Assert.Contains("HD · 3D HSBS", cut.FindAll(".poster-meta-sub").Select(e => e.TextContent.Trim()));
    }

    [Fact]
    public void VrFeatureFilter_ShowsOnlyVrAnd3DMovies()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", VrType = "VR180 SBS" });
            db.Movies.Add(new Movie { Code = "AAA-002", VrType = "3D" });
            db.Movies.Add(new Movie { Code = "AAA-003" });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Features")).Click();
        cut.FindAll(".sort-dropdown-subitem").Single(b => b.TextContent.Trim() == "VR").Click();

        Assert.Equal(new[] { "AAA-001", "AAA-002" }, RenderedCodes(cut).OrderBy(c => c));
    }

    [Fact]
    public void ClickingGenreFilter_MatchesWholeGenreNotAsASubstring()
    {
        // This is really testing that selecting "VR" doesn't also match a movie whose genre is
        // merely a superstring like "VRKM" (a naive Contains() on MetaGenres's raw text would get
        // this wrong) — the grid now matches against the exact canonical Tag name instead.
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            AddMovieWithGenres(db, "AAA-001", "VR");
            AddMovieWithGenres(db, "AAA-002", "VRKM");
            AddMovieWithGenres(db, "AAA-003", "Solowork", "VR");
            AddMovieWithGenres(db, "AAA-004", "VR", "Solowork");
            AddMovieWithGenres(db, "AAA-005", "Anal");
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Genre")).Click();
        cut.FindAll("button").Single(b => b.TextContent == "VR").Click();

        var codes = RenderedCodes(cut);
        Assert.Equal(new[] { "AAA-001", "AAA-003", "AAA-004" }, codes.OrderBy(c => c));
    }

    [Fact]
    public void GenreFacetsAndFilter_TagNameContainingAComma_ResolvesAsOneGenreNotTwo()
    {
        // A canonical Tag name that itself contains a comma (e.g. "Nasty, hardcore") must stay one
        // genre — both as a single facet in the dropdown and as a single exact match when clicked —
        // not get split into "Nasty" and "hardcore" pieces the way re-parsing MetaGenres's own
        // comma-joined text would.
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            AddMovieWithGenres(db, "AAA-001", "Nasty, hardcore", "VR");
            AddMovieWithGenres(db, "AAA-002", "Nasty", "hardcore");
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Genre")).Click();

        var options = cut.FindAll(".sort-dropdown-subitem").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("Nasty, hardcore", options);
        Assert.Contains("Nasty", options);
        Assert.Contains("hardcore", options);

        cut.FindAll("button").Single(b => b.TextContent == "Nasty, hardcore").Click();

        var codes = RenderedCodes(cut);
        Assert.Equal(new[] { "AAA-001" }, codes);
    }

    [Fact]
    public void SelectingTwoGenres_ShowsMoviesMatchingEither()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            AddMovieWithGenres(db, "AAA-001", "VR");
            AddMovieWithGenres(db, "AAA-002", "Solowork");
            AddMovieWithGenres(db, "AAA-003", "Anal");
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Genre")).Click();
        cut.FindAll("button").Single(b => b.TextContent == "VR").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Solowork").Click();

        var codes = RenderedCodes(cut);
        Assert.Equal(new[] { "AAA-001", "AAA-002" }, codes.OrderBy(c => c));
    }

    [Fact]
    public void TypingInGenreSearch_FiltersTheVisibleOptions()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            AddMovieWithGenres(db, "AAA-001", "VR");
            AddMovieWithGenres(db, "AAA-002", "Solowork");
            AddMovieWithGenres(db, "AAA-003", "Anal");
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Genre")).Click();
        Assert.Equal(3, cut.FindAll(".sort-dropdown-subitem").Count);

        cut.Find("input[type=text]").Input("solo");

        var visible = cut.FindAll(".sort-dropdown-subitem").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Solowork" }, visible);
    }

    [Fact]
    public void ClickingSelectAllGenres_SelectsOnlyTheCurrentlySearchedOnes()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            AddMovieWithGenres(db, "AAA-001", "VR");
            AddMovieWithGenres(db, "AAA-002", "VR", "Solowork");
            AddMovieWithGenres(db, "AAA-003", "Anal");
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Genre")).Click();
        // Narrows the visible options to just "VR" — "Solowork" and "Anal" don't match "vr".
        cut.Find("input[type=text]").Input("vr");

        cut.FindAll("button").Single(b => b.TextContent == "Select All").Click();

        var codes = RenderedCodes(cut);
        Assert.Equal(new[] { "AAA-001", "AAA-002" }, codes.OrderBy(c => c));
        Assert.Contains("Genre (1)", cut.Markup);
    }

    [Fact]
    public void ClickingNoneGenres_ClearsAllSelectionsRegardlessOfActiveSearch()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            AddMovieWithGenres(db, "AAA-001", "VR");
            AddMovieWithGenres(db, "AAA-002", "Solowork");
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Genre")).Click();
        cut.FindAll("button").Single(b => b.TextContent == "VR").Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Solowork")).Click();
        Assert.Contains("Genre (2)", cut.Markup);

        // "None" clears every selection, not just whatever the active search happens to show.
        cut.Find("input[type=text]").Input("vr");
        cut.FindAll("button").Single(b => b.TextContent == "None").Click();

        Assert.Equal(2, cut.FindAll(".poster-card").Count);
        Assert.DoesNotContain("Genre (", cut.Markup);
    }

    [Fact]
    public void ClickingHasSubtitlesFeature_MatchesEitherSidecarFileOrEmbeddedTrack()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MediaHasSubtitleFile = true });               // sidecar file
            db.Movies.Add(new Movie { Code = "AAA-002", MediaSubtitleCount = 2 });                     // embedded track
            db.Movies.Add(new Movie { Code = "AAA-003", MediaHasSubtitleFile = false, MediaSubtitleCount = 0 });
            db.Movies.Add(new Movie { Code = "AAA-004" });                                             // never scanned
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Features")).Click();
        cut.FindAll("button").Single(b => b.TextContent == "Has subtitles").Click();

        var codes = RenderedCodes(cut);
        Assert.Equal(new[] { "AAA-001", "AAA-002" }, codes.OrderBy(c => c));
        Assert.Contains("Filter (1)", cut.Markup);
    }

    [Fact]
    public void ClickingHasTrailersFeature_MatchesOnlyMoviesWithATrailerFile()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MediaHasTrailerFile = true });
            db.Movies.Add(new Movie { Code = "AAA-002", MediaHasTrailerFile = false });
            db.Movies.Add(new Movie { Code = "AAA-003" });                                              // never scanned
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Features")).Click();
        cut.FindAll("button").Single(b => b.TextContent == "Has trailers").Click();

        var codes = RenderedCodes(cut);
        Assert.Equal(new[] { "AAA-001" }, codes);
        Assert.Contains("Filter (1)", cut.Markup);
    }

    [Fact]
    public void ClickingFavoritesFeature_MatchesOnlyFavoritedMovies()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", IsFavorite = true });
            db.Movies.Add(new Movie { Code = "AAA-002" });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Features")).Click();
        cut.FindAll("button").Single(b => b.TextContent == "Favorites").Click();

        Assert.Equal(new[] { "AAA-001" }, RenderedCodes(cut));
        Assert.Contains("Filter (1)", cut.Markup);
    }

    [Fact]
    public void WriteNfoButton_OnlyShowsWithAnNfoDriftFilter_AndOpensTheConfirmation()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", NfoDriftKind = NfoDriftKind.JavbuddyChanged });
            db.Movies.Add(new Movie { Code = "AAA-002", NfoDriftKind = NfoDriftKind.ExternalEdit });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Write Javbuddy metadata to .nfo"));

        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("NFO drift")).Click();
        cut.FindAll("button.sort-dropdown-subitem").Single(b => b.TextContent.Trim() == "External edit").Click();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Write Javbuddy metadata to .nfo").Click();

        // The confirmation counts only what the current filter shows.
        cut.WaitForAssertion(() => Assert.Contains("External edit (1) / Both changed (0)", cut.Markup));
        Assert.Contains("Javbuddy changed (0)", cut.Markup);
    }

    [Fact]
    public void FavoritesSort_PutsFavoritesFirstThenOrdersByCode()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001" });
            db.Movies.Add(new Movie { Code = "AAA-002", IsFavorite = true });
            db.Movies.Add(new Movie { Code = "AAA-003" });
            db.Movies.Add(new Movie { Code = "AAA-004", IsFavorite = true });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Sort:")).Click();
        cut.FindAll("button.sort-dropdown-item").Single(b => b.TextContent.Trim().StartsWith("Favorites")).Click();

        Assert.Equal(new[] { "AAA-002", "AAA-004", "AAA-001", "AAA-003" }, RenderedCodes(cut));
    }

    [Fact]
    public void ClickingPosterHeart_FavoritesTheMovieAndFillsTheHeart()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001" });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        var heart = cut.Find("button.poster-favorite");
        Assert.Equal("false", heart.GetAttribute("aria-pressed"));

        heart.Click();

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("button.poster-favorite").GetAttribute("aria-pressed")));
        using var readDb = factory.CreateDbContext();
        Assert.True(readDb.Movies.Single(m => m.Code == "AAA-001").IsFavorite);
    }

    [Fact]
    public void UnfavoritingUnderFavoritesFilter_DropsTheMovieFromTheGrid()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", IsFavorite = true });
            db.Movies.Add(new Movie { Code = "AAA-002", IsFavorite = true });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Features")).Click();
        cut.FindAll("button").Single(b => b.TextContent == "Favorites").Click();
        Assert.Equal(2, cut.FindAll(".poster-card").Count);

        cut.Find("button.poster-favorite[aria-label='Unfavorite AAA-001']").Click();

        cut.WaitForAssertion(() => Assert.Equal(new[] { "AAA-002" }, RenderedCodes(cut)));
    }

    [Fact]
    public void OptionsButton_OpensModalWithDefaultValues()
    {
        // No IHttpContextAccessor is registered in bUnit's DI container (see SetUpServices),
        // so LoadPosterOptionsFromCookie degrades to "no cookie, keep the defaults" — same as a
        // fresh browser with nothing saved yet.
        using var factory = SetUpServices();

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent == "Options").Click();

        Assert.Contains("Poster Options", cut.Markup);
        var posterSizeSelect = cut.Find("#poster-size-select");
        Assert.Equal("medium", posterSizeSelect.GetAttribute("value"));
        Assert.True(((AngleSharp.Html.Dom.IHtmlInputElement)cut.Find("#show-title-check")).IsChecked);
        Assert.True(((AngleSharp.Html.Dom.IHtmlInputElement)cut.Find("#show-status-check")).IsChecked);
        Assert.True(((AngleSharp.Html.Dom.IHtmlInputElement)cut.Find("#show-size-check")).IsChecked);
        Assert.True(((AngleSharp.Html.Dom.IHtmlInputElement)cut.Find("#show-quality-check")).IsChecked);
        Assert.True(((AngleSharp.Html.Dom.IHtmlInputElement)cut.Find("#show-vr-check")).IsChecked);
    }

    [Fact]
    public void ClosingOptionsModal_ViaCloseButton_HidesIt()
    {
        using var factory = SetUpServices();

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent == "Options").Click();
        Assert.Contains("Poster Options", cut.Markup);

        cut.FindAll("button").Single(b => b.TextContent == "Close").Click();

        Assert.DoesNotContain("Poster Options", cut.Markup);
    }

    [Fact]
    public void UncheckingShowTitle_AddsHideTitleClassToGrid()
    {
        using var factory = SetUpServices();

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent == "Options").Click();
        cut.Find("#show-title-check").Change(false);

        var grid = cut.Find(".movie-poster-grid");
        Assert.Contains("movie-poster-hide-title", grid.ClassList);
    }

    [Fact]
    public void UncheckingShowStatus_AddsHideStatusClassToGrid()
    {
        using var factory = SetUpServices();

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent == "Options").Click();
        cut.Find("#show-status-check").Change(false);

        var grid = cut.Find(".movie-poster-grid");
        Assert.Contains("movie-poster-hide-status", grid.ClassList);
    }

    [Fact]
    public void PosterMetaLine_ShowsTheMoviesActualStatus()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Missing });
            db.Movies.Add(new Movie { Code = "AAA-002", Status = MovieStatus.Got });
            db.SaveChanges();
        }

        var cut = Render<Movies>();

        var metaLines = cut.FindAll(".poster-meta").Select(e => e.TextContent.Trim()).ToList();
        Assert.Contains("Missing", metaLines);
        Assert.Contains("Got", metaLines);
    }

    [Fact]
    public void TurningOffEveryOption_CollapsesPosterInfoEntirely()
    {
        // Regression: PosterCard's ".poster-info" wrapper has its own padding regardless of
        // whether title/meta/meta-sub end up with anything to show inside it — with every toggle
        // off, that padding was left behind as dead space under the status bar unless the whole
        // block collapses (movie-poster-hide-info), not just its individual children.
        using var factory = SetUpServices();

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent == "Options").Click();
        cut.Find("#show-title-check").Change(false);
        cut.Find("#show-status-check").Change(false);
        cut.Find("#show-size-check").Change(false);
        cut.Find("#show-quality-check").Change(false);

        var grid = cut.Find(".movie-poster-grid");
        Assert.Contains("movie-poster-hide-info", grid.ClassList);
    }

    [Fact]
    public void TurningOnAnySingleOption_DoesNotCollapsePosterInfo()
    {
        using var factory = SetUpServices();

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent == "Options").Click();
        cut.Find("#show-title-check").Change(false);
        cut.Find("#show-status-check").Change(false);
        cut.Find("#show-size-check").Change(false);
        // Show Quality stays on.

        var grid = cut.Find(".movie-poster-grid");
        Assert.DoesNotContain("movie-poster-hide-info", grid.ClassList);
    }

    // A live circuit's HttpContext keeps the cookie from its first request, so poster
    // options changed earlier in the session only reach a revisit through the browser's own copy.
    [Fact]
    public void Movies_UsesThePosterOptionsTheBrowserRemembers()
    {
        JSInterop.Setup<string?>("lsPosterOptions.get").SetResult("""{"PosterSize":"large","ShowTitle":false}""");
        using var factory = SetUpServices();

        var cut = Render<Movies>();

        cut.WaitForAssertion(() =>
        {
            var classes = cut.Find(".movie-poster-grid").ClassList;
            Assert.Contains("movie-poster-size-large", classes);
            Assert.Contains("movie-poster-hide-title", classes);
        });
    }

    [Theory]
    [InlineData("small")]
    [InlineData("large")]
    public void ChangingPosterSize_UpdatesGridSizeClass(string size)
    {
        using var factory = SetUpServices();

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent == "Options").Click();
        cut.Find("#poster-size-select").Change(size);

        var grid = cut.Find(".movie-poster-grid");
        Assert.Contains($"movie-poster-size-{size}", grid.ClassList);
    }

    [Fact]
    public void TogglingShowQuality_AddsAndRemovesTheResolutionMetaLine()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MediaWidth = 1920 }); // HD bucket
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        Assert.Contains("HD", cut.Markup); // Show Quality defaults to on

        cut.FindAll("button").Single(b => b.TextContent == "Options").Click();
        cut.Find("#show-quality-check").Change(false);

        Assert.DoesNotContain("HD", cut.Markup);
    }

    [Fact]
    public void TogglingShowSize_AddsAndRemovesTheFileSizeMetaLine()
    {
        // Scoped to .poster-meta-sub specifically, not the whole page: the stats footer's own
        // "Total File Size" figure formats the same way and isn't gated by this per-card toggle,
        // so with a single 1GB movie it would otherwise also read "1 GB" and mask a real failure.
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", LocalFileSizeBytes = 1024L * 1024 * 1024 }); // "1 GB"
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        Assert.Contains(cut.FindAll(".poster-meta-sub"), e => e.TextContent == "1 GB"); // Show Size defaults to on

        cut.FindAll("button").Single(b => b.TextContent == "Options").Click();
        cut.Find("#show-size-check").Change(false);

        Assert.DoesNotContain(cut.FindAll(".poster-meta-sub"), e => e.TextContent == "1 GB");
    }

    [Fact]
    public void ReleaseYear_IsNeverRenderedOnAPosterCard()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MetaReleaseDate = new DateTime(2019, 1, 1) });
            db.SaveChanges();
        }

        var cut = Render<Movies>();

        Assert.DoesNotContain("2019", cut.Markup);
    }

    [Fact]
    public void TypingInTextFilter_DoesNotImmediatelyReloadTheGrid()
    {
        // Regression: the text filter used to call ResetAndLoadAsync synchronously on
        // every keystroke; OnTextFilterInput now debounces it behind a 250ms Task.Delay, so right
        // after the input event fires — before that delay elapses — the grid must still be
        // showing whatever it had before this keystroke.
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001" });
            db.Movies.Add(new Movie { Code = "BBB-002" });
            db.SaveChanges();
        }

        var cut = Render<Movies>();

        cut.Find("input[type=search]").Input("AAA");

        Assert.Equal(new[] { "AAA-001", "BBB-002" }, RenderedCodes(cut).OrderBy(c => c));
    }

    [Fact]
    public void TypingInTextFilter_AfterTheDebounceDelay_FiltersTheGrid()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001" });
            db.Movies.Add(new Movie { Code = "BBB-002" });
            db.SaveChanges();
        }

        var cut = Render<Movies>();

        cut.Find("input[type=search]").Input("AAA");

        cut.WaitForAssertion(() => Assert.Equal(new[] { "AAA-001" }, RenderedCodes(cut)), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RapidSuccessiveKeystrokes_OnlyTriggerOneReloadForTheFinalValue()
    {
        // The actual bug: without cancel-and-restart, every keystroke queued its own
        // reload. Each cancels the previous keystroke's still-pending debounce (see
        // textFilterDebounceCts in OnTextFilterInput), so of these three rapid keystrokes only the
        // last one's Task.Delay ever completes — the first two never reach ResetAndLoadAsync.
        var (factory, counting) = SetUpServicesWithCallCounting();
        using var disposableFactory = factory;
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001" });
            db.Movies.Add(new Movie { Code = "AAB-002" });
            db.Movies.Add(new Movie { Code = "BBB-003" });
            db.SaveChanges();
        }

        // The debounce runs on this clock, so a slow runner can't let a keystroke's 250ms window
        // elapse before the next keystroke arrives.
        var clock = new FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(clock);

        var cut = Render<Movies>();
        var baseline = counting.CreateCount;

        // Each keystroke's task completes once its handler has: the superseded ones when cancelled,
        // the last one only after its reload, so awaiting them needs no wall-clock timeout.
        var keystrokes = new[] { "A", "AA", "AAB" }
            .Select(value => cut.Find("input[type=search]").InputAsync(new ChangeEventArgs { Value = value }))
            .ToList();
        // Events only queue while the renderer's dispatcher is busy; this runs after all three, so
        // the last one's debounce is on the fake clock before it's advanced.
        await cut.InvokeAsync(() => { });

        // None of the three 250ms debounce windows has elapsed yet, so no reload has started.
        Assert.Equal(baseline, counting.CreateCount);

        clock.Advance(TimeSpan.FromMilliseconds(250));
        await Task.WhenAll(keystrokes);
        Assert.Equal(new[] { "AAB-002" }, RenderedCodes(cut));

        // ResetAndLoadAsync opens exactly three DbContexts per call (the filtered-count query,
        // the summary query, and the window fetch) — one settled reload, not one per keystroke, means
        // exactly 3 more here regardless of how many keystrokes preceded it.
        Assert.Equal(baseline + 3, counting.CreateCount);
    }

    [Fact]
    public void Studio_QueryParameter_IsIgnored()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MetaStudio = "Studio A" });
            db.Movies.Add(new Movie { Code = "BBB-002", MetaStudio = "Studio B" });
            db.SaveChanges();
        }

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("?studio=Studio%20A");

        var cut = Render<Movies>();

        Assert.Equal(new[] { "AAA-001", "BBB-002" }, RenderedCodes(cut).OrderBy(c => c));
    }

    [Fact]
    public void Genre_QueryParameter_IsIgnored()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MetaGenres = "Drama, Romance" });
            db.Movies.Add(new Movie { Code = "BBB-002", MetaGenres = "Comedy" });
            db.SaveChanges();
        }

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("?genre=Drama");

        var cut = Render<Movies>();

        Assert.Equal(new[] { "AAA-001", "BBB-002" }, RenderedCodes(cut).OrderBy(c => c));
    }

    [Fact]
    public void Studio_PendingNavigationState_FiltersGridToMatchingMovies()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MetaStudio = "Studio A" });
            db.Movies.Add(new Movie { Code = "BBB-002", MetaStudio = "Studio B" });
            db.SaveChanges();
        }

        var filterState = Services.GetRequiredService<MovieFilterNavigationState>();
        filterState.SetPendingFilter(studio: "Studio A");

        var cut = Render<Movies>();

        Assert.Equal(new[] { "AAA-001" }, RenderedCodes(cut));
        Assert.Null(filterState.PeekPendingFilter());
    }

    [Fact]
    public void Genre_PendingNavigationState_FiltersGridToMatchingMovies()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            AddMovieWithGenres(db, "AAA-001", "Drama", "Romance");
            AddMovieWithGenres(db, "BBB-002", "Comedy");
        }

        var filterState = Services.GetRequiredService<MovieFilterNavigationState>();
        filterState.SetPendingFilter(genre: "Drama");

        var cut = Render<Movies>();

        Assert.Equal(new[] { "AAA-001" }, RenderedCodes(cut));
        Assert.Null(filterState.PeekPendingFilter());
    }

    [Fact]
    public void CodePrefix_PendingNavigationState_FiltersCodesWithoutMatchingTitles()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "HMN-869", MetaTitle = "Matching code" });
            db.Movies.Add(new Movie { Code = "ABC-123", MetaTitle = "HMN appears only in this title" });
            db.Movies.Add(new Movie { Code = "IPX-456", Title = "Different movie" });
            db.SaveChanges();
        }

        var filterState = Services.GetRequiredService<MovieFilterNavigationState>();
        filterState.SetPendingFilter(codePrefix: "HMN");

        var cut = Render<Movies>();

        Assert.Equal(new[] { "HMN-869" }, RenderedCodes(cut));
        Assert.Equal("HMN", cut.Find("input[type=search]").GetAttribute("value"));
        Assert.Null(filterState.PeekPendingFilter());
    }

    [Fact]
    public void ClearingTextFilter_AfterCodePrefixNavigation_ReturnsToNormalSearch()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "HMN-869" });
            db.Movies.Add(new Movie { Code = "ABC-123", MetaTitle = "HMN appears only in this title" });
            db.Movies.Add(new Movie { Code = "IPX-456", Title = "Different movie" });
            db.SaveChanges();
        }

        var filterState = Services.GetRequiredService<MovieFilterNavigationState>();
        filterState.SetPendingFilter(codePrefix: "HMN");
        var cut = Render<Movies>();

        cut.Find("input[type=search]").Input(string.Empty);

        cut.WaitForAssertion(
            () => Assert.Equal(new[] { "ABC-123", "HMN-869", "IPX-456" }, RenderedCodes(cut).OrderBy(code => code)),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ViewStateCookie_RestoresStatusFilterOnFreshLoad()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Missing });
            db.Movies.Add(new Movie { Code = "AAA-002", Status = MovieStatus.Got });
            db.SaveChanges();
        }

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "javbuddy-movies-view=" + Uri.EscapeDataString("""{"Filter":1}""");
        httpContextAccessor.HttpContext.Returns(httpContext);
        Services.AddSingleton(httpContextAccessor);

        var cut = Render<Movies>();

        Assert.Equal(new[] { "AAA-002" }, RenderedCodes(cut));
    }

    [Fact]
    public void ViewStateCookie_WithLegacyNfoFeatureOption_MovesItIntoTheNfoDriftGroup()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", NfoDriftKind = NfoDriftKind.Unreadable });
            db.Movies.Add(new Movie { Code = "AAA-002", NfoDriftKind = NfoDriftKind.ExternalEdit });
            db.Movies.Add(new Movie { Code = "AAA-003" });
            db.SaveChanges();
        }

        // 3 = the old "NFO drift: any" Features option (now LegacyNfoConflict).
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "javbuddy-movies-view=" + Uri.EscapeDataString("""{"FeatureFilter":[3]}""");
        httpContextAccessor.HttpContext.Returns(httpContext);
        Services.AddSingleton(httpContextAccessor);

        var cut = Render<Movies>();

        Assert.Equal(new[] { "AAA-001", "AAA-002" }, RenderedCodes(cut).OrderBy(c => c));
        Assert.Contains("Filter (4)", cut.Markup);
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Write Javbuddy metadata to .nfo");
    }

    [Fact]
    public void PendingNavigationState_OverridesPersistedStudioFilterCookie()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", MetaStudio = "Studio A" });
            db.Movies.Add(new Movie { Code = "BBB-002", MetaStudio = "Studio B" });
            db.SaveChanges();
        }

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "javbuddy-movies-view=" + Uri.EscapeDataString("""{"StudioFilter":["Studio B"]}""");
        httpContextAccessor.HttpContext.Returns(httpContext);
        Services.AddSingleton(httpContextAccessor);

        var filterState = Services.GetRequiredService<MovieFilterNavigationState>();
        filterState.SetPendingFilter(studio: "Studio A");

        var cut = Render<Movies>();

        // The explicit deep link (Studio A) wins over the persisted cookie (Studio B) — a fresh
        // "view movies by this studio" navigation should never be shadowed by a stale selection
        // from a previous session.
        Assert.Equal(new[] { "AAA-001" }, RenderedCodes(cut));
    }

    [Theory]
    [InlineData("studio")]
    [InlineData("genre")]
    [InlineData("codePrefix")]
    public void PendingNavigationState_ClearsEveryOtherFilter_AndSavesTheCleanViewState(string target)
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            AddMovieWithGenres(db, "AAA-001", "Drama");
            AddMovieWithGenres(db, "AAA-002", "Comedy");
            AddMovieWithGenres(db, "BBB-003", "Comedy");
            foreach (var movie in db.Movies)
            {
                movie.MetaStudio = movie.Code!.StartsWith("AAA") ? "Studio A" : "Studio B";
                movie.Status = movie.Code == "AAA-001" ? MovieStatus.Missing : MovieStatus.Got;
            }
            db.SaveChanges();
        }

        // A remembered view that, combined with the clicked filter, would match nothing or the wrong movies.
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "javbuddy-movies-view=" + Uri.EscapeDataString(
            """{"Filter":1,"StudioFilter":["Studio B"],"GenreFilter":["Comedy"],"TextFilter":"003","CodePrefixFilter":"BBB","ResolutionFilter":[0]}""");
        httpContextAccessor.HttpContext.Returns(httpContext);
        Services.AddSingleton(httpContextAccessor);

        var filterState = Services.GetRequiredService<MovieFilterNavigationState>();
        switch (target)
        {
            case "studio": filterState.SetPendingFilter(studio: "Studio A"); break;
            case "genre": filterState.SetPendingFilter(genre: "Drama"); break;
            default: filterState.SetPendingFilter(codePrefix: "AAA"); break;
        }

        var cut = Render<Movies>();

        var expected = target == "genre" ? new[] { "AAA-001" } : new[] { "AAA-001", "AAA-002" };
        Assert.Equal(expected, RenderedCodes(cut).OrderBy(c => c));

        var saved = JSInterop.Invocations.Last(inv => inv.Identifier == "setJsonCookie"
            && inv.Arguments.Any(a => a?.ToString() == "javbuddy-movies-view"));
        using var json = System.Text.Json.JsonDocument.Parse(saved.Arguments[1]!.ToString()!);
        var root = json.RootElement;
        Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty("Filter").ValueKind);
        Assert.Equal(0, root.GetProperty("ResolutionFilter").GetArrayLength());
        Assert.Equal(target == "studio" ? 1 : 0, root.GetProperty("StudioFilter").GetArrayLength());
        Assert.Equal(target == "genre" ? 1 : 0, root.GetProperty("GenreFilter").GetArrayLength());
        Assert.Equal(target == "codePrefix" ? "AAA" : "", root.GetProperty("TextFilter").GetString());
    }

    [Fact]
    public void ChangingFilter_WritesViewStateCookie()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Missing });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Missing (")).Click();

        Assert.Contains(JSInterop.Invocations, inv => inv.Identifier == "setJsonCookie" && inv.Arguments.Any(a => a?.ToString() == "javbuddy-movies-view"));
    }

    [Fact]
    public void MultiVersionMovie_RendersVersionCountInMetaSubLinesAndStatusCount()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie
            {
                Code = "ABC-123",
                Status = MovieStatus.Got,
                FileCount = 2,
                LocalFileSizeBytes = 2_000_000_000,
                MediaWidth = 1920,
                MediaHeight = 1080,
            };
            movie.MovieFiles.Add(new MovieFile
            {
                FileName = "ABC-123.mp4",
                VersionTag = "Original",
                IsPrimary = true,
                FileSizeBytes = 1_000_000_000,
            });
            movie.MovieFiles.Add(new MovieFile
            {
                FileName = "ABC-123-RIFE.mp4",
                VersionTag = "RIFE",
                IsPrimary = false,
                FileSizeBytes = 1_000_000_000,
            });
            db.Movies.Add(movie);
            db.SaveChanges();
        }

        var cut = Render<Movies>();

        // Check poster card meta line
        var card = cut.Find(".poster-card");
        Assert.Contains("2 versions", card.TextContent);

        // Check status count
        var filesRow = cut.FindAll(".movies-stats-row").First(r => r.TextContent.Contains("Files"));
        Assert.Equal("2", filesRow.QuerySelector("span")!.TextContent.Trim());
    }

    [Fact]
    public void MultipleVersionsFilter_NarrowsTheGridToMoviesWithMoreThanOneFile()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.AddRange(
                new Movie { Code = "AAA-001", FileCount = 1 },
                new Movie { Code = "BBB-002", FileCount = 2 },
                new Movie { Code = "CCC-003", FileCount = 3 });
            db.SaveChanges();
        }

        var cut = Render<Movies>();
        cut.FindAll("button").Single(button => button.TextContent.StartsWith("Filter")).Click();
        cut.FindAll("button").Single(button => button.TextContent.StartsWith("Features")).Click();
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Multiple versions").Click();

        Assert.Equal(new[] { "BBB-002", "CCC-003" }, RenderedCodes(cut).OrderBy(code => code));
        Assert.Contains("Filter (1)", cut.Markup);
    }

    [Fact]
    public void MovieChangeNotifier_NotifyChanged_ReloadsCountsAndWindowInPlace()
    {
        using var factory = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Missing });
            db.SaveChanges();
        }

        var cut = Render<Movies>();

        Assert.Equal(new[] { "AAA-001" }, RenderedCodes(cut));
        Assert.Contains("All (1)", cut.Markup);
        Assert.Contains("Missing (1)", cut.Markup);
        Assert.Contains("Got (0)", cut.Markup);

        var spacer = cut.Find(".virtualized-grid-spacer");
        Assert.Equal("1", spacer.GetAttribute("data-total-count"));

        // Add a new Got movie directly to DB, simulating a background import/sync
        using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Code = "BBB-002", Status = MovieStatus.Got });
            db.SaveChanges();
        }

        // Fire the cross-circuit change notification
        var notifier = Services.GetRequiredService<MovieChangeNotifier>();
        notifier.NotifyChanged();

        // The grid must update in place without manual navigation or scroll
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll(".poster-card").Count);
            Assert.Equal(new[] { "AAA-001", "BBB-002" }, RenderedCodes(cut).OrderBy(c => c));
            Assert.Contains("All (2)", cut.Markup);
            Assert.Contains("Missing (1)", cut.Markup);
            Assert.Contains("Got (1)", cut.Markup);

            var updatedSpacer = cut.Find(".virtualized-grid-spacer");
            Assert.Equal("2", updatedSpacer.GetAttribute("data-total-count"));

            var moviesRow = cut.FindAll(".movies-stats-row").First(r => r.TextContent.Contains("Movies"));
            Assert.Equal("2", moviesRow.QuerySelector("span")!.TextContent.Trim());
            var gotRow = cut.FindAll(".movies-stats-row").First(r => r.TextContent.Contains("Got"));
            Assert.Equal("1", gotRow.QuerySelector("span")!.TextContent.Trim());
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task MovieChangeNotifier_NotifyChanged_WhenScrolled_PreservesScrollWindowOffsetAndUpdatesCounts()
    {
        using var factory = SetUpServices();
        SeedOrderedMovies(factory, 200);
        var cut = Render<Movies>();

        // Scroll down to start offset 30
        await cut.InvokeAsync(() => cut.Instance.SetVisibleRange(30, 60, 6));
        Assert.Equal("M030", RenderedCodes(cut)[0]);

        // Add 10 new movies to the database
        using (var db = factory.CreateDbContext())
        {
            for (var i = 1; i <= 10; i++)
            {
                db.Movies.Add(new Movie { Code = $"NEW-{i:D3}", Status = MovieStatus.Got, CreatedAt = DateTime.UtcNow.AddMinutes(i) });
            }
            db.SaveChanges();
        }

        var notifier = Services.GetRequiredService<MovieChangeNotifier>();
        notifier.NotifyChanged();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("All (210)", cut.Markup);
            var spacer = cut.Find(".virtualized-grid-spacer");
            Assert.Equal("210", spacer.GetAttribute("data-total-count"));
            // Window start must remain at 30 (offset preserved in place, not reset to 0)
            Assert.Equal("30", spacer.GetAttribute("data-window-start"));
        }, TimeSpan.FromSeconds(2));
    }

    private IR18DevReleaseBrowseService SetUpReleaseBrowsing()
    {
        var browse = Substitute.For<IR18DevReleaseBrowseService>();
        browse.BrowseAsync(default!, default, default, default)
            .ReturnsForAnyArgs(new R18DevReleaseBrowseResult(true, R18DevCatalogAvailability.Ready, Array.Empty<R18DevReleaseRow>()));
        browse.CountAsync(default!, default).ReturnsForAnyArgs(new R18DevCatalogCounts(0, 0, 0, 0));
        browse.GetFilterOptionsAsync(default).ReturnsForAnyArgs(new R18DevCatalogOptions([new R18DevCategoryOption("Creampie", 5)], 2000, 2025));
        Services.AddSingleton(browse);
        Services.AddSingleton(Substitute.For<IMovieAddService>());
        Services.AddSingleton(Substitute.For<IProwlarrClient>());
        Services.AddSingleton(Substitute.For<ITorrentGrabService>());
        return browse;
    }

    private static AngleSharp.Dom.IElement? FindButton(IRenderedComponent<Movies> cut, string text) =>
        cut.FindAll("button").FirstOrDefault(b => b.TextContent.Trim() == text);

    [Fact]
    public void ReleasesToggle_WithoutAnyFilter_IsShownWithLibraryActive()
    {
        using var factory = SetUpServices();

        var cut = Render<Movies>();

        Assert.NotNull(FindButton(cut, "All releases (r18.dev)"));
        Assert.Contains("btn-secondary", FindButton(cut, "In library")!.ClassName);
    }

    [Fact]
    public void AllReleases_WithoutAnyQuery_BrowsesTheWholeCatalog()
    {
        using var factory = SetUpServices();
        var browse = SetUpReleaseBrowsing();
        var cut = Render<Movies>();

        FindButton(cut, "All releases (r18.dev)")!.Click();

        cut.WaitForAssertion(() => browse.Received().BrowseAsync(
            Arg.Is<R18DevCatalogFilter>(f => f.CodePrefix == null && string.IsNullOrEmpty(f.SearchText) && f.Studios.Count == 0 && f.ActiveCount == 0),
            0, null, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void AllReleases_TypedSearchText_IsTheR18DevQuery()
    {
        using var factory = SetUpServices();
        var browse = SetUpReleaseBrowsing();
        var cut = Render<Movies>();
        FindButton(cut, "All releases (r18.dev)")!.Click();

        cut.Find("input[placeholder^='Filter by code']").Input("some title");

        cut.WaitForAssertion(() => browse.Received().BrowseAsync(
            Arg.Is<R18DevCatalogFilter>(f => f.CodePrefix == null && f.SearchText == "some title" && f.Studios.Count == 0),
            0, null, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void AllReleases_PrefixFromNavigation_IsNotAlsoSentAsSearchText()
    {
        using var factory = SetUpServices();
        var browse = SetUpReleaseBrowsing();
        Services.GetRequiredService<MovieFilterNavigationState>().SetPendingFilter(codePrefix: "HMN");
        var cut = Render<Movies>();

        FindButton(cut, "All releases (r18.dev)")!.Click();

        // The prefix badge also fills the search box with "HMN"; sending both would turn the exact
        // prefix match into a looser "contains" one.
        cut.WaitForAssertion(() => browse.Received().BrowseAsync(
            Arg.Is<R18DevCatalogFilter>(f => f.CodePrefix == "HMN" && f.SearchText == null), 0, null, Arg.Any<CancellationToken>()));
        browse.DidNotReceive().BrowseAsync(Arg.Is<R18DevCatalogFilter>(f => f.SearchText == "HMN"), Arg.Any<int>(), Arg.Any<IReadOnlySet<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AllReleases_ReplacesTheLocalGridWithTheR18DevBrowse_AndInLibraryRestoresIt()
    {
        using var factory = SetUpServices();
        var browse = SetUpReleaseBrowsing();
        Services.GetRequiredService<MovieFilterNavigationState>().SetPendingFilter(codePrefix: "HMN");
        var cut = Render<Movies>();

        FindButton(cut, "All releases (r18.dev)")!.Click();

        cut.WaitForAssertion(() => Assert.Contains("No releases on r18.dev match", cut.Markup));
        Assert.Contains("movies-grid-hidden", cut.Find(".movies-grid").ClassName);
        browse.Received().BrowseAsync(Arg.Is<R18DevCatalogFilter>(f => f.CodePrefix == "HMN" && f.Studios.Count == 0), 0, null, Arg.Any<CancellationToken>());

        FindButton(cut, "In library")!.Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain("No releases on r18.dev match", cut.Markup));
        Assert.DoesNotContain("movies-grid-hidden", cut.Find(".movies-grid").ClassName);
    }

    [Fact]
    public void AllReleases_WithStudioFilter_BrowsesThatStudio()
    {
        using var factory = SetUpServices();
        var browse = SetUpReleaseBrowsing();
        Services.GetRequiredService<MovieFilterNavigationState>().SetPendingFilter(studio: "Studio A");
        var cut = Render<Movies>();

        FindButton(cut, "All releases (r18.dev)")!.Click();

        cut.WaitForAssertion(() => browse.Received().BrowseAsync(
            Arg.Is<R18DevCatalogFilter>(f => f.CodePrefix == null && f.Studios.SequenceEqual(new[] { "Studio A" })), 0, null, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void AllReleases_HidesTheLocalOnlyToolbarControls()
    {
        using var factory = SetUpServices();
        SetUpReleaseBrowsing();
        Services.GetRequiredService<MovieFilterNavigationState>().SetPendingFilter(codePrefix: "HMN");
        var cut = Render<Movies>();
        Assert.Contains("All (", cut.Markup);

        FindButton(cut, "All releases (r18.dev)")!.Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain("Missing (", cut.Find(".d-flex.flex-wrap").TextContent));
        Assert.DoesNotContain("Sort:", cut.Markup);
    }

    [Fact]
    public void ResettingFilters_WhileBrowsingAllReleases_StaysInBrowseModeWithNoQuery()
    {
        using var factory = SetUpServices();
        var browse = SetUpReleaseBrowsing();
        Services.GetRequiredService<MovieFilterNavigationState>().SetPendingFilter(codePrefix: "HMN");
        var cut = Render<Movies>();
        FindButton(cut, "All releases (r18.dev)")!.Click();
        cut.WaitForAssertion(() => Assert.Contains("No releases on r18.dev match", cut.Markup));

        cut.Find("button[title='Reset all filters']").Click();

        // The view mode is the user's explicit choice, not a filter, so a reset doesn't undo it.
        cut.WaitForAssertion(() => browse.Received().BrowseAsync(
            Arg.Is<R18DevCatalogFilter>(f => f.CodePrefix == null && string.IsNullOrEmpty(f.SearchText)), 0, null, Arg.Any<CancellationToken>()));
        Assert.Contains("movies-grid-hidden", cut.Find(".movies-grid").ClassName);
    }

    [Fact]
    public void AllReleases_GenreFromTheFilterMenu_IsSentToTheBrowse_AndResetClearsIt()
    {
        using var factory = SetUpServices();
        var browse = SetUpReleaseBrowsing();
        var cut = Render<Movies>();
        FindButton(cut, "All releases (r18.dev)")!.Click();
        cut.WaitForAssertion(() => Assert.Contains("No releases on r18.dev match", cut.Markup));

        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Filter")).Click();
        // The catalog's genre options load after the page renders, so the group isn't in the menu straight away.
        // Labelled apart from the library's own Genre filter.
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim().StartsWith("Genre (r18.dev)")));
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Genre (r18.dev)")).Click();
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Creampie").Click();

        cut.WaitForAssertion(() => browse.Received().BrowseAsync(
            Arg.Is<R18DevCatalogFilter>(f => f.Genres.SequenceEqual(new[] { "Creampie" })), 0, null, Arg.Any<CancellationToken>()));
        // The genre plus "Hide previously deleted", which is on by default.
        cut.WaitForAssertion(() => Assert.Contains("Filter (2)", cut.Markup));
        browse.ClearReceivedCalls();

        cut.Find("button[title='Reset all filters']").Click();

        cut.WaitForAssertion(() => browse.Received().BrowseAsync(
            Arg.Is<R18DevCatalogFilter>(f => f.Genres.Count == 0), 0, null, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void CatalogNavigationTarget_OpensTheBrowseNarrowedToThatSeries()
    {
        using var factory = SetUpServices();
        var browse = SetUpReleaseBrowsing();
        var series = new R18DevCatalogRef(20, "Series A");
        Services.GetRequiredService<MovieFilterNavigationState>().SetPendingCatalogFilter(series: series);

        var cut = Render<Movies>();

        cut.WaitForAssertion(() => browse.Received().BrowseAsync(
            Arg.Is<R18DevCatalogFilter>(f => f.Series == series && f.Label == null), 0, null, Arg.Any<CancellationToken>()));
        Assert.Contains("btn-secondary", FindButton(cut, "All releases (r18.dev)")!.ClassName);
        Assert.Contains("Series: Series A", cut.Markup);
    }

    private void SetViewStateCookie(string json)
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "javbuddy-movies-view=" + Uri.EscapeDataString(json);
        httpContextAccessor.HttpContext.Returns(httpContext);
        Services.AddSingleton(httpContextAccessor);
    }

    private List<string> LastSavedViewState() =>
        JSInterop.Invocations.Last(inv => inv.Identifier == "setJsonCookie").Arguments.Select(a => a?.ToString() ?? "").ToList();

    [Fact]
    public void ViewStateCookie_WithAllReleases_ReopensTheCatalogWithItsFilters()
    {
        using var factory = SetUpServices();
        var browse = SetUpReleaseBrowsing();
        SetViewStateCookie("""{"BrowseReleases":true,"CatalogFilter":{"Genres":["Creampie"],"Status":1,"Series":{"Id":20,"Name":"Series A"}}}""");

        var cut = Render<Movies>();

        cut.WaitForAssertion(() => browse.Received().BrowseAsync(
            Arg.Is<R18DevCatalogFilter>(f => f.Genres.SequenceEqual(new[] { "Creampie" }) && f.Status == R18DevCatalogStatus.Untracked
                && f.Series == new R18DevCatalogRef(20, "Series A") && f.HidePreviouslyDeleted),
            0, null, Arg.Any<CancellationToken>()));
        Assert.Contains("btn-secondary", FindButton(cut, "All releases (r18.dev)")!.ClassName);
        Assert.Contains("movies-grid-hidden", cut.Find(".movies-grid").ClassName);
    }

    [Fact]
    public void AllReleasesAndItsFilters_AreSavedInTheViewStateCookie_AndInLibraryClearsTheMode()
    {
        using var factory = SetUpServices();
        SetUpReleaseBrowsing();
        var cut = Render<Movies>();

        FindButton(cut, "All releases (r18.dev)")!.Click();
        cut.WaitForAssertion(() => Assert.Contains(LastSavedViewState(), a => a.Contains("\"BrowseReleases\":true", StringComparison.Ordinal)));

        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Filter")).Click();
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Genre (r18.dev)")).Click();
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Creampie").Click();
        cut.WaitForAssertion(() => Assert.Contains(LastSavedViewState(), a => a.Contains("\"Genres\":[\"Creampie\"]", StringComparison.Ordinal)));

        FindButton(cut, "In library")!.Click();
        cut.WaitForAssertion(() => Assert.Contains(LastSavedViewState(), a => a.Contains("\"BrowseReleases\":false", StringComparison.Ordinal)));
    }

    [Fact]
    public void StudioNavigationTarget_WithAllReleasesSaved_ShowsTheLibrary()
    {
        using var factory = SetUpServices();
        SetUpReleaseBrowsing();
        SetViewStateCookie("""{"BrowseReleases":true}""");
        Services.GetRequiredService<MovieFilterNavigationState>().SetPendingFilter(studio: "Studio A");

        var cut = Render<Movies>();

        cut.WaitForAssertion(() => Assert.Contains("btn-secondary", FindButton(cut, "In library")?.ClassName ?? ""));
        Assert.DoesNotContain("movies-grid-hidden", cut.Find(".movies-grid").ClassName);
    }
}
