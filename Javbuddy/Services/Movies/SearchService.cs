using System.Linq.Expressions;
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
    /// case-insensitive: see <see cref="CaseInsensitiveSearch"/>), each ranked with prefix matches
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
            .Where(db.ContainsAny<Movie>(term, m => m.Code, m => m.MetaTitle, m => m.Title))
            .OrderByDescending(db.StartsWithAny<Movie>(term, m => m.Code))
            .ThenBy(m => m.Code)
            .Take(limit)
            .ToListAsync(ct);
        // Same poster versions as the Movies grid, so a cropped cover isn't the browser's cached old one.
        await PosterVersion.ApplyAsync(db, matches, ct);

        // "LastName FirstName" is only a name when there is a last name.
        Expression<Func<Actor, bool>> hasLastName = a => a.LastName != null;
        var actorMatches = await db.Actors
            .Where(db.ContainsAny<Actor>(term, a => a.FirstName).Or(hasLastName.And(db.ContainsAny<Actor>(term, a => a.LastName + " " + a.FirstName))))
            .OrderByDescending(db.StartsWithAny<Actor>(term, a => a.FirstName).Or(hasLastName.And(db.StartsWithAny<Actor>(term, a => a.LastName + " " + a.FirstName))))
            .ThenBy(a => a.LastName != null)
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
