using System.Threading.Channels;
using Javbuddy.Services.Metrics;

namespace Javbuddy.Services.Movies;

/// <summary>Wakes NewMovieArtworkWorker, so a library rescan that imported movies (or probed their durations) never
/// waits on the ffmpeg work itself.</summary>
public sealed class NewMovieArtworkSignal
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public void Signal() => channel.Writer.TryWrite(true);

    public async Task WaitAsync(CancellationToken ct) => await channel.Reader.ReadAsync(ct);
}

/// <summary>Generates the missing artwork of movies a library rescan imported (<see cref="INewMovieArtworkService"/>), one
/// movie at a time in the background: on startup, which picks up whatever a cancelled run or a restart left pending, and
/// whenever <see cref="NewMovieArtworkSignal"/> fires.</summary>
public sealed class NewMovieArtworkWorker(
    IServiceScopeFactory scopeFactory,
    NewMovieArtworkSignal signal,
    ILogger<NewMovieArtworkWorker> logger,
    TimeSpan? retryDelay = null) : BackgroundService
{
    private readonly TimeSpan retryDelay = retryDelay ?? TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await GeneratePendingAsync(stoppingToken);
                await signal.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The pending markers keep the work, so retry after a pause.
                logger.LogError(ex, "Generating artwork for new movies failed");
                try { await Task.Delay(retryDelay, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    // One pass, as one trace; a failure is recorded on it before the loop's handler retries.
    private async Task GeneratePendingAsync(CancellationToken ct)
    {
        using var span = JavbuddyTracing.StartRoot("new movie artwork");
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var generated = await scope.ServiceProvider.GetRequiredService<INewMovieArtworkService>().GeneratePendingAsync(ct);
            span?.SetTag("javbuddy.artwork.generated_movies", generated);
            span.SetOutcome(generated > 0 ? "success" : "skipped");
            if (generated > 0) logger.LogInformation("Generated artwork for {Count} new movie(s)", generated);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            span.SetOutcome("cancelled");
            throw;
        }
        catch (Exception ex)
        {
            span.SetOutcome("failure", ex);
            throw;
        }
    }
}
