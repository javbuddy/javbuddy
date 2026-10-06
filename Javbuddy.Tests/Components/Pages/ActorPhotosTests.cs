using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Common;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class ActorPhotosTests : BunitContext
{
    private readonly IActorPhotoService photoService = Substitute.For<IActorPhotoService>();

    public ActorPhotosTests()
    {
        JSInterop.SetupModule("./Components/Shared/ImageZoomControls.razor.js").Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(photoService);
    }

    [Fact]
    public void EmptyLibrary_ShowsNoPhotosMessage()
    {
        photoService.GetGlobalPhotosAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<GlobalActorPhotoItem>());
        photoService.GetActorsWithPhotosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ActorWithPhotoCountSummary>());

        var cut = Render<ActorPhotos>();

        Assert.Contains("No actor photos uploaded yet", cut.Markup);
        Assert.Empty(cut.FindAll(".actor-photo-tile"));
    }

    [Fact]
    public void PhotosPresent_RendersTimelineSectionsAndJustifiedTiles()
    {
        var photos = new List<GlobalActorPhotoItem>
        {
            new(1, 101, "Mikami Yua", 10, "Summer Gravure", 1.5, new DateTime(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc)),
            new(2, 101, "Mikami Yua", null, null, 0.75, new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc)),
            new(3, 102, "Fukada Eimi", null, null, 1.33, new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc)),
        };

        var actors = new List<ActorWithPhotoCountSummary>
        {
            new(102, "Fukada Eimi", 1),
            new(101, "Mikami Yua", 2)
        };

        photoService.GetGlobalPhotosAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(photos);
        photoService.GetActorsWithPhotosAsync(Arg.Any<CancellationToken>())
            .Returns(actors);

        var cut = Render<ActorPhotos>();

        // Check timeline headings
        var headings = cut.FindAll(".actor-photos-timeline-heading");
        Assert.Equal(2, headings.Count);
        Assert.Contains("September 2026", headings[0].TextContent);
        Assert.Contains("August 2026", headings[1].TextContent);

        // Check justified photo tiles
        var tiles = cut.FindAll(".actor-photo-tile");
        Assert.Equal(3, tiles.Count);

        // Check aspect ratio style and image data-src for proximity loading
        var firstTile = tiles[0];
        Assert.Contains("--ar: 1.5", firstTile.GetAttribute("style"));
        var firstImg = firstTile.QuerySelector("img");
        Assert.NotNull(firstImg);
        Assert.Equal("/actor-photo/1/thumb", firstImg.GetAttribute("data-src"));
        Assert.Null(firstImg.GetAttribute("src"));

        // Check actor overlay attribution
        Assert.Contains("Mikami Yua", firstTile.TextContent);
        Assert.Contains("Summer Gravure", firstTile.TextContent);

        // Second tile has no album
        var secondTile = tiles[1];
        Assert.Contains("--ar: 0.75", secondTile.GetAttribute("style"));
        Assert.Null(secondTile.QuerySelector(".actor-photo-tile-album"));
    }

    [Fact]
    public void FilterBySearchText_UpdatesVisiblePhotos()
    {
        var photos = new List<GlobalActorPhotoItem>
        {
            new(1, 101, "Mikami Yua", 10, "Summer Gravure", 1.5, DateTime.UtcNow),
            new(2, 102, "Fukada Eimi", null, null, 1.33, DateTime.UtcNow),
        };

        photoService.GetGlobalPhotosAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(photos);
        photoService.GetActorsWithPhotosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ActorWithPhotoCountSummary>());

        var cut = Render<ActorPhotos>();
        Assert.Equal(2, cut.FindAll(".actor-photo-tile").Count);

        var searchInput = cut.Find("input[type='search']");
        searchInput.Input("Fukada");

        var filteredTiles = cut.FindAll(".actor-photo-tile");
        Assert.Single(filteredTiles);
        Assert.Contains("Fukada Eimi", filteredTiles[0].TextContent);

        // Search for non-matching term
        searchInput.Input("NonExistentActor");
        Assert.Empty(cut.FindAll(".actor-photo-tile"));
        Assert.Contains("No photos match your current filters", cut.Markup);

        // Click Reset filters
        cut.Find("button.btn-link").Click();
        Assert.Equal(2, cut.FindAll(".actor-photo-tile").Count);
    }

    [Fact]
    public void ClickingPhoto_OpensLightbox_AndHandlesNextPrevClose()
    {
        var photos = new List<GlobalActorPhotoItem>
        {
            new(1, 101, "Mikami Yua", 10, "Summer Gravure", 1.5, DateTime.UtcNow.AddMinutes(-5)),
            new(2, 102, "Fukada Eimi", null, null, 1.33, DateTime.UtcNow),
        };

        photoService.GetGlobalPhotosAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(photos);
        photoService.GetActorsWithPhotosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ActorWithPhotoCountSummary>());

        var cut = Render<ActorPhotos>();
        Assert.Empty(cut.FindAll(".gallery-lightbox"));

        // Click first rendered tile (newest first, which is ID 2)
        cut.FindAll(".actor-photo-tile")[0].Click();

        var lightbox = cut.Find(".gallery-lightbox");
        Assert.NotNull(lightbox);
        var lightboxImg = cut.Find(".gallery-image");
        Assert.Equal("/actor-photo/2/full", lightboxImg.GetAttribute("src"));

        // Check progress bar is rendered
        var segments = cut.FindAll(".gallery-progress-segment");
        Assert.Equal(2, segments.Count);
        Assert.Contains("is-current", segments[0].GetAttribute("class"));

        // Click Next button -> wraps to ID 1
        cut.Find(".gallery-nav-next").Click();
        Assert.Equal("/actor-photo/1/full", cut.Find(".gallery-image").GetAttribute("src"));
        Assert.Contains("is-current", cut.FindAll(".gallery-progress-segment")[1].GetAttribute("class"));

        // Click first segment -> jumps back to ID 2
        cut.FindAll(".gallery-progress-segment")[0].Click();
        Assert.Equal("/actor-photo/2/full", cut.Find(".gallery-image").GetAttribute("src"));

        // Click Prev button -> wraps back to ID 1
        cut.Find(".gallery-nav-prev").Click();
        Assert.Equal("/actor-photo/1/full", cut.Find(".gallery-image").GetAttribute("src"));

        // Click Close button
        cut.Find(".gallery-close-btn").Click();
        Assert.Empty(cut.FindAll(".gallery-lightbox"));
    }

    [Fact]
    public void Lightbox_LinksActorFromTitleOnly()
    {
        var photos = new List<GlobalActorPhotoItem>
        {
            new(1, 101, "Mikami Yua", null, null, 1.5, DateTime.UtcNow)
        };

        photoService.GetGlobalPhotosAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(photos);
        photoService.GetActorsWithPhotosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ActorWithPhotoCountSummary>());

        var cut = Render<ActorPhotos>();
        cut.Find(".actor-photo-tile").Click();

        Assert.Equal("/actors/Mikami%20Yua", cut.Find(".gallery-actor-link").GetAttribute("href"));
        Assert.Empty(cut.FindAll(".gallery-topbar-actions a"));
        Assert.Single(cut.FindAll(".gallery-topbar-actions button.btn-outline-danger"));
    }

    [Fact]
    public async Task DeleteInLightbox_CallsService_AndRemovesPhoto()
    {
        var photos = new List<GlobalActorPhotoItem>
        {
            new(1, 101, "Mikami Yua", null, null, 1.5, DateTime.UtcNow)
        };

        photoService.GetGlobalPhotosAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(photos);
        photoService.GetActorsWithPhotosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ActorWithPhotoCountSummary>());
        photoService.DeletePhotoAsync(101, 1, Arg.Any<CancellationToken>())
            .Returns(ActorPhotoOperationResult.Ok());

        var cut = Render<ActorPhotos>();
        cut.Find(".actor-photo-tile").Click();

        // Click Delete inside lightbox
        var deleteBtn = cut.Find(".gallery-topbar-actions button.btn-outline-danger");
        await cut.InvokeAsync(() => deleteBtn.Click());

        await photoService.Received(1).DeletePhotoAsync(101, 1, Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll(".gallery-lightbox"));
        Assert.Empty(cut.FindAll(".actor-photo-tile"));
    }

    [Fact]
    public void OriginalToggle_IsHidden_WhenPhotoHasNoOriginal()
    {
        var photos = new List<GlobalActorPhotoItem>
        {
            new(1, 101, "Mikami Yua", null, null, 1.5, DateTime.UtcNow, HasOriginal: false)
        };
        photoService.GetGlobalPhotosAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(photos);
        photoService.GetActorsWithPhotosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ActorWithPhotoCountSummary>());

        var cut = Render<ActorPhotos>();
        cut.Find(".actor-photo-tile").Click();

        Assert.Single(cut.FindAll(".gallery-lightbox"));
        Assert.Empty(cut.FindAll(".gallery-original-toggle"));
    }

    [Fact]
    public void OriginalToggle_PersistsAcrossNavigation_UntilToggledOffOrClosed()
    {
        var photos = new List<GlobalActorPhotoItem>
        {
            new(1, 101, "Mikami Yua", null, null, 1.5, DateTime.UtcNow.AddMinutes(-5), HasOriginal: true),
            new(2, 102, "Fukada Eimi", null, null, 1.33, DateTime.UtcNow, HasOriginal: true),
        };
        photoService.GetGlobalPhotosAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(photos);
        photoService.GetActorsWithPhotosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ActorWithPhotoCountSummary>());

        var cut = Render<ActorPhotos>();
        // Newest first, so tile 0 is photo ID 2.
        cut.FindAll(".actor-photo-tile")[0].Click();
        Assert.Equal("/actor-photo/2/full", cut.Find(".gallery-image").GetAttribute("src"));

        cut.Find(".gallery-original-toggle").Click();
        Assert.Equal("/actor-photo/2/original", cut.Find(".gallery-image").GetAttribute("src"));
        Assert.Equal("true", cut.Find(".gallery-original-toggle").GetAttribute("aria-pressed"));

        // Original mode persists across navigation.
        cut.Find(".gallery-nav-next").Click();
        Assert.Equal("/actor-photo/1/original", cut.Find(".gallery-image").GetAttribute("src"));
        Assert.Equal("true", cut.Find(".gallery-original-toggle").GetAttribute("aria-pressed"));

        // Closing and reopening the lightbox resets it.
        cut.Find(".gallery-close-btn").Click();
        cut.FindAll(".actor-photo-tile")[0].Click();
        Assert.Equal("/actor-photo/2/full", cut.Find(".gallery-image").GetAttribute("src"));
        Assert.Equal("false", cut.Find(".gallery-original-toggle").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void SortDropdown_ChangesSorting_AndReordersPhotos()
    {
        var now = DateTime.UtcNow;
        var photos = new List<GlobalActorPhotoItem>
        {
            new(1, 101, "Mikami Yua", null, null, 1.5, now.AddHours(-2)),
            new(2, 102, "Fukada Eimi", null, null, 1.33, now.AddHours(-1)),
            new(3, 103, "Hashimoto Arina", null, null, 1.0, now),
        };

        photoService.GetGlobalPhotosAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(photos);
        photoService.GetActorsWithPhotosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ActorWithPhotoCountSummary>());

        var cut = Render<ActorPhotos>();

        // Default: Newest first -> ID 3, ID 2, ID 1
        var initialTiles = cut.FindAll(".actor-photo-tile > img");
        Assert.Equal("/actor-photo/3/thumb", initialTiles[0].GetAttribute("data-src"));
        Assert.Equal("/actor-photo/2/thumb", initialTiles[1].GetAttribute("data-src"));
        Assert.Equal("/actor-photo/1/thumb", initialTiles[2].GetAttribute("data-src"));

        // Open Sort dropdown
        var sortBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Sort: Newest"));
        sortBtn.Click();

        // Click "Oldest" button in menu
        var oldestBtn = cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains("Oldest"));
        oldestBtn.Click();

        // Check button label updated
        Assert.Contains("Sort: Oldest", cut.FindAll("button").First(b => b.TextContent.Contains("Sort:")).TextContent);

        // Tiles re-ordered to oldest first -> ID 1, ID 2, ID 3
        var oldestTiles = cut.FindAll(".actor-photo-tile > img");
        Assert.Equal("/actor-photo/1/thumb", oldestTiles[0].GetAttribute("data-src"));
        Assert.Equal("/actor-photo/2/thumb", oldestTiles[1].GetAttribute("data-src"));
        Assert.Equal("/actor-photo/3/thumb", oldestTiles[2].GetAttribute("data-src"));

        // Open Sort dropdown again
        cut.FindAll("button").First(b => b.TextContent.Contains("Sort: Oldest")).Click();

        // Click "Actor Name"
        var actorBtn = cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains("Actor Name"));
        actorBtn.Click();

        Assert.Contains("Sort: Actor Name", cut.FindAll("button").First(b => b.TextContent.Contains("Sort:")).TextContent);
        var actorHeadings = cut.FindAll(".actor-photos-timeline-heading");
        Assert.Equal(3, actorHeadings.Count);
        Assert.Contains("Fukada Eimi", actorHeadings[0].TextContent);
        Assert.Contains("Hashimoto Arina", actorHeadings[1].TextContent);
        Assert.Contains("Mikami Yua", actorHeadings[2].TextContent);

        // Open Sort dropdown again and click "Random"
        cut.FindAll("button").First(b => b.TextContent.Contains("Sort: Actor Name")).Click();
        var randomBtn = cut.FindAll(".sort-dropdown-item").First(b => b.TextContent.Contains("Random"));
        randomBtn.Click();

        Assert.Contains("Sort: Random", cut.FindAll("button").First(b => b.TextContent.Contains("Sort:")).TextContent);
        var randomHeading = Assert.Single(cut.FindAll(".actor-photos-timeline-heading"));
        Assert.Contains("All Photos", randomHeading.TextContent);
        Assert.Equal(3, cut.FindAll(".actor-photo-tile").Count);
    }

    [Fact]
    public void PhotoTiles_UseProximityLoading_DoesNotEagerlySetSrc()
    {
        var photos = new List<GlobalActorPhotoItem>
        {
            new(1, 101, "Mikami Yua", 10, "Summer Gravure", 1.5, DateTime.UtcNow),
            new(2, 102, "Fukada Eimi", null, null, 1.33, DateTime.UtcNow)
        };

        photoService.GetGlobalPhotosAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(photos);
        photoService.GetActorsWithPhotosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ActorWithPhotoCountSummary>());

        var cut = Render<ActorPhotos>();
        var tiles = cut.FindAll(".actor-photo-tile");
        Assert.Equal(2, tiles.Count);

        foreach (var tile in tiles)
        {
            var photoImg = tile.QuerySelector(".actor-photo-tile-img");
            Assert.NotNull(photoImg);
            Assert.Null(photoImg.GetAttribute("src"));
            Assert.NotNull(photoImg.GetAttribute("data-src"));

            var avatarImg = tile.QuerySelector(".actor-photo-tile-avatar");
            Assert.NotNull(avatarImg);
            Assert.Null(avatarImg.GetAttribute("src"));
            Assert.NotNull(avatarImg.GetAttribute("data-src"));
        }
    }

    [Fact]
    public void LargeCollection_GroupsLightboxPictureBarIntoSegments()
    {
        var photos = Enumerable.Range(1, 250)
            .Select(i => new GlobalActorPhotoItem(i, 101, "Mikami Yua", null, null, 1.0, DateTime.UtcNow.AddMinutes(-i)))
            .ToList();

        photoService.GetGlobalPhotosAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(photos);
        photoService.GetActorsWithPhotosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ActorWithPhotoCountSummary>());

        var cut = Render<ActorPhotos>();
        cut.FindAll(".actor-photo-tile")[50].Click();

        var segments = cut.FindAll(".gallery-progress-segment");
        Assert.Equal(100, segments.Count);

        // Click a segment to jump
        segments[10].Click();
        var currentImg = cut.Find(".gallery-image");
        Assert.NotNull(currentImg.GetAttribute("src"));
    }

    [Fact]
    public async Task LargeCollection_LoadsOneGlobalBatchAcrossGroups_UntilComplete()
    {
        SetUpLargeCollection();
        var cut = Render<ActorPhotos>();

        Assert.Contains("250", cut.Find("h1").TextContent);
        Assert.Equal(120, cut.FindAll(".actor-photo-tile").Count);
        var sections = cut.FindAll(".actor-photos-timeline-section");
        Assert.Equal(2, sections.Count);
        Assert.Equal(80, sections[0].QuerySelectorAll(".actor-photo-tile").Length);
        Assert.Equal(40, sections[1].QuerySelectorAll(".actor-photo-tile").Length);
        Assert.Equal("(170)", sections[1].QuerySelector(".actor-photos-timeline-count")!.TextContent);

        var generation = GetGridGeneration(cut);
        await cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(generation));
        Assert.Equal(240, cut.FindAll(".actor-photo-tile").Count);
        await cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(generation));
        Assert.Equal(250, cut.FindAll(".actor-photo-tile").Count);
        await cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(generation));
        var sources = cut.FindAll(".actor-photo-tile-img").Select(image => image.GetAttribute("data-src")).ToList();
        Assert.Equal(250, sources.Count);
        Assert.Equal(250, sources.Distinct().Count());
        Assert.Equal("/actor-photo/250/thumb", sources[^1]);
    }

    [Fact]
    public void Lightbox_NavigatesBeyondRenderedBatch_AndWrapsAcrossFullCollection()
    {
        SetUpLargeCollection();
        var cut = Render<ActorPhotos>();
        cut.FindAll(".actor-photo-tile")[119].Click();

        cut.Find(".gallery-nav-next").Click();
        Assert.Equal("/actor-photo/121/full", cut.Find(".gallery-image").GetAttribute("src"));
        Assert.Equal("121 / 250", cut.Find(".gallery-index").TextContent);
        Assert.Equal(120, cut.FindAll(".actor-photo-tile").Count);

        cut.FindAll(".gallery-progress-segment")[0].Click();
        cut.Find(".gallery-nav-prev").Click();
        Assert.Equal("/actor-photo/250/full", cut.Find(".gallery-image").GetAttribute("src"));
        cut.Find(".gallery-nav-next").Click();
        Assert.Equal("/actor-photo/1/full", cut.Find(".gallery-image").GetAttribute("src"));
    }

    [Fact]
    public async Task Search_FindsUnrenderedPhotos_AndResetRestoresInitialBatch()
    {
        SetUpLargeCollection();
        var cut = Render<ActorPhotos>();
        await cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(GetGridGeneration(cut)));
        Assert.Equal(240, cut.FindAll(".actor-photo-tile").Count);

        cut.Find("input[type='search']").Input("Hidden Album");
        Assert.Single(cut.FindAll(".actor-photo-tile"));
        Assert.Equal("/actor-photo/250/thumb", cut.Find(".actor-photo-tile-img").GetAttribute("data-src"));

        cut.Find("input[type='search']").Input("No matching album");
        cut.Find("button.btn-link").Click();
        Assert.Equal(120, cut.FindAll(".actor-photo-tile").Count);
        Assert.Equal("/actor-photo/1/thumb", cut.Find(".actor-photo-tile-img").GetAttribute("data-src"));
    }

    [Fact]
    public async Task Sort_ResetsBatch_AndIgnoresCallbackFromPreviousGeneration()
    {
        SetUpLargeCollection();
        var cut = Render<ActorPhotos>();
        var initialGeneration = GetGridGeneration(cut);
        await cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(initialGeneration));

        cut.FindAll("button").First(button => button.TextContent.Contains("Sort: Newest")).Click();
        cut.FindAll(".sort-dropdown-item").First(button => button.TextContent.Contains("Oldest")).Click();
        Assert.Equal(120, cut.FindAll(".actor-photo-tile").Count);
        Assert.Equal("/actor-photo/250/thumb", cut.Find(".actor-photo-tile-img").GetAttribute("data-src"));

        await cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(initialGeneration));
        Assert.Equal(120, cut.FindAll(".actor-photo-tile").Count);
        await cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(GetGridGeneration(cut)));
        Assert.Equal(240, cut.FindAll(".actor-photo-tile").Count);
    }

    [Fact]
    public async Task DeleteAfterLoadingMore_RefreshesGroupsAndLightbox_AndPreservesBatch()
    {
        SetUpLargeCollection();
        photoService.DeletePhotoAsync(101, 1, Arg.Any<CancellationToken>())
            .Returns(ActorPhotoOperationResult.Ok());
        var cut = Render<ActorPhotos>();
        await cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(GetGridGeneration(cut)));
        cut.Find(".actor-photo-tile").Click();

        await cut.InvokeAsync(() => cut.Find(".gallery-topbar-actions button.btn-outline-danger").Click());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("249", cut.Find("h1").TextContent);
            Assert.Equal(240, cut.FindAll(".actor-photo-tile").Count);
            Assert.Empty(cut.FindAll(".actor-photo-tile-img[data-src='/actor-photo/1/thumb']"));
            Assert.Equal("(79)", cut.Find(".actor-photos-timeline-count").TextContent);
            Assert.Equal("/actor-photo/2/full", cut.Find(".gallery-image").GetAttribute("src"));
            Assert.Equal("1 / 249", cut.Find(".gallery-index").TextContent);
        });
        await photoService.Received(1).DeletePhotoAsync(101, 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LayoutMetricsChanged_RendersPlaceholder_SizedForUnrenderedTail()
    {
        SetUpLargeCollection();
        var cut = Render<ActorPhotos>();

        // 900px container, 4 ar=1.0 tiles fit per row (see JustifiedGridLayoutTests) -
        // the truncated August group has 170 - 40 = 130 unrendered photos left -> 33 rows.
        await cut.InvokeAsync(() => cut.Instance.OnLayoutMetricsChanged(900, 60));

        var placeholder = cut.Find(".actor-photos-placeholder");
        var expectedHeight = 33 * (JustifiedGridLayout.TileHeight + JustifiedGridLayout.TileGap);
        Assert.Contains($"height: {expectedHeight:0}px", placeholder.GetAttribute("style"));
    }

    [Fact]
    public async Task LayoutMetricsChanged_AddsSectionOverhead_ForFullyUnrenderedGroups()
    {
        var september = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        var photos = Enumerable.Range(1, 200)
            .Select(i => new GlobalActorPhotoItem(i, 101, "Mikami Yua", null, null, 1.0,
                (i <= 50 ? september : i <= 100 ? september.AddMonths(-1) : i <= 150 ? september.AddMonths(-2) : september.AddMonths(-3))
                    .AddMinutes(-i)))
            .ToList();
        photoService.GetGlobalPhotosAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(photos);
        photoService.GetActorsWithPhotosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ActorWithPhotoCountSummary>());

        var cut = Render<ActorPhotos>();
        // Sept(50) + Aug(50) fully rendered (100), Jul gets 20 of its 50 (visiblePhotoCount=120),
        // leaving a 30-photo tail in Jul plus the entirely-unrendered June group (50 photos).
        await cut.InvokeAsync(() => cut.Instance.OnLayoutMetricsChanged(900, 60));

        var placeholder = cut.Find(".actor-photos-placeholder");
        var julTailRows = 8; // ceil(30 / 4)
        var juneRows = 13; // ceil(50 / 4)
        var expectedHeight = julTailRows * (JustifiedGridLayout.TileHeight + JustifiedGridLayout.TileGap)
            + 60 // June's section overhead - its heading isn't rendered.
            + juneRows * (JustifiedGridLayout.TileHeight + JustifiedGridLayout.TileGap);
        Assert.Contains($"height: {expectedHeight:0}px", placeholder.GetAttribute("style"));
    }

    [Fact]
    public async Task LayoutMetricsChanged_NoPlaceholder_OnceEntireCollectionIsLoaded()
    {
        SetUpLargeCollection();
        var cut = Render<ActorPhotos>();
        var generation = GetGridGeneration(cut);
        await cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(generation));
        await cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(generation));
        await cut.InvokeAsync(() => cut.Instance.OnLayoutMetricsChanged(900, 60));

        Assert.Equal(250, cut.FindAll(".actor-photo-tile").Count);
        Assert.Empty(cut.FindAll(".actor-photos-placeholder"));
    }

    private void SetUpLargeCollection()
    {
        var september = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        var photos = Enumerable.Range(1, 250)
            .Select(i => new GlobalActorPhotoItem(i, 101, "Mikami Yua", i == 250 ? 10 : null,
                i == 250 ? "Hidden Album" : null, 1.0,
                (i <= 80 ? september : september.AddMonths(-1)).AddMinutes(-i)))
            .ToList();
        photoService.GetGlobalPhotosAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(photos);
        photoService.GetActorsWithPhotosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ActorWithPhotoCountSummary>());
    }

    private static int GetGridGeneration(IRenderedComponent<ActorPhotos> cut) =>
        int.Parse(cut.Find(".actor-photos-timeline").GetAttribute("data-generation")!, System.Globalization.CultureInfo.InvariantCulture);
}

