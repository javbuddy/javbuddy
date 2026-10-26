using System.Linq.Expressions;
using Javbuddy.Models;

namespace Javbuddy.Services.Movies;

/// <summary>The Sort menu's orders, shared by the Movies grid (translated to SQL) and Actor Detail (applied in memory via
/// AsQueryable), like <see cref="MovieFilterPredicates"/>. In memory, the movies need Scenes, Highlights, Apexes and
/// MovieActors loaded. A movie without the sorted value goes last either way.</summary>
public static class MovieSortOrders
{
    // int.MaxValue (2^31 - 1) is a Mersenne prime, so multiplying any nonzero seed by a value below
    // it and reducing mod it never collides — see the "random" case.
    private const long RandomSortModulus = int.MaxValue;

    /// <param name="dateAdded">The "added" key: the DateAdded column in SQL, its own expression in memory (EF.Property
    /// only works inside an EF query).</param>
    public static IQueryable<Movie> Apply(IQueryable<Movie> query, MovieGridSort sort, Expression<Func<Movie, DateTime>> dateAdded)
    {
        var descending = sort.Descending;

        return sort.Field switch
        {
            "title" => descending
                ? query.OrderByDescending(m => m.Code)
                : query.OrderBy(m => m.Code),
            // Natural order: by the prefix before the first '-', then the rest by length before text, so ABC-2 comes
            // before ABC-10. A code without a '-' is all prefix.
            "code" => descending
                ? query.OrderBy(m => m.Code == null)
                    .ThenByDescending(m => m.Code!.Contains('-') ? m.Code.Substring(0, m.Code.IndexOf('-')) : m.Code)
                    .ThenByDescending(m => m.Code!.Contains('-') ? m.Code.Length - m.Code.IndexOf('-') : 0)
                    .ThenByDescending(m => m.Code)
                : query.OrderBy(m => m.Code == null)
                    .ThenBy(m => m.Code!.Contains('-') ? m.Code.Substring(0, m.Code.IndexOf('-')) : m.Code)
                    .ThenBy(m => m.Code!.Contains('-') ? m.Code.Length - m.Code.IndexOf('-') : 0)
                    .ThenBy(m => m.Code),
            "size" => descending
                ? query.OrderBy(m => m.LocalFileSizeBytes == null).ThenByDescending(m => m.LocalFileSizeBytes)
                : query.OrderBy(m => m.LocalFileSizeBytes == null).ThenBy(m => m.LocalFileSizeBytes),
            // The video stream's bitrate, else the whole file's (some containers report only that), as the Bitrate filter.
            // Every Missing movie has none, so code breaks ties and neighboring windows agree.
            "bitrate" => descending
                ? query.OrderBy(m => (m.MediaVideoBitRateKbps ?? m.MediaOverallBitRateKbps) == null).ThenByDescending(m => m.MediaVideoBitRateKbps ?? m.MediaOverallBitRateKbps).ThenBy(m => m.Code)
                : query.OrderBy(m => (m.MediaVideoBitRateKbps ?? m.MediaOverallBitRateKbps) == null).ThenBy(m => m.MediaVideoBitRateKbps ?? m.MediaOverallBitRateKbps).ThenBy(m => m.Code),
            // Status is persisted as a string (see AppDbContext), so sorting the column directly
            // would order alphabetically ("Got" < "Missing") rather than Missing-before-Got — order
            // by a rank instead so ascending always means Missing first, Got last.
            "status" => descending
                ? query.OrderBy(m => m.Status == MovieStatus.Missing)
                : query.OrderByDescending(m => m.Status == MovieStatus.Missing),
            "release" => descending
                ? query.OrderBy(m => m.MetaReleaseDate == null).ThenByDescending(m => m.MetaReleaseDate)
                : query.OrderBy(m => m.MetaReleaseDate == null).ThenBy(m => m.MetaReleaseDate),
            "runtime" => descending
                ? query.OrderBy(m => m.MetaRuntimeMinutes == null).ThenByDescending(m => m.MetaRuntimeMinutes)
                : query.OrderBy(m => m.MetaRuntimeMinutes == null).ThenBy(m => m.MetaRuntimeMinutes),
            // Date favorited: favorites by when they were favorited (newest first when descending, the default), the rest
            // after them by code. The key stays "favorites", the field the earlier favorites-first sort was saved under.
            "favorites" => descending
                ? query.OrderByDescending(m => m.IsFavorite).ThenBy(m => m.FavoritedAt == null).ThenByDescending(m => m.FavoritedAt).ThenBy(m => m.Code)
                : query.OrderByDescending(m => m.IsFavorite).ThenBy(m => m.FavoritedAt == null).ThenBy(m => m.FavoritedAt).ThenBy(m => m.Code),
            // The counts tie a lot (most movies have none), so code breaks ties and neighboring windows agree.
            "scenes" => descending
                ? query.OrderByDescending(m => m.Scenes.Count).ThenBy(m => m.Code)
                : query.OrderBy(m => m.Scenes.Count).ThenBy(m => m.Code),
            "highlights" => descending
                ? query.OrderByDescending(m => m.Highlights.Count).ThenBy(m => m.Code)
                : query.OrderBy(m => m.Highlights.Count).ThenBy(m => m.Code),
            "apexes" => descending
                ? query.OrderByDescending(m => m.Apexes.Count).ThenBy(m => m.Code)
                : query.OrderBy(m => m.Apexes.Count).ThenBy(m => m.Code),
            // Linked (tracked) actors, as the Cast size filter counts them.
            "cast" => descending
                ? query.OrderByDescending(m => m.MovieActors.Count).ThenBy(m => m.Code)
                : query.OrderBy(m => m.MovieActors.Count).ThenBy(m => m.Code),
            // A fixed pseudo-random order, not ORDER BY RANDOM(): multiplying by a seed coprime
            // with the modulus (any nonzero seed, since the modulus is prime) and reducing mod that
            // modulus is a bijection over ids in range, i.e. a full shuffle — and unlike RANDOM(),
            // it gives the same order across every Skip/Take call until the seed changes.
            "random" => query.OrderBy(m => (long)m.Id * sort.RandomSeed % RandomSortModulus),
            // DateAdded is FileAddedAt, falling back to CreatedAt (DB-insert time) for a movie with no resolved
            // local file yet (manually-added, still Missing, or not yet probed since import) — see Movie.FileAddedAt.
            _ => descending
                ? query.OrderByDescending(dateAdded)
                : query.OrderBy(dateAdded),
        };
    }
}
