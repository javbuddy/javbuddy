using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Torrents;
using Javbuddy.Services.Torrents.SortWizard;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Javbuddy.Tests.Services.Torrents.SortWizard;

/// <summary>Exercises the wizard's step parts on their own — no coordinator, no real delays.</summary>
public class SortWizardStepTests
{
    [Fact]
    public async Task ScanStep_ScanWithNothingMatched_SelectsVideoFilesOnly()
    {
        var sortService = Substitute.For<ITorrentSortService>();
        sortService.ScanAsync(7, "/dl", Arg.Any<CancellationToken>()).Returns(new TorrentScanResult(true,
        [
            new FileInfoDto { Name = "a.mp4", Path = "/dl/a.mp4" },
            new FileInfoDto { Name = "readme.txt", Path = "/dl/readme.txt" },
            new FileInfoDto { Name = "sub", Path = "/dl/sub", IsDir = true }
        ], null));
        var session = new WizardSession { TorrentDownloadId = 7 };
        var scan = new ScanStep(session, sortService);
        scan.SetSourcePath("/dl", mapped: false);

        await scan.ScanAsync();

        Assert.Equal(["/dl/a.mp4"], scan.SelectedFilePaths);
        Assert.False(scan.Scanning);
    }

    [Fact]
    public async Task OrganizeStep_TickerNotifiesEverySecondUntilOrganizeReturns()
    {
        var time = new FakeTimeProvider();
        var release = new TaskCompletionSource<TorrentOrganizeResult>();
        var sortService = Substitute.For<ITorrentSortService>();
        sortService.OrganizeAsync(7, "/media", Arg.Any<CancellationToken>()).Returns(release.Task);
        var session = new WizardSession { TorrentDownloadId = 7, Destination = "/media" };
        var notifications = 0;
        session.Changed += () => notifications++;
        var organize = new OrganizeStep(session, sortService, time);

        var running = organize.OrganizeAsync();
        await Task.Yield();
        var before = notifications;
        time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50);

        Assert.True(organize.Organizing);
        Assert.True(notifications > before);
        Assert.Equal(TimeSpan.FromSeconds(1), organize.OrganizeElapsed);

        release.SetResult(new TorrentOrganizeResult(true, null));
        await running;

        Assert.False(organize.Organizing);
        Assert.Equal(WizardStep.Done, session.Step);
    }

    [Fact]
    public async Task ScrapeStep_PollLoopMovesToReviewWhenJobCompletes_AndDisposeStopsIt()
    {
        var time = new FakeTimeProvider();
        var sortService = Substitute.For<ITorrentSortService>();
        sortService.PollJobAsync(7, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci => new JavinizerBatchJobResult(true, new BatchJobResponseDto { Status = JavinizerJobStatus.Completed }, null));
        var session = new WizardSession { TorrentDownloadId = 7 };
        var scrape = new ScrapeStep(session, sortService, new ConfigurationBuilder().Build(), time);

        scrape.StartPolling();
        await Task.Delay(100);

        Assert.Equal(WizardStep.Review, session.Step);
        await scrape.DisposeAsync();
    }
}
