using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Javbuddy.Services.Scenes;

/// <summary>Wakes ClipActorRefreshWorker after movies were marked stale. Coalescing: any number of
/// signals before the worker wakes give one pass.</summary>
public sealed class ClipActorRefreshSignal
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public void Signal() => channel.Writer.TryWrite(true);

    public async Task WaitAsync(CancellationToken ct) => await channel.Reader.ReadAsync(ct);
}

/// <summary>Marks a movie's stored effective actors stale (Movie.ClipActorsStale) in the same
/// SaveChanges as any tracked change that can alter them — a clip added, deleted or moved, an own-actor or cast
/// link added or removed, the movie's duration, an actor deleted — then signals the worker. ExecuteUpdate and
/// ExecuteDelete bypass it; those call sites use ClipActorStale.MarkAsync.</summary>
public sealed class ClipActorStaleInterceptor(ClipActorRefreshSignal signal) : SaveChangesInterceptor
{
    private static readonly object Marker = new();

    // Contexts whose pending save marked a movie, so the worker is signalled once it's saved.
    private readonly ConditionalWeakTable<DbContext, object> marked = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is AppDbContext db)
        {
            var movieIds = AffectedMovieIds(db, out var deletedActorIds);
            if (deletedActorIds.Count > 0)
            {
                movieIds.UnionWith(db.MovieActors.Where(ma => deletedActorIds.Contains(ma.ActorId)).Select(ma => ma.MovieId).ToList());
            }
            var untracked = Untracked(db, movieIds);
            if (untracked.Count > 0)
            {
                db.Movies.Where(m => untracked.Contains(m.Id)).Load();
            }
            Mark(db, movieIds);
        }
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext db)
        {
            var movieIds = AffectedMovieIds(db, out var deletedActorIds);
            if (deletedActorIds.Count > 0)
            {
                movieIds.UnionWith(await db.MovieActors.Where(ma => deletedActorIds.Contains(ma.ActorId)).Select(ma => ma.MovieId).ToListAsync(cancellationToken));
            }
            var untracked = Untracked(db, movieIds);
            if (untracked.Count > 0)
            {
                await db.Movies.Where(m => untracked.Contains(m.Id)).LoadAsync(cancellationToken);
            }
            Mark(db, movieIds);
        }
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Finish(eventData.Context, saved: true);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Finish(eventData.Context, saved: true);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Finish(eventData.Context, saved: false);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Finish(eventData.Context, saved: false);
        return Task.CompletedTask;
    }

    private static HashSet<int> AffectedMovieIds(AppDbContext db, out List<int> deletedActorIds)
    {
        // This hook runs before SaveChanges detects changes. Enumerating Entries() detects them, which is what
        // catches time-only edits (the time-edit tests guard that), so keep it the first thing that reads entries.
        var movieIds = new HashSet<int>();
        deletedActorIds = [];
        foreach (var entry in db.ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case Scene scene when Touches(entry, nameof(Scene.StartSeconds), nameof(Scene.EndSeconds)): movieIds.Add(scene.MovieId); break;
                case MovieHighlight highlight when Touches(entry, nameof(MovieHighlight.StartSeconds), nameof(MovieHighlight.EndSeconds)): movieIds.Add(highlight.MovieId); break;
                case MovieApex apex when Touches(entry, nameof(MovieApex.Seconds)): movieIds.Add(apex.MovieId); break;
                case SceneActor link when AddedOrDeleted(entry): movieIds.Add(link.MovieId); break;
                case HighlightActor link when AddedOrDeleted(entry): movieIds.Add(link.MovieId); break;
                case ApexActor link when AddedOrDeleted(entry): movieIds.Add(link.MovieId); break;
                case MovieActor link when AddedOrDeleted(entry): movieIds.Add(link.MovieId); break;
                case Movie movie when entry.State == EntityState.Modified && entry.Property(nameof(Movie.MediaDurationSeconds)).IsModified: movieIds.Add(movie.Id); break;
                case Actor actor when entry.State == EntityState.Deleted: deletedActorIds.Add(actor.Id); break;
            }
        }
        return movieIds;
    }

    private static bool AddedOrDeleted(EntityEntry entry) => entry.State is EntityState.Added or EntityState.Deleted;

    private static bool Touches(EntityEntry entry, params string[] times) =>
        AddedOrDeleted(entry) || (entry.State == EntityState.Modified && times.Any(time => entry.Property(time).IsModified));

    /// <summary>Affected movies the context doesn't track, to load before marking. Loading the real rows rather
    /// than attaching a placeholder keeps the context's clips pointing at the real movie: a placeholder became
    /// their Movie, and detaching it after the save detached them too.</summary>
    private static List<int> Untracked(AppDbContext db, HashSet<int> movieIds)
    {
        var tracked = db.ChangeTracker.Entries<Movie>().Select(e => e.Entity.Id).ToHashSet();
        // A movie being added has a temporary (non-positive) key and is stale already.
        return movieIds.Where(id => id > 0 && !tracked.Contains(id)).ToList();
    }

    private void Mark(AppDbContext db, HashSet<int> movieIds)
    {
        // A movie being added has a temporary (non-positive) key and is stale already.
        movieIds.RemoveWhere(id => id <= 0);
        if (movieIds.Count == 0) return;

        // A movie being added starts stale anyway (several may share the unsaved Id 0); one being deleted takes
        // its rows with it. Every other tracked movie has a unique key, and one still untracked after the load is gone.
        var tracked = db.ChangeTracker.Entries<Movie>()
            .Where(e => e.State is not (EntityState.Added or EntityState.Deleted))
            .ToDictionary(e => e.Entity.Id);
        foreach (var movieId in movieIds)
        {
            if (!tracked.TryGetValue(movieId, out var entry)) continue;
            entry.Entity.ClipActorsStale = true;
            // Written even when the snapshot already says stale: the worker may have cleared it since.
            entry.Property(m => m.ClipActorsStale).IsModified = true;
        }
        marked.AddOrUpdate(db, Marker);
    }

    private void Finish(DbContext? context, bool saved)
    {
        if (context is null || !marked.Remove(context)) return;
        if (saved) signal.Signal();
    }
}
