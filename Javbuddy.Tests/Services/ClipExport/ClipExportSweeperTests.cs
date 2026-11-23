using Javbuddy.Services.ClipExport;
using Javbuddy.Services.Images;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Javbuddy.Tests.Services.ClipExport;

public sealed class ClipExportSweeperTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"javbuddy-clip-sweeper-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider time = new(DateTimeOffset.UtcNow);
    private readonly ClipExportDownloads downloads;

    public ClipExportSweeperTests()
    {
        var cache = Substitute.For<ILocalImageCache>();
        cache.RootPath.Returns(root);
        downloads = new ClipExportDownloads(cache, time);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.True(condition());
    }

    [Fact]
    public async Task SweepsAtStartup_ThenOnEveryInterval_WithoutAnyExport()
    {
        var leftover = downloads.NewTempPath();
        File.WriteAllText(leftover, "from before a restart");
        File.SetLastWriteTimeUtc(leftover, time.GetUtcNow().UtcDateTime - ClipExportDownloads.Lifetime - TimeSpan.FromMinutes(1));
        var unclaimed = downloads.NewTempPath();
        File.WriteAllText(unclaimed, "never downloaded");
        File.SetLastWriteTimeUtc(unclaimed, time.GetUtcNow().UtcDateTime);
        downloads.Add(unclaimed, "a.mp4", "video/mp4");

        using var sweeper = new ClipExportSweeper(downloads, NullLogger<ClipExportSweeper>.Instance, time);
        await sweeper.StartAsync(CancellationToken.None);

        await WaitUntilAsync(() => !File.Exists(leftover));
        Assert.True(File.Exists(unclaimed));

        time.Advance(ClipExportDownloads.Lifetime + ClipExportSweeper.Interval);
        await WaitUntilAsync(() => !File.Exists(unclaimed));

        await sweeper.StopAsync(CancellationToken.None);
    }
}
