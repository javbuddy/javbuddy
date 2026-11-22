using Javbuddy.Services.MovieDiscovery;
using Javbuddy.Services.Tasks;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tasks;

public class MovieDiscoveryScanTaskTests
{
    [Fact]
    public async Task RunAsync_DiscoveryOff_DoesNotScan()
    {
        var discovery = Substitute.For<IMovieDiscoveryService>();
        var settings = Substitute.For<IDiscoverySettingsService>();
        settings.IsAnyEnabledAsync(Arg.Any<CancellationToken>()).Returns(false);

        var summary = await new MovieDiscoveryScanTask(discovery, settings).RunAsync(CancellationToken.None, new Progress<TaskProgress>());

        Assert.Contains("off", summary);
        await discovery.DidNotReceiveWithAnyArgs().RunScanAsync(default, default);
    }

    [Fact]
    public async Task RunAsync_DiscoveryOn_RunsTheScan()
    {
        var discovery = Substitute.For<IMovieDiscoveryService>();
        discovery.RunScanAsync(Arg.Any<IProgress<TaskProgress>?>(), Arg.Any<CancellationToken>()).Returns(new MovieDiscoveryScanResult(3, 2, 1));
        var settings = Substitute.For<IDiscoverySettingsService>();
        settings.IsAnyEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);

        var summary = await new MovieDiscoveryScanTask(discovery, settings).RunAsync(CancellationToken.None, new Progress<TaskProgress>());

        Assert.Equal("found 3, 2 new, 1 already tracked", summary);
    }
}
