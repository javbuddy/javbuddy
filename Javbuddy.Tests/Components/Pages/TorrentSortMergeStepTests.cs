using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Metrics;
using Javbuddy.Services.Torrents;
using Javbuddy.Services.VrMerge;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

/// <summary>Covers the new Merge step of TorrentSort.razor: rendering
/// detected parts, exclude/reorder recomputing chapter titles live, and the merge button driving the
/// real VrMergeJobTracker into the substituted IVrMergeService.</summary>
public class TorrentSortMergeStepTests : BunitContext
{
    private readonly string tempFolder = Directory.CreateTempSubdirectory("javbuddy-torrentsort-merge-test-").FullName;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(tempFolder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static VrMergeCandidatePart Part(string path, string name, string chapterTitle) =>
        new() { Path = path, Name = name, SizeBytes = 1_000_000_000, DurationSeconds = 100, ChapterTitle = chapterTitle };

    private (TestDbContextFactory Factory, int TorrentDownloadId, IVrMergeService VrMergeService) SetUpServices()
    {
        var factory = new TestDbContextFactory();
        int torrentDownloadId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "DEVR-041" };
            db.Movies.Add(movie);
            db.SaveChanges();

            var torrent = new TorrentDownload { MovieId = movie.Id, MovieCode = "DEVR-041", SavePath = "/downloads/devr-041" };
            db.TorrentDownloads.Add(torrent);
            db.SaveChanges();
            torrentDownloadId = torrent.Id;
        }

        var torrentSortService = Substitute.For<ITorrentSortService>();
        torrentSortService.GetJavinizerBaseUrlAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var pathMappingService = Substitute.For<IPathMappingService>();
        pathMappingService.TranslateToAppPathAsync("/downloads/devr-041", Arg.Any<CancellationToken>()).Returns(tempFolder);
        pathMappingService.TranslateAsync("/downloads/devr-041", Arg.Any<CancellationToken>()).Returns("/scratch/devr-041");

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();

        var ffmpegResolver = Substitute.For<IFfmpegBinaryResolver>();
        ffmpegResolver.IsAvailable.Returns(true);

        var parts = new List<VrMergeCandidatePart>
        {
            Part(Path.Combine(tempFolder, "release_1.mp4"), "release_1.mp4", "DEVR-041-A"),
            Part(Path.Combine(tempFolder, "release_2.mp4"), "release_2.mp4", "DEVR-041-B"),
            Part(Path.Combine(tempFolder, "release_3.mp4"), "release_3.mp4", "DEVR-041-C"),
        };
        var candidate = new VrMergeCandidate { FolderPath = tempFolder, CodeBase = "DEVR-041", Parts = parts };

        var vrMergeService = Substitute.For<IVrMergeService>();
        vrMergeService.DetectAsync(tempFolder, Arg.Any<CancellationToken>()).Returns(candidate);

        // The real tracker, not a substitute: StartMergeAsync's call into IVrMergeService only
        // happens if GetOrStart actually invokes the runMerge callback it's given.
        var vrMergeJobTracker = new VrMergeJobTracker(new JavbuddyMetrics());

        // Built directly and registered as an instance rather than via AddTransient: the DI
        // container disposes transient instances synchronously at scope teardown, but
        // TorrentSortWizardState only implements IAsyncDisposable — an instance registration is
        // never disposed by the container itself (its lifetime stays owned by this test).
        var state = new TorrentSortWizardState(
            torrentSortService, pathMappingService, localLibraryClient, factory,
            new ConfigurationBuilder().Build(), vrMergeService, vrMergeJobTracker, ffmpegResolver);
        Services.AddSingleton(state);

        return (factory, torrentDownloadId, vrMergeService);
    }

    [Fact]
    public void MergeStep_RendersDetectedPartsInOrderWithChapterTitles()
    {
        var (factory, torrentDownloadId, _) = SetUpServices();
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        Assert.Contains("release_1.mp4", cut.Markup);
        Assert.Contains("release_2.mp4", cut.Markup);
        Assert.Contains("release_3.mp4", cut.Markup);
        Assert.Contains("DEVR-041-A", cut.Markup);
        Assert.Contains("DEVR-041-B", cut.Markup);
        Assert.Contains("DEVR-041-C", cut.Markup);
    }

    [Fact]
    public void ExcludingFirstPart_RecomputesChapterTitlesForRemainingParts()
    {
        var (factory, torrentDownloadId, _) = SetUpServices();
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        var firstCheckbox = cut.FindAll("div.sort-merge-file-row input[type=checkbox]")[0];
        firstCheckbox.Change(false);

        Assert.Contains("excluded", cut.Markup);
        // release_2.mp4 is now the first included part, so it should carry the "-A" chapter title.
        var rows = cut.FindAll("div.sort-merge-file-row");
        Assert.Contains("DEVR-041-A", rows[1].InnerHtml);
    }

    [Fact]
    public void MovingSecondPartUp_ReordersParts()
    {
        var (factory, torrentDownloadId, _) = SetUpServices();
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        var moveUpButtons = cut.FindAll("div.sort-merge-file-row .btn-group button");
        // Row 0's "up" button (index 0) is disabled; row 1's "up" button is index 2 (2 buttons/row).
        moveUpButtons[2].Click();

        var rows = cut.FindAll("div.sort-merge-file-row");
        Assert.Contains("release_2.mp4", rows[0].InnerHtml);
        Assert.Contains("DEVR-041-A", rows[0].InnerHtml);
        Assert.Contains("release_1.mp4", rows[1].InnerHtml);
        Assert.Contains("DEVR-041-B", rows[1].InnerHtml);
    }

    [Fact]
    public void ClickingMergeButton_InvokesVrMergeService()
    {
        var (factory, torrentDownloadId, vrMergeService) = SetUpServices();
        using var f = factory;

        var cut = Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));

        var mergeButton = cut.FindAll("button").First(b => b.TextContent.Contains("Merge") && b.TextContent.Contains("Part"));
        mergeButton.Click();

        vrMergeService.Received(1).MergeAsync(
            Arg.Is<VrMergeRequest>(r => r.FolderPath == tempFolder && r.OrderedParts.Count == 3),
            Arg.Any<IProgress<FfmpegProgress>>(),
            Arg.Any<CancellationToken>());
    }
}
