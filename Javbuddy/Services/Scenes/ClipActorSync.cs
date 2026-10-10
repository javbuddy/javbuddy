using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Tags;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>Keeps the stored effective actors (SceneEffectiveActor, HighlightEffectiveActor, ApexEffectiveActor) and
/// effective actor tags (SceneEffectiveActorTag and its siblings) equal to what ClipActors and ClipActorTags compute, one movie at a time, for movies flagged
/// Movie.ClipActorsStale. ClipActors stays the only definition of inheritance; the Scenes wall filters on the
/// stored rows. ClipActorRefreshWorker runs RefreshStaleAsync in the app; tests call it directly.</summary>
public static class ClipActorSync
{
    /// <summary>Recomputes one movie's rows in one transaction and returns false when there's no such movie.
    /// The flag is cleared first: that write takes SQLite's write lock, so an edit made meanwhile waits and
    /// then marks the movie stale again, and a failure rolls the clear back.</summary>
    public static Task<bool> RefreshAsync(AppDbContext db, int movieId, CancellationToken ct) =>
        RefreshAsync(db, movieId, onMovieTagsChanged: null, ct);

    /// <summary>As above; onMovieTagsChanged runs after the movie's plain tags (and MetaGenres) changed through
    /// its actor tags, for the .nfo drift check the worker can't make from here.</summary>
    public static async Task<bool> RefreshAsync(AppDbContext db, int movieId, Func<int, CancellationToken, Task>? onMovieTagsChanged, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var cleared = await db.Movies.Where(m => m.Id == movieId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ClipActorsStale, false), ct);
        if (cleared == 0) return false;

        var (effective, tags) = await ClipAssignments.LoadEffectiveActorsAndTagsAsync(db, movieId, ct);
        Apply(db, await db.SceneEffectiveActors.Where(r => r.MovieId == movieId).ToListAsync(ct), Pairs(effective.Scenes),
            r => (r.SceneId, r.ActorId), (owner, actor) => new SceneEffectiveActor { SceneId = owner, MovieId = movieId, ActorId = actor });
        Apply(db, await db.HighlightEffectiveActors.Where(r => r.MovieId == movieId).ToListAsync(ct), Pairs(effective.Highlights),
            r => (r.HighlightId, r.ActorId), (owner, actor) => new HighlightEffectiveActor { HighlightId = owner, MovieId = movieId, ActorId = actor });
        Apply(db, await db.ApexEffectiveActors.Where(r => r.MovieId == movieId).ToListAsync(ct), Pairs(effective.Apexes),
            r => (r.ApexId, r.ActorId), (owner, actor) => new ApexEffectiveActor { ApexId = owner, MovieId = movieId, ActorId = actor });
        Apply(db, await db.SceneEffectiveActorTags.Where(r => r.MovieId == movieId).ToListAsync(ct), Triples(tags.Scenes),
            r => (r.SceneId, r.ActorId, r.TagId), r => r.IsRolledUp, (r, rolledUp) => r.IsRolledUp = rolledUp,
            (owner, actor, tag, rolledUp) => new SceneEffectiveActorTag { SceneId = owner, MovieId = movieId, ActorId = actor, TagId = tag, IsRolledUp = rolledUp });
        Apply(db, await db.HighlightEffectiveActorTags.Where(r => r.MovieId == movieId).ToListAsync(ct), Triples(tags.Highlights),
            r => (r.HighlightId, r.ActorId, r.TagId), r => r.IsRolledUp, (r, rolledUp) => r.IsRolledUp = rolledUp,
            (owner, actor, tag, rolledUp) => new HighlightEffectiveActorTag { HighlightId = owner, MovieId = movieId, ActorId = actor, TagId = tag, IsRolledUp = rolledUp });
        Apply(db, await db.ApexEffectiveActorTags.Where(r => r.MovieId == movieId).ToListAsync(ct), Triples(tags.Apexes),
            r => (r.ApexId, r.ActorId, r.TagId), _ => false, (_, _) => { },
            (owner, actor, tag, _) => new ApexEffectiveActorTag { ApexId = owner, MovieId = movieId, ActorId = actor, TagId = tag });
        await db.SaveChangesAsync(ct);

        // Leaving the cast drops an actor's tags by DB cascade, which no service sees: bring the movie's plain tags
        // back in step, for the movies that have any actor tags. After the commit, not inside it: ClipTagSync waits for
        // other refreshes of its own, and one of them may be waiting for this transaction's write lock.
        var hasActorTags = await db.MovieTags.AnyAsync(mt => mt.MovieId == movieId && mt.Tag.IsActorTag, ct)
            || await db.MovieActorTags.AnyAsync(t => t.MovieId == movieId, ct)
            || tags.Scenes.Values.Concat(tags.Highlights.Values).Concat(tags.Apexes.Values).Any(list => list.Count > 0);
        await transaction.CommitAsync(ct);

        if (hasActorTags && await ClipTagSync.RefreshAsync(db, movieId, ct))
        {
            await TagNormalization.SyncMetaGenresAsync(db, [movieId], ct);
            await db.SaveChangesAsync(ct);
            if (onMovieTagsChanged is not null) await onMovieTagsChanged(movieId, ct);
        }
        return true;
    }

    /// <summary>Refreshes every stale movie, in id order, until none is left, and returns how many it refreshed.
    /// A movie whose refresh throws stays stale (its transaction rolls back), is logged and is skipped for the
    /// rest of this call. refresh replaces RefreshAsync in tests; onMovieTagsChanged is passed on to it.</summary>
    public static async Task<int> RefreshStaleAsync(
        AppDbContext db,
        ILogger? logger = null,
        Func<AppDbContext, int, CancellationToken, Task<bool>>? refresh = null,
        Func<int, CancellationToken, Task>? onMovieTagsChanged = null,
        CancellationToken ct = default)
    {
        refresh ??= (d, movieId, c) => RefreshAsync(d, movieId, onMovieTagsChanged, c);
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

    private static Dictionary<(int Owner, int Actor, int Tag), bool> Triples(IReadOnlyDictionary<int, IReadOnlyList<EffectiveActorTag>> byOwner) =>
        byOwner.SelectMany(owner => owner.Value.Select(t => (Key: (owner.Key, t.ActorId, t.TagId), t.IsRolledUp))).ToDictionary(t => t.Key, t => t.IsRolledUp);

    // Like Apply for the actor-tag rows, which also carry whether the tag only rolled up.
    private static void Apply<TRow>(AppDbContext db, List<TRow> stored, Dictionary<(int Owner, int Actor, int Tag), bool> wanted,
        Func<TRow, (int Owner, int Actor, int Tag)> key, Func<TRow, bool> isRolledUp, Action<TRow, bool> setRolledUp,
        Func<int, int, int, bool, TRow> create) where TRow : class
    {
        var kept = new HashSet<(int Owner, int Actor, int Tag)>();
        foreach (var row in stored)
        {
            if (!wanted.TryGetValue(key(row), out var rolledUp))
            {
                db.Remove(row);
                continue;
            }
            kept.Add(key(row));
            if (isRolledUp(row) != rolledUp) setRolledUp(row, rolledUp);
        }
        foreach (var (triple, rolledUp) in wanted.Where(kv => !kept.Contains(kv.Key)))
        {
            db.Add(create(triple.Owner, triple.Actor, triple.Tag, rolledUp));
        }
    }

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
