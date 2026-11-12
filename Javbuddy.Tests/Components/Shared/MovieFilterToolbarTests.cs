using Bunit;
using Javbuddy.Components.Pages.MoviesSections;
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

    private int changes;

    // The toolbar edits the state it is given and then raises OnChanged; every test renders through here so it has both.
    private IRenderedComponent<MovieFilterToolbar> RenderToolbar(
        MovieGridFilterState? state = null,
        Action<ComponentParameterCollectionBuilder<MovieFilterToolbar>>? configure = null) =>
        Render<MovieFilterToolbar>(p =>
        {
            p.Add(x => x.State, state ?? new MovieGridFilterState())
                .Add(x => x.OnChanged, EventCallback.Factory.Create(this, () => changes++));
            configure?.Invoke(p);
        });

    [Fact]
    public void RendersStatusCounts_AndSetsTheStatusOnTheState()
    {
        var state = new MovieGridFilterState();

        var cut = RenderToolbar(state, p => p.Add(x => x.StatusCounts, new MovieStatusCounts(12, 5, 7)));

        Assert.Contains("All (12)", cut.Markup);
        Assert.Contains("Missing (5)", cut.Markup);
        Assert.Contains("Got (7)", cut.Markup);

        var missingBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Missing (5)"));
        missingBtn.Click();

        Assert.Equal(1, changes);
        Assert.Equal(MovieStatus.Missing, state.Status);
    }

    [Fact]
    public void LibraryFilter_IsAGroupInTheFilterDropdown_NotAToolbarSelect()
    {
        var state = new MovieGridFilterState();

        var cut = RenderToolbar(state, p => p.Add(x => x.Choices, new MovieFilterChoices { LibraryNames = ["Library A", "Library B"] }));

        Assert.Empty(cut.FindAll("select"));

        OpenFilterMenu(cut);
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Library")).Click();
        var options = cut.FindAll("button.sort-dropdown-subitem").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Library A", "Library B" }, options);

        cut.FindAll("button.sort-dropdown-subitem").First(b => b.TextContent.Trim() == "Library A").Click();
        Assert.Equal("Library A", state.Library);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void LibraryFilter_SelectingAnotherReplacesIt_AndSelectingTheActiveOneClearsIt()
    {
        var state = new MovieGridFilterState { Library = "Library A" };

        var cut = RenderToolbar(state, p => p.Add(x => x.Choices, new MovieFilterChoices { LibraryNames = ["Library A", "Library B"] }));

        OpenFilterMenu(cut);
        Assert.Contains("Filter (1)", cut.Markup);
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Library")).Click();
        Assert.Equal(["Library A"], cut.FindAll("button.sort-dropdown-subitem.active").Select(b => b.TextContent.Replace("✓", "").Trim()));

        cut.FindAll("button.sort-dropdown-subitem").First(b => b.TextContent.Trim() == "Library B").Click();
        Assert.Equal("Library B", state.Library);

        cut.FindAll("button.sort-dropdown-subitem").First(b => b.TextContent.Contains("Library B")).Click();
        Assert.Null(state.Library);
    }

    [Fact]
    public void LibraryFilter_GroupIsHiddenWhenNoLibrariesAreOffered()
    {
        var cut = RenderToolbar();

        OpenFilterMenu(cut);

        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.StartsWith("Library"));
    }

    [Fact]
    public void TextInput_DebouncesThenSetsTheTextAndDropsTheCodePrefix()
    {
        var state = new MovieGridFilterState { CodePrefix = "ABC" };

        var cut = RenderToolbar(state);

        var input = cut.Find("input[type=search]");
        input.Input("IPX");

        Assert.Equal("", state.Text);
        Assert.Equal(0, changes);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, changes);
            Assert.Equal("IPX", state.Text);
        }, TimeSpan.FromSeconds(2));
        // Typing in the search box replaces a code-prefix navigation target.
        Assert.Null(state.CodePrefix);
    }

    [Fact]
    public void FilterDropdown_RendersGroupsAndTogglesOptionOnTheState()
    {
        var state = new MovieGridFilterState();

        var cut = RenderToolbar(state, p => p.Add(x => x.Choices, new MovieFilterChoices { Codecs = ["AVC", "HEVC"], Studios = ["IdeaPocket"], Genres = ["Drama"] }));

        // Open filter dropdown
        var filterBtn = cut.FindAll("button").First(b => b.TextContent.StartsWith("Filter"));
        filterBtn.Click();

        // Subgroup "Resolution" toggle
        var resBtn = cut.FindAll("button").First(b => b.TextContent.StartsWith("Resolution"));
        resBtn.Click();

        // Option "SD"
        var sdBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "SD");
        sdBtn.Click();

        Assert.Equal([MovieResolutionFilterOption.Sd], state.Resolutions);
        Assert.Equal(1, changes);

        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("SD")).Click();
        Assert.Empty(state.Resolutions);
    }

    [Fact]
    public void MatchAllGenres_CheckboxShowsOnlyWithTwoGenres_AndSetsTheFlag()
    {
        IRenderedComponent<MovieFilterToolbar> Build(MovieGridFilterState state, bool showMatchAll = true) => RenderToolbar(state, p => p
            .Add(x => x.Choices, new MovieFilterChoices { Genres = ["Drama", "Solo"] })
            .Add(x => x.ShowMatchAll, showMatchAll));

        var one = Build(new MovieGridFilterState { Genres = new(["Drama"], StringComparer.OrdinalIgnoreCase) });
        OpenFilterMenu(one);
        OpenGenreGroup(one);
        Assert.Empty(one.FindAll("label.match-all-check"));

        var twoState = new MovieGridFilterState { Genres = new(["Drama", "Solo"], StringComparer.OrdinalIgnoreCase) };
        var two = Build(twoState);
        OpenFilterMenu(two);
        OpenGenreGroup(two);
        two.Find(".multiselect-actions-row label.match-all-check input").Change(true);
        Assert.True(twoState.MatchAllGenres);
        Assert.Equal(1, changes);

        // A host that doesn't offer match-all never shows the checkbox.
        var off = Build(new MovieGridFilterState { Genres = new(["Drama", "Solo"], StringComparer.OrdinalIgnoreCase) }, showMatchAll: false);
        OpenFilterMenu(off);
        OpenGenreGroup(off);
        Assert.Empty(off.FindAll("label.match-all-check"));
    }

    [Fact]
    public void FilterDropdown_HasAnNfoDriftGroupUnderFeatures_WithEveryDirection_AndTogglesOne()
    {
        var state = new MovieGridFilterState();
        state.NfoDriftKinds.Add(NfoDriftKind.Unreadable);

        var cut = RenderToolbar(state);

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

        Assert.True(state.NfoDriftKinds.SetEquals([NfoDriftKind.Unreadable, NfoDriftKind.ExternalEdit]));

        // None of it is offered as a Features option any more, and the Features options show without a header of their own.
        Assert.Single(cut.FindAll("button"), b => b.TextContent.StartsWith("Features"));
        Assert.Contains(cut.FindAll("button.sort-dropdown-subitem"), b => b.TextContent.Trim() == "Has subtitles");
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("NFO drift:"));
    }

    [Fact]
    public void FilterDropdown_ClearButton_InvokesOnResetFilters()
    {
        var resetCalled = false;

        var cut = RenderToolbar(new MovieGridFilterState { Status = MovieStatus.Got }, p => p
            .Add(x => x.OnResetFilters, EventCallback.Factory.Create(this, () => resetCalled = true)));

        var clearBtn = cut.Find(".dropdown-clear-btn");
        clearBtn.Click();

        Assert.True(resetCalled);
    }

    [Fact]
    public void SortDropdown_SelectsDifferentField_UsesDefaultDirection()
    {
        var state = new MovieGridFilterState { SortField = "added", SortDescending = true };

        var cut = RenderToolbar(state);

        // Open sort dropdown
        var sortBtn = cut.FindAll("button").First(b => b.TextContent.StartsWith("Sort:"));
        sortBtn.Click();

        // Pick "Title"
        var titleBtn = cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains("Title"));
        titleBtn.Click();

        Assert.Equal(1, changes);
        Assert.Equal("title", state.SortField);
        Assert.False(state.SortDescending); // Title defaults to ascending
    }

    [Fact]
    public void SortDropdown_SelectingAlreadySelectedField_TogglesDirection()
    {
        var state = new MovieGridFilterState { SortField = "title", SortDescending = false };

        var cut = RenderToolbar(state);

        var sortBtn = cut.FindAll("button").First(b => b.TextContent.StartsWith("Sort:"));
        sortBtn.Click();

        var titleBtn = cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains("Title"));
        titleBtn.Click();

        Assert.Equal("title", state.SortField);
        Assert.True(state.SortDescending);
    }

    [Fact]
    public void SortDropdown_SelectingRandom_DrawsANewSeed()
    {
        var state = new MovieGridFilterState { SortField = "added", SortDescending = true };
        var before = state.RandomSortSeed;

        var cut = RenderToolbar(state);

        var sortBtn = cut.FindAll("button").First(b => b.TextContent.StartsWith("Sort:"));
        sortBtn.Click();

        var randomBtn = cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains("Random"));
        randomBtn.Click();

        Assert.Equal("random", state.SortField);
        Assert.True(state.SortDescending);
        Assert.True(state.RandomSortSeed > 0);
        Assert.NotEqual(before, state.RandomSortSeed);
    }

    [Fact]
    public void RendersChildContent()
    {
        var cut = RenderToolbar(null, p => p
            .AddChildContent("<button id=\"custom-extra-btn\">Extra</button>"));

        Assert.NotNull(cut.Find("#custom-extra-btn"));
        Assert.Equal("Extra", cut.Find("#custom-extra-btn").TextContent);
    }

    [Fact]
    public void ReleasesBrowseMode_HidesTheLocalOnlyControlsButKeepsSearchAndStudio()
    {
        var cut = RenderToolbar(null, p => p
            .Add(x => x.StatusCounts, new MovieStatusCounts(12, 0, 0))
            .Add(x => x.Choices, new MovieFilterChoices { LibraryNames = ["Library A"], Studios = ["Studio A"] })
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
        var cut = RenderToolbar(null, p => p
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
        var cut = RenderToolbar(null, p => p.Add(x => x.HidePreviouslyDeleted, true));
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
        var cut = RenderToolbar(null, p => p.Add(x => x.Choices, new MovieFilterChoices { ActorAttributes = AttributeOptions() }));

        OpenFilterMenu(cut);

        Assert.DoesNotContain("Cup Size", cut.Markup);
        Assert.DoesNotContain("Bust", cut.Markup);
    }

    [Fact]
    public void ActorAttributes_ShowsGroupsAndCountsEachInTheBadge()
    {
        var cut = RenderToolbar(new MovieGridFilterState { ActorAttributes = new ActorAttributeSelection { CupSizes = ["E"], Bust = new IntRange(85, 95) } }, p => p
            .Add(x => x.ShowActorAttributes, true)
            .Add(x => x.Choices, new MovieFilterChoices { ActorAttributes = AttributeOptions() }));

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
        var cut = RenderToolbar(null, p => p.Add(x => x.ShowActorAttributes, true));

        OpenFilterMenu(cut);

        Assert.DoesNotContain("Cup Size", cut.Markup);
        Assert.DoesNotContain("Height", cut.Markup);
    }

    [Fact]
    public void ActorAttributes_ChipClickEditsTheStatesSelection()
    {
        var state = new MovieGridFilterState();
        var cut = RenderToolbar(state, p => p
            .Add(x => x.ShowActorAttributes, true)
            .Add(x => x.Choices, new MovieFilterChoices { ActorAttributes = AttributeOptions() }));

        OpenFilterMenu(cut);
        OpenActorSection(cut);
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Cup Size")).Click();
        cut.FindAll("button").First(b => b.TextContent.Trim() == "E").Click();

        Assert.Equal(["E"], state.ActorAttributes.CupSizes);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ActorAttributes_AreNestedUnderACollapsedActorSection_WhoseHeaderCountsThem()
    {
        var cut = RenderToolbar(new MovieGridFilterState { ActorAttributes = new ActorAttributeSelection { CupSizes = ["E"], Bust = new IntRange(85, 95) } }, p => p
            .Add(x => x.ShowActorAttributes, true)
            .Add(x => x.Choices, new MovieFilterChoices { ActorAttributes = AttributeOptions() }));

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
        var cut = RenderToolbar(null, p => p.Add(x => x.ShowActorAttributes, true));

        OpenFilterMenu(cut);

        Assert.DoesNotContain("Actor", cut.Markup.Replace("ActorAttribute", ""));
    }
    [Fact]
    public void ExcludedGenres_ShowStruckThroughWithACross_AndCountInTheBadges()
    {
        var state = new MovieGridFilterState();
        state.Genres.Add("Drama");
        state.ExcludedGenres.Add("Horror");
        var cut = RenderToolbar(state, p => p
            .Add(x => x.Choices, new MovieFilterChoices { Genres = ["Drama", "Comedy", "Horror"] })
            .Add(x => x.Excludable, true));

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
    public void ExcludedContentTypes_ShowStruckThroughWithACross_AndCountInTheBadges()
    {
        var state = new MovieGridFilterState();
        state.ContentTypes.Add(MovieContentType.Censored);
        state.ExcludedContentTypes.Add(MovieContentType.Uncensored);
        var cut = RenderToolbar(state, p => p.Add(x => x.Excludable, true));

        Assert.Contains("Filter (2)", cut.Markup);
        OpenFilterMenu(cut);
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Content Type")).Click();

        var items = cut.FindAll("button.sort-dropdown-subitem");
        var uncensored = items.Single(b => b.TextContent.Contains("Uncensored"));
        Assert.Contains("multiselect-excluded", uncensored.ClassList);
        Assert.Contains("✕", uncensored.TextContent);
        Assert.Equal("Click to clear", uncensored.GetAttribute("title"));
    }

    [Fact]
    public void ReleasesBrowseMode_DoesNotShowExcludedStudios()
    {
        var state = new MovieGridFilterState();
        state.ExcludedStudios.Add("S1");
        var cut = RenderToolbar(state, p => p
            .Add(x => x.ReleasesBrowseMode, true)
            .Add(x => x.Choices, new MovieFilterChoices { Studios = ["S1"] })
            .Add(x => x.Excludable, true));

        OpenFilterMenu(cut);
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Studio")).Click();

        Assert.Empty(cut.FindAll(".multiselect-excluded"));
        Assert.Null(cut.FindAll("button.sort-dropdown-subitem").Single().GetAttribute("title"));
    }

    [Fact]
    public void Ranges_AreTopLevelFilters_AndCastSizeSitsUnderActor()
    {
        var state = new MovieGridFilterState { Ranges = new MovieRangeSelection { RuntimeMinutes = new IntRange(90, null), CastSize = new IntRange(1, 1) } };
        var cut = RenderToolbar(state, p => p
            .Add(x => x.Choices, new MovieFilterChoices { Ranges = new MovieRangeOptions { ReleaseYear = new RangeBounds(2010, 2024), RuntimeMinutes = new RangeBounds(60, 240), CastSize = new RangeBounds(0, 4) } }));

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
        Assert.Equal(new IntRange(2015, null), state.Ranges.ReleaseYear);
        Assert.Equal(new IntRange(90, null), state.Ranges.RuntimeMinutes);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ExcludableGroups_StepAValueThroughIncludedAndExcluded_ButAPlainGroupJustToggles()
    {
        var state = new MovieGridFilterState();
        var cut = RenderToolbar(state, p => p
            .Add(x => x.Choices, new MovieFilterChoices { Genres = ["Drama"] })
            .Add(x => x.Excludable, true));
        OpenFilterMenu(cut);
        OpenGenreGroup(cut);

        void Click() => cut.FindAll("button.sort-dropdown-subitem").Single(b => b.TextContent.Contains("Drama")).Click();
        Click();
        Assert.Contains("Drama", state.Genres);
        Click();
        Assert.Empty(state.Genres);
        Assert.Contains("Drama", state.ExcludedGenres);
        Click();
        Assert.Empty(state.ExcludedGenres);

        var plainState = new MovieGridFilterState();
        var plain = RenderToolbar(plainState, p => p.Add(x => x.Choices, new MovieFilterChoices { Genres = ["Drama"] }));
        OpenFilterMenu(plain);
        OpenGenreGroup(plain);
        plain.FindAll("button.sort-dropdown-subitem").Single().Click();
        plain.FindAll("button.sort-dropdown-subitem").Single().Click();
        Assert.Empty(plainState.Genres);
        Assert.Empty(plainState.ExcludedGenres);
    }

    [Fact]
    public void ContentTypes_StayIncludeOnly_WhenTheHostSaysTheyCantBeExcluded()
    {
        var state = new MovieGridFilterState();
        var cut = RenderToolbar(state, p => p
            .Add(x => x.Excludable, true)
            .Add(x => x.ExcludableContentTypes, false));
        OpenFilterMenu(cut);
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Content Type")).Click();

        var item = cut.FindAll("button.sort-dropdown-subitem").First(b => b.TextContent.Contains("Uncensored"));
        Assert.Null(item.GetAttribute("title"));
        item.Click();
        Assert.Equal([MovieContentType.Uncensored], state.ContentTypes);
        cut.FindAll("button.sort-dropdown-subitem").First(b => b.TextContent.Contains("Uncensored")).Click();
        Assert.Empty(state.ContentTypes);
        Assert.Empty(state.ExcludedContentTypes);
    }

    [Fact]
    public void StudioInReleasesBrowseMode_OnlyIncludesOrClears_AndDropsAnExclusion()
    {
        var state = new MovieGridFilterState();
        state.ExcludedStudios.Add("S1");
        var cut = RenderToolbar(state, p => p
            .Add(x => x.ReleasesBrowseMode, true)
            .Add(x => x.Excludable, true)
            .Add(x => x.Choices, new MovieFilterChoices { Studios = ["S1"] }));
        OpenFilterMenu(cut);
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Studio")).Click();

        cut.FindAll("button.sort-dropdown-subitem").Single().Click();

        Assert.Equal(["S1"], state.Studios);
        Assert.Empty(state.ExcludedStudios);
        cut.FindAll("button.sort-dropdown-subitem").Single().Click();
        Assert.Empty(state.Studios);
        Assert.Empty(state.ExcludedStudios);
    }

    [Fact]
    public void ActorSelectAll_IncludesEveryActorWithAVisibleName_AndNoneClearsBothSets()
    {
        var state = new MovieGridFilterState();
        state.ExcludedActorIds.Add(3);
        var cut = RenderToolbar(state, p => p
            .Add(x => x.Excludable, true)
            .Add(x => x.Choices, new MovieFilterChoices { Actors = [new(1, "Amy"), new(2, "Amy"), new(3, "Bea")] }));
        OpenFilterMenu(cut);
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Actor")).Click();
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Name")).Click();

        cut.Find(".multiselect-search-input").Input("Am");
        cut.FindAll("button.multiselect-action-link").First(b => b.TextContent == "Select All").Click();
        Assert.Equal([1, 2], state.ActorIds.OrderBy(i => i));
        Assert.Equal([3], state.ExcludedActorIds);

        cut.FindAll("button.multiselect-action-link").First(b => b.TextContent == "None").Click();
        Assert.Empty(state.ActorIds);
        Assert.Empty(state.ExcludedActorIds);
    }

    [Fact]
    public void CountSliders_EditTheStatesCounts_AndHostsCanReset()
    {
        var state = new MovieGridFilterState();
        var reset = false;
        var cut = RenderToolbar(state, p => p
            .Add(x => x.Choices, new MovieFilterChoices { Counts = MovieCountOptions.FromMaximums(5, 0, 0) })
            .Add(x => x.OnResetFilters, EventCallback.Factory.Create(this, () => reset = true)));
        OpenFilterMenu(cut);
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Clips")).Click();
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Scenes")).Click();

        cut.FindAll(".range-thumb-input")[0].Change("2");

        Assert.Equal(new IntRange(2, null), state.Counts.Scenes);
        Assert.Equal(1, changes);
        Assert.Contains("Filter (1)", cut.Markup);

        cut.Find(".dropdown-clear-btn").Click();
        Assert.True(reset);
    }

    [Fact]
    public void SortDropdown_OffersTheNewSorts_AndLabelsFavoritesAsDateFavorited()
    {
        var cut = RenderToolbar();
        cut.FindAll("button").First(b => b.TextContent.StartsWith("Sort:")).Click();

        var labels = cut.FindAll("button.sort-dropdown-item").Select(b => b.TextContent.Trim()).ToList();
        foreach (var label in new[] { "Code", "Bitrate", "Date favorited", "Scenes", "Highlights", "Apexes", "Cast size" })
        {
            Assert.Contains(labels, l => l.StartsWith(label, StringComparison.Ordinal));
        }
        Assert.DoesNotContain(labels, l => l == "Favorites");
    }
}
