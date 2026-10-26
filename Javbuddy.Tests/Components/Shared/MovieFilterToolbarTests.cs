using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Javbuddy.Tests.Components.Shared;

public class MovieFilterToolbarTests : BunitContext
{
    public MovieFilterToolbarTests()
    {
        Services.AddSingleton(TimeProvider.System);
    }

    [Fact]
    public void RendersStatusCounts_AndInvokesCallback()
    {
        MovieStatus? selectedStatus = null;
        var callbackFired = false;

        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.TotalCount, 12)
            .Add(x => x.MissingCount, 5)
            .Add(x => x.GotCount, 7)
            .Add(x => x.StatusFilter, null)
            .Add(x => x.OnStatusFilterChanged, EventCallback.Factory.Create<MovieStatus?>(this, s =>
            {
                selectedStatus = s;
                callbackFired = true;
            })));

        Assert.Contains("All (12)", cut.Markup);
        Assert.Contains("Missing (5)", cut.Markup);
        Assert.Contains("Got (7)", cut.Markup);

        var missingBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Missing (5)"));
        missingBtn.Click();

        Assert.True(callbackFired);
        Assert.Equal(MovieStatus.Missing, selectedStatus);
    }

    [Fact]
    public void LibraryFilter_IsAGroupInTheFilterDropdown_NotAToolbarSelect()
    {
        string? selectedLibrary = "unset";

        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.LibraryNames, new[] { "Library A", "Library B" })
            .Add(x => x.LibraryFilter, null)
            .Add(x => x.OnLibraryFilterChanged, EventCallback.Factory.Create<string?>(this, l => selectedLibrary = l)));

        Assert.Empty(cut.FindAll("select"));

        OpenFilterMenu(cut);
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Library")).Click();
        var options = cut.FindAll("button.sort-dropdown-subitem").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Library A", "Library B" }, options);

        cut.FindAll("button.sort-dropdown-subitem").First(b => b.TextContent.Trim() == "Library A").Click();
        Assert.Equal("Library A", selectedLibrary);
    }

    [Fact]
    public void LibraryFilter_SelectingAnotherReplacesIt_AndSelectingTheActiveOneClearsIt()
    {
        string? selectedLibrary = "unset";

        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.LibraryNames, new[] { "Library A", "Library B" })
            .Add(x => x.LibraryFilter, "Library A")
            .Add(x => x.OnLibraryFilterChanged, EventCallback.Factory.Create<string?>(this, l => selectedLibrary = l)));

        OpenFilterMenu(cut);
        Assert.Contains("Filter (1)", cut.Markup);
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Library")).Click();
        Assert.Equal(["Library A"], cut.FindAll("button.sort-dropdown-subitem.active").Select(b => b.TextContent.Replace("✓", "").Trim()));

        cut.FindAll("button.sort-dropdown-subitem").First(b => b.TextContent.Trim() == "Library B").Click();
        Assert.Equal("Library B", selectedLibrary);

        cut.FindAll("button.sort-dropdown-subitem").First(b => b.TextContent.Contains("Library A")).Click();
        Assert.Null(selectedLibrary);
    }

    [Fact]
    public void LibraryFilter_GroupIsHiddenWhenNoLibrariesAreOffered()
    {
        var cut = Render<MovieFilterToolbar>();

        OpenFilterMenu(cut);

        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.StartsWith("Library"));
    }

    [Fact]
    public void TextInput_DebouncesAndInvokesCallback()
    {
        var textValues = new List<string>();

        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.TextFilter, "")
            .Add(x => x.OnTextFilterChanged, EventCallback.Factory.Create<string>(this, s => textValues.Add(s))));

        var input = cut.Find("input[type=search]");
        input.Input("IPX");

        Assert.Empty(textValues);

        cut.WaitForAssertion(() =>
        {
            Assert.Single(textValues);
            Assert.Equal("IPX", textValues[0]);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void FilterDropdown_RendersGroupsAndTogglesOption()
    {
        MovieResolutionFilterOption? toggledResolution = null;

        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.AvailableCodecs, new[] { "AVC", "HEVC" })
            .Add(x => x.AvailableStudios, new[] { "IdeaPocket" })
            .Add(x => x.AvailableGenres, new[] { "Drama" })
            .Add(x => x.OnToggleResolution, EventCallback.Factory.Create<MovieResolutionFilterOption>(this, r => toggledResolution = r)));

        // Open filter dropdown
        var filterBtn = cut.FindAll("button").First(b => b.TextContent.StartsWith("Filter"));
        filterBtn.Click();

        // Subgroup "Resolution" toggle
        var resBtn = cut.FindAll("button").First(b => b.TextContent.StartsWith("Resolution"));
        resBtn.Click();

        // Option "SD"
        var sdBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "SD");
        sdBtn.Click();

        Assert.Equal(MovieResolutionFilterOption.Sd, toggledResolution);
    }

    [Fact]
    public void MatchAllGenres_CheckboxShowsOnlyWithTwoGenres_AndReportsTheChange()
    {
        bool? changed = null;
        Action<ComponentParameterCollectionBuilder<MovieFilterToolbar>> build(params string[] selected) => p => p
            .Add(x => x.AvailableGenres, new[] { "Drama", "Solo" })
            .Add(x => x.GenreFilter, selected.ToHashSet())
            .Add(x => x.OnMatchAllGenresChanged, EventCallback.Factory.Create<bool>(this, v => changed = v));

        var one = Render<MovieFilterToolbar>(build("Drama"));
        OpenFilterMenu(one);
        OpenGenreGroup(one);
        Assert.Empty(one.FindAll("label.match-all-check"));

        var two = Render<MovieFilterToolbar>(build("Drama", "Solo"));
        OpenFilterMenu(two);
        OpenGenreGroup(two);
        two.Find(".multiselect-actions-row label.match-all-check input").Change(true);
        Assert.True(changed);
    }

    [Fact]
    public void FilterDropdown_HasAnNfoDriftGroupUnderFeatures_WithEveryDirection_AndTogglesOne()
    {
        NfoDriftKind? toggled = null;

        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.NfoDriftFilter, new HashSet<NfoDriftKind> { NfoDriftKind.Unreadable })
            .Add(x => x.OnToggleNfoDrift, EventCallback.Factory.Create<NfoDriftKind>(this, k => toggled = k)));

        cut.FindAll("button").First(b => b.TextContent.StartsWith("Filter")).Click();
        Assert.Contains("Filter (1)", cut.Markup);
        // NFO drift sits inside the Features section, which counts it.
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.StartsWith("NFO drift"));
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Features (1)")).Click();
        cut.FindAll("button").First(b => b.TextContent.StartsWith("NFO drift")).Click();

        // The Features options show first, above the NFO drift group.
        var options = cut.FindAll("button.sort-dropdown-subitem").TakeLast(4).ToList();
        Assert.Equal(["Javbuddy changed", "External edit", "Both changed", "Unreadable"], options.Select(b => b.TextContent.Replace("✓", "").Trim()));
        Assert.Equal(["Unreadable"], options.Where(b => b.ClassList.Contains("active")).Select(b => b.TextContent.Replace("✓", "").Trim()));

        cut.FindAll("button.sort-dropdown-subitem").First(b => b.TextContent.Trim() == "External edit").Click();

        Assert.Equal(NfoDriftKind.ExternalEdit, toggled);

        // None of it is offered as a Features option any more, and the Features options show without a header of their own.
        Assert.Single(cut.FindAll("button"), b => b.TextContent.StartsWith("Features"));
        Assert.Contains(cut.FindAll("button.sort-dropdown-subitem"), b => b.TextContent.Trim() == "Has subtitles");
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("NFO drift:"));
    }

    [Fact]
    public void FilterDropdown_ClearButton_InvokesOnResetFilters()
    {
        var resetCalled = false;

        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.HasActiveFilters, true)
            .Add(x => x.OnResetFilters, EventCallback.Factory.Create(this, () => resetCalled = true)));

        var clearBtn = cut.Find(".dropdown-clear-btn");
        clearBtn.Click();

        Assert.True(resetCalled);
    }

    [Fact]
    public void SortDropdown_SelectsDifferentField_UsesDefaultDirection()
    {
        MovieSortChange? lastSort = null;

        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.SortField, "added")
            .Add(x => x.SortDescending, true)
            .Add(x => x.OnSortChanged, EventCallback.Factory.Create<MovieSortChange>(this, s => lastSort = s)));

        // Open sort dropdown
        var sortBtn = cut.FindAll("button").First(b => b.TextContent.StartsWith("Sort:"));
        sortBtn.Click();

        // Pick "Title"
        var titleBtn = cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains("Title"));
        titleBtn.Click();

        Assert.NotNull(lastSort);
        Assert.Equal("title", lastSort.Value.Field);
        Assert.False(lastSort.Value.Descending); // Title defaults to ascending
    }

    [Fact]
    public void SortDropdown_SelectingAlreadySelectedField_TogglesDirection()
    {
        MovieSortChange? lastSort = null;

        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.SortField, "title")
            .Add(x => x.SortDescending, false)
            .Add(x => x.OnSortChanged, EventCallback.Factory.Create<MovieSortChange>(this, s => lastSort = s)));

        var sortBtn = cut.FindAll("button").First(b => b.TextContent.StartsWith("Sort:"));
        sortBtn.Click();

        var titleBtn = cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains("Title"));
        titleBtn.Click();

        Assert.NotNull(lastSort);
        Assert.Equal("title", lastSort.Value.Field);
        Assert.True(lastSort.Value.Descending);
    }

    [Fact]
    public void SortDropdown_SelectingRandom_ProvidesSeed()
    {
        MovieSortChange? lastSort = null;

        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.SortField, "added")
            .Add(x => x.SortDescending, true)
            .Add(x => x.OnSortChanged, EventCallback.Factory.Create<MovieSortChange>(this, s => lastSort = s)));

        var sortBtn = cut.FindAll("button").First(b => b.TextContent.StartsWith("Sort:"));
        sortBtn.Click();

        var randomBtn = cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains("Random"));
        randomBtn.Click();

        Assert.NotNull(lastSort);
        Assert.Equal("random", lastSort.Value.Field);
        Assert.NotNull(lastSort.Value.RandomSortSeed);
        Assert.True(lastSort.Value.RandomSortSeed > 0);
    }

    [Fact]
    public void RendersChildContent()
    {
        var cut = Render<MovieFilterToolbar>(p => p
            .AddChildContent("<button id=\"custom-extra-btn\">Extra</button>"));

        Assert.NotNull(cut.Find("#custom-extra-btn"));
        Assert.Equal("Extra", cut.Find("#custom-extra-btn").TextContent);
    }

    [Fact]
    public void ReleasesBrowseMode_HidesTheLocalOnlyControlsButKeepsSearchAndStudio()
    {
        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.TotalCount, 12)
            .Add(x => x.LibraryNames, new[] { "Library A" })
            .Add(x => x.AvailableStudios, new[] { "Studio A" })
            .Add(x => x.ReleasesBrowseMode, true));

        Assert.Empty(cut.FindAll(".btn-group"));
        Assert.Empty(cut.FindAll("select"));
        Assert.DoesNotContain("Sort:", cut.Markup);
        Assert.NotNull(cut.Find("input[type=search]"));

        cut.FindAll("button").First(b => b.TextContent.Contains("Filter")).Click();

        Assert.Contains("Studio", cut.Markup);
        Assert.DoesNotContain("Resolution", cut.Markup);
        Assert.DoesNotContain("Scan Type", cut.Markup);
        Assert.DoesNotContain("Features", cut.Markup);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.StartsWith("Library"));
    }

    [Fact]
    public void ReleasesBrowseMode_HidePreviouslyDeletedIsAFilterMenuToggle()
    {
        var toggles = 0;
        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.ReleasesBrowseMode, true)
            .Add(x => x.HidePreviouslyDeleted, true)
            .Add(x => x.OnToggleHidePreviouslyDeleted, () => toggles++));

        Assert.Contains("Filter (1)", cut.Markup);
        OpenFilterMenu(cut);
        var toggle = cut.FindAll("button").Single(b => b.TextContent.Contains("Hide previously deleted"));
        Assert.Equal("true", toggle.GetAttribute("aria-pressed"));
        toggle.Click();
        Assert.Equal(1, toggles);

        cut.Render(p => p.Add(x => x.HidePreviouslyDeleted, false));
        Assert.DoesNotContain("Filter (1)", cut.Markup);
        Assert.Equal("false", cut.FindAll("button").Single(b => b.TextContent.Contains("Hide previously deleted")).GetAttribute("aria-pressed"));
    }

    [Fact]
    public void LibraryMode_HasNoHidePreviouslyDeletedToggle()
    {
        var cut = Render<MovieFilterToolbar>(p => p.Add(x => x.HidePreviouslyDeleted, true));
        OpenFilterMenu(cut);
        Assert.DoesNotContain("Hide previously deleted", cut.Markup);
    }

    private static void OpenFilterMenu(IRenderedComponent<MovieFilterToolbar> cut) =>
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Filter")).Click();

    private static void OpenGenreGroup(IRenderedComponent<MovieFilterToolbar> cut) =>
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Genre")).Click();

    private static void OpenActorSection(IRenderedComponent<MovieFilterToolbar> cut) =>
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Actor")).Click();

    private static ActorAttributeOptions AttributeOptions() => new()
    {
        CupSizes = ["D", "E"],
        Bust = new RangeBounds(80, 100),
        Age = new RangeBounds(19, 40),
    };

    [Fact]
    public void ActorAttributes_HiddenUnlessEnabled()
    {
        var cut = Render<MovieFilterToolbar>(p => p.Add(x => x.ActorAttributeOptions, AttributeOptions()));

        OpenFilterMenu(cut);

        Assert.DoesNotContain("Cup Size", cut.Markup);
        Assert.DoesNotContain("Bust", cut.Markup);
    }

    [Fact]
    public void ActorAttributes_ShowsGroupsAndCountsEachInTheBadge()
    {
        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.ShowActorAttributes, true)
            .Add(x => x.ActorAttributeOptions, AttributeOptions())
            .Add(x => x.ActorAttributeSelection, new ActorAttributeSelection { CupSizes = ["E"], Bust = new IntRange(85, 95) }));

        OpenFilterMenu(cut);
        OpenActorSection(cut);

        Assert.Contains("Cup Size (1)", cut.Markup);
        Assert.Contains("Bust (85–95 cm)", cut.Markup);
        Assert.Contains("Age", cut.Markup);
        Assert.Contains("Filter (2)", cut.Markup); // one chip + one narrowed range
    }

    [Fact]
    public void ActorAttributes_NothingOffered_ShowsNothing()
    {
        var cut = Render<MovieFilterToolbar>(p => p.Add(x => x.ShowActorAttributes, true));

        OpenFilterMenu(cut);

        Assert.DoesNotContain("Cup Size", cut.Markup);
        Assert.DoesNotContain("Height", cut.Markup);
    }

    [Fact]
    public void ActorAttributes_ChipClickRaisesTheEditedSelection()
    {
        ActorAttributeSelection? changed = null;
        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.ShowActorAttributes, true)
            .Add(x => x.ActorAttributeOptions, AttributeOptions())
            .Add(x => x.OnActorAttributesChanged, EventCallback.Factory.Create<ActorAttributeSelection>(this, s => changed = s)));

        OpenFilterMenu(cut);
        OpenActorSection(cut);
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Cup Size")).Click();
        cut.FindAll("button").First(b => b.TextContent.Trim() == "E").Click();

        Assert.Equal(["E"], changed!.CupSizes);
    }

    [Fact]
    public void ActorAttributes_AreNestedUnderACollapsedActorSection_WhoseHeaderCountsThem()
    {
        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.ShowActorAttributes, true)
            .Add(x => x.ActorAttributeOptions, AttributeOptions())
            .Add(x => x.ActorAttributeSelection, new ActorAttributeSelection { CupSizes = ["E"], Bust = new IntRange(85, 95) }));

        OpenFilterMenu(cut);

        Assert.Contains("Actor (2)", cut.Markup);
        Assert.DoesNotContain("Cup Size", cut.Markup);     // collapsed: nothing nested is rendered yet
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Actor")).Click();
        Assert.Single(cut.FindAll(".filter-section-body"));
        Assert.Contains("Cup Size (1)", cut.Find(".filter-section-body").TextContent);
        Assert.Contains("Bust (85–95 cm)", cut.Find(".filter-section-body").TextContent);
    }

    [Fact]
    public void ActorSection_IsHiddenWhenNothingIsOffered()
    {
        var cut = Render<MovieFilterToolbar>(p => p.Add(x => x.ShowActorAttributes, true));

        OpenFilterMenu(cut);

        Assert.DoesNotContain("Actor", cut.Markup.Replace("ActorAttribute", ""));
    }
    [Fact]
    public void ExcludedGenres_ShowStruckThroughWithACross_AndCountInTheBadges()
    {
        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.AvailableGenres, new[] { "Drama", "Comedy", "Horror" })
            .Add(x => x.GenreFilter, new HashSet<string> { "Drama" })
            .Add(x => x.Excludable, true)
            .Add(x => x.ExcludedGenreFilter, new HashSet<string> { "Horror" }));

        Assert.Contains("Filter (2)", cut.Markup);
        OpenFilterMenu(cut);
        Assert.Contains("Genre (2)", cut.Markup);
        OpenGenreGroup(cut);

        var items = cut.FindAll("button.sort-dropdown-subitem");
        var horror = items.Single(b => b.TextContent.Contains("Horror"));
        Assert.Contains("multiselect-excluded", horror.ClassList);
        Assert.Contains("✕", horror.TextContent);
        Assert.Equal("Click to clear", horror.GetAttribute("title"));
        Assert.Contains("✓", items.Single(b => b.TextContent.Contains("Drama")).TextContent);
        Assert.DoesNotContain("multiselect-excluded", items.Single(b => b.TextContent.Contains("Comedy")).ClassList);
    }

    [Fact]
    public void ReleasesBrowseMode_DoesNotShowExcludedStudios()
    {
        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.ReleasesBrowseMode, true)
            .Add(x => x.AvailableStudios, new[] { "S1" })
            .Add(x => x.Excludable, true)
            .Add(x => x.ExcludedStudioFilter, new HashSet<string> { "S1" }));

        OpenFilterMenu(cut);
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Studio")).Click();

        Assert.Empty(cut.FindAll(".multiselect-excluded"));
        Assert.Null(cut.FindAll("button.sort-dropdown-subitem").Single().GetAttribute("title"));
    }

    [Fact]
    public void Ranges_AreTopLevelFilters_AndCastSizeSitsUnderActor()
    {
        MovieRangeSelection? emitted = null;
        var cut = Render<MovieFilterToolbar>(p => p
            .Add(x => x.RangeOptions, new MovieRangeOptions { ReleaseYear = new RangeBounds(2010, 2024), RuntimeMinutes = new RangeBounds(60, 240), CastSize = new RangeBounds(0, 4) })
            .Add(x => x.RangeSelection, new MovieRangeSelection { RuntimeMinutes = new IntRange(90, null), CastSize = new IntRange(1, 1) })
            .Add(x => x.OnRangesChanged, EventCallback.Factory.Create<MovieRangeSelection>(this, r => emitted = r)));

        Assert.Contains("Filter (2)", cut.Markup);
        OpenFilterMenu(cut);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.StartsWith("Ranges"));
        Assert.Contains("Runtime (90–240 min)", cut.Markup);
        Assert.DoesNotContain("Bitrate", cut.Markup);
        Assert.DoesNotContain("Cast size", cut.Markup);

        cut.FindAll("button").First(b => b.TextContent.StartsWith("Actor")).Click();
        Assert.Contains("Cast size (1)", cut.Markup);

        cut.FindAll("button").First(b => b.TextContent.StartsWith("Release year")).Click();
        cut.FindAll(".range-thumb-input")[0].Change("2015");
        Assert.Equal(new IntRange(2015, null), emitted!.ReleaseYear);
        Assert.Equal(new IntRange(90, null), emitted.RuntimeMinutes);
    }

    [Fact]
    public void SortDropdown_OffersTheNewSorts_AndLabelsFavoritesAsDateFavorited()
    {
        var cut = Render<MovieFilterToolbar>();
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Sort:")).Click();

        var labels = cut.FindAll("button.sort-dropdown-item").Select(b => b.TextContent.Trim()).ToList();
        foreach (var label in new[] { "Code", "Bitrate", "Date favorited", "Scenes", "Highlights", "Apexes", "Cast size" })
        {
            Assert.Contains(labels, l => l.StartsWith(label, StringComparison.Ordinal));
        }
        Assert.DoesNotContain(labels, l => l == "Favorites");
    }
}
