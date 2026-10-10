using System.Threading.Channels;

namespace Javbuddy.Services.Nfo;

public interface INfoDriftCheckQueue
{
    /// <summary>Queues the .nfo drift check for each movie and returns at once; the checks run one at a time in the background.</summary>
    void Enqueue(IReadOnlyCollection<int> movieIds);
}

/// <summary>Runs the .nfo drift check (<see cref="INfoSyncService.CheckMovieNfoConflictAsync"/>) in the background for
/// bulk tag changes (a tag deleted, ignored or changing kind can touch hundreds of movies), so the caller isn't blocked
/// on them. Movies already queued aren't queued twice.</summary>
public sealed class NfoDriftCheckQueue(IServiceScopeFactory scopeFactory, ILogger<NfoDriftCheckQueue> logger) : BackgroundService, INfoDriftCheckQueue
{
    private readonly Channel<int> channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });
    private readonly HashSet<int> pending = [];

    public void Enqueue(IReadOnlyCollection<int> movieIds)
    {
        foreach (var movieId in movieIds.Distinct())
        {
            bool added;
            lock (pending) added = pending.Add(movieId);
            if (added) channel.Writer.TryWrite(movieId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var movieId in channel.Reader.ReadAllAsync(stoppingToken))
            {
                // Dequeued first, so a change that lands mid-check queues the movie again.
                lock (pending) pending.Remove(movieId);
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<INfoSyncService>().CheckMovieNfoConflictAsync(movieId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "The .nfo drift check for movie {MovieId} failed", movieId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
