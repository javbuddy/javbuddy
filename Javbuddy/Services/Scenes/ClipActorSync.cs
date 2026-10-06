using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>Keeps the stored effective actors (SceneEffectiveActor, HighlightEffectiveActor, ApexEffectiveActor)
/// equal to what ClipActors computes, one movie at a time, for movies flagged
/// Movie.ClipActorsStale. ClipActors stays the only definition of inheritance; the Scenes wall filters on the
/// stored rows. ClipActorRefreshWorker runs RefreshStaleAsync in the app; tests call it directly.</summary>
public static class ClipActorSync
{
    /// <summary>Recomputes one movie's rows in one transaction and returns false when there's no such movie.
    /// The flag is cleared first: that write takes SQLite's write lock, so an edit made meanwhile waits and
    /// then marks the movie stale again, and a failure rolls the clear back.</summary>
    public static async Task<bool> RefreshAsync(AppDbContext db, int movieId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var cleared = await db.Movies.Where(m => m.Id == movieId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ClipActorsStale, false), ct);
        if (cleared == 0) return false;

        var effective = await ClipAssignments.LoadEffectiveActorsAsync(db, movieId, ct);
        Apply(db, await db.SceneEffectiveActors.Where(r => r.MovieId == movieId).ToListAsync(ct), Pairs(effective.Scenes),
            r => (r.SceneId, r.ActorId), (owner, actor) => new SceneEffectiveActor { SceneId = owner, MovieId = movieId, ActorId = actor });
        Apply(db, await db.HighlightEffectiveActors.Where(r => r.MovieId == movieId).ToListAsync(ct), Pairs(effective.Highlights),
            r => (r.HighlightId, r.ActorId), (owner, actor) => new HighlightEffectiveActor { HighlightId = owner, MovieId = movieId, ActorId = actor });
        Apply(db, await db.ApexEffectiveActors.Where(r => r.MovieId == movieId).ToListAsync(ct), Pairs(effective.Apexes),
            r => (r.ApexId, r.ActorId), (owner, actor) => new ApexEffectiveActor { ApexId = owner, MovieId = movieId, ActorId = actor });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    /// <summary>Refreshes every stale movie, in id order, until none is left, and returns how many it refreshed.
    /// A movie whose refresh throws stays stale (its transaction rolls back), is logged and is skipped for the
    /// rest of this call. refresh replaces RefreshAsync in tests.</summary>
    public static async Task<int> RefreshStaleAsync(
        AppDbContext db,
        ILogger? logger = null,
        Func<AppDbContext, int, CancellationToken, Task<bool>>? refresh = null,
        CancellationToken ct = default)
    {
        refresh ??= RefreshAsync;
        var failed = new List<int>();
        var refreshed = 0;
        while (true)
        {
            var batch = await db.Movies
                .Where(m => m.ClipActorsStale && !failed.Contains(m.Id))
                .OrderBy(m => m.Id)
                .Select(m => m.Id)
                .Take(100)
                .ToListAsync(ct);
            if (batch.Count == 0) return refreshed;

            foreach (var movieId in batch)
            {
                try
                {
                    if (await refresh(db, movieId, ct)) refreshed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed.Add(movieId);
                    logger?.LogWarning(ex, "Could not refresh the stored actors of movie {MovieId}", movieId);
                }
                finally
                {
                    db.ChangeTracker.Clear();
                }
            }
        }
    }

    private static HashSet<(int Owner, int Actor)> Pairs(IReadOnlyDictionary<int, EffectiveActors> byOwner) =>
        byOwner.SelectMany(owner => owner.Value.Actors.Select(a => (owner.Key, a.ActorId))).ToHashSet();

    // Removes stored rows no longer wanted and adds the missing ones, leaving the rest untouched.
    private static void Apply<TRow>(AppDbContext db, List<TRow> stored, HashSet<(int Owner, int Actor)> wanted,
        Func<TRow, (int Owner, int Actor)> key, Func<int, int, TRow> create) where TRow : class
    {
        var kept = new HashSet<(int Owner, int Actor)>();
        foreach (var row in stored)
        {
            if (wanted.Contains(key(row))) kept.Add(key(row));
            else db.Remove(row);
        }
        foreach (var (owner, actor) in wanted.Where(pair => !kept.Contains(pair)))
        {
            db.Add(create(owner, actor));
        }
    }
}

/// <summary>Marks movies' stored effective actors stale where ClipActorStaleInterceptor can't see
/// the change: the ExecuteDelete paths and the "Apply Rules to Library" self-heal. Callers then call
/// ClipActorRefreshSignal.Signal so the worker picks them up.</summary>
public static class ClipActorStale
{
    public static Task MarkAsync(AppDbContext db, int movieId, CancellationToken ct) =>
        db.Movies.Where(m => m.Id == movieId).ExecuteUpdateAsync(s => s.SetProperty(m => m.ClipActorsStale, true), ct);

    public static Task MarkAllAsync(AppDbContext db, CancellationToken ct) =>
        db.Movies.ExecuteUpdateAsync(s => s.SetProperty(m => m.ClipActorsStale, true), ct);
}
