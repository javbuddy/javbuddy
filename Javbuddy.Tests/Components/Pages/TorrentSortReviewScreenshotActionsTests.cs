using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Tags;
using Javbuddy.Services.Torrents;
using Javbuddy.Services.Torrents.SortWizard;
using Javbuddy.Services.VrMerge;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

/// <summary>Covers TorrentSort.razor's Review step screenshot hover-icon actions
/// The poster/fanart/delete buttons
/// rendered via MovieGallery's ThumbOverlay/ThumbBadge parameters, wired to
/// TorrentSortWizardState.SetPosterFromScreenshotAsync/SetCoverFromScreenshotAsync/
/// RemoveScreenshotAsync.</summary>
public class TorrentSortReviewScreenshotActionsTests : BunitContext
{
    private (TestDbContextFactory Factory, int TorrentDownloadId, ITorrentSortService TorrentSortService, TorrentSortWizardState State)
        SetUpServices(MovieViewDto movie)
    {
        var factory = new TestDbContextFactory();
        int torrentDownloadId;
        using (var db = factory.CreateDbContext())
        {
            var dbMovie = new Movie { Code = "ABC-123" };
            db.Movies.Add(dbMovie);
            db.SaveChanges();

            var torrent = new TorrentDownload { MovieId = dbMovie.Id, MovieCode = "ABC-123" };
            db.TorrentDownloads.Add(torrent);
            db.SaveChanges();
            torrentDownloadId = torrent.Id;
        }

        var torrentSortService = Substitute.For<ITorrentSortService>();
        torrentSortService.GetJavinizerBaseUrlAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
        // RefreshJobDataAsync (called after a successful screenshot-action PATCH) re-polls with
        // includeData: true — stub it to just echo back the same job so the action's success
        // path completes without needing to model a whole re-scrape round trip.
        torrentSortService.PollJobAsync(torrentDownloadId, true, Arg.Any<CancellationToken>())
            .Returns(_ => new JavinizerBatchJobResult(true, new BatchJobResponseDto
            {
                Id = "job-123",
                Results = new Dictionary<string, BatchFileResultDto>
                {
                    ["res-1"] = new() { ResultId = "res-1", FilePath = "/path/1.mp4", Status = JavinizerJobStatus.Completed, Movie = movie }
                }
            }, null));

        torrentSortService.GetResultSourcesAsync(default, default!, default).ReturnsForAnyArgs(new JavinizerSourcesResult(true, [], null));
        torrentSortService.GetScrapersAsync(default).ReturnsForAnyArgs(new JavinizerScrapersResult(true, [], null));
        torrentSortService.PreviewWithEditsAsync(default, default!, default!, default!, default)
            .ReturnsForAnyArgs(new JavinizerOrganizePreviewResult(true, new OrganizePreviewResponseDto(), null));

        var pathMappingService = Substitute.For<IPathMappingService>();
        pathMappingService.TranslateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<string>());

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        var vrMergeService = Substitute.For<IVrMergeService>();
        var vrMergeJobTracker = Substitute.For<IVrMergeJobTracker>();
        var ffmpegResolver = Substitute.For<IFfmpegBinaryResolver>();
        ffmpegResolver.IsAvailable.Returns(false);

        var lookup = Substitute.For<ISortEditorLookupService>();
        lookup.MatchTrackedActorsAsync(Arg.Any<IReadOnlyList<ActressViewDto>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IReadOnlyList<ActressViewDto>>().Select(_ => (TrackedActorMatch?)null).ToList());
        lookup.ResolveGenresAsync(Arg.Any<IEnumerable<string?>>(), Arg.Any<CancellationToken>()).Returns([]);
        lookup.GetTopTagsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        Services.AddSingleton(lookup);
        Services.AddSingleton(Substitute.For<IActorService>());
        Services.AddSingleton(Substitute.For<ITagService>());

        var state = new TorrentSortWizardState(
            torrentSortService, pathMappingService, localLibraryClient, factory,
            new ConfigurationBuilder().Build(), vrMergeService, vrMergeJobTracker, ffmpegResolver,
            sortEditorLookup: lookup);

        // Seed JobData/Step directly (mirrors TorrentSortWizardStateTests' reflection-based
        // JobData seeding) rather than driving InitializeAsync's full job-status resolution,
        // which isn't what this test is exercising.
        state.Session.JobData = new BatchJobResponseDto
        {
            Id = "job-123",
            Results = new Dictionary<string, BatchFileResultDto>
            {
                ["res-1"] = new() { ResultId = "res-1", FilePath = "/path/1.mp4", Status = JavinizerJobStatus.Completed, Movie = movie }
            }
        };
        state.Step = WizardStep.Review;

        Services.AddSingleton(state);

        return (factory, torrentDownloadId, torrentSortService, state);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(null, true)]
    public void PosterPreview_RespectsImageSelectionInBothSteps(bool? shouldCrop, bool uncropped)
    {
        var movie = new MovieViewDto
        {
            Id = "ABC-123",
            PosterUrl = "poster.jpg",
            ShouldCropPoster = shouldCrop,
            OriginalPosterUrl = uncropped ? "default.jpg" : "poster.jpg",
            CroppedPosterUrl = "cached-crop.jpg"
        };
        var (factory, torrentDownloadId, _, state) = SetUpServices(movie);
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));
        var poster = cut.Find(".sort-movie-poster-img");
        Assert.Equal("poster.jpg", poster.GetAttribute("src"));
        Assert.Equal(uncropped, poster.ClassList.Contains("sort-poster-uncropped"));

        state.Preview.Previews["res-1"] = new OrganizePreviewResponseDto { FolderName = "ABC-123" };
        state.Step = WizardStep.Preview;
        cut.Render();

        var thumbnail = cut.Find(".sort-preview-thumb");
        Assert.Equal("poster.jpg", thumbnail.GetAttribute("src"));
        Assert.Equal(uncropped, thumbnail.ClassList.Contains("sort-poster-uncropped"));
    }

    [Fact]
    public void ReviewStep_MatchedMovieWithScreenshots_RendersThreeActionButtonsPerThumbnail()
    {
        var movie = new MovieViewDto { Id = "ABC-123", PosterUrl = "shot1.jpg", ScreenshotUrls = ["shot1.jpg", "shot2.jpg"] };
        var (factory, torrentDownloadId, _, _) = SetUpServices(movie);
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        Assert.Equal(2, cut.FindAll(".sort-screenshot-action-poster").Count);
        Assert.Equal(2, cut.FindAll(".sort-screenshot-action-fanart").Count);
        Assert.Equal(2, cut.FindAll(".sort-screenshot-action-delete").Count);

        // The gallery always displays the last-built tile first, so with
        // two screenshots the display order is [shot2, shot1] — PosterUrl (shot1.jpg) is tile [1].
        Assert.Contains("is-active", cut.FindAll(".sort-screenshot-action-poster")[1].ClassList);
        Assert.Single(cut.FindAll(".sort-screenshot-badge-poster"));
    }

    [Fact]
    public void ReviewStep_ScreenshotIsBothPosterAndCover_ShowsBothBadgeIcons()
    {
        var movie = new MovieViewDto
        {
            Id = "ABC-123",
            PosterUrl = "shot1.jpg",
            CoverUrl = "shot1.jpg", // same screenshot serves as both — must show both badges, not just one
            ScreenshotUrls = ["shot1.jpg", "shot2.jpg"]
        };
        var (factory, torrentDownloadId, _, _) = SetUpServices(movie);
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        var badgeGroups = cut.FindAll(".sort-screenshot-badges");
        Assert.Single(badgeGroups);
        Assert.NotNull(badgeGroups[0].QuerySelector(".sort-screenshot-badge-poster"));
        Assert.NotNull(badgeGroups[0].QuerySelector(".sort-screenshot-badge-fanart"));

        // Badges are icon-only now (no text label) — always visible, not hover-gated.
        Assert.NotNull(badgeGroups[0].QuerySelector(".sort-screenshot-badge-poster svg"));
        Assert.DoesNotContain("poster</span>", cut.Markup);
    }

    [Fact]
    public async Task ClickingPosterAction_CallsSetPosterFromUrlAsync()
    {
        // follow-up: poster picks now go through javinizer-go's dedicated
        // poster-from-url endpoint, not the generic whole-movie PATCH — confirmed against a real
        // javinizer-go instance that the generic PATCH leaves CroppedPosterUrl stale, which is what
        // javinizer-go's own review UI actually displays.
        var movie = new MovieViewDto { Id = "ABC-123", ScreenshotUrls = ["shot1.jpg", "shot2.jpg"] };
        var (factory, torrentDownloadId, sortService, _) = SetUpServices(movie);
        using var f = factory;

        sortService.SetPosterFromUrlAsync(torrentDownloadId, "res-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new JavinizerPosterFromUrlResult(true, null));

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));
        // Display order is [shot2, shot1] — the last-built tile always leads.
        cut.FindAll(".sort-screenshot-action-poster")[0].Click();

        await sortService.Received(1).SetPosterFromUrlAsync(torrentDownloadId, "res-1", "shot2.jpg", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClickingFanartAction_CallsSetCoverFromScreenshotAsync()
    {
        var movie = new MovieViewDto { Id = "ABC-123", ScreenshotUrls = ["shot1.jpg", "shot2.jpg"] };
        var (factory, torrentDownloadId, sortService, _) = SetUpServices(movie);
        using var f = factory;

        sortService.UpdateResultAsync(torrentDownloadId, "res-1", Arg.Any<MovieViewDto>(), Arg.Any<CancellationToken>())
            .Returns(new JavinizerUpdateResult(true, null));

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));
        // Display order is [shot2, shot1] — tile [1] is shot1.jpg.
        cut.FindAll(".sort-screenshot-action-fanart")[1].Click();

        await sortService.Received(1).UpdateResultAsync(
            torrentDownloadId, "res-1",
            Arg.Is<MovieViewDto>(m => m.CoverUrl == "shot1.jpg"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClickingDeleteAction_CallsRemoveScreenshotAsync()
    {
        var movie = new MovieViewDto { Id = "ABC-123", ScreenshotUrls = ["shot1.jpg", "shot2.jpg"] };
        var (factory, torrentDownloadId, sortService, _) = SetUpServices(movie);
        using var f = factory;

        sortService.UpdateResultAsync(torrentDownloadId, "res-1", Arg.Any<MovieViewDto>(), Arg.Any<CancellationToken>())
            .Returns(new JavinizerUpdateResult(true, null));

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));
        // Display order is [shot2, shot1] — tile [0] is shot2.jpg.
        cut.FindAll(".sort-screenshot-action-delete")[0].Click();

        await sortService.Received(1).UpdateResultAsync(
            torrentDownloadId, "res-1",
            Arg.Is<MovieViewDto>(m => m.ScreenshotUrls != null && !m.ScreenshotUrls.Contains("shot2.jpg") && m.ScreenshotUrls.Contains("shot1.jpg")),
            Arg.Any<CancellationToken>());
    }

    // The Review-step gallery also appends the movie's original (pre-edit) poster/cover
    // as pickable tiles, deduped against ScreenshotUrls and against each other, with the Delete
    // hover-icon shown only for real screenshot tiles. It also always displays whichever tile ends up
    // last in the built list first (display-order only — no data/PATCH change).

    [Fact]
    public void ReviewStep_OriginalPosterAndCoverNotInScreenshots_AppendsTwoExtraTiles()
    {
        var movie = new MovieViewDto
        {
            Id = "ABC-123",
            ScreenshotUrls = ["shot1.jpg", "shot2.jpg"],
            OriginalPosterUrl = "original-poster.jpg",
            OriginalCoverUrl = "original-cover.jpg"
        };
        var (factory, torrentDownloadId, _, _) = SetUpServices(movie);
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        Assert.Equal(movie.ScreenshotUrls.Count + 2, cut.FindAll("img.fanart-thumb").Count);
    }

    [Fact]
    public void ReviewStep_OriginalPosterAlreadyInScreenshots_IsNotDuplicated()
    {
        var movie = new MovieViewDto
        {
            Id = "ABC-123",
            ScreenshotUrls = ["shot1.jpg", "shot2.jpg"],
            OriginalPosterUrl = "shot1.jpg", // already present — should not add a duplicate tile
            OriginalCoverUrl = "original-cover.jpg"
        };
        var (factory, torrentDownloadId, _, _) = SetUpServices(movie);
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        // Only OriginalCoverUrl is genuinely new.
        Assert.Equal(movie.ScreenshotUrls.Count + 1, cut.FindAll("img.fanart-thumb").Count);
    }

    [Fact]
    public void ReviewStep_OriginalCoverAlreadyInScreenshots_IsNotDuplicated()
    {
        var movie = new MovieViewDto
        {
            Id = "ABC-123",
            ScreenshotUrls = ["shot1.jpg", "shot2.jpg"],
            OriginalPosterUrl = "original-poster.jpg",
            OriginalCoverUrl = "shot2.jpg" // already present — should not add a duplicate tile
        };
        var (factory, torrentDownloadId, _, _) = SetUpServices(movie);
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        // Only OriginalPosterUrl is genuinely new.
        Assert.Equal(movie.ScreenshotUrls.Count + 1, cut.FindAll("img.fanart-thumb").Count);
    }

    [Fact]
    public void ReviewStep_OriginalCoverEqualsOriginalPoster_OnlyOneExtraTileAppended()
    {
        var movie = new MovieViewDto
        {
            Id = "ABC-123",
            ScreenshotUrls = ["shot1.jpg", "shot2.jpg"],
            OriginalPosterUrl = "original.jpg",
            OriginalCoverUrl = "original.jpg" // same as OriginalPosterUrl — must not append twice
        };
        var (factory, torrentDownloadId, _, _) = SetUpServices(movie);
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        Assert.Equal(movie.ScreenshotUrls.Count + 1, cut.FindAll("img.fanart-thumb").Count);
    }

    [Fact]
    public void ReviewStep_DeleteButton_AbsentOnAppendedOriginalTiles_PresentOnRealScreenshots()
    {
        var movie = new MovieViewDto
        {
            Id = "ABC-123",
            ScreenshotUrls = ["shot1.jpg", "shot2.jpg"],
            OriginalPosterUrl = "original-poster.jpg",
            OriginalCoverUrl = "original-cover.jpg"
        };
        var (factory, torrentDownloadId, _, _) = SetUpServices(movie);
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        // Only the two real screenshot tiles get a Delete icon — the two appended "original" tiles don't.
        Assert.Equal(movie.ScreenshotUrls.Count, cut.FindAll(".sort-screenshot-action-delete").Count);
    }

    [Fact]
    public void ReviewStep_LastScreenshot_IsMovedToFirstPosition_ForDisplayOnly()
    {
        var movie = new MovieViewDto
        {
            Id = "ABC-123",
            ScreenshotUrls = ["shot1.jpg", "shot2.jpg", "shot3.jpg"],
            // CoverUrl deliberately points elsewhere — the reorder is positional (last tile always
            // leads), not based on which screenshot is currently the fanart.
            CoverUrl = "shot1.jpg"
        };
        var (factory, torrentDownloadId, _, _) = SetUpServices(movie);
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        var thumbs = cut.FindAll("img.fanart-thumb");
        Assert.Equal(3, thumbs.Count);
        Assert.Equal("shot3.jpg", thumbs[0].GetAttribute("src"));
        Assert.Equal("shot1.jpg", thumbs[1].GetAttribute("src"));
        Assert.Equal("shot2.jpg", thumbs[2].GetAttribute("src"));

        // Purely a display reorder — every screenshot (including the reordered one) still counts
        // as a real screenshot, so all three still get a Delete icon.
        Assert.Equal(3, cut.FindAll(".sort-screenshot-action-delete").Count);
    }

    [Fact]
    public void ReviewStep_LastTileIsAnAppendedOriginal_IsMovedToFirstPosition_WithNoDeleteIcon()
    {
        var movie = new MovieViewDto
        {
            Id = "ABC-123",
            ScreenshotUrls = ["shot1.jpg", "shot2.jpg"],
            OriginalPosterUrl = "original-poster.jpg",
            OriginalCoverUrl = "original-cover.jpg" // last tile appended, by BuildGalleryUrls' order
        };
        var (factory, torrentDownloadId, _, _) = SetUpServices(movie);
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        var thumbs = cut.FindAll("img.fanart-thumb");
        Assert.Equal(4, thumbs.Count);
        Assert.Equal("original-cover.jpg", thumbs[0].GetAttribute("src"));
        Assert.Equal("shot1.jpg", thumbs[1].GetAttribute("src"));
        Assert.Equal("shot2.jpg", thumbs[2].GetAttribute("src"));
        Assert.Equal("original-poster.jpg", thumbs[3].GetAttribute("src"));

        // Real-screenshot classification is membership-based, not positional — the appended
        // original still has no Delete icon even after moving to the front.
        Assert.Equal(2, cut.FindAll(".sort-screenshot-action-delete").Count);
    }

    [Fact]
    public void ReviewStep_NoScreenshots_ButOriginalPosterAndCoverPresent_GalleryStillRenders()
    {
        var movie = new MovieViewDto
        {
            Id = "ABC-123",
            ScreenshotUrls = null,
            OriginalPosterUrl = "original-poster.jpg",
            OriginalCoverUrl = "original-cover.jpg"
        };
        var (factory, torrentDownloadId, _, _) = SetUpServices(movie);
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        Assert.Equal(2, cut.FindAll("img.fanart-thumb").Count);
        Assert.Empty(cut.FindAll(".sort-screenshot-action-delete"));
        Assert.Contains("sort-movie-gallery-section", cut.Markup);
    }

    [Fact]
    public void EditMetadata_OpensWideDrawerWithEditor()
    {
        var movie = new MovieViewDto { Id = "ABC-123", Title = "T" };
        var (factory, torrentDownloadId, _, _) = SetUpServices(movie);
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));
        cut.FindAll("button").First(b => b.TextContent.Contains("Edit metadata")).Click();

        Assert.Contains("slide-over-drawer-panel-wide", cut.Find(".slide-over-drawer-panel").ClassList);
        Assert.NotNull(cut.Find(".sme-save"));
        Assert.Empty(cut.FindAll(".modal-dialog"));
    }

    [Fact]
    public void ReviewCard_ShowsQualityBadgeCount()
    {
        var movie = new MovieViewDto { Id = "ABC-123", Title = "Real", ReleaseYear = 2020 };
        var (factory, torrentDownloadId, _, _) = SetUpServices(movie);
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        var badge = cut.Find(".sort-quality-badge");
        Assert.Contains("2", badge.TextContent);
        Assert.Contains("No maker", badge.GetAttribute("title"));
    }

    [Fact]
    public void ReviewCard_CompleteMovie_HasNoQualityBadge()
    {
        var movie = new MovieViewDto { Id = "ABC-123", Title = "Real", ReleaseYear = 2020, Maker = "S1", Actresses = [new() { FirstName = "Yui" }] };
        var (factory, torrentDownloadId, _, _) = SetUpServices(movie);
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        Assert.Empty(cut.FindAll(".sort-quality-badge"));
    }
}
