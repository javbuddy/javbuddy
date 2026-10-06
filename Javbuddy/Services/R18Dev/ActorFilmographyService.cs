using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.R18Dev;

public sealed record ActorFilmographyResult(bool DumpAvailable, int LinkedMovieCount, IReadOnlyList<R18DevReleaseRow> Rows);

public interface IActorFilmographyService
{
    /// <summary>The actor's r18.dev filmography, each release marked Got / Wanted / Missing
    /// against the local library (exact normalized code first, then canonical key).</summary>
    Task<ActorFilmographyResult> GetFilmographyAsync(int actorId, string displayName, string? r18DevName, CancellationToken ct = default);

    /// <summary>Stores the actor's r18.dev name override (trimmed; blank clears it) and returns
    /// the stored value.</summary>
    Task<string?> SaveR18DevNameAsync(int actorId, string? r18DevName, CancellationToken ct = default);
}

/// <summary>Backs the actor Missing page: joins the sidecar dump store (which can't see the main
/// database) with the local library.</summary>
public class ActorFilmographyService(IDbContextFactory<AppDbContext> dbFactory, IR18DevDumpStore dumpStore) : IActorFilmographyService
{
    public async Task<ActorFilmographyResult> GetFilmographyAsync(int actorId, string displayName, string? r18DevName, CancellationToken ct = default)
    {
        List<string?> linkedCodes;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            linkedCodes = await db.MovieActors
                .Where(link => link.ActorId == actorId)
                .Select(link => link.Movie.Code)
                .ToListAsync(ct);
        }

        var result = await dumpStore.GetFilmographyForActorAsync(linkedCodes.OfType<string>().ToList(), displayName, r18DevName, ct);
        if (!result.DumpAvailable)
        {
            return new ActorFilmographyResult(false, linkedCodes.Count, Array.Empty<R18DevReleaseRow>());
        }

        List<Movie> localMovies;
        List<DeletedMovie> deletedMovies;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            localMovies = await db.Movies
                .Where(m => m.Code != null)
                .AsNoTracking()
                .Select(m => new Movie { Code = m.Code, Status = m.Status })
                .ToListAsync(ct);
            deletedMovies = await DeletedMovieHistory.GetMatchesAsync(db, result.Movies.Select(m => m.DvdId), ct);
        }

        return new ActorFilmographyResult(true, linkedCodes.Count, R18DevReleaseMatcher.Classify(result.Movies, localMovies, deletedMovies));
    }

    public async Task<string?> SaveR18DevNameAsync(int actorId, string? r18DevName, CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(r18DevName) ? null : r18DevName.Trim();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var actor = await db.Actors.FindAsync([actorId], ct);
        if (actor is null) return normalized;

        actor.R18DevName = normalized;
        await db.SaveChangesAsync(ct);
        return normalized;
    }
}
