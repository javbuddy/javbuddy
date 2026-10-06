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

/// <summary>Comprehensive coverage for ActorMissing.razor's filter/sort/dedup pipeline
/// (ApplyFiltersAndSort and friends). These are private to the component (FilmographyRow, the
/// filter fields) so there's no pure-function seam to unit test directly — every case here
/// drives the real component end to end: seed IR18DevDumpStore with a curated
/// R18DevFilmographyEntry list (and, where relevant, local Movie rows), render, click/change the
/// actual filter controls, and assert on the rendered PosterCard titles and summary counts.</summary>
public class ActorMissingFilterTests : BunitContext
{
    [Fact]
    public async Task PreviouslyDeleted_HasOwnCountAndCanBeHidden_ExcludedFromMissingAndLibraryFilters()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("MIDE-001", "Deleted", null, null, null),
            new R18DevFilmographyEntry("MIDE-002", "Never tracked", null, null, null),
        ]);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await DeletedMovieHistory.RecordAsync(db, new Movie { Code = "MIDE-001" });
            await db.SaveChangesAsync();
        }
        var cut = RenderPage();
        cut.WaitForAssertion(() => Assert.Equal(new[] { "MIDE-002" }, DisplayedCodes(cut)));
        Assert.Contains("1 missing · 0 already in your library · 1 previously deleted, hidden", cut.Find(".missing-summary").TextContent);
        ClickFilterToggle(cut, "Hide previously deleted");
        Assert.DoesNotContain("hidden", cut.Find(".missing-summary").TextContent);
        Assert.Equal(2, DisplayedCodes(cut).Count);
        Assert.Contains("1 previously deleted", cut.Find(".missing-summary").TextContent);
        Assert.Contains("1 missing", cut.Find(".missing-summary").TextContent);
        Assert.Contains("0 already in your library", cut.Find(".missing-summary").TextContent);
        ClickFilterToggle(cut, "Hide previously deleted");
        Assert.Equal(["MIDE-002"], DisplayedCodes(cut));
        ClickFilterToggle(cut, "Hide previously deleted");
        Assert.Equal(2, DisplayedCodes(cut).Count);
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Missing (1)").Click();
        Assert.Equal(["MIDE-002"], DisplayedCodes(cut));
        cut.FindAll("button").First(b => b.TextContent.Trim() == "In Library (0)").Click();
        Assert.Empty(DisplayedCodes(cut));
    }

    private const string ActorName = "Test Actress";

    private TestDbContextFactory SetUpServices(
        IReadOnlyList<R18DevFilmographyEntry> filmography,
        IEnumerable<Movie>? localMovies = null)
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton<IActorService>(new ActorService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));

        using (var db = factory.CreateDbContext())
        {
            db.Actors.Add(new Actor { FirstName = "Actress", LastName = "Test" });
            db.R18DevSettings.Add(new R18DevSettings { Enabled = true });
            if (localMovies is not null)
            {
                db.Movies.AddRange(localMovies);
            }
            db.SaveChanges();
        }

        var dumpStore = Substitute.For<IR18DevDumpStore>();
        dumpStore.GetFilmographyForActorAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new R18DevFilmographyResult(true, filmography));
        Services.AddSingleton(dumpStore);
        Services.AddSingleton(Substitute.For<IProwlarrClient>());
        Services.AddSingleton(Substitute.For<ITorrentGrabService>());
        Services.AddSingleton(Substitute.For<IMovieAddService>());
        Services.AddSingleton<IActorFilmographyService, ActorFilmographyService>();

        return factory;
    }

    private IRenderedComponent<ActorMissing> RenderPage() =>
        Render<ActorMissing>(p => p.Add(x => x.RouteName, ActorName));

    private static List<string> DisplayedCodes(IRenderedComponent<ActorMissing> cut) =>
        cut.FindAll(".poster-title").Select(e => e.TextContent.Trim()).ToList();

    /// <summary>Opens whichever dropdown isn't already open (Filter and Sort share the same
    /// showFilterDropdown/showSortDropdown mutual-exclusion, so at most one .sort-dropdown-menu
    /// is ever rendered at once).</summary>
    private static void OpenDropdown(IRenderedComponent<ActorMissing> cut, string buttonLabelPrefix)
    {
        if (cut.FindAll(".sort-dropdown-menu").Count > 0) return;
        cut.FindAll("button").First(b => b.TextContent.TrimStart().StartsWith(buttonLabelPrefix)).Click();
    }

    private static void ClickFilterToggle(IRenderedComponent<ActorMissing> cut, string label)
    {
        OpenDropdown(cut, "Filter");
        cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains(label)).Click();
    }

    private static void ClickSortField(IRenderedComponent<ActorMissing> cut, string fieldLabel)
    {
        OpenDropdown(cut, "Sort:");
        cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains(fieldLabel)).Click();
    }

    /// <summary>Studio/Year are collapsible multi-select groups (see MultiSelectFilterGroup) —
    /// expands the group first if its options aren't already showing, then toggles the given
    /// option. Calling this again with the same value toggles it back off.</summary>
    private static void ToggleMakerFilter(IRenderedComponent<ActorMissing> cut, string maker)
    {
        OpenDropdown(cut, "Filter");
        if (!cut.FindAll(".sort-dropdown-item").Any(b => b.TextContent.StartsWith(maker)))
        {
            cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.StartsWith("Studio")).Click();
        }
        cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.StartsWith(maker)).Click();
    }

    private static void ToggleYearFilter(IRenderedComponent<ActorMissing> cut, string year)
    {
        OpenDropdown(cut, "Filter");
        if (!cut.FindAll(".sort-dropdown-item").Any(b => b.TextContent.StartsWith(year)))
        {
            cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.StartsWith("Year")).Click();
        }
        cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.StartsWith(year)).Click();
    }

    private static void SetTextFilter(IRenderedComponent<ActorMissing> cut, string text)
    {
        cut.Find("input[type=search]").Input(text);
    }

    // ---- Empty / no-match states ----

    [Fact]
    public void NoFilmography_ShowsNoFilmographyMessage()
    {
        using var factory = SetUpServices([]);

        var cut = RenderPage();

        Assert.Contains("No filmography found on r18.dev", cut.Markup);
    }

    [Fact]
    public void FiltersExcludeEverything_ShowsNoMatchMessage()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "Title", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();
        ClickFilterToggle(cut, "Only with posters"); // none of the entries have a poster

        Assert.Contains("No movies match the selected filters", cut.Markup);
    }

    // ---- Status: missing vs already-in-library ----

    [Fact]
    public void LocalMatch_ByExactCode_ShowsAsOwnedStatus_NotMissing()
    {
        using var factory = SetUpServices(
            [new R18DevFilmographyEntry("ABCD-100", "Title", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1)],
            localMovies: [new Movie { Code = "ABCD-100", Status = MovieStatus.Got }]);

        var cut = RenderPage();

        Assert.Contains("Got", cut.Find(".poster-meta").TextContent);
        Assert.Contains("0 missing", cut.Find(".missing-summary").TextContent);
        Assert.Contains("1 already in your library", cut.Find(".missing-summary").TextContent);
    }

    [Fact]
    public void NoLocalMatch_ShowsAsMissing_WithReleaseDateAsLabel()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "Title", null, new DateOnly(2026, 3, 15), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();

        Assert.Equal("2026-03-15", cut.Find(".poster-meta").TextContent.Trim());
    }

    /// <summary>Guards against a real regression: a locally-tracked movie whose own status is
    /// MovieStatus.Missing must still count as "In Library" here — this page's "missing" sentinel
    /// means "not tracked in the library at all", a different concept from a tracked movie's own
    /// Missing/Got status. Both used to derive from the same ToString().ToLowerInvariant() call,
    /// which collided the moment MovieStatus.Wanted was renamed to Missing (see
    /// MovieStatusExtensions.ToPosterStatusSuffix).</summary>
    [Fact]
    public void StatusFilter_InLibraryOnly_IncludesLocallyTrackedMissingStatusMovie()
    {
        using var factory = SetUpServices(
            [
                new R18DevFilmographyEntry("ABCD-100", "Tracked but missing", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
                new R18DevFilmographyEntry("ABCD-200", "Not tracked", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            ],
            localMovies: [new Movie { Code = "ABCD-100", Status = MovieStatus.Missing }]);

        var cut = RenderPage();
        cut.FindAll("button").First(b => b.TextContent.Contains("In Library (")).Click();

        Assert.Equal(["ABCD-100"], DisplayedCodes(cut));
    }

    [Fact]
    public void StatusFilter_MissingOnly_HidesOwnedEntries()
    {
        using var factory = SetUpServices(
            [
                new R18DevFilmographyEntry("ABCD-100", "Owned", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
                new R18DevFilmographyEntry("ABCD-200", "Missing", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            ],
            localMovies: [new Movie { Code = "ABCD-100", Status = MovieStatus.Got }]);

        var cut = RenderPage();
        cut.FindAll("button").First(b => b.TextContent.Contains("Missing (")).Click();

        Assert.Equal(["ABCD-200"], DisplayedCodes(cut));
    }

    [Fact]
    public void StatusFilter_InLibraryOnly_HidesMissingEntries()
    {
        using var factory = SetUpServices(
            [
                new R18DevFilmographyEntry("ABCD-100", "Owned", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
                new R18DevFilmographyEntry("ABCD-200", "Missing", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            ],
            localMovies: [new Movie { Code = "ABCD-100", Status = MovieStatus.Got }]);

        var cut = RenderPage();
        cut.FindAll("button").First(b => b.TextContent.Contains("In Library (")).Click();

        Assert.Equal(["ABCD-100"], DisplayedCodes(cut));
    }

    [Fact]
    public void StatusFilter_All_ShowsEverythingAgainAfterNarrowing()
    {
        using var factory = SetUpServices(
            [
                new R18DevFilmographyEntry("ABCD-100", "Owned", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
                new R18DevFilmographyEntry("ABCD-200", "Missing", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            ],
            localMovies: [new Movie { Code = "ABCD-100", Status = MovieStatus.Got }]);

        var cut = RenderPage();
        cut.FindAll("button").First(b => b.TextContent.Contains("Missing (")).Click();
        cut.FindAll("button").First(b => b.TextContent.Contains("All (")).Click();

        Assert.Equal(2, DisplayedCodes(cut).Count);
    }

    // ---- Deduplication ----

    [Fact]
    public void HideDuplicates_DefaultOn_CollapsesZeroPaddingVariantIntoOneCard()
    {
        // Neither code is distributor-flagged (see CodeNormalizationTests) so this isolates
        // hideDuplicates specifically, without hideDistributorReleases (also on by default)
        // incidentally removing one side too.
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ARMD-710", "Plain", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ARMD-000710", "Zero-padded variant", null, new DateOnly(2026, 1, 2), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();

        // Both share a canonical key; the non-zero-padded, shorter code scores higher (see
        // CodeNormalizationTests), so it wins.
        Assert.Equal(["ARMD-710"], DisplayedCodes(cut));
        Assert.Contains("1 duplicate releases filtered", cut.Find(".missing-summary").TextContent);
    }

    [Fact]
    public void HideDuplicates_TurnedOff_ShowsBothVariantsSeparately()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ARMD-710", "Plain", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ARMD-000710", "Zero-padded variant", null, new DateOnly(2026, 1, 2), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();
        ClickFilterToggle(cut, "Hide duplicates");

        Assert.Equal(2, DisplayedCodes(cut).Count);
        Assert.Contains("ARMD-710", DisplayedCodes(cut));
        Assert.Contains("ARMD-000710", DisplayedCodes(cut));
    }

    [Fact]
    public void HideDuplicates_GroupOwnsALocalMatch_SurvivingCardShowsOwnedStatus()
    {
        // The plain code is both the highest-scored variant AND the one with a local match —
        // the common real-world case (zero-padded variants are almost never what's on disk).
        using var factory = SetUpServices(
            [
                new R18DevFilmographyEntry("ARMD-710", "Plain", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
                new R18DevFilmographyEntry("ARMD-000710", "Zero-padded variant", null, new DateOnly(2026, 1, 2), null, "Studio A", ActressCount: 1),
            ],
            localMovies: [new Movie { Code = "ARMD-710", Status = MovieStatus.Missing }]);

        var cut = RenderPage();

        var onlyRow = Assert.Single(DisplayedCodes(cut));
        Assert.Equal("ARMD-710", onlyRow);
        Assert.Equal("Missing", cut.Find(".poster-meta").TextContent.Trim());
    }

    [Fact]
    public void UniqueCodes_HideDuplicatesOn_NothingCollapsed()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "A", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-200", "B", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();

        Assert.Equal(2, DisplayedCodes(cut).Count);
        Assert.DoesNotContain("duplicate releases filtered", cut.Find(".missing-summary").TextContent);
    }

    // ---- Distributor releases ----

    [Fact]
    public void HideDistributorReleases_DefaultOn_HidesDistributorCodedEntry()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "Normal", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("9ABCD-200", "Distributor", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();

        Assert.Equal(["ABCD-100"], DisplayedCodes(cut));
    }

    [Fact]
    public void HideDistributorReleases_TurnedOff_ShowsDistributorCodedEntryToo()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "Normal", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("9ABCD-200", "Distributor", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();
        ClickFilterToggle(cut, "Hide distributor releases");

        Assert.Equal(2, DisplayedCodes(cut).Count);
    }

    // ---- Solo only ----

    [Fact]
    public void SoloOnly_Off_ShowsAllActressCounts()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "Solo", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-200", "Group", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 3),
        ]);

        var cut = RenderPage();

        Assert.Equal(2, DisplayedCodes(cut).Count);
    }

    [Fact]
    public void SoloOnly_On_HidesMultiActressEntries()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "Solo", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-200", "Group", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 3),
        ]);

        var cut = RenderPage();
        ClickFilterToggle(cut, "Solo works only");

        Assert.Equal(["ABCD-100"], DisplayedCodes(cut));
    }

    // ---- Compilations ----

    [Fact]
    public void HideCompilations_On_HidesCompilationEntry()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "Regular", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1, IsCompilation: false),
            new R18DevFilmographyEntry("ABCD-200", "Best Hits", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1, IsCompilation: true),
        ]);

        var cut = RenderPage();

        Assert.Equal(["ABCD-100"], DisplayedCodes(cut));
    }

    [Fact]
    public void HideCompilations_Off_ShowsCompilationEntry()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "Regular", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1, IsCompilation: false),
            new R18DevFilmographyEntry("ABCD-200", "Best Hits", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1, IsCompilation: true),
        ]);

        var cut = RenderPage();
        ClickFilterToggle(cut, "Hide compilations");

        Assert.Equal(2, DisplayedCodes(cut).Count);
    }

    // ---- VR ----

    [Fact]
    public void HideVr_Off_ShowsVrEntry()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "Regular", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1, IsVr: false),
            new R18DevFilmographyEntry("ABCD-200", "VR Title", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1, IsVr: true),
        ]);

        var cut = RenderPage();

        Assert.Equal(2, DisplayedCodes(cut).Count);
    }

    [Fact]
    public void HideVr_On_HidesVrEntry()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "Regular", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1, IsVr: false),
            new R18DevFilmographyEntry("ABCD-200", "VR Title", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1, IsVr: true),
        ]);

        var cut = RenderPage();
        ClickFilterToggle(cut, "Hide VR releases");

        Assert.Equal(["ABCD-100"], DisplayedCodes(cut));
    }

    // ---- Only with posters ----

    [Fact]
    public void OnlyWithPosters_Off_ShowsPosterlessEntry()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "Has poster", null, new DateOnly(2026, 1, 1), "http://x/1.jpg", "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-200", "No poster", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();

        Assert.Equal(2, DisplayedCodes(cut).Count);
    }

    [Fact]
    public void OnlyWithPosters_On_HidesPosterlessEntry()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "Has poster", null, new DateOnly(2026, 1, 1), "http://x/1.jpg", "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-200", "No poster", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();
        ClickFilterToggle(cut, "Only with posters");

        Assert.Equal(["ABCD-100"], DisplayedCodes(cut));
    }

    // ---- Maker / year dropdowns ----

    [Fact]
    public void MakerFilter_NarrowsToTheSelectedStudio()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "A", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-200", "B", null, new DateOnly(2026, 1, 1), null, "Studio B", ActressCount: 1),
        ]);

        var cut = RenderPage();
        ToggleMakerFilter(cut, "Studio A");

        Assert.Equal(["ABCD-100"], DisplayedCodes(cut));
    }

    [Fact]
    public void MakerFilter_ToggledOff_ShowsEverythingAgain()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "A", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-200", "B", null, new DateOnly(2026, 1, 1), null, "Studio B", ActressCount: 1),
        ]);

        var cut = RenderPage();
        ToggleMakerFilter(cut, "Studio A");
        ToggleMakerFilter(cut, "Studio A");

        Assert.Equal(2, DisplayedCodes(cut).Count);
    }

    [Fact]
    public void YearFilter_NarrowsToTheSelectedReleaseYear()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "A", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-200", "B", null, new DateOnly(2024, 6, 1), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();
        ToggleYearFilter(cut, "2024");

        Assert.Equal(["ABCD-200"], DisplayedCodes(cut));
    }

    [Fact]
    public void SelectingTwoStudios_ShowsMoviesFromEither()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "A", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-200", "B", null, new DateOnly(2026, 1, 1), null, "Studio B", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-300", "C", null, new DateOnly(2026, 1, 1), null, "Studio C", ActressCount: 1),
        ]);

        var cut = RenderPage();
        ToggleMakerFilter(cut, "Studio A");
        ToggleMakerFilter(cut, "Studio B");

        Assert.Equal(["ABCD-100", "ABCD-200"], DisplayedCodes(cut).OrderBy(c => c));
    }

    [Fact]
    public void MakerAndYearFilters_CombineWithAndSemantics()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "A", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-200", "B", null, new DateOnly(2024, 6, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-300", "C", null, new DateOnly(2026, 1, 1), null, "Studio B", ActressCount: 1),
        ]);

        var cut = RenderPage();
        ToggleMakerFilter(cut, "Studio A");
        ToggleYearFilter(cut, "2026");

        Assert.Equal(["ABCD-100"], DisplayedCodes(cut));
    }

    // ---- Free-text filter ----

    [Fact]
    public void TextFilter_MatchesDvdIdSubstring_CaseInsensitively()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("SIVR-505", "Some Title", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-200", "Other", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();
        SetTextFilter(cut, "sivr");

        Assert.Equal(["SIVR-505"], DisplayedCodes(cut));
    }

    [Fact]
    public void TextFilter_MatchesDisplayTitleSubstring()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "A Very Special Movie", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-200", "Something Else", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();
        SetTextFilter(cut, "Special");

        Assert.Equal(["ABCD-100"], DisplayedCodes(cut));
    }

    [Fact]
    public void TextFilter_NoMatches_ShowsEmptyState()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "A", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();
        SetTextFilter(cut, "nothing-matches-this");

        Assert.Contains("No movies match the selected filters", cut.Markup);
    }

    // ---- Sorting ----

    [Fact]
    public void DefaultSort_ReleaseDateDescending_NewestFirst()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "Older", null, new DateOnly(2025, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-200", "Newer", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();

        Assert.Equal(["ABCD-200", "ABCD-100"], DisplayedCodes(cut));
    }

    [Fact]
    public void ReleaseDateSort_UndatedEntriesSortLast_InBothDirections()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "Undated", null, null, null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-200", "Newer", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-300", "Older", null, new DateOnly(2025, 1, 1), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();

        // Descending (default): newest dated first, undated always last.
        Assert.Equal(["ABCD-200", "ABCD-300", "ABCD-100"], DisplayedCodes(cut));

        // Toggle to ascending by clicking the already-selected "Release Date" field again.
        ClickSortField(cut, "Release Date");

        Assert.Equal(["ABCD-300", "ABCD-200", "ABCD-100"], DisplayedCodes(cut));
    }

    [Fact]
    public void SortByCode_Ascending_IsAlphabetical()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("CCCC-100", "C", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("AAAA-100", "A", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("BBBB-100", "B", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();
        ClickSortField(cut, "Release Code");

        // Code sort defaults to ascending (only ReleaseDate defaults to descending).
        Assert.Equal(["AAAA-100", "BBBB-100", "CCCC-100"], DisplayedCodes(cut));
    }

    [Fact]
    public void SortByCode_ClickedTwice_TogglesToDescending()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("CCCC-100", "C", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("AAAA-100", "A", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();
        ClickSortField(cut, "Release Code");
        ClickSortField(cut, "Release Code");

        Assert.Equal(["CCCC-100", "AAAA-100"], DisplayedCodes(cut));
    }

    [Fact]
    public void SortByTitle_Ascending_IsAlphabeticalByDisplayTitle()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "Zebra", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("ABCD-200", "Apple", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();
        ClickSortField(cut, "Title");

        Assert.Equal(["ABCD-200", "ABCD-100"], DisplayedCodes(cut));
    }

    // ---- ActiveFilterCount badge ----

    [Fact]
    public void FilterButtonBadge_ReflectsCountOfActiveFiltersAndSelects()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("ABCD-100", "A", null, new DateOnly(2026, 1, 1), "http://x/1.jpg", "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();
        // Duplicate, distributor, compilation and previously deleted filters are on by default = 4.
        Assert.Contains("Filter (4)", cut.Markup);

        ClickFilterToggle(cut, "Solo works only");
        Assert.Contains("Filter (5)", cut.Markup);

        ToggleMakerFilter(cut, "Studio A");
        Assert.Contains("Filter (6)", cut.Markup);
    }

    // ---- Combined filters ----

    [Fact]
    public void CombinedFilters_StatusAndSoloAndMaker_AllApplyTogether()
    {
        using var factory = SetUpServices(
            [
                new R18DevFilmographyEntry("ABCD-100", "Owned solo Studio A", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
                new R18DevFilmographyEntry("ABCD-200", "Missing solo Studio A", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
                new R18DevFilmographyEntry("ABCD-300", "Missing group Studio A", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 2),
                new R18DevFilmographyEntry("ABCD-400", "Missing solo Studio B", null, new DateOnly(2026, 1, 1), null, "Studio B", ActressCount: 1),
            ],
            localMovies: [new Movie { Code = "ABCD-100", Status = MovieStatus.Got }]);

        var cut = RenderPage();
        cut.FindAll("button").First(b => b.TextContent.Contains("Missing (")).Click();
        ClickFilterToggle(cut, "Solo works only");
        ToggleMakerFilter(cut, "Studio A");

        Assert.Equal(["ABCD-200"], DisplayedCodes(cut));
    }

    // ---- Summary counts vs. the actually-displayed grid ----

    /// <summary>Regression test: missingCount/inLibraryCount used to be computed
    /// right after dedup, before the other default-on filters (hide distributor releases, hide
    /// compilations) ran — so the "N missing / M in library" summary could count entries the grid
    /// itself had already filtered out, and the two numbers wouldn't add up to what was on
    /// screen.</summary>
    [Fact]
    public void SummaryCounts_ExcludeEntriesHiddenByOtherDefaultFilters_AndSumToWhatsDisplayed()
    {
        using var factory = SetUpServices(
            [
                new R18DevFilmographyEntry("9ABCD-100", "Distributor release", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
                new R18DevFilmographyEntry("ABCD-200", "Best Hits", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1, IsCompilation: true),
                new R18DevFilmographyEntry("ABCD-300", "Regular missing", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
                new R18DevFilmographyEntry("ABCD-400", "Regular owned", null, new DateOnly(2026, 1, 1), null, "Studio A", ActressCount: 1),
            ],
            localMovies: [new Movie { Code = "ABCD-400", Status = MovieStatus.Got }]);

        var cut = RenderPage();

        Assert.Equal(["ABCD-300", "ABCD-400"], DisplayedCodes(cut).OrderBy(c => c));
        var summary = cut.Find(".missing-summary").TextContent;
        Assert.Contains("2 movies", summary);
        Assert.Contains("1 missing", summary);
        Assert.Contains("1 already in your library", summary);
    }

    // ---- Real dvd_id data, end to end through the page ----
    //
    // CodeNormalizationRealDataTests covers the underlying scoring/canonicalization logic
    // directly against dvd_id values pulled from the real r18.dev dump; these two confirm the
    // same real groups also collapse correctly once they're flowing through the actual page.

    [Fact]
    public void RealDedupGroup_Job19Variants_DefaultFiltersShowOnlyTheCleanestCode()
    {
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("JOB-019", "Job Real Title", null, new DateOnly(2026, 1, 1), "http://x/1.jpg", "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("JOB-19", "Job Real Title", null, new DateOnly(2026, 1, 1), "http://x/2.jpg", "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("JOB-019DOD", "Job Real Title", null, new DateOnly(2026, 1, 1), "http://x/3.jpg", "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("JOB019", "Job Real Title", null, new DateOnly(2026, 1, 1), "http://x/4.jpg", "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();

        Assert.Equal(["JOB-19"], DisplayedCodes(cut));
    }

    [Fact]
    public void RealMultiDiscSet_DefaultFiltersCollapseAllFourDiscsToOneCard()
    {
        // GRCH-315-1..4 are four real, genuinely-different discs of the same boxed set, not
        // duplicate listings of one movie — but they share a canonical key, so the page's
        // default hideDuplicates only ever shows one of them. Documents the real limitation
        // rather than asserting which specific disc wins a four-way score tie.
        using var factory = SetUpServices([
            new R18DevFilmographyEntry("GRCH-315-1", "Boxed Set Disc 1", null, new DateOnly(2026, 1, 1), "http://x/1.jpg", "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("GRCH-315-2", "Boxed Set Disc 2", null, new DateOnly(2026, 1, 1), "http://x/2.jpg", "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("GRCH-315-3", "Boxed Set Disc 3", null, new DateOnly(2026, 1, 1), "http://x/3.jpg", "Studio A", ActressCount: 1),
            new R18DevFilmographyEntry("GRCH-315-4", "Boxed Set Disc 4", null, new DateOnly(2026, 1, 1), "http://x/4.jpg", "Studio A", ActressCount: 1),
        ]);

        var cut = RenderPage();

        Assert.Single(DisplayedCodes(cut));
    }
}
