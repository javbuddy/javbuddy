using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Common;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class ActorPhotoGalleryTests : InteractiveBunitContext
{
    public ActorPhotoGalleryTests()
    {
        JSInterop.SetupModule("./Components/Shared/ImageZoomControls.razor.js").Mode = JSRuntimeMode.Loose;
        JSInterop.AllowModalShell();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void UncategorizedFilter_IsShownOnlyWhenPhotosAreUncategorized(int uncategorizedPhotoCount, bool shouldShowFilter)
    {
        var service = Substitute.For<IActorPhotoService>();
        var photos = Enumerable.Range(1, uncategorizedPhotoCount)
            .Select(id => new ActorPhotoSummary(id, null, DateTime.UtcNow))
            .ToList();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(new ActorPhotoGalleryData([], photos));
        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));

        Assert.Equal(shouldShowFilter, cut.FindAll(".actor-photo-album-pill")
            .Any(button => button.TextContent.Contains("Uncategorized")));
    }

    [Fact]
    public void SelectingAnAlbum_FiltersTheVisibleGalleryPhotos()
    {
        var service = Substitute.For<IActorPhotoService>();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(new ActorPhotoGalleryData(
            [new ActorPhotoAlbumSummary(3, "Events", 1)],
            [new ActorPhotoSummary(11, 3, DateTime.UtcNow), new ActorPhotoSummary(12, null, DateTime.UtcNow.AddMinutes(-1))]));
        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".actor-photo-tile").Count));

        cut.FindAll(".actor-photo-album-pill").Single(button => button.TextContent.Contains("Events")).Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".actor-photo-tile")));
        Assert.Contains("/actor-photo/11/thumb", cut.Find(".actor-photo-tile-img").GetAttribute("data-src"));
    }

    [Fact]
    public void SelectingAnAlbum_ShowsItsPhotosInUploadOrder()
    {
        var service = Substitute.For<IActorPhotoService>();
        var uploadedAt = DateTime.UtcNow;
        // The service returns newest first; a batch can share a timestamp, so Id breaks the tie.
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(new ActorPhotoGalleryData(
            [new ActorPhotoAlbumSummary(3, "Photobook", 4)],
            [
                new ActorPhotoSummary(14, 3, uploadedAt.AddSeconds(2)),
                new ActorPhotoSummary(13, 3, uploadedAt),
                new ActorPhotoSummary(12, 3, uploadedAt),
                new ActorPhotoSummary(11, 3, uploadedAt.AddSeconds(-2)),
            ]));
        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll(".actor-photo-tile").Count));
        Assert.Equal(
            ["/actor-photo/14/thumb", "/actor-photo/13/thumb", "/actor-photo/12/thumb", "/actor-photo/11/thumb"],
            cut.FindAll(".actor-photo-tile-img").Select(img => img.GetAttribute("data-src")));

        cut.FindAll(".actor-photo-album-pill").Single(button => button.TextContent.Contains("Photobook")).Click();

        cut.WaitForAssertion(() => Assert.Equal(
            ["/actor-photo/11/thumb", "/actor-photo/12/thumb", "/actor-photo/13/thumb", "/actor-photo/14/thumb"],
            cut.FindAll(".actor-photo-tile-img").Select(img => img.GetAttribute("data-src"))));
    }

    [Fact]
    public void ClickingTheActiveAlbumAgain_DeselectsItAndShowsAllPhotos()
    {
        var service = Substitute.For<IActorPhotoService>();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(new ActorPhotoGalleryData(
            [new ActorPhotoAlbumSummary(3, "Events", 1)],
            [new ActorPhotoSummary(11, 3, DateTime.UtcNow), new ActorPhotoSummary(12, null, DateTime.UtcNow.AddMinutes(-1))]));
        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".actor-photo-tile").Count));

        var eventsPill = cut.FindAll(".actor-photo-album-pill").Single(button => button.TextContent.Contains("Events"));
        eventsPill.Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".actor-photo-tile")));

        cut.FindAll(".actor-photo-album-pill").Single(button => button.TextContent.Contains("Events")).Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".actor-photo-tile").Count));
        Assert.Contains("active", cut.FindAll(".actor-photo-album-pill").Single(button => button.TextContent.StartsWith("All")).ClassList);
    }

    [Fact]
    public void LargeGallery_StartsCollapsedAndCanBeExpanded()
    {
        var service = Substitute.For<IActorPhotoService>();
        var photos = Enumerable.Range(1, 100)
            .Select(id => new ActorPhotoSummary(id, null, DateTime.UtcNow))
            .ToList();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(new ActorPhotoGalleryData([], photos));
        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));
        cut.WaitForAssertion(() => Assert.Equal("Show all 100 photos", cut.Find(".actor-photo-gallery-expand").TextContent.Trim()));
        Assert.DoesNotContain("actor-photo-gallery-expanded", cut.Find(".actor-photo-gallery").ClassList);

        cut.Find(".actor-photo-gallery-expand").Click();

        cut.WaitForAssertion(() => Assert.Equal("Show fewer photos", cut.Find(".actor-photo-gallery-expand").TextContent.Trim()));
        Assert.Contains("actor-photo-gallery-expanded", cut.Find(".actor-photo-gallery").ClassList);
    }

    [Fact]
    public void SwitchingAlbums_KeepsAnExpandedGalleryExpanded()
    {
        var service = Substitute.For<IActorPhotoService>();
        var photos = Enumerable.Range(1, 6)
            .Select(id => new ActorPhotoSummary(id, 3, DateTime.UtcNow))
            .Concat(Enumerable.Range(7, 6)
                .Select(id => new ActorPhotoSummary(id, 4, DateTime.UtcNow)))
            .ToList();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(new ActorPhotoGalleryData(
            [new ActorPhotoAlbumSummary(3, "Events", 6), new ActorPhotoAlbumSummary(4, "Portraits", 6)],
            photos));
        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));
        cut.WaitForAssertion(() => Assert.Equal("Show all 12 photos", cut.Find(".actor-photo-gallery-expand").TextContent.Trim()));

        cut.Find(".actor-photo-gallery-expand").Click();
        cut.FindAll(".actor-photo-album-pill").Single(button => button.TextContent.Contains("Events")).Click();

        cut.WaitForAssertion(() => Assert.Equal("Show fewer photos", cut.Find(".actor-photo-gallery-expand").TextContent.Trim()));
        Assert.Contains("actor-photo-gallery-expanded", cut.Find(".actor-photo-gallery").ClassList);
    }

    [Fact]
    public void JustifiedGrid_PreservesNaturalAspectRatioOnTiles()
    {
        var service = Substitute.For<IActorPhotoService>();
        var photos = new List<ActorPhotoSummary>
        {
            new(101, null, DateTime.UtcNow, AspectRatio: 0.6667),
            new(102, null, DateTime.UtcNow, AspectRatio: 1.5)
        };
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(new ActorPhotoGalleryData([], photos));
        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".actor-photo-tile").Count));
        var tiles = cut.FindAll(".actor-photo-tile");
        Assert.Contains("--ar: 0.6667;", tiles[0].GetAttribute("style"));
        Assert.Contains("--ar: 1.5;", tiles[1].GetAttribute("style"));
    }

    [Fact]
    public void ClickingPhotoTile_OpensLightbox_AndCanNavigateAndClose()
    {
        var service = Substitute.For<IActorPhotoService>();
        var photos = new List<ActorPhotoSummary>
        {
            new(101, null, DateTime.UtcNow, AspectRatio: 0.6667),
            new(102, null, DateTime.UtcNow, AspectRatio: 1.5)
        };
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(new ActorPhotoGalleryData([], photos));
        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".actor-photo-tile").Count));
        Assert.Empty(cut.FindAll(".gallery-lightbox"));

        // Click first photo tile to open lightbox
        cut.FindAll(".actor-photo-tile")[0].Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".gallery-lightbox")));
        Assert.Contains("/actor-photo/101/full", cut.Find(".gallery-image").GetAttribute("src"));
        Assert.Equal("1 / 2", cut.Find(".gallery-index").TextContent.Trim());

        // Navigate to next photo
        cut.Find(".gallery-nav-next").Click();
        cut.WaitForAssertion(() => Assert.Contains("/actor-photo/102/full", cut.Find(".gallery-image").GetAttribute("src")));
        Assert.Equal("2 / 2", cut.Find(".gallery-index").TextContent.Trim());

        // Navigate to previous photo
        cut.Find(".gallery-nav-prev").Click();
        cut.WaitForAssertion(() => Assert.Contains("/actor-photo/101/full", cut.Find(".gallery-image").GetAttribute("src")));

        // Close lightbox via close button
        cut.Find(".gallery-close-btn").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".gallery-lightbox")));
    }

    [Fact]
    public void Lightbox_OriginalToggle_IsHidden_WhenPhotoHasNoOriginal()
    {
        var service = Substitute.For<IActorPhotoService>();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(new ActorPhotoGalleryData(
            [], [new ActorPhotoSummary(101, null, DateTime.UtcNow, HasOriginal: false)]));
        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".actor-photo-tile")));

        cut.Find(".actor-photo-tile").Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".gallery-lightbox")));
        Assert.Empty(cut.FindAll(".gallery-original-toggle"));
    }

    [Fact]
    public void Lightbox_OriginalToggle_PersistsAcrossNavigation_UntilToggledOff()
    {
        var service = Substitute.For<IActorPhotoService>();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(new ActorPhotoGalleryData(
            [],
            [
                new ActorPhotoSummary(101, null, DateTime.UtcNow, HasOriginal: true),
                new ActorPhotoSummary(102, null, DateTime.UtcNow.AddMinutes(-1), HasOriginal: true),
            ]));
        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".actor-photo-tile").Count));

        cut.FindAll(".actor-photo-tile")[0].Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".gallery-lightbox")));
        Assert.Contains("/actor-photo/101/full", cut.Find(".gallery-image").GetAttribute("src"));

        cut.Find(".gallery-original-toggle").Click();
        Assert.Contains("/actor-photo/101/original", cut.Find(".gallery-image").GetAttribute("src"));
        Assert.Equal("true", cut.Find(".gallery-original-toggle").GetAttribute("aria-pressed"));

        cut.Find(".gallery-nav-next").Click();

        Assert.Contains("/actor-photo/102/original", cut.Find(".gallery-image").GetAttribute("src"));
        Assert.Equal("true", cut.Find(".gallery-original-toggle").GetAttribute("aria-pressed"));

        cut.Find(".gallery-original-toggle").Click();
        Assert.Contains("/actor-photo/102/full", cut.Find(".gallery-image").GetAttribute("src"));
    }

    [Fact]
    public void Lightbox_OriginalToggle_ResetsToTheWebPPreview_WhenClosedAndReopened()
    {
        var service = Substitute.For<IActorPhotoService>();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(new ActorPhotoGalleryData(
            [],
            [new ActorPhotoSummary(101, null, DateTime.UtcNow, HasOriginal: true)]));
        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".actor-photo-tile")));

        cut.Find(".actor-photo-tile").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".gallery-lightbox")));
        cut.Find(".gallery-original-toggle").Click();
        Assert.Equal("true", cut.Find(".gallery-original-toggle").GetAttribute("aria-pressed"));

        cut.Find(".gallery-close-btn").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".gallery-lightbox")));
        cut.Find(".actor-photo-tile").Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".gallery-lightbox")));
        Assert.Contains("/actor-photo/101/full", cut.Find(".gallery-image").GetAttribute("src"));
        Assert.Equal("false", cut.Find(".gallery-original-toggle").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task Lightbox_DeletePhoto_DeletesViaServiceAndClosesWhenEmpty()
    {
        var service = Substitute.For<IActorPhotoService>();
        var photos = new List<ActorPhotoSummary>
        {
            new(101, null, DateTime.UtcNow)
        };
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(
            _ => new ActorPhotoGalleryData([], photos.ToList()));
        service.DeletePhotoAsync(7, 101, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult.Ok()))
            .AndDoes(_ => photos.Clear());

        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".actor-photo-tile")));
        cut.Find(".actor-photo-tile").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".gallery-lightbox")));

        // Click Delete button in lightbox
        var deleteBtn = cut.FindAll(".gallery-topbar-actions button")
            .Single(b => b.TextContent.Trim() == "Delete");
        await cut.InvokeAsync(() => deleteBtn.Click());

        await service.Received(1).DeletePhotoAsync(7, 101, Arg.Any<CancellationToken>());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".gallery-lightbox")));
        Assert.Empty(cut.FindAll(".actor-photo-tile"));
    }

    [Fact]
    public void EmptyGallery_OpeningUploadModal_ShowsModalWithoutError()
    {
        var service = Substitute.For<IActorPhotoService>();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(new ActorPhotoGalleryData([], []));
        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));

        cut.WaitForAssertion(() => Assert.Contains("No photos in this selection yet.", cut.Markup));

        var addPhotosBtn = cut.FindAll(".actor-photo-gallery-header-actions button")
            .Single(b => b.TextContent.Trim() == "Add photos");
        addPhotosBtn.Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".modal-shell-panel")));
    }

    [Fact]
    public async Task UploadPhotosOnPreviouslyEmptyGallery_ImportsJsModuleAndObservesNewPhotos()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var service = Substitute.For<IActorPhotoService>();
        var photos = new List<ActorPhotoSummary>();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(_ => new ActorPhotoGalleryData([], photos.ToList()));
        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));

        cut.WaitForAssertion(() => Assert.Contains("No photos in this selection yet.", cut.Markup));
        Assert.DoesNotContain(JSInterop.Invocations, inv => inv.Identifier == "import" && inv.Arguments.Any(a => a?.ToString() == "./Components/Shared/ActorPhotoGallery.razor.js"));

        // Simulate uploading 10 photos
        photos.AddRange(Enumerable.Range(1, 10).Select(id => new ActorPhotoSummary(id, null, DateTime.UtcNow)));
        var uploadModal = cut.FindComponent<ActorPhotoUploadModal>();
        await cut.InvokeAsync(() => uploadModal.Instance.OnUploaded.InvokeAsync());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(10, cut.FindAll(".actor-photo-tile").Count);
            Assert.Contains(JSInterop.Invocations, inv => inv.Identifier == "import" && inv.Arguments.Any(a => a?.ToString() == "./Components/Shared/ActorPhotoGallery.razor.js"));
        });

        // Click "Show all 10 photos"
        var expandBtn = cut.Find(".actor-photo-gallery-expand");
        Assert.Equal("Show all 10 photos", expandBtn.TextContent.Trim());
        expandBtn.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("actor-photo-gallery-expanded", cut.Find(".actor-photo-gallery").ClassList);
            Assert.Equal("Show fewer photos", cut.Find(".actor-photo-gallery-expand").TextContent.Trim());
        });
    }

    private IRenderedComponent<ActorPhotoGallery> RenderLargeGallery(int photoCount, int albumPhotoCount = 0)
    {
        var service = Substitute.For<IActorPhotoService>();
        var photos = Enumerable.Range(1, photoCount)
            .Select(id => new ActorPhotoSummary(id, id <= albumPhotoCount ? 3 : null, DateTime.UtcNow))
            .ToList();
        var albums = albumPhotoCount > 0 ? [new ActorPhotoAlbumSummary(3, "Events", albumPhotoCount)] : new List<ActorPhotoAlbumSummary>();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(new ActorPhotoGalleryData(albums, photos));
        Services.AddSingleton(service);
        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".actor-photo-tile")));
        return cut;
    }

    private static int SentinelGeneration(IRenderedComponent<ActorPhotoGallery> cut) =>
        int.Parse(cut.Find(".actor-photo-gallery-sentinel").GetAttribute("data-generation")!);

    [Fact]
    public void LargeGallery_RendersOnlyTheFirstBatchOfTiles_WithTheFullCountOnTheButton()
    {
        var cut = RenderLargeGallery(400);

        Assert.Equal(150, cut.FindAll(".actor-photo-tile").Count);
        Assert.Equal("Show all 400 photos", cut.Find(".actor-photo-gallery-expand").TextContent.Trim());
        Assert.Empty(cut.FindAll(".actor-photo-gallery-sentinel"));
    }

    [Fact]
    public void Expanded_LoadMorePhotos_GrowsByABatchUntilEverythingIsRendered()
    {
        var cut = RenderLargeGallery(400);
        cut.Find(".actor-photo-gallery-expand").Click();
        Assert.Equal(150, cut.FindAll(".actor-photo-tile").Count);

        cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(SentinelGeneration(cut)));
        Assert.Equal(300, cut.FindAll(".actor-photo-tile").Count);

        cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(SentinelGeneration(cut)));
        Assert.Equal(400, cut.FindAll(".actor-photo-tile").Count);
        Assert.Empty(cut.FindAll(".actor-photo-gallery-sentinel"));
    }

    [Fact]
    public void LoadMorePhotos_IgnoresAStaleGeneration_AndACollapsedGallery()
    {
        var cut = RenderLargeGallery(400);
        cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(0));
        Assert.Equal(150, cut.FindAll(".actor-photo-tile").Count);

        cut.Find(".actor-photo-gallery-expand").Click();
        var generation = SentinelGeneration(cut);
        cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(generation - 1));
        Assert.Equal(150, cut.FindAll(".actor-photo-tile").Count);

        cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(generation));
        Assert.Equal(300, cut.FindAll(".actor-photo-tile").Count);
    }

    [Fact]
    public void ExpandedAlbum_RendersWhole_WhileAllAndCollapsingStartTheBatchesOver()
    {
        var cut = RenderLargeGallery(400, albumPhotoCount: 200);
        cut.Find(".actor-photo-gallery-expand").Click();
        cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(SentinelGeneration(cut)));
        Assert.Equal(300, cut.FindAll(".actor-photo-tile").Count);

        // An album opened in the expanded view renders all its photos at once, with nothing left to load.
        cut.FindAll(".actor-photo-album-pill").Single(button => button.TextContent.Contains("Events")).Click();
        Assert.Equal(200, cut.FindAll(".actor-photo-tile").Count);
        Assert.Empty(cut.FindAll(".actor-photo-gallery-sentinel"));
        Assert.Equal("Show fewer photos", cut.Find(".actor-photo-gallery-expand").TextContent.Trim());

        // Back to All: the batches start over.
        cut.FindAll(".actor-photo-album-pill").Single(button => button.TextContent.StartsWith("All")).Click();
        Assert.Equal(150, cut.FindAll(".actor-photo-tile").Count);
        Assert.NotEmpty(cut.FindAll(".actor-photo-gallery-sentinel"));

        cut.FindAll(".actor-photo-album-pill").Single(button => button.TextContent.Contains("Events")).Click();
        cut.Find(".actor-photo-gallery-expand").Click();
        Assert.Equal(150, cut.FindAll(".actor-photo-tile").Count);
        Assert.Equal("Show all 200 photos", cut.Find(".actor-photo-gallery-expand").TextContent.Trim());
    }

    [Fact]
    public void Lightbox_NavigatesPastTheRenderedTiles_ToAnyPhotoOfTheFilteredList()
    {
        var cut = RenderLargeGallery(400);

        cut.FindAll(".actor-photo-tile")[149].Click();
        Assert.Equal("150 / 400", cut.Find(".gallery-index").TextContent.Trim());

        cut.Find(".gallery-nav-next").Click();

        Assert.Equal("151 / 400", cut.Find(".gallery-index").TextContent.Trim());
        Assert.Contains("/actor-photo/151/full", cut.Find(".gallery-image").GetAttribute("src"));
        Assert.Equal(150, cut.FindAll(".actor-photo-tile").Count);
    }

    private sealed record InitialView(int ActorId, List<ActorPhotoAlbumSummary> Albums, List<ActorPhotoSummary> Photos, int TotalCount, int UncategorizedCount);

    private static List<ActorPhotoSummary> Photos(int count, int albumPhotoCount = 0) =>
        Enumerable.Range(1, count).Select(id => new ActorPhotoSummary(id, id <= albumPhotoCount ? 3 : null, new DateTime(2026, 1, 1))).ToList();

    [Fact]
    public void WhenNotInteractive_PrerendersTheFirstBatch_WithTheEagerThumbnailScript()
    {
        Interactive = false;
        var service = Substitute.For<IActorPhotoService>();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(new ActorPhotoGalleryData([], Photos(400)));
        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters
            .Add(component => component.ActorId, 7)
            .Add(component => component.ActorName, "Mikami Yua"));

        Assert.Equal(150, cut.FindAll(".actor-photo-tile").Count);
        Assert.Empty(cut.FindAll(".actor-photo-skeleton-tile"));
        Assert.Single(cut.FindAll("script"));
        Assert.Equal("Show all 400 photos", cut.Find(".actor-photo-gallery-expand").TextContent.Trim());
    }

    [Fact]
    public void TheFirstTiles_CarryTheirThumbnailSrc_TheRestOnlyDataSrc()
    {
        var cut = RenderLargeGallery(400);

        var images = cut.FindAll(".actor-photo-tile-img");
        Assert.Equal("/actor-photo/1/thumb", images[0].GetAttribute("src"));
        Assert.Equal("/actor-photo/60/thumb", images[59].GetAttribute("src"));
        Assert.Null(images[60].GetAttribute("src"));
        Assert.Equal("/actor-photo/61/thumb", images[60].GetAttribute("data-src"));
    }

    [Fact]
    public void Interactive_DoesNotRenderTheInlineScript()
    {
        var cut = RenderLargeGallery(400);

        Assert.Empty(cut.FindAll("script"));
    }

    [Fact]
    public void WhenNotInteractive_PersistsTheFirstBatchAndTheCounts_ForTheCircuit()
    {
        Interactive = false;
        var state = AddBunitPersistentComponentState();
        var albums = new List<ActorPhotoAlbumSummary> { new(3, "Events", 20) };
        var service = Substitute.For<IActorPhotoService>();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(new ActorPhotoGalleryData(albums, Photos(400, albumPhotoCount: 20)));
        Services.AddSingleton(service);
        Render<ActorPhotoGallery>(parameters => parameters.Add(component => component.ActorId, 7));

        state.TriggerOnPersisting();

        Assert.True(state.TryTake<InitialView>("actor-photo-gallery", out var persisted));
        Assert.Equal(7, persisted!.ActorId);
        Assert.Equal(150, persisted.Photos.Count);
        Assert.Equal(400, persisted.TotalCount);
        Assert.Equal(380, persisted.UncategorizedCount);
        Assert.Equal("Events", Assert.Single(persisted.Albums).Name);
    }

    [Fact]
    public void Interactive_WithTheSnapshot_RendersItAtOnce_ThenSwapsInTheFullList()
    {
        var state = AddBunitPersistentComponentState();
        var albums = new List<ActorPhotoAlbumSummary> { new(3, "Events", 20) };
        var all = Photos(400, albumPhotoCount: 20);
        var full = new TaskCompletionSource<ActorPhotoGalleryData>();
        var service = Substitute.For<IActorPhotoService>();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(full.Task);
        Services.AddSingleton(service);
        state.Persist("actor-photo-gallery", new InitialView(7, albums, all.Take(150).ToList(), 400, 380));

        var cut = Render<ActorPhotoGallery>(parameters => parameters.Add(component => component.ActorId, 7));

        // No skeleton while the full list loads: the snapshot's tiles and counts are on screen.
        Assert.Empty(cut.FindAll(".actor-photo-skeleton-tile"));
        Assert.Equal(150, cut.FindAll(".actor-photo-tile").Count);
        Assert.Equal("(400)", cut.Find(".actor-photo-count").TextContent.Trim());
        Assert.Contains(cut.FindAll(".actor-photo-album-pill"), pill => pill.TextContent.Trim() == "Uncategorized (380)");
        Assert.Equal("Show all 400 photos", cut.Find(".actor-photo-gallery-expand").TextContent.Trim());

        cut.InvokeAsync(() => full.SetResult(new ActorPhotoGalleryData(albums, all)));

        cut.Find(".actor-photo-gallery-expand").Click();
        cut.InvokeAsync(() => cut.Instance.LoadMorePhotos(SentinelGeneration(cut)));
        Assert.Equal(300, cut.FindAll(".actor-photo-tile").Count);
    }

    [Fact]
    public void Interactive_WithoutASnapshot_ShowsTheSkeletonUntilThePhotosLoad()
    {
        var full = new TaskCompletionSource<ActorPhotoGalleryData>();
        var service = Substitute.For<IActorPhotoService>();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(full.Task);
        Services.AddSingleton(service);

        var cut = Render<ActorPhotoGallery>(parameters => parameters.Add(component => component.ActorId, 7));
        Assert.NotEmpty(cut.FindAll(".actor-photo-skeleton-tile"));

        cut.InvokeAsync(() => full.SetResult(new ActorPhotoGalleryData([], Photos(10))));

        cut.WaitForAssertion(() => Assert.Equal(10, cut.FindAll(".actor-photo-tile").Count));
        Assert.Empty(cut.FindAll(".actor-photo-skeleton-tile"));
    }

    [Fact]
    public void Interactive_ASnapshotForAnotherActor_IsIgnored()
    {
        var state = AddBunitPersistentComponentState();
        var full = new TaskCompletionSource<ActorPhotoGalleryData>();
        var service = Substitute.For<IActorPhotoService>();
        service.GetGalleryAsync(7, Arg.Any<CancellationToken>()).Returns(full.Task);
        Services.AddSingleton(service);
        state.Persist("actor-photo-gallery", new InitialView(99, [], Photos(5), 5, 5));

        var cut = Render<ActorPhotoGallery>(parameters => parameters.Add(component => component.ActorId, 7));

        Assert.NotEmpty(cut.FindAll(".actor-photo-skeleton-tile"));
        Assert.Empty(cut.FindAll(".actor-photo-tile"));
    }
}
