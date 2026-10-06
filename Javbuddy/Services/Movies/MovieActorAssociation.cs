using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

/// <summary>Keeps tracked-actor links in sync with a movie's metadata cast summary.</summary>
public static class MovieActorAssociation
{
    public static async Task SynchronizeAsync(AppDbContext db, Movie movie, CancellationToken ct = default)
    {
        var actors = new ActorMatching.ActorIndex(await ActorMatching.LoadActorLookupsAsync(db, ct));
        var (actorIds, unmatchedNames) = GetActorIds(movie.MetaActresses, actors);
        ApplyUnmatchedActors(movie, unmatchedNames);

        var existingLinks = await db.MovieActors
            .Where(link => link.MovieId == movie.Id)
            .ToListAsync(ct);
        db.MovieActors.RemoveRange(existingLinks.Where(link => !actorIds.Contains(link.ActorId)));

        var existingActorIds = existingLinks.Select(link => link.ActorId).ToHashSet();
        foreach (var actorId in actorIds.Where(actorId => !existingActorIds.Contains(actorId)))
        {
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actorId });
        }
    }

    public static async Task SynchronizeAllAsync(AppDbContext db, CancellationToken ct = default)
    {
        // Only the cast summary and the two flag columns are needed here, so project them rather
        // than materializing every Movie with its long text columns. A movie this
        // context already tracks is used as-is, matching what a tracking query's identity
        // resolution would return (including any unsaved in-memory MetaActresses edit).
        var movies = await db.Movies
            .Select(movie => new MovieCastRow(movie.Id, movie.MetaActresses, movie.HasUnmatchedActors, movie.UnmatchedActorNames))
            .ToListAsync(ct);
        var trackedMovies = db.ChangeTracker.Entries<Movie>()
            .Where(entry => entry.State != EntityState.Added)
            .ToDictionary(entry => entry.Entity.Id, entry => entry.Entity);
        var actors = new ActorMatching.ActorIndex(await ActorMatching.LoadActorLookupsAsync(db, ct));
        var linksByMovie = (await db.MovieActors.ToListAsync(ct))
            .GroupBy(link => link.MovieId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var changedFlags = new Dictionary<int, List<string>>();

        foreach (var row in movies)
        {
            var trackedMovie = trackedMovies.GetValueOrDefault(row.Id);
            var (actorIds, unmatchedNames) = GetActorIds(trackedMovie is not null ? trackedMovie.MetaActresses : row.MetaActresses, actors);
            if (trackedMovie is not null)
            {
                ApplyUnmatchedActors(trackedMovie, unmatchedNames);
            }
            else if (row.HasUnmatchedActors != unmatchedNames.Count > 0 || row.UnmatchedActorNames != JoinUnmatchedNames(unmatchedNames))
            {
                changedFlags[row.Id] = unmatchedNames;
            }

            var existingLinks = linksByMovie.GetValueOrDefault(row.Id, []);
            db.MovieActors.RemoveRange(existingLinks.Where(link => !actorIds.Contains(link.ActorId)));

            var existingActorIds = existingLinks.Select(link => link.ActorId).ToHashSet();
            foreach (var actorId in actorIds.Where(actorId => !existingActorIds.Contains(actorId)))
            {
                db.MovieActors.Add(new MovieActor { MovieId = row.Id, ActorId = actorId });
            }
        }

        // Only movies whose flags actually change are loaded as tracked entities to be updated —
        // typically a handful, since per-movie SynchronizeAsync keeps them current in between.
        if (changedFlags.Count > 0)
        {
            var changedIds = changedFlags.Keys.ToList();
            var changedMovies = await db.Movies
                .Where(movie => changedIds.Contains(movie.Id))
                .ToListAsync(ct);
            foreach (var movie in changedMovies)
            {
                ApplyUnmatchedActors(movie, changedFlags[movie.Id]);
            }
        }
    }

    private sealed record MovieCastRow(int Id, string? MetaActresses, bool HasUnmatchedActors, string? UnmatchedActorNames);

    private static (HashSet<int> ActorIds, List<string> UnmatchedNames) GetActorIds(string? metaActresses, ActorMatching.ActorIndex actors)
    {
        // An actor is linked if any cast name matches one of its identities (display name,
        // reversed order, Japanese kanji/kana, R18Dev name, or alias); a name is unmatched if no
        // tracked actor's identity resolves to it. One index lookup per name answers both.
        var actorIds = new HashSet<int>();
        var unmatchedNames = new List<string>();
        foreach (var name in ActorMatching.SplitNames(metaActresses))
        {
            if (!actors.AddMatchingIds(name, actorIds))
            {
                unmatchedNames.Add(name);
            }
        }

        return (actorIds, unmatchedNames);
    }

    private static void ApplyUnmatchedActors(Movie movie, List<string> unmatchedNames)
    {
        movie.HasUnmatchedActors = unmatchedNames.Count > 0;
        movie.UnmatchedActorNames = JoinUnmatchedNames(unmatchedNames);
    }

    private static string? JoinUnmatchedNames(List<string> unmatchedNames) =>
        unmatchedNames.Count > 0 ? string.Join(", ", unmatchedNames) : null;
}
