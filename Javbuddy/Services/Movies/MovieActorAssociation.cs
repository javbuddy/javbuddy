using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

/// <summary>Keeps tracked-actor links in sync with a movie's metadata cast summary, and normalizes
/// that summary so each tracked actor appears once, under their main name.</summary>
public static class MovieActorAssociation
{
    public static async Task SynchronizeAsync(AppDbContext db, Movie movie, CancellationToken ct = default)
    {
        var actors = new ActorMatching.ActorIndex(await ActorMatching.LoadActorLookupsAsync(db, ct));
        var cast = ResolveCast(movie.MetaActresses, actors);
        ApplyCast(movie, cast);

        var existingLinks = await db.MovieActors
            .Where(link => link.MovieId == movie.Id)
            .ToListAsync(ct);
        db.MovieActors.RemoveRange(existingLinks.Where(link => !cast.ActorIds.Contains(link.ActorId)));

        var existingActorIds = existingLinks.Select(link => link.ActorId).ToHashSet();
        foreach (var actorId in cast.ActorIds.Where(actorId => !existingActorIds.Contains(actorId)))
        {
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actorId });
        }
    }

    /// <summary>The cast summary as a sync would store it (see <see cref="ResolveCast"/>), for
    /// previewing an incoming cast against the stored one without changing anything.</summary>
    public static async Task<string?> NormalizeCastAsync(AppDbContext db, string? metaActresses, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(metaActresses)) return metaActresses;
        var actors = new ActorMatching.ActorIndex(await ActorMatching.LoadActorLookupsAsync(db, ct));
        return ResolveCast(metaActresses, actors).MetaActresses;
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
        var changedCasts = new Dictionary<int, ResolvedCast>();

        foreach (var row in movies)
        {
            var trackedMovie = trackedMovies.GetValueOrDefault(row.Id);
            var cast = ResolveCast(trackedMovie is not null ? trackedMovie.MetaActresses : row.MetaActresses, actors);
            if (trackedMovie is not null)
            {
                ApplyCast(trackedMovie, cast);
            }
            else if (row.MetaActresses != cast.MetaActresses
                || row.HasUnmatchedActors != cast.UnmatchedNames.Count > 0
                || row.UnmatchedActorNames != JoinUnmatchedNames(cast.UnmatchedNames))
            {
                changedCasts[row.Id] = cast;
            }

            var existingLinks = linksByMovie.GetValueOrDefault(row.Id, []);
            db.MovieActors.RemoveRange(existingLinks.Where(link => !cast.ActorIds.Contains(link.ActorId)));

            var existingActorIds = existingLinks.Select(link => link.ActorId).ToHashSet();
            foreach (var actorId in cast.ActorIds.Where(actorId => !existingActorIds.Contains(actorId)))
            {
                db.MovieActors.Add(new MovieActor { MovieId = row.Id, ActorId = actorId });
            }
        }

        // Only movies whose cast or flags actually change are loaded as tracked entities to be
        // updated — typically a handful, since per-movie SynchronizeAsync keeps them current in between.
        if (changedCasts.Count > 0)
        {
            var changedIds = changedCasts.Keys.ToList();
            var changedMovies = await db.Movies
                .Where(movie => changedIds.Contains(movie.Id))
                .ToListAsync(ct);
            foreach (var movie in changedMovies)
            {
                ApplyCast(movie, changedCasts[movie.Id]);
            }
        }
    }

    private sealed record MovieCastRow(int Id, string? MetaActresses, bool HasUnmatchedActors, string? UnmatchedActorNames);

    private sealed record ResolvedCast(string? MetaActresses, HashSet<int> ActorIds, List<string> UnmatchedNames);

    /// <summary>Resolves a cast summary against the tracked actors. An actor is linked if any cast
    /// name matches one of its identities (display name, reversed order, Japanese kanji/kana,
    /// R18Dev name, or alias); a name is unmatched if no tracked actor's identity resolves to it.
    /// A name resolving to exactly one actor is rewritten to that actor's main name, and repeats
    /// collapse onto the first, so "Julia, Kyoko" with Kyoko an alias of Julia becomes "Julia". A
    /// name matching several actors is ambiguous and kept as written. The summary is returned
    /// unchanged (delimiters included) when no name was rewritten or dropped.</summary>
    private static ResolvedCast ResolveCast(string? metaActresses, ActorMatching.ActorIndex actors)
    {
        var actorIds = new HashSet<int>();
        var unmatchedNames = new List<string>();
        var names = ActorMatching.SplitNames(metaActresses).ToList();
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (!actors.AddMatchingIds(name, actorIds))
            {
                if (seen.Add(name))
                {
                    unmatchedNames.Add(name);
                    normalized.Add(name);
                }
                continue;
            }

            // The main name must itself resolve only to that actor (another actor's alias could
            // match it), or the next sync would read the rewritten name as ambiguous and link both.
            var match = actors.FindSingleMatch(name);
            var resolved = match is not null && actors.FindSingleMatch(match.DisplayName)?.Id == match.Id
                ? match.DisplayName
                : name;
            if (seen.Add(resolved))
            {
                normalized.Add(resolved);
            }
        }

        var cast = normalized.SequenceEqual(names, StringComparer.Ordinal)
            ? metaActresses
            : (normalized.Count > 0 ? string.Join(", ", normalized) : null);
        return new ResolvedCast(cast, actorIds, unmatchedNames);
    }

    private static void ApplyCast(Movie movie, ResolvedCast cast)
    {
        movie.MetaActresses = cast.MetaActresses;
        movie.HasUnmatchedActors = cast.UnmatchedNames.Count > 0;
        movie.UnmatchedActorNames = JoinUnmatchedNames(cast.UnmatchedNames);
    }

    private static string? JoinUnmatchedNames(List<string> unmatchedNames) =>
        unmatchedNames.Count > 0 ? string.Join(", ", unmatchedNames) : null;
}
