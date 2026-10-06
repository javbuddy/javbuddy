using Javbuddy.Models;
using Javbuddy.Services.Metrics;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.Torrents;

public class TorrentGrabServiceTests
{
    private static TorrentGrabService CreateService(TestDbContextFactory factory, JavbuddyMetrics metrics, IQBittorrentClient? qBittorrentClient = null) =>
        new(
            qBittorrentClient ?? Substitute.For<IQBittorrentClient>(),
            new FakeHttpClientFactory(FakeHttpMessageHandler.ReturningStatus(System.Net.HttpStatusCode.NotFound)),
            factory,
            new TorrentChangeNotifier(),
            metrics);

    [Fact]
    public async Task GrabAsync_MovieHasNoCode_RecordsFailureOutcome()
    {
        using var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);
        using var factory = new TestDbContextFactory();

        var result = await CreateService(factory, metrics).GrabAsync(new Movie(), new ReleaseResourceDto());

        Assert.False(result.Success);
        var grab = Assert.Single(recorder.Measurements, m => m.InstrumentName == "javbuddy_torrent_grabs_total");
        Assert.Equal("failure", grab.Tags["outcome"]);
    }

    [Fact]
    public async Task GrabAsync_MagnetAccepted_RecordsSuccessOutcome()
    {
        using var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);
        using var factory = new TestDbContextFactory();
        var qBittorrentClient = Substitute.For<IQBittorrentClient>();
        qBittorrentClient.AddMagnetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new QBittorrentAddResult(true, null));

        var movie = new Movie { Code = "ABC-123" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        var release = new ReleaseResourceDto { MagnetUrl = "magnet:?xt=urn:btih:abc", Title = "Some Release", Indexer = "TestIndexer", Size = 1234 };

        var result = await CreateService(factory, metrics, qBittorrentClient).GrabAsync(movie, release);

        Assert.True(result.Success);
        var grab = Assert.Single(recorder.Measurements, m => m.InstrumentName == "javbuddy_torrent_grabs_total");
        Assert.Equal("success", grab.Tags["outcome"]);
    }
}
