using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.R18Dev;

/// <summary>Keeps the last <see cref="R18DevLibrarySnapshot"/> for a few seconds so the catalog browse, counts and the
/// add wizard's suggestions (one call per keystroke) don't each reload every movie and deleted-movie row. A library
/// change reported through <see cref="MovieChangeNotifier"/> drops it at once; the short lifetime covers changes
/// that don't report there. Concurrent callers share one load. Singleton.</summary>
public sealed class R18DevLibrarySnapshotCache : IDisposable
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    private readonly IDbContextFactory<AppDbContext> dbFactory;
    private readonly TimeProvider timeProvider;
    private readonly MovieChangeNotifier? changeNotifier;
    private readonly Lock gate = new();
    private Task<R18DevLibrarySnapshot>? current;
    private DateTimeOffset loadedAt;

    public R18DevLibrarySnapshotCache(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider, MovieChangeNotifier? changeNotifier = null)
    {
        this.dbFactory = dbFactory;
        this.timeProvider = timeProvider;
        this.changeNotifier = changeNotifier;
        if (changeNotifier is not null) changeNotifier.Changed += Invalidate;
    }

    public void Invalidate()
    {
        lock (gate) current = null;
    }

    public async Task<R18DevLibrarySnapshot> GetAsync(CancellationToken ct = default)
    {
        Task<R18DevLibrarySnapshot> load;
        lock (gate)
        {
            if (current is null || timeProvider.GetUtcNow() - loadedAt >= Lifetime || current.IsFaulted || current.IsCanceled)
            {
                var started = LoadAsync();
                current = started;
                loadedAt = timeProvider.GetUtcNow();
            }

            load = current;
        }

        // The load is shared, so one caller's cancellation must not cancel it for the others.
        return await load.WaitAsync(ct);
    }

    private async Task<R18DevLibrarySnapshot> LoadAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        // Only what matching reads (Code, Status), untracked — the full Movie rows carry
        // description/genre text that's dead weight here.
        var movies = await db.Movies
            .Where(m => m.Code != null)
            .AsNoTracking()
            .Select(m => new Movie { Code = m.Code, Status = m.Status })
            .ToListAsync();
        // The whole history: it only grows by one row per deleted movie, and the status filters need
        // every key, not just the page's.
        var deleted = await db.DeletedMovies.AsNoTracking().ToListAsync();
        return new R18DevLibrarySnapshot(movies, deleted);
    }

    public void Dispose()
    {
        if (changeNotifier is not null) changeNotifier.Changed -= Invalidate;
    }
}
