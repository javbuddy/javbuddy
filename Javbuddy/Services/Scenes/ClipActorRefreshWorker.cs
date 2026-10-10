using System.Diagnostics;
using Javbuddy.Data;
using Javbuddy.Services.Nfo;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>Refreshes movies whose stored effective actors are stale: on startup — after the
/// upgrade every movie is, so that is the first fill (~40 s for 4000 movies) — and whenever
/// ClipActorRefreshSignal fires. One movie at a time, so a movie is never refreshed twice at once. A movie whose
/// plain tags changed through its actor tags gets the .nfo drift check (scopeFactory, left out in tests).</summary>
public sealed class ClipActorRefreshWorker(
    IDbContextFactory<AppDbContext> dbFactory,
    ClipActorRefreshSignal signal,
    ILogger<ClipActorRefreshWorker> logger,
    IServiceScopeFactory? scopeFactory = null) : BackgroundService
{
    // Below this a pass is routine (an edit or two); above it, worth one line in the log.
    private const int LogThreshold = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
                await signal.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // E.g. the database briefly unavailable; the stale flags keep the work, so retry later.
                logger.LogError(ex, "Refreshing stale stored actors failed");
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var count = await ClipActorSync.RefreshStaleAsync(db, logger, onMovieTagsChanged: CheckNfoAsync, ct: ct);
        if (count >= LogThreshold)
        {
            logger.LogInformation("Refreshed the stored actors of {Count} movies in {Elapsed}", count, Stopwatch.GetElapsedTime(started));
        }
    }

    private async Task CheckNfoAsync(int movieId, CancellationToken ct)
    {
        if (scopeFactory is null) return;
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INfoSyncService>().CheckMovieNfoConflictAsync(movieId, ct);
    }
}
