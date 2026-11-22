using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Images;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

public sealed record ActorSearchResult(int Id, string FirstName, string? LastName, bool HasImage)
{
    public string DisplayName => ActorDisplayName.Format(FirstName, LastName);
}

public sealed record SearchResults(List<Movie> Movies, List<ActorSearchResult> Actors);

public interface ISearchService
{
    /// <summary>Matches movies by Code/MetaTitle/Title and actors by display name (all substring,
    /// case-insensitive via SQLite's default LIKE collation), each ranked with prefix matches
    /// first. Backs the header search box's live dropdown.</summary>
    Task<SearchResults> SearchAsync(string term, int limit = 6, CancellationToken ct = default);
}

public class SearchService(IDbContextFactory<AppDbContext> dbFactory) : ISearchService
{
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;

    public async Task<SearchResults> SearchAsync(string term, int limit = 6, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // % and _ in what was typed are literal characters, not wildcards.
        var contains = SqlLike.Contains(term);
        var startsWith = SqlLike.StartsWith(term);
        const string esc = SqlLike.EscapeChar;

        var matches = await db.Movies
            .AsNoTracking()
            .Where(m => (m.Code != null && EF.Functions.Like(m.Code, contains, esc))
                     || (m.MetaTitle != null && EF.Functions.Like(m.MetaTitle, contains, esc))
                     || (m.Title != null && EF.Functions.Like(m.Title, contains, esc)))
            .OrderByDescending(m => m.Code != null && EF.Functions.Like(m.Code, startsWith, esc))
            .ThenBy(m => m.Code)
            .Take(limit)
            .ToListAsync(ct);
        // Same poster versions as the Movies grid, so a cropped cover isn't the browser's cached old one.
        await PosterVersion.ApplyAsync(db, matches, ct);

        // An unidentified placeholder is shown on its own movie only, not found by search.
        var actorMatches = await db.Actors
            .Where(a => a.PlaceholderMovieId == null
                     && (EF.Functions.Like(a.FirstName!, contains, esc)
                         || (a.LastName != null && EF.Functions.Like(a.LastName + " " + a.FirstName, contains, esc))))
            .OrderByDescending(a => EF.Functions.Like(a.FirstName!, startsWith, esc)
                                  || (a.LastName != null && EF.Functions.Like(a.LastName + " " + a.FirstName, startsWith, esc)))
            .ThenBy(a => a.LastName)
            .ThenBy(a => a.FirstName)
            .Select(a => new ActorSearchResult(
                a.Id,
                a.FirstName ?? string.Empty,
                a.LastName,
                db.ActorImages.Any(ai => ai.ActorId == a.Id && ai.Variant == "thumb")))
            .Take(limit)
            .ToListAsync(ct);

        return new SearchResults(matches, actorMatches);
    }
}
