using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Services.Actors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class ActorsTests : BunitContext
{
    private readonly IActorService actorService = Substitute.For<IActorService>();

    public ActorsTests()
    {
        Services.AddSingleton(actorService);
        // VirtualizedGrid imports its JS module on first render; nothing here exercises it.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void EmptyLibrary_ShowsNoActorsMessage()
    {
        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ActorCardModel>());

        var cut = Render<Actors>();

        Assert.Contains("No actors yet", cut.Markup);
        Assert.Empty(cut.FindAll(".actor-card"));
    }

    [Fact]
    public void ActorsPresent_RendersActorCards_WithPhotosAndInitials()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Mikami Yua", 5, true, ImageVersion: 123456789, ImageCount: 2),
            new(2, "Remu", 0, false),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        var cards = cut.FindAll(".actor-card");
        Assert.Equal(2, cards.Count);

        // First card has photo avatar
        var img = cards[0].QuerySelector(".actor-avatar-photo img");
        Assert.NotNull(img);
        Assert.Equal("/actor-image/1/thumb?v=123456789", img.GetAttribute("src"));
        Assert.Equal("lazy", img.GetAttribute("loading"));
        Assert.Equal("async", img.GetAttribute("decoding"));
        Assert.Contains("actor-avatar-img", img.GetAttribute("class"));
        Assert.Contains("Mikami Yua", cards[0].TextContent);
        Assert.Contains("0 owned · 5 missing", cards[0].TextContent);
        Assert.Contains("2 images", cards[0].TextContent);

        // Second card has initials avatar
        Assert.Null(cards[1].QuerySelector(".actor-avatar-photo img"));
        Assert.Equal("R", cards[1].QuerySelector(".actor-avatar")?.TextContent.Trim());
        Assert.Contains("Remu", cards[1].TextContent);
        Assert.Contains("No movies", cards[1].TextContent);
        Assert.DoesNotContain("image", cards[1].TextContent);
    }

    [Fact]
    public void ActorsPresent_RendersFavoriteButtons_WithCorrectActiveStates()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Mikami Yua", 5, true, IsFavorite: true),
            new(2, "Remu", 0, false, IsFavorite: false),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        var favButtons = cut.FindAll(".actor-card-favorite");
        Assert.Equal(2, favButtons.Count);
        Assert.Contains("active", favButtons[0].ClassList);
        Assert.DoesNotContain("active", favButtons[1].ClassList);
    }

    private static void EnsureFilterDropdownOpen(IRenderedComponent<Actors> cut)
    {
        if (cut.FindAll(".sort-dropdown-menu").Count == 0)
        {
            var filterBtn = cut.FindAll(".sort-dropdown-wrapper button")
                .First(b => b.TextContent.Trim().StartsWith("Filter"));
            filterBtn.Click();
        }
    }

    private static void ToggleFilter(IRenderedComponent<Actors> cut, string optionLabel)
    {
        EnsureFilterDropdownOpen(cut);
        cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains(optionLabel)).Click();
    }

    [Fact]
    public void FavoritesOnlyFilter_TogglesFiltering_AndShowsEmptyMessageWhenNone()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Mikami Yua", 5, true, IsFavorite: true),
            new(2, "Remu", 0, false, IsFavorite: false),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        Assert.Equal(2, cut.FindAll(".actor-card").Count);

        // Toggle "Favorites only" inside filter dropdown
        ToggleFilter(cut, "Favorites only");

        // Only favorite actor is shown
        var cards = cut.FindAll(".actor-card");
        Assert.Single(cards);
        Assert.Contains("Mikami Yua", cards[0].TextContent);

        // Toggle back off
        ToggleFilter(cut, "Favorites only");
        Assert.Equal(2, cut.FindAll(".actor-card").Count);
    }

    [Fact]
    public void FavoritesOnlyFilter_WhenNoFavorites_DisplaysEmptyFavoritesMessage()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Remu", 0, false, IsFavorite: false),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();
        ToggleFilter(cut, "Favorites only");

        Assert.Empty(cut.FindAll(".actor-card"));
        Assert.Contains("No favorite actors yet", cut.Markup);
    }

    [Fact]
    public void JellyfinFilter_WhenJellyfinEnabled_FiltersByJellyfinLinked()
    {
        actorService.IsJellyfinEnabledAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        var items = new List<ActorCardModel>
        {
            new(1, "Mikami Yua", 5, true, HasJellyfin: true),
            new(2, "Remu", 0, false, HasJellyfin: false),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        Assert.Equal(2, cut.FindAll(".actor-card").Count);

        // Toggle "Linked to Jellyfin" inside filter dropdown
        ToggleFilter(cut, "Linked to Jellyfin");

        // Only linked actor is shown
        var cards = cut.FindAll(".actor-card");
        Assert.Single(cards);
        Assert.Contains("Mikami Yua", cards[0].TextContent);

        // Toggle back off
        ToggleFilter(cut, "Linked to Jellyfin");
        Assert.Equal(2, cut.FindAll(".actor-card").Count);
    }

    [Fact]
    public void JellyfinFilter_WhenJellyfinDisabled_OptionNotPresent()
    {
        actorService.IsJellyfinEnabledAsync(Arg.Any<CancellationToken>())
            .Returns(false);

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ActorCardModel> { new(1, "Mikami Yua", 5, true) });

        var cut = Render<Actors>();

        EnsureFilterDropdownOpen(cut);
        Assert.DoesNotContain(cut.FindAll(".sort-dropdown-item"), b => b.TextContent.Contains("Linked to Jellyfin"));
    }

    [Fact]
    public void FavoritesFirstSort_OrdersFavoritesBeforeNonFavorites()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Aoi Sora", 2, false, IsFavorite: false),
            new(2, "Mikami Yua", 5, true, IsFavorite: true),
            new(3, "Remu", 0, false, IsFavorite: false),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        // Default order: Name A-Z (Aoi Sora, Mikami Yua, Remu)
        var cardsDefault = cut.FindAll(".actor-card .actor-name");
        Assert.Equal("Aoi Sora", cardsDefault[0].TextContent.Trim());
        Assert.Equal("Mikami Yua", cardsDefault[1].TextContent.Trim());
        Assert.Equal("Remu", cardsDefault[2].TextContent.Trim());

        // Open sort dropdown and click "Favorites"
        var sortDropdownBtn = cut.FindAll(".sort-dropdown-wrapper button")
            .First(b => b.TextContent.Trim().StartsWith("Sort:"));
        sortDropdownBtn.Click();

        var sortItems = cut.FindAll(".sort-dropdown-item");
        var favSortBtn = sortItems.Single(b => b.TextContent.Contains("Favorites"));
        favSortBtn.Click();

        // Mikami Yua (favorite) should come first, followed by Aoi Sora, then Remu
        var cardsSorted = cut.FindAll(".actor-card .actor-name");
        Assert.Equal("Mikami Yua", cardsSorted[0].TextContent.Trim());
        Assert.Equal("Aoi Sora", cardsSorted[1].TextContent.Trim());
        Assert.Equal("Remu", cardsSorted[2].TextContent.Trim());

        // Click "Favorites" again -> toggles to non-favorites first
        sortDropdownBtn.Click();
        sortItems = cut.FindAll(".sort-dropdown-item");
        sortItems.Single(b => b.TextContent.Contains("Favorites")).Click();

        var cardsReversed = cut.FindAll(".actor-card .actor-name");
        Assert.Equal("Aoi Sora", cardsReversed[0].TextContent.Trim());
        Assert.Equal("Remu", cardsReversed[1].TextContent.Trim());
        Assert.Equal("Mikami Yua", cardsReversed[2].TextContent.Trim());
    }

    [Fact]
    public void QuickToggleFavorite_CallsService_AndUpdatesCardState_WithoutNavigating()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Mikami Yua", 5, true, IsFavorite: false),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);
        actorService.ToggleFavoriteAsync(1, Arg.Any<CancellationToken>())
            .Returns(true);

        var cut = Render<Actors>();
        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        var initialUri = nav.Uri;

        var favBtn = cut.Find(".actor-card-favorite");
        Assert.DoesNotContain("active", favBtn.ClassList);

        favBtn.Click();

        actorService.Received(1).ToggleFavoriteAsync(1, Arg.Any<CancellationToken>());
        Assert.Contains("active", cut.Find(".actor-card-favorite").ClassList);
        Assert.Equal(initialUri, nav.Uri);
    }

    [Fact]
    public void SearchInput_FiltersCardsByNameAndAliases_AndShowsEmptyMessageWhenNoMatch()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Mikami Yua", 5, true, JapaneseNameKanji: "三上悠亜", Aliases: new[] { "Momona Kito" }),
            new(2, "Remu", 2, false),
            new(3, "Aoi Sora", 3, true),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();
        var searchInput = cut.Find("input[type='search']");

        // The text filter is debounced, so the grid only updates once the debounce
        // delay elapses after each keystroke.

        // Filter by partial display name
        searchInput.Input("rem");
        cut.WaitForAssertion(() =>
        {
            var cards = cut.FindAll(".actor-card .actor-name");
            Assert.Single(cards);
            Assert.Equal("Remu", cards[0].TextContent.Trim());
        }, TimeSpan.FromSeconds(2));

        // Filter by Japanese kanji
        searchInput.Input("三上");
        cut.WaitForAssertion(() =>
        {
            var cards = cut.FindAll(".actor-card .actor-name");
            Assert.Single(cards);
            Assert.Equal("Mikami Yua", cards[0].TextContent.Trim());
        }, TimeSpan.FromSeconds(2));

        // Filter by alias
        searchInput.Input("Momona");
        cut.WaitForAssertion(() =>
        {
            var cards = cut.FindAll(".actor-card .actor-name");
            Assert.Single(cards);
            Assert.Equal("Mikami Yua", cards[0].TextContent.Trim());
        }, TimeSpan.FromSeconds(2));

        // No match
        searchInput.Input("Nonexistent");
        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".actor-card"));
            Assert.Contains("No actors match the selected filters.", cut.Markup);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void MovieFilter_CyclesBetweenActorsWithAndWithoutMovies()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Mikami Yua", 5, true),
            new(2, "Remu", 0, false),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        // Click 1: All -> Has Movies (Mikami Yua only)
        ToggleFilter(cut, "Has Movies");

        var cards = cut.FindAll(".actor-card .actor-name");
        Assert.Single(cards);
        Assert.Equal("Mikami Yua", cards[0].TextContent.Trim());

        // Click 2: Has Movies -> No Movies (Remu only)
        ToggleFilter(cut, "Has Movies");

        cards = cut.FindAll(".actor-card .actor-name");
        Assert.Single(cards);
        Assert.Equal("Remu", cards[0].TextContent.Trim());

        // Click 3: No Movies -> All (all actors)
        ToggleFilter(cut, "No Movies");

        Assert.Equal(2, cut.FindAll(".actor-card").Count);
    }

    [Fact]
    public void PhotoFilter_CyclesBetweenActorsWithAndWithoutPhoto()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Mikami Yua", 5, true),
            new(2, "Remu", 0, false),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        // Click 1: All -> Has Photo (Mikami Yua only)
        ToggleFilter(cut, "Has Photo");

        var cards = cut.FindAll(".actor-card .actor-name");
        Assert.Single(cards);
        Assert.Equal("Mikami Yua", cards[0].TextContent.Trim());

        // Click 2: Has Photo -> Missing Photo (Remu only)
        ToggleFilter(cut, "Has Photo");

        cards = cut.FindAll(".actor-card .actor-name");
        Assert.Single(cards);
        Assert.Equal("Remu", cards[0].TextContent.Trim());

        // Click 3: Missing Photo -> All (all actors)
        ToggleFilter(cut, "Missing Photo");

        Assert.Equal(2, cut.FindAll(".actor-card").Count);
    }

    [Fact]
    public void ImageFilter_CyclesBetweenActorsWithAndWithoutGalleryImages()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Mikami Yua", 5, true, ImageCount: 3),
            new(2, "Remu", 0, false),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        ToggleFilter(cut, "Has Images");

        var cards = cut.FindAll(".actor-card .actor-name");
        Assert.Single(cards);
        Assert.Equal("Mikami Yua", cards[0].TextContent.Trim());

        ToggleFilter(cut, "Has Images");

        cards = cut.FindAll(".actor-card .actor-name");
        Assert.Single(cards);
        Assert.Equal("Remu", cards[0].TextContent.Trim());

        ToggleFilter(cut, "No Images");

        Assert.Equal(2, cut.FindAll(".actor-card").Count);
    }

    [Fact]
    public void CombinedFilters_AndResetChip_ResetsFilters()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Mikami Yua", 5, true),
            new(2, "Remu", 2, false),
            new(3, "Aoi Sora", 0, false),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();
        Assert.Equal(3, cut.FindAll(".actor-card").Count);

        // Combine: Has Movies + Missing Photo -> Remu only
        ToggleFilter(cut, "Has Movies");
        ToggleFilter(cut, "Has Photo");
        ToggleFilter(cut, "Has Photo");

        var cards = cut.FindAll(".actor-card .actor-name");
        Assert.Single(cards);
        Assert.Equal("Remu", cards[0].TextContent.Trim());

        // Verify active filter clear button
        var clearBtn = cut.Find(".dropdown-clear-btn");
        Assert.Equal("×", clearBtn.TextContent.Trim());
        Assert.Equal("Filter (2)", cut.Find(".dropdown-main-btn").TextContent.Trim());

        // Click active filter clear button
        clearBtn.Click();

        Assert.Equal(3, cut.FindAll(".actor-card").Count);
        Assert.Empty(cut.FindAll(".dropdown-clear-btn"));
        Assert.StartsWith("Filter", cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).TextContent);
        Assert.DoesNotContain("Filter (", cut.FindAll("button").Single(b => b.TextContent.StartsWith("Filter")).TextContent);
    }

    [Fact]
    public void ResetFilter_WithSearchInput_ResetsTextFilter()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Mikami Yua", 5, true),
            new(2, "Remu", 2, false),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();
        var searchInput = cut.Find("input[type='search']");
        searchInput.Input("rem");

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".actor-card")), TimeSpan.FromSeconds(2));
        var clearBtn = cut.Find(".dropdown-clear-btn");
        Assert.Equal("×", clearBtn.TextContent.Trim());

        clearBtn.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll(".actor-card").Count);
            Assert.Empty(cut.FindAll(".dropdown-clear-btn"));
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SortingOptions_SortByName_MovieCount_ImageCount_AndRecentlyAdded_WithDirectionToggles()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Aoi Sora", 1, true, CreatedAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ImageCount: 2),
            new(2, "Mikami Yua", 10, true, CreatedAt: new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), ImageCount: 5),
            new(3, "Remu", 5, false, CreatedAt: new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc), ImageCount: 0),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();
        var sortDropdownBtn = cut.FindAll(".sort-dropdown-wrapper button")
            .First(b => b.TextContent.Trim().StartsWith("Sort:"));

        // Default: Name ascending (A-Z) -> Aoi Sora, Mikami Yua, Remu
        var cardsDefault = cut.FindAll(".actor-card .actor-name");
        Assert.Equal("Aoi Sora", cardsDefault[0].TextContent.Trim());
        Assert.Equal("Mikami Yua", cardsDefault[1].TextContent.Trim());
        Assert.Equal("Remu", cardsDefault[2].TextContent.Trim());

        // Click "Name" a second time -> toggles to descending (Z-A)
        sortDropdownBtn.Click();
        var sortItems = cut.FindAll(".sort-dropdown-item");
        sortItems.Single(b => b.TextContent.Contains("Name")).Click();

        var cards = cut.FindAll(".actor-card .actor-name");
        Assert.Equal("Remu", cards[0].TextContent.Trim());
        Assert.Equal("Mikami Yua", cards[1].TextContent.Trim());
        Assert.Equal("Aoi Sora", cards[2].TextContent.Trim());

        // Sort Movie Count (Highest first by default)
        sortDropdownBtn.Click();
        sortItems = cut.FindAll(".sort-dropdown-item");
        sortItems.Single(b => b.TextContent.Contains("Movie Count")).Click();

        cards = cut.FindAll(".actor-card .actor-name");
        Assert.Equal("Mikami Yua", cards[0].TextContent.Trim());
        Assert.Equal("Remu", cards[1].TextContent.Trim());
        Assert.Equal("Aoi Sora", cards[2].TextContent.Trim());

        // Click Movie Count a second time -> toggles to lowest first
        sortDropdownBtn.Click();
        sortItems = cut.FindAll(".sort-dropdown-item");
        sortItems.Single(b => b.TextContent.Contains("Movie Count")).Click();

        cards = cut.FindAll(".actor-card .actor-name");
        Assert.Equal("Aoi Sora", cards[0].TextContent.Trim());
        Assert.Equal("Remu", cards[1].TextContent.Trim());
        Assert.Equal("Mikami Yua", cards[2].TextContent.Trim());

        // Sort Image Count (Highest first by default)
        sortDropdownBtn.Click();
        sortItems = cut.FindAll(".sort-dropdown-item");
        sortItems.Single(b => b.TextContent.Contains("Image Count")).Click();

        cards = cut.FindAll(".actor-card .actor-name");
        Assert.Equal("Mikami Yua", cards[0].TextContent.Trim());
        Assert.Equal("Aoi Sora", cards[1].TextContent.Trim());
        Assert.Equal("Remu", cards[2].TextContent.Trim());

        // Sort Recently Added (Newest first by default)
        sortDropdownBtn.Click();
        sortItems = cut.FindAll(".sort-dropdown-item");
        sortItems.Single(b => b.TextContent.Contains("Recently Added")).Click();

        cards = cut.FindAll(".actor-card .actor-name");
        Assert.Equal("Mikami Yua", cards[0].TextContent.Trim());
        Assert.Equal("Remu", cards[1].TextContent.Trim());
        Assert.Equal("Aoi Sora", cards[2].TextContent.Trim());
    }

    [Fact]
    public void SortingByHeight_OrdersByHeightCm_WithMissingHeightAlwaysLast()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Aoi Sora", 1, true, HeightCm: 158),
            new(2, "Mikami Yua", 1, true, HeightCm: 165),
            new(3, "Remu", 1, true, HeightCm: null),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();
        var sortDropdownBtn = cut.FindAll(".sort-dropdown-wrapper button")
            .First(b => b.TextContent.Trim().StartsWith("Sort:"));

        // Sort Height (Tallest first by default) - missing height sorts last
        sortDropdownBtn.Click();
        var sortItems = cut.FindAll(".sort-dropdown-item");
        sortItems.Single(b => b.TextContent.Contains("Height")).Click();

        var cards = cut.FindAll(".actor-card .actor-name");
        Assert.Equal("Mikami Yua", cards[0].TextContent.Trim());
        Assert.Equal("Aoi Sora", cards[1].TextContent.Trim());
        Assert.Equal("Remu", cards[2].TextContent.Trim());

        // Click Height a second time -> toggles to shortest first, missing height still last
        sortDropdownBtn.Click();
        sortItems = cut.FindAll(".sort-dropdown-item");
        sortItems.Single(b => b.TextContent.Contains("Height")).Click();

        cards = cut.FindAll(".actor-card .actor-name");
        Assert.Equal("Aoi Sora", cards[0].TextContent.Trim());
        Assert.Equal("Mikami Yua", cards[1].TextContent.Trim());
        Assert.Equal("Remu", cards[2].TextContent.Trim());
    }

    [Fact]
    public void SortingByCupSize_OrdersByLengthThenValue_WithMissingCupSizeAlwaysLast()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Aoi Sora", 1, true, CupSize: "C"),
            new(2, "Mikami Yua", 1, true, CupSize: "AA"),
            new(3, "Remu", 1, true, CupSize: "B"),
            new(4, "Someone Else", 1, true, CupSize: null),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();
        var sortDropdownBtn = cut.FindAll(".sort-dropdown-wrapper button")
            .First(b => b.TextContent.Trim().StartsWith("Sort:"));

        // Sort Cup Size (largest first by default): two-letter "AA" ranks above single letters,
        // then "C" above "B" alphabetically; missing cup size sorts last.
        sortDropdownBtn.Click();
        var sortItems = cut.FindAll(".sort-dropdown-item");
        sortItems.Single(b => b.TextContent.Contains("Cup Size")).Click();

        var cards = cut.FindAll(".actor-card .actor-name");
        Assert.Equal("Mikami Yua", cards[0].TextContent.Trim());
        Assert.Equal("Aoi Sora", cards[1].TextContent.Trim());
        Assert.Equal("Remu", cards[2].TextContent.Trim());
        Assert.Equal("Someone Else", cards[3].TextContent.Trim());

        // Click Cup Size a second time -> toggles to smallest first, missing cup size still last
        sortDropdownBtn.Click();
        sortItems = cut.FindAll(".sort-dropdown-item");
        sortItems.Single(b => b.TextContent.Contains("Cup Size")).Click();

        cards = cut.FindAll(".actor-card .actor-name");
        Assert.Equal("Remu", cards[0].TextContent.Trim());
        Assert.Equal("Aoi Sora", cards[1].TextContent.Trim());
        Assert.Equal("Mikami Yua", cards[2].TextContent.Trim());
        Assert.Equal("Someone Else", cards[3].TextContent.Trim());
    }

    [Fact]
    public void SortingByHeight_ShowsHeightBadgeOnCards_ButNotForOtherSorts()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Aoi Sora", 1, true, HeightCm: 158),
            new(2, "Remu", 1, true, HeightCm: null),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        // Default sort (Name) - no sort-value badge on any card
        Assert.Empty(cut.FindAll(".actor-sort-value-pill"));

        var sortDropdownBtn = cut.FindAll(".sort-dropdown-wrapper button")
            .First(b => b.TextContent.Trim().StartsWith("Sort:"));
        sortDropdownBtn.Click();
        cut.FindAll(".sort-dropdown-item").Single(b => b.TextContent.Contains("Height")).Click();

        // Height badge shown only for the actor with a recorded height
        var badges = cut.FindAll(".actor-sort-value-pill");
        Assert.Single(badges);
        Assert.Equal("158 cm", badges[0].TextContent.Trim());

        // ...but every card reserves the line, so all cards stay the same height for the virtualized grid.
        Assert.Equal(2, cut.FindAll(".actor-card .actor-sort-value-row").Count);
    }

    [Fact]
    public void SortingByCupSize_ShowsCupSizeBadgeOnCards_ButNotForOtherSorts()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Aoi Sora", 1, true, CupSize: "D"),
            new(2, "Remu", 1, true, CupSize: null),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        // Default sort (Name) - no sort-value badge on any card
        Assert.Empty(cut.FindAll(".actor-sort-value-pill"));

        var sortDropdownBtn = cut.FindAll(".sort-dropdown-wrapper button")
            .First(b => b.TextContent.Trim().StartsWith("Sort:"));
        sortDropdownBtn.Click();
        cut.FindAll(".sort-dropdown-item").Single(b => b.TextContent.Contains("Cup Size")).Click();

        // Cup Size badge shown only for the actor with a recorded cup size
        var badges = cut.FindAll(".actor-sort-value-pill");
        Assert.Single(badges);
        Assert.Equal("Cup D", badges[0].TextContent.Trim());
    }

    [Fact]
    public void Virtualization_RendersInitialWindow_AndSpacersHoldDatasetAttributes()
    {
        var items = Enumerable.Range(0, 100)
            .Select(i => new ActorCardModel(i + 1, $"Actor {i:D2}", 1, false))
            .ToList();

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        var spacers = cut.FindAll(".virtualized-grid-spacer");
        Assert.Equal(2, spacers.Count);

        var leadingSpacer = spacers[0];
        Assert.Equal("0", leadingSpacer.GetAttribute("data-window-start"));
        Assert.Equal("60", leadingSpacer.GetAttribute("data-window-count"));
        Assert.Equal("100", leadingSpacer.GetAttribute("data-total-count"));

        var cards = cut.FindAll(".actor-card");
        Assert.Equal(60, cards.Count);
        Assert.Equal("Actor 00", cards[0].QuerySelector(".actor-name")?.TextContent.Trim());
        Assert.Equal("Actor 59", cards[59].QuerySelector(".actor-name")?.TextContent.Trim());
    }

    [Fact]
    public async Task Virtualization_SetVisibleRange_SlidesWindow_AndUpdatesSpacerAttributes()
    {
        var items = Enumerable.Range(0, 100)
            .Select(i => new ActorCardModel(i + 1, $"Actor {i:D2}", 1, false))
            .ToList();

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        await cut.InvokeAsync(() => cut.Instance.SetVisibleRange(20, 30, 4));

        var leadingSpacer = cut.FindAll(".virtualized-grid-spacer")[0];
        Assert.Equal("20", leadingSpacer.GetAttribute("data-window-start"));
        Assert.Equal("30", leadingSpacer.GetAttribute("data-window-count"));
        Assert.Equal("100", leadingSpacer.GetAttribute("data-total-count"));

        var cards = cut.FindAll(".actor-card");
        Assert.Equal(30, cards.Count);
        Assert.Equal("Actor 20", cards[0].QuerySelector(".actor-name")?.TextContent.Trim());
        Assert.Equal("Actor 49", cards[29].QuerySelector(".actor-name")?.TextContent.Trim());
    }

    [Fact]
    public async Task Virtualization_Filtering_ResetsWindowStart()
    {
        var items = Enumerable.Range(0, 100)
            .Select(i => new ActorCardModel(i + 1, $"Actor {i:D2}", i % 2 == 0 ? 5 : 0, false))
            .ToList();

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        await cut.InvokeAsync(() => cut.Instance.SetVisibleRange(40, 20, 4));

        var leadingSpacer = cut.FindAll(".virtualized-grid-spacer")[0];
        Assert.Equal("40", leadingSpacer.GetAttribute("data-window-start"));

        // Toggle a filter (e.g. Has Movies)
        ToggleFilter(cut, "Has Movies");

        leadingSpacer = cut.FindAll(".virtualized-grid-spacer")[0];
        Assert.Equal("0", leadingSpacer.GetAttribute("data-window-start"));
        Assert.Equal("50", leadingSpacer.GetAttribute("data-total-count"));
    }

    [Fact]
    public void Virtualization_InitializesFromWindowStartCookie()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "javbuddy-actors-window-start=16";
        httpContextAccessor.HttpContext.Returns(httpContext);
        Services.AddSingleton(httpContextAccessor);

        var items = Enumerable.Range(0, 100)
            .Select(i => new ActorCardModel(i + 1, $"Actor {i:D2}", 1, false))
            .ToList();

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        var spacers = cut.FindAll(".virtualized-grid-spacer");
        Assert.Equal("16", spacers[0].GetAttribute("data-window-start"));

        var cards = cut.FindAll(".actor-card");
        Assert.Equal("Actor 16", cards[0].QuerySelector(".actor-name")?.TextContent.Trim());
    }

    [Fact]
    public void ViewStateCookie_RestoresFavoritesOnlyFilterOnFreshLoad()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "javbuddy-actors-view=" + Uri.EscapeDataString("""{"FavoritesOnly":true}""");
        httpContextAccessor.HttpContext.Returns(httpContext);
        Services.AddSingleton(httpContextAccessor);

        var items = new List<ActorCardModel>
        {
            new(1, "Mikami Yua", 5, true, IsFavorite: true),
            new(2, "Remu", 0, false, IsFavorite: false),
        };
        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        var cards = cut.FindAll(".actor-card");
        Assert.Single(cards);
        Assert.Contains("Mikami Yua", cards[0].TextContent);
    }

    [Fact]
    public void ChangingFilter_WritesViewStateCookie()
    {
        // Loose mode: the default Strict JSInterop throws on any unconfigured call, including the
        // "import" that loads the JS module — which OnAfterRenderAsync's catch-all silently
        // swallows, same graceful-degradation path real disconnected-circuit errors take. That's
        // fine for tests asserting server-side filtering only, but this test needs the resulting
        // module reference to actually invoke setJsonCookie on.
        JSInterop.Mode = JSRuntimeMode.Loose;

        var items = new List<ActorCardModel>
        {
            new(1, "Mikami Yua", 5, true, IsFavorite: true),
            new(2, "Remu", 0, false, IsFavorite: false),
        };
        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();
        ToggleFilter(cut, "Favorites only");

        cut.WaitForAssertion(
            () => Assert.Contains(JSInterop.Invocations, inv => inv.Identifier == "setJsonCookie" && inv.Arguments.Any(a => a?.ToString() == "javbuddy-actors-view")),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void CupSizeFilter_FiltersActors_AndUpdatesCounter()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Actor C", 1, false, CupSize: "C"),
            new(2, "Actor D", 1, false, CupSize: "D"),
            new(3, "Actor Unknown", 1, false, CupSize: null),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        Assert.Equal(3, cut.FindAll(".actor-card").Count);

        // Open Filter dropdown and expand Cup Size group
        EnsureFilterDropdownOpen(cut);
        var cupGroup = cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains("Cup Size"));
        cupGroup.Click();

        // Select "C"
        var cOption = cut.FindAll(".filter-chip").First(b => b.TextContent.Trim() == "C");
        cOption.Click();

        // Filter button should reflect (1)
        var filterBtn = cut.FindAll(".sort-dropdown-wrapper button")
            .First(b => b.TextContent.Trim().StartsWith("Filter"));
        Assert.Contains("(1)", filterBtn.TextContent);

        // Only Actor C is shown
        var cards = cut.FindAll(".actor-card");
        Assert.Single(cards);
        Assert.Contains("Actor C", cards[0].TextContent);

        // Select "D" as well
        EnsureFilterDropdownOpen(cut);
        var dOption = cut.FindAll(".filter-chip").First(b => b.TextContent.Trim() == "D");
        dOption.Click();

        // Filter button should reflect (2)
        filterBtn = cut.FindAll(".sort-dropdown-wrapper button")
            .First(b => b.TextContent.Trim().StartsWith("Filter"));
        Assert.Contains("(2)", filterBtn.TextContent);

        // Both Actor C and Actor D are shown
        cards = cut.FindAll(".actor-card");
        Assert.Equal(2, cards.Count);

        // Clear filters
        EnsureFilterDropdownOpen(cut);
        var clearBtn = cut.Find(".dropdown-clear-btn");
        clearBtn.Click();

        // All 3 actors return
        Assert.Equal(3, cut.FindAll(".actor-card").Count);
    }

    [Fact]
    public void HeightFilter_FiltersActors_AndUpdatesCounter()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Actor Short", 1, false, HeightCm: 145),
            new(2, "Actor Tall", 1, false, HeightCm: 170),
            new(3, "Actor Unknown", 1, false, HeightCm: null),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        Assert.Equal(3, cut.FindAll(".actor-card").Count);

        // Open Filter dropdown and expand the Height group
        EnsureFilterDropdownOpen(cut);
        var heightGroup = cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains("Height"));
        heightGroup.Click();

        // Drag the max thumb down to 150 (excludes Actor Tall and the unknown-height actor)
        var maxThumb = cut.FindAll(".range-thumb-input")[1];
        maxThumb.Change("150");

        // Filter button should reflect (1)
        var filterBtn = cut.FindAll(".sort-dropdown-wrapper button")
            .First(b => b.TextContent.Trim().StartsWith("Filter"));
        Assert.Contains("(1)", filterBtn.TextContent);

        // Only Actor Short is shown
        var cards = cut.FindAll(".actor-card");
        Assert.Single(cards);
        Assert.Contains("Actor Short", cards[0].TextContent);

        // Clear filters
        EnsureFilterDropdownOpen(cut);
        var clearBtn = cut.Find(".dropdown-clear-btn");
        clearBtn.Click();

        // All 3 actors return
        Assert.Equal(3, cut.FindAll(".actor-card").Count);
    }

    [Fact]
    public void HeightFilter_HiddenWhenNoActorHasRecordedHeight()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Actor A", 1, false),
            new(2, "Actor B", 1, false),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        EnsureFilterDropdownOpen(cut);

        Assert.DoesNotContain(cut.FindAll(".sort-dropdown-item"), b => b.TextContent.Contains("Height"));
    }

    [Fact]
    public void StatusFilter_CyclesBetweenActiveAndRetired()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Active Actor", 5, true, IsRetired: false),
            new(2, "Retired Actor", 0, false, IsRetired: true),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        // Retired actor card has .actor-retired-pill
        Assert.NotNull(cut.Find(".actor-retired-pill"));

        // Click 1: All -> Active only (Active Actor only)
        ToggleFilter(cut, "Status: All");

        var cards = cut.FindAll(".actor-card .actor-name");
        Assert.Single(cards);
        Assert.Equal("Active Actor", cards[0].TextContent.Trim());

        // Click 2: Active only -> Retired only (Retired Actor only)
        ToggleFilter(cut, "Active only");

        cards = cut.FindAll(".actor-card .actor-name");
        Assert.Single(cards);
        Assert.Contains("Retired Actor", cards[0].TextContent);

        // Click 3: Retired only -> All (both actors)
        ToggleFilter(cut, "Retired only");

        Assert.Equal(2, cut.FindAll(".actor-card").Count);
    }

    [Fact]
    public void ActorName_RendersNameText_AndRetiredPillWhenRetired()
    {
        var items = new List<ActorCardModel>
        {
            new(1, "Active Actor", 5, true, IsRetired: false),
            new(2, "Retired Actor", 3, false, IsRetired: true),
        };

        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(items);

        var cut = Render<Actors>();

        var cards = cut.FindAll(".actor-card");
        Assert.Equal(2, cards.Count);

        // Active actor has .actor-name-text but no .actor-retired-pill
        var activeName = cards[0].QuerySelector(".actor-name");
        Assert.NotNull(activeName);
        var activeNameText = activeName.QuerySelector(".actor-name-text");
        Assert.NotNull(activeNameText);
        Assert.Equal("Active Actor", activeNameText.TextContent.Trim());
        Assert.Null(activeName.QuerySelector(".actor-retired-pill"));

        // Retired actor has .actor-name-text and .actor-retired-pill
        var retiredName = cards[1].QuerySelector(".actor-name");
        Assert.NotNull(retiredName);
        var retiredNameText = retiredName.QuerySelector(".actor-name-text");
        Assert.NotNull(retiredNameText);
        Assert.Equal("Retired Actor", retiredNameText.TextContent.Trim());
        var pill = retiredName.QuerySelector(".actor-retired-pill");
        Assert.NotNull(pill);
        Assert.Equal("Retired", pill.TextContent.Trim());
    }

    [Fact]
    public void OptionsModal_CardSizeSlider_ResizesTheGrid_AndIsOnlyOfferedInGridView()
    {
        actorService.GetActorCardsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ActorCardModel> { new(1, "Mikami Yua", 1, true) });

        var cut = Render<Actors>();
        Assert.Empty(cut.FindAll("input[type=range]"));
        Assert.Contains("size-2", cut.Find(".actor-grid").ClassList);

        cut.FindAll("button").Single(b => b.TextContent == "Options").Click();
        Assert.Contains("Actor Options", cut.Find(".options-modal-title").TextContent);

        var slider = cut.Find("#actor-card-size");
        Assert.Equal("2", slider.GetAttribute("value"));
        slider.Input("0");
        Assert.Contains("size-0", cut.Find(".actor-grid").ClassList);

        cut.FindAll("button").Single(b => b.TextContent == "Close").Click();
        Assert.Empty(cut.FindAll(".options-modal-panel"));

        cut.FindAll("button").Single(b => b.TextContent == "Table").Click();
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent == "Options");
    }
}
