using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

/// <summary>One name from a movie's cast text, with the tracked actor it resolved to (if any).</summary>
public sealed record MovieCastEntry(string Name, int? ActorId, bool HasImage, long? ImageVersion = null, int? Age = null, DateTime? BirthDate = null)
{
    // Performers are at least MinimumActorAge, so an age at release below it means bad metadata:
    // a wrong release date, a wrong birthdate, or the name matched the wrong actor.
    public bool IsUnderage => Age < ActorPhysicalAttributesHelper.MinimumActorAge;
}

public interface IMovieDetailQueryService
{
    /// <summary>The movie's cast text split into names, each matched against the actors linked to it
    /// (by display name, reversed name, Japanese names, r18.dev name or alias), with portrait presence
    /// and age at the movie's release.</summary>
    Task<List<MovieCastEntry>> GetCastAsync(Movie movie, CancellationToken ct = default);

    /// <summary><see cref="GetCastAsync(Movie, CancellationToken)"/> for the movie's current cast
    /// text, re-read by ID; empty when the movie doesn't exist.</summary>
    Task<List<MovieCastEntry>> GetCastAsync(int movieId, CancellationToken ct = default);

    /// <summary>The movie with this code (case-insensitive) with its files and tags (and their parent tags), or null.</summary>
    Task<Movie?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>A movie's tag links with each tag and its parent, for re-reading the tag chips after a change made elsewhere.</summary>
    Task<List<MovieTag>> GetMovieTagsAsync(int movieId, CancellationToken ct = default);
}

/// <summary>Movie Detail's reads, kept out of the page so it doesn't open a DbContext itself.</summary>
public class MovieDetailQueryService(IDbContextFactory<AppDbContext> dbFactory) : IMovieDetailQueryService
{
    public async Task<Movie?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Movies
            .AsNoTrackingWithIdentityResolution()
            .Include(m => m.MovieFiles)
            .Include(m => m.MovieTags).ThenInclude(mt => mt.Tag).ThenInclude(t => t.ParentTag)
            // Code is a NOCASE column, so plain equality is case-insensitive and seeks IX_Movies_Code.
            .FirstOrDefaultAsync(m => m.Code == code, ct);
    }

    public async Task<List<MovieTag>> GetMovieTagsAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.MovieTags.AsNoTracking()
            .Where(mt => mt.MovieId == movieId)
            .Include(mt => mt.Tag).ThenInclude(t => t.ParentTag)
            .ToListAsync(ct);
    }

    public async Task<List<MovieCastEntry>> GetCastAsync(int movieId, CancellationToken ct = default)
    {
        Movie? movie;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == movieId, ct);
        }
        return movie is null ? [] : await GetCastAsync(movie, ct);
    }

    public async Task<List<MovieCastEntry>> GetCastAsync(Movie movie, CancellationToken ct = default)
    {
        var castNames = ActorMatching.SplitNames(movie.MetaActresses).ToList();
        if (castNames.Count == 0) return [];

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var linkedActors = (await db.MovieActors
            .Where(link => link.MovieId == movie.Id)
            .Select(link => new
            {
                link.Actor.Id,
                link.Actor.FirstName,
                link.Actor.LastName,
                link.Actor.JapaneseNameKanji,
                link.Actor.JapaneseNameKana,
                link.Actor.R18DevName,
                link.Actor.BirthDate,
                Aliases = link.Actor.Aliases.Select(a => a.Name).ToList()
            })
            .ToListAsync(ct))
            .Select(link => new
            {
                link.Id,
                DisplayName = ActorDisplayName.Format(link.FirstName ?? string.Empty, link.LastName),
                ReversedName = !string.IsNullOrWhiteSpace(link.LastName) ? $"{link.FirstName} {link.LastName}" : null,
                link.JapaneseNameKanji,
                link.JapaneseNameKana,
                link.R18DevName,
                link.BirthDate,
                link.Aliases
            })
            .ToList();

        var linkedIds = linkedActors.Select(a => a.Id).ToList();
        var actorThumbnails = await db.ActorImages
            .Where(ai => ai.Variant == "thumb" && linkedIds.Contains(ai.ActorId))
            .Select(ai => new { ai.ActorId, ai.UpdatedAt })
            .ToDictionaryAsync(ai => ai.ActorId, ai => ai.UpdatedAt.Ticks, ct);

        return castNames.Select(name =>
        {
            var compactName = name.Replace(" ", "");
            var match = linkedActors.FirstOrDefault(actor =>
                string.Equals(actor.DisplayName, name, StringComparison.OrdinalIgnoreCase)
                || (actor.ReversedName is not null && string.Equals(actor.ReversedName, name, StringComparison.OrdinalIgnoreCase))
                || (actor.JapaneseNameKanji is not null && (string.Equals(actor.JapaneseNameKanji, name, StringComparison.OrdinalIgnoreCase) || actor.JapaneseNameKanji.Replace(" ", "").Equals(compactName, StringComparison.OrdinalIgnoreCase)))
                || (actor.JapaneseNameKana is not null && (string.Equals(actor.JapaneseNameKana, name, StringComparison.OrdinalIgnoreCase) || actor.JapaneseNameKana.Replace(" ", "").Equals(compactName, StringComparison.OrdinalIgnoreCase)))
                || (actor.R18DevName is not null && string.Equals(actor.R18DevName, name, StringComparison.OrdinalIgnoreCase))
                || actor.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase) || a.Replace(" ", "").Equals(compactName, StringComparison.OrdinalIgnoreCase)));
            var hasImage = match is not null && actorThumbnails.ContainsKey(match.Id);
            var imageVersion = match is not null && actorThumbnails.TryGetValue(match.Id, out var v) ? v : (long?)null;
            var age = ActorPhysicalAttributesHelper.CalculateAgeAtRelease(match?.BirthDate, movie.MetaReleaseDate);
            return new MovieCastEntry(name, match?.Id, hasImage, imageVersion, age, match?.BirthDate);
        }).ToList();
    }
}
