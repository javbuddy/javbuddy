using Javbuddy.Services.MovieDiscovery;

namespace Javbuddy.Services.Tasks;

public class MovieDiscoveryScanTask(IMovieDiscoveryService discoveryService) : IScheduledTask
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromDays(7);

    public string Name => "Movie Discovery Scan";

    public string Description =>
        "Scans studio sites (currently S1) for new and upcoming releases not yet tracked, " +
        "surfacing them on Movies > Discover.";

    public TimeSpan GetInterval() => DefaultInterval;

    public async Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress)
    {
        var result = await discoveryService.RunScanAsync(progress, ct);
        return $"found {result.TotalFound}, {result.NewCount} new, {result.AlreadyTrackedCount} already tracked";
    }
}
