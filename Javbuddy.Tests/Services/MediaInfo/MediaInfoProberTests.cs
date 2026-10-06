using Javbuddy.Services.MediaInfo;

namespace Javbuddy.Tests.Services.MediaInfo;

public class MediaInfoProberTests
{
    [Theory]
    [InlineData(10063104d, 10063.104)]
    [InlineData(0d, null)]
    [InlineData(null, null)]
    public void MsToSeconds_ConvertsOrReturnsNull(double? milliseconds, double? expected)
    {
        Assert.Equal(expected, MediaInfoProber.MsToSeconds(milliseconds));
    }

    [Theory]
    [InlineData(4971830d, 4972)]
    [InlineData(64000d, 64)]
    [InlineData(0d, null)]
    [InlineData(null, null)]
    public void BpsToKbps_ConvertsOrReturnsNull(double? bps, int? expected)
    {
        Assert.Equal(expected, MediaInfoProber.BpsToKbps(bps));
    }

    [Fact]
    public void SkippedResult_HasNoData()
    {
        var result = MediaProbeResult.SkippedResult();

        Assert.True(result.Skipped);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task RaceWithTimeoutAsync_ProbeCompletesFirst_ReturnsItsResult()
    {
        var expected = new MediaProbeResult { Success = true, ContainerFormat = "MPEG-4" };

        var result = await MediaInfoProber.RaceWithTimeoutAsync(Task.FromResult(expected), TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task RaceWithTimeoutAsync_ProbeNeverCompletes_ReturnsFailedAfterTimeout()
    {
        // A TaskCompletionSource that's never completed stands in for a hung native call — the
        // point of this timeout is exactly that the real thing can't be cancelled once started.
        var neverCompletes = new TaskCompletionSource<MediaProbeResult>();

        var result = await MediaInfoProber.RaceWithTimeoutAsync(neverCompletes.Task, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("timed out", result.ErrorMessage);
    }

    [Fact]
    public async Task RaceWithTimeoutAsync_CallerCancels_ThrowsInsteadOfReportingATimeout()
    {
        var neverCompletes = new TaskCompletionSource<MediaProbeResult>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            MediaInfoProber.RaceWithTimeoutAsync(neverCompletes.Task, TimeSpan.FromSeconds(10), cts.Token));
    }
}
