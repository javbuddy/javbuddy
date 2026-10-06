using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Torrents;
using Javbuddy.Services.VrMerge;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

/// <summary>Covers TorrentSort.razor's Done step cleanup UI:
/// the cleanup button only appears when the torrent's qBittorrent hash is known (deleting via
/// qBittorrent is the only mechanism offered — a direct filesystem delete was deliberately left
/// out, since a torrent without its own dedicated subfolder can have ContentPath == SavePath, a
/// directory shared with other torrents, making a blind recursive delete unsafe), and the preview
/// → confirm/cancel flow driving the substituted ITorrentSortService.</summary>
public class TorrentSortDoneStepTests : BunitContext
{
    private (TestDbContextFactory Factory, int TorrentDownloadId, ITorrentSortService TorrentSortService)
        SetUpServices(string? hash, string? contentPath)
    {
        var factory = new TestDbContextFactory();
        int torrentDownloadId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();

            var torrent = new TorrentDownload { MovieId = movie.Id, MovieCode = "ABC-123", Hash = hash, ContentPath = contentPath };
            db.TorrentDownloads.Add(torrent);
            db.SaveChanges();
            torrentDownloadId = torrent.Id;
        }

        var torrentSortService = Substitute.For<ITorrentSortService>();
        torrentSortService.GetJavinizerBaseUrlAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var pathMappingService = Substitute.For<IPathMappingService>();
        pathMappingService.TranslateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<string>());

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        var vrMergeService = Substitute.For<IVrMergeService>();
        var vrMergeJobTracker = Substitute.For<IVrMergeJobTracker>();
        var ffmpegResolver = Substitute.For<IFfmpegBinaryResolver>();
        ffmpegResolver.IsAvailable.Returns(false);

        var state = new TorrentSortWizardState(
            torrentSortService, pathMappingService, localLibraryClient, factory,
            new ConfigurationBuilder().Build(), vrMergeService, vrMergeJobTracker, ffmpegResolver);
        Services.AddSingleton(state);

        return (factory, torrentDownloadId, torrentSortService);
    }

    private async Task<IRenderedComponent<TorrentSort>> RenderAtDoneStepAsync(int torrentDownloadId)
    {
        var state = Services.GetRequiredService<TorrentSortWizardState>();
        await state.InitializeAsync(torrentDownloadId);
        state.Step = WizardStep.Done;

        return Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));
    }

    [Fact]
    public async Task DoneStep_HashKnown_ShowsCleanupButton()
    {
        var (factory, torrentDownloadId, _) = SetUpServices(hash: "abc123", contentPath: "/media/x");
        using var f = factory;

        var cut = await RenderAtDoneStepAsync(torrentDownloadId);

        Assert.Contains("Delete via qBittorrent", cut.Markup);
    }

    [Fact]
    public async Task DoneStep_NoHash_HidesCleanupSection()
    {
        var (factory, torrentDownloadId, _) = SetUpServices(hash: null, contentPath: "/media/x");
        using var f = factory;

        var cut = await RenderAtDoneStepAsync(torrentDownloadId);

        Assert.DoesNotContain("Delete via qBittorrent", cut.Markup);
    }

    [Fact]
    public async Task ClickingCleanupButton_ShowsPreviewList_BeforeDeleting()
    {
        var (factory, torrentDownloadId, sortService) = SetUpServices(hash: "abc123", contentPath: "/media/x");
        using var f = factory;

        sortService.PreviewCleanupAsync(torrentDownloadId, Arg.Any<CancellationToken>())
            .Returns(new TorrentCleanupPreviewResult(true, "/media/x", ["movie.mp4", "ad.txt"], null));

        var cut = await RenderAtDoneStepAsync(torrentDownloadId);
        cut.Find("button:contains('Delete via qBittorrent')").Click();

        Assert.Contains("movie.mp4", cut.Markup);
        Assert.Contains("ad.txt", cut.Markup);
        Assert.Contains("Confirm Delete", cut.Markup);
        await sortService.DidNotReceive().CleanupSourceAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmingCleanup_CallsCleanupSourceAsync_AndShowsSuccess()
    {
        var (factory, torrentDownloadId, sortService) = SetUpServices(hash: "abc123", contentPath: "/media/x");
        using var f = factory;

        sortService.PreviewCleanupAsync(torrentDownloadId, Arg.Any<CancellationToken>())
            .Returns(new TorrentCleanupPreviewResult(true, "/media/x", ["movie.mp4"], null));
        sortService.CleanupSourceAsync(torrentDownloadId, Arg.Any<CancellationToken>())
            .Returns(new TorrentCleanupResult(true, null));

        var cut = await RenderAtDoneStepAsync(torrentDownloadId);
        cut.Find("button:contains('Delete via qBittorrent')").Click();
        cut.Find("button:contains('Confirm Delete')").Click();

        Assert.Contains("Source cleaned up", cut.Markup);
        await sortService.Received(1).CleanupSourceAsync(torrentDownloadId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellingPreview_ReturnsToStartingButton_WithoutDeleting()
    {
        var (factory, torrentDownloadId, sortService) = SetUpServices(hash: "abc123", contentPath: "/media/x");
        using var f = factory;

        sortService.PreviewCleanupAsync(torrentDownloadId, Arg.Any<CancellationToken>())
            .Returns(new TorrentCleanupPreviewResult(true, "/media/x", ["movie.mp4"], null));

        var cut = await RenderAtDoneStepAsync(torrentDownloadId);
        cut.Find("button:contains('Delete via qBittorrent')").Click();
        cut.Find("button:contains('Cancel')").Click();

        Assert.Contains("Delete via qBittorrent", cut.Markup);
        Assert.DoesNotContain("Confirm Delete", cut.Markup);
        await sortService.DidNotReceive().CleanupSourceAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
