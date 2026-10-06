using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.R18Dev;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Javbuddy.Tests.Services.R18Dev;

public class R18DevReleaseBrowseServiceTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private readonly IR18DevDumpStore store = Substitute.For<IR18DevDumpStore>();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));

    public R18DevReleaseBrowseServiceTests()
    {
        store.GetCatalogAvailabilityAsync(default).ReturnsForAnyArgs(R18DevCatalogAvailability.Ready);
        store.GetCatalogPageAsync(default!, default, default, default, default).ReturnsForAnyArgs(Array.Empty<R18DevFilmographyEntry>());
    }

    public void Dispose() => factory.Dispose();

    private R18DevReleaseBrowseService Service => new(factory, store, time);

    private static R18DevFilmographyEntry Entry(string dvdId, DateOnly? released = null, string? title = "Title") =>
        new(dvdId, title, null, released, null);

    private void SeedSettings(bool enabled)
    {
        using var db = factory.CreateDbContext();
        db.R18DevSettings.Add(new R18DevSettings { Enabled = enabled });
        db.SaveChanges();
    }

    private void SeedMovies(params (string Code, MovieStatus Status)[] movies)
    {
        using var db = factory.CreateDbContext();
        foreach (var (code, status) in movies)
        {
            db.Movies.Add(new Movie { Code = code, Status = status });
        }
        db.SaveChanges();
    }

    private static string Key(string code) => CodeNormalization.GetCanonicalKey(code);

    private void SeedDeleted(params string[] codes)
    {
        using var db = factory.CreateDbContext();
        foreach (var code in codes)
        {
            DeletedMovieHistory.RecordAsync(db, new Movie { Code = code }).GetAwaiter().GetResult();
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task BrowseAsync_SourceDisabled_ReportsDisabledWithoutQueryingTheDump()
    {
        SeedSettings(enabled: false);

        var result = await Service.BrowseAsync(new R18DevCatalogFilter(), 0);

        Assert.False(result.Enabled);
        Assert.Empty(result.Rows);
        await store.DidNotReceiveWithAnyArgs().GetCatalogAvailabilityAsync(default);
        await store.DidNotReceiveWithAnyArgs().GetCatalogPageAsync(default!, default, default, default, default);
    }

    [Fact]
    public async Task BrowseAsync_NoSettingsRow_IsTreatedAsDisabled()
    {
        var result = await Service.BrowseAsync(new R18DevCatalogFilter(), 0);

        Assert.False(result.Enabled);
    }

    [Theory]
    [InlineData(R18DevCatalogAvailability.NoDump)]
    [InlineData(R18DevCatalogAvailability.NeedsReimport)]
    public async Task BrowseAsync_CatalogNotQueryable_ReportsWhyWithoutQueryingIt(R18DevCatalogAvailability availability)
    {
        SeedSettings(enabled: true);
        store.GetCatalogAvailabilityAsync(default).ReturnsForAnyArgs(availability);

        var result = await Service.BrowseAsync(new R18DevCatalogFilter(), 0);

        Assert.True(result.Enabled);
        Assert.Equal(availability, result.Availability);
        await store.DidNotReceiveWithAnyArgs().GetCatalogPageAsync(default!, default, default, default, default);
    }

    [Fact]
    public async Task BrowseAsync_ClassifiesAgainstTheLibraryAndCollapsesVariants_InTheStoresOrder()
    {
        SeedSettings(enabled: true);
        SeedMovies(("MIDE-001", MovieStatus.Got), ("MIDE-002", MovieStatus.Missing));
        store.GetCatalogPageAsync(default!, default, default, default, default).ReturnsForAnyArgs(new[]
        {
            Entry("MIDE-003", new DateOnly(2020, 3, 1)),
            Entry("MIDE-002", new DateOnly(2020, 2, 1)),
            Entry("MIDE-001", new DateOnly(2020, 1, 1)),
            Entry("MIDE-00001", new DateOnly(2020, 1, 1)),
        });

        var result = await Service.BrowseAsync(new R18DevCatalogFilter { CodePrefix = "MIDE" }, 0);

        Assert.Equal(R18DevCatalogAvailability.Ready, result.Availability);
        Assert.Equal(new[] { "MIDE-003", "MIDE-002", "MIDE-001" }, result.Rows.Select(r => r.Movie.DvdId));
        Assert.Equal(new[] { "missing", "wanted", "got" }, result.Rows.Select(r => r.StatusClass));
        await store.Received(1).GetCatalogPageAsync(Arg.Is<R18DevCatalogFilter>(f => f.CodePrefix == "MIDE"), null, 0, R18DevReleaseBrowseService.PageSize, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BrowseAsync_FullPage_HasMoreFromTheNextOffset()
    {
        SeedSettings(enabled: true);
        var page = Enumerable.Range(1, R18DevReleaseBrowseService.PageSize).Select(i => Entry($"ABC-{i}")).ToList();
        store.GetCatalogPageAsync(default!, default, default, default, default).ReturnsForAnyArgs(page);

        var result = await Service.BrowseAsync(new R18DevCatalogFilter(), 400);

        Assert.True(result.HasMore);
        Assert.Equal(400 + R18DevReleaseBrowseService.PageSize, result.NextOffset);
    }

    [Fact]
    public async Task BrowseAsync_ShortPage_IsTheLast()
    {
        SeedSettings(enabled: true);
        store.GetCatalogPageAsync(default!, default, default, default, default).ReturnsForAnyArgs(new[] { Entry("ABC-1") });

        var result = await Service.BrowseAsync(new R18DevCatalogFilter(), 0);

        Assert.False(result.HasMore);
        Assert.Equal(1, result.NextOffset);
    }

    [Fact]
    public async Task BrowseAsync_SkipsReleasesAlreadyShownOnAnEarlierPage()
    {
        SeedSettings(enabled: true);
        store.GetCatalogPageAsync(default!, default, default, default, default).ReturnsForAnyArgs(new[] { Entry("ABC-2"), Entry("ABC-00001") });

        var result = await Service.BrowseAsync(new R18DevCatalogFilter(), 200, new HashSet<string> { Key("ABC-1") });

        Assert.Equal(new[] { "ABC-2" }, result.Rows.Select(r => r.Movie.DvdId));
    }

    [Fact]
    public async Task BrowseAsync_Untracked_ExcludesEveryTrackedCode()
    {
        SeedSettings(enabled: true);
        SeedMovies(("MIDE-001", MovieStatus.Got), ("MIDE-002", MovieStatus.Missing));

        await Service.BrowseAsync(new R18DevCatalogFilter { Status = R18DevCatalogStatus.Untracked }, 0);

        await store.Received(1).GetCatalogPageAsync(Arg.Any<R18DevCatalogFilter>(),
            Arg.Is<R18DevCanonicalKeyFilter?>(k => k != null && k.Exclude && k.Keys.ToHashSet().SetEquals(new[] { Key("MIDE-001"), Key("MIDE-002") })),
            0, R18DevReleaseBrowseService.PageSize, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BrowseAsync_WantedAndGot_IncludeOnlyThatStatusesCodes()
    {
        SeedSettings(enabled: true);
        SeedMovies(("MIDE-001", MovieStatus.Got), ("MIDE-002", MovieStatus.Missing));

        await Service.BrowseAsync(new R18DevCatalogFilter { Status = R18DevCatalogStatus.Wanted }, 0);
        await Service.BrowseAsync(new R18DevCatalogFilter { Status = R18DevCatalogStatus.Got }, 0);

        await store.Received(1).GetCatalogPageAsync(Arg.Any<R18DevCatalogFilter>(),
            Arg.Is<R18DevCanonicalKeyFilter?>(k => k != null && !k.Exclude && k.Keys.SequenceEqual(new[] { Key("MIDE-002") })),
            Arg.Is(0), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await store.Received(1).GetCatalogPageAsync(Arg.Any<R18DevCatalogFilter>(),
            Arg.Is<R18DevCanonicalKeyFilter?>(k => k != null && !k.Exclude && k.Keys.SequenceEqual(new[] { Key("MIDE-001") })),
            Arg.Is(0), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BrowseAsync_GotWithNothingGot_IsEmptyWithoutQueryingTheDump()
    {
        SeedSettings(enabled: true);
        SeedMovies(("MIDE-002", MovieStatus.Missing));

        var result = await Service.BrowseAsync(new R18DevCatalogFilter { Status = R18DevCatalogStatus.Got }, 0);

        Assert.Empty(result.Rows);
        await store.DidNotReceiveWithAnyArgs().GetCatalogPageAsync(default!, default, default, default, default);
    }

    [Fact]
    public async Task CountAsync_SplitsTheTotalByLibraryStatus()
    {
        SeedSettings(enabled: true);
        SeedMovies(("MIDE-001", MovieStatus.Got), ("MIDE-002", MovieStatus.Missing), ("MIDE-003", MovieStatus.Missing));
        store.CountCatalogAsync(Arg.Any<R18DevCatalogFilter>(), null, Arg.Any<CancellationToken>()).Returns(50);
        store.CountCatalogAsync(Arg.Any<R18DevCatalogFilter>(), Arg.Is<R18DevCanonicalKeyFilter?>(k => k != null && k.Keys.Count == 2), Arg.Any<CancellationToken>()).Returns(2);
        store.CountCatalogAsync(Arg.Any<R18DevCatalogFilter>(), Arg.Is<R18DevCanonicalKeyFilter?>(k => k != null && k.Keys.Count == 1), Arg.Any<CancellationToken>()).Returns(1);

        var counts = await Service.CountAsync(new R18DevCatalogFilter { Status = R18DevCatalogStatus.Got });

        Assert.Equal(new R18DevCatalogCounts(50, 47, 2, 1), counts);
    }

    [Fact]
    public async Task CountAsync_SourceDisabled_IsZero()
    {
        SeedSettings(enabled: false);

        Assert.Equal(new R18DevCatalogCounts(0, 0, 0, 0), await Service.CountAsync(new R18DevCatalogFilter()));
    }

    [Fact]
    public async Task GetFilterOptionsAsync_StopsTheYearRangeAtNextYear()
    {
        SeedSettings(enabled: true);
        store.GetCategoriesAsync(default).ReturnsForAnyArgs(new[] { new R18DevCategoryOption("Creampie", 3) });
        store.GetReleaseYearBoundsAsync(default).ReturnsForAnyArgs((1968, 2030));

        var options = await Service.GetFilterOptionsAsync();

        Assert.Equal("Creampie", Assert.Single(options.Categories).Name);
        Assert.Equal(1968, options.MinYear);
        Assert.Equal(2026, options.MaxYear);
    }

    [Fact]
    public async Task GetRelatedAsync_PrefersTheSeriesAndLeavesOutTheMovieItself()
    {
        SeedSettings(enabled: true);
        SeedMovies(("MIDE-002", MovieStatus.Got));
        var series = new R18DevCatalogRef(20, "Series A");
        store.GetMovieByCodeAsync("MIDE-001", Arg.Any<CancellationToken>()).Returns(Detail(series, new R18DevCatalogRef(10, "Label")));
        store.GetCatalogPageAsync(default!, default, default, default, default).ReturnsForAnyArgs(new[] { Entry("MIDE-003"), Entry("MIDE-002") });
        store.CountCatalogAsync(default!, default, default).ReturnsForAnyArgs(9);

        var related = await Service.GetRelatedAsync("MIDE-001", 12);

        Assert.Equal(series, related.Series);
        Assert.Equal(9, related.Total);
        Assert.Equal(new[] { "missing", "got" }, related.Rows.Select(r => r.StatusClass));
        await store.Received(1).GetCatalogPageAsync(
            Arg.Is<R18DevCatalogFilter>(f => f.Series == series && f.Label == null),
            Arg.Is<R18DevCanonicalKeyFilter?>(k => k != null && k.Exclude && k.Keys.SequenceEqual(new[] { Key("MIDE-001") })),
            0, 24, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRelatedAsync_WithoutASeries_HasNoReleasesButKeepsTheLabelForItsBadge()
    {
        SeedSettings(enabled: true);
        var label = new R18DevCatalogRef(10, "Label");
        store.GetMovieByCodeAsync("MIDE-001", Arg.Any<CancellationToken>()).Returns(Detail(null, label));

        var related = await Service.GetRelatedAsync("MIDE-001", 12);

        Assert.Null(related.Series);
        Assert.Equal(label, related.Label);
        Assert.Empty(related.Rows);
        await store.DidNotReceiveWithAnyArgs().GetCatalogPageAsync(default!, default, default, default, default);
    }

    [Fact]
    public async Task GetRelatedAsync_NotInTheDump_IsNone()
    {
        SeedSettings(enabled: true);

        var related = await Service.GetRelatedAsync("MIDE-001", 12);

        Assert.Same(R18DevRelatedReleases.None, related);
    }

    [Fact]
    public async Task SuggestAsync_MarksSuggestionsAgainstTheLibrary()
    {
        SeedSettings(enabled: true);
        SeedMovies(("SSIS-001", MovieStatus.Got));
        store.SuggestAsync("ssis", 5, Arg.Any<CancellationToken>()).Returns(new[] { Entry("SSIS-002"), Entry("SSIS-001") });

        var suggestions = await Service.SuggestAsync("ssis", 5);

        Assert.Equal(new[] { false, true }, suggestions.Select(s => s.InLibrary));
    }

    [Fact]
    public async Task SuggestAsync_SourceDisabled_IsEmptyWithoutQueryingTheDump()
    {
        SeedSettings(enabled: false);

        Assert.Empty(await Service.SuggestAsync("ssis", 5));
        await store.DidNotReceiveWithAnyArgs().SuggestAsync(default!, default, default);
    }

    private static R18DevMovieDetail Detail(R18DevCatalogRef? series, R18DevCatalogRef? label) =>
        new("MIDE-001", null, null, null, null, null, null, null, null, label?.Name, series?.Name, null, [], [], null, [], null, series, label);

    [Fact]
    public async Task BrowseAsync_DeletedReleaseVariant_IsPreviouslyDeleted()
    {
        SeedSettings(enabled: true);
        SeedDeleted("MIDE-001");
        store.GetCatalogPageAsync(default!, default, default, default, default).ReturnsForAnyArgs(new[] { Entry("MIDE-00001") });

        var result = await Service.BrowseAsync(new R18DevCatalogFilter { HidePreviouslyDeleted = false }, 0);

        Assert.True(Assert.Single(result.Rows).PreviouslyDeleted);
        Assert.False(result.Rows[0].InLibrary);
    }

    [Fact]
    public async Task BrowseAsync_HidingPreviouslyDeleted_ExcludesDeletedCodesThatAreNotTrackedAgain()
    {
        SeedSettings(enabled: true);
        SeedMovies(("MIDE-002", MovieStatus.Got));
        SeedDeleted("MIDE-001", "MIDE-002");

        await Service.BrowseAsync(new R18DevCatalogFilter(), 0);

        await store.Received(1).GetCatalogPageAsync(Arg.Any<R18DevCatalogFilter>(),
            Arg.Is<R18DevCanonicalKeyFilter?>(k => k != null && k.Exclude && k.Keys.SequenceEqual(new[] { Key("MIDE-001") })),
            Arg.Is(0), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BrowseAsync_ShowingPreviouslyDeleted_DoesNotNarrowAll_AndUntrackedOnlyLeavesOutTrackedCodes()
    {
        SeedSettings(enabled: true);
        SeedMovies(("MIDE-002", MovieStatus.Got));
        SeedDeleted("MIDE-001");

        await Service.BrowseAsync(new R18DevCatalogFilter { HidePreviouslyDeleted = false }, 0);
        await Service.BrowseAsync(new R18DevCatalogFilter { HidePreviouslyDeleted = false, Status = R18DevCatalogStatus.Untracked }, 0);

        await store.Received(1).GetCatalogPageAsync(Arg.Any<R18DevCatalogFilter>(), null, Arg.Is(0), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await store.Received(1).GetCatalogPageAsync(Arg.Any<R18DevCatalogFilter>(),
            Arg.Is<R18DevCanonicalKeyFilter?>(k => k != null && k.Exclude && k.Keys.SequenceEqual(new[] { Key("MIDE-002") })),
            Arg.Is(0), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BrowseAsync_UntrackedWhileHidingPreviouslyDeleted_ExcludesTrackedAndDeletedCodes()
    {
        SeedSettings(enabled: true);
        SeedMovies(("MIDE-002", MovieStatus.Got));
        SeedDeleted("MIDE-001");

        await Service.BrowseAsync(new R18DevCatalogFilter { Status = R18DevCatalogStatus.Untracked }, 0);

        await store.Received(1).GetCatalogPageAsync(Arg.Any<R18DevCatalogFilter>(),
            Arg.Is<R18DevCanonicalKeyFilter?>(k => k != null && k.Exclude && k.Keys.ToHashSet().SetEquals(new[] { Key("MIDE-001"), Key("MIDE-002") })),
            Arg.Is(0), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CountAsync_CountsPreviouslyDeleted_AndLeavesThemOutOfAllAndUntrackedWhenHidden()
    {
        SeedSettings(enabled: true);
        SeedMovies(("MIDE-002", MovieStatus.Got));
        SeedDeleted("MIDE-001");
        store.CountCatalogAsync(Arg.Any<R18DevCatalogFilter>(), null, Arg.Any<CancellationToken>()).Returns(50);
        store.CountCatalogAsync(Arg.Any<R18DevCatalogFilter>(), Arg.Is<R18DevCanonicalKeyFilter?>(k => k != null && k.Keys.Contains(Key("MIDE-002"))), Arg.Any<CancellationToken>()).Returns(1);
        store.CountCatalogAsync(Arg.Any<R18DevCatalogFilter>(), Arg.Is<R18DevCanonicalKeyFilter?>(k => k != null && k.Keys.Contains(Key("MIDE-001"))), Arg.Any<CancellationToken>()).Returns(3);

        var hidden = await Service.CountAsync(new R18DevCatalogFilter());
        var shown = await Service.CountAsync(new R18DevCatalogFilter { HidePreviouslyDeleted = false });

        Assert.Equal(new R18DevCatalogCounts(47, 46, 0, 1, 3), hidden);
        Assert.Equal(new R18DevCatalogCounts(50, 49, 0, 1, 3), shown);
    }
}
