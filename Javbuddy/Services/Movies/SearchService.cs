using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Images;
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

        var matches = await db.Movies
            .AsNoTracking()
            .Where(m => (m.Code != null && EF.Functions.Like(m.Code, $"%{term}%"))
                     || (m.MetaTitle != null && EF.Functions.Like(m.MetaTitle, $"%{term}%"))
                     || (m.Title != null && EF.Functions.Like(m.Title, $"%{term}%")))
            .OrderByDescending(m => m.Code != null && EF.Functions.Like(m.Code, $"{term}%"))
            .ThenBy(m => m.Code)
            .Take(limit)
            .ToListAsync(ct);
        // Same poster versions as the Movies grid, so a cropped cover isn't the browser's cached old one.
        await PosterVersion.ApplyAsync(db, matches, ct);

        var actorMatches = await db.Actors
            .Where(a => EF.Functions.Like(a.FirstName!, $"%{term}%")
                     || (a.LastName != null && EF.Functions.Like(a.LastName + " " + a.FirstName, $"%{term}%")))
            .OrderByDescending(a => EF.Functions.Like(a.FirstName!, $"{term}%")
                                  || (a.LastName != null && EF.Functions.Like(a.LastName + " " + a.FirstName, $"{term}%")))
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
