using Javbuddy.Components.Shared;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.Tags;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

/// <summary>Every filter the Movies grid can apply at once. Collections are snapshots taken by
/// the caller, so a filter toggled mid-query can't change a query that's already been built.</summary>
public sealed record MovieGridFilter(
    MovieStatus? Status = null,
    string? Library = null,
    IReadOnlyCollection<MovieResolutionFilterOption>? Resolutions = null,
    IReadOnlyCollection<MovieScanTypeFilterOption>? ScanTypes = null,
    IReadOnlyCollection<string>? Codecs = null,
    IReadOnlyCollection<string>? Studios = null,
    IReadOnlyCollection<string>? Genres = null,
    IReadOnlyCollection<MovieFeatureFilterOption>? Features = null,
    string? Text = null,
    string? CodePrefix = null,
    IReadOnlyCollection<NfoDriftKind>? NfoDriftKinds = null,
    ActorAttributeSelection? ActorAttributes = null,
    IReadOnlyCollection<int>? ActorIds = null,
    IReadOnlyCollection<int>? ActorTagIds = null);

/// <summary>A tracked actor offered by an actor-name filter.</summary>
public sealed record MovieActorOption(int Id, string Name);

public sealed record MovieGridSort(string Field, bool Descending, int RandomSeed);

public sealed record MissingMoviesPage(IReadOnlyList<Movie> Movies, int TotalCount);

/// <summary>Status counts narrowed by the grid's other active filters, plus library-wide totals
/// and filter options that deliberately ignore every active filter.</summary>
public sealed record MovieGridSummary(
    int TotalCount,
    int MissingCount,
    int GotCount,
    int FilesCount,
    long TotalFileSizeBytes,
    List<string> LibraryNames,
    List<string> Codecs,
    List<string> Studios,
    List<string> Genres,
    ActorAttributeOptions ActorAttributes)
{
    /// <summary>The actor-name filter's options: tracked actors linked to any movie, by name. Like the
    /// lists above, unaffected by the active filters.</summary>
    public IReadOnlyList<MovieActorOption> Actors { get; init; } = [];

    /// <summary>The actor-tag filter's options: actor tags (Tag.IsActorTag) some actor has in a movie or one of its clips.</summary>
    public IReadOnlyList<MovieActorOption> ActorTags { get; init; } = [];
}

public interface IMovieGridQueryService
{
    Task<int> CountAsync(MovieGridFilter filter, CancellationToken ct = default);

    Task<MovieGridSummary> GetSummaryAsync(MovieGridFilter filter, CancellationToken ct = default);

    /// <summary>Only the columns the grid renders, untracked — the window is re-queried on every
    /// scroll, so a Movie row's unread text columns would be dead weight.</summary>
    Task<List<Movie>> GetRangeAsync(MovieGridFilter filter, MovieGridSort sort, int skip, int take, CancellationToken ct = default);

    /// <summary>Every Got movie matching <paramref name="filter"/> that has a local video file, in
    /// <paramref name="sort"/> order — the DeoVR lists; a Missing movie has nothing to
    /// play. Only the columns those lists render, untracked.</summary>
    Task<List<Movie>> GetWithFilesAsync(MovieGridFilter filter, MovieGridSort sort, CancellationToken ct = default);

    /// <summary>The tracked actors linked to any movie, by name — the same list as
    /// <see cref="MovieGridSummary.Actors"/>, on its own for a caller that already has the rest of
    /// the summary.</summary>
    Task<List<MovieActorOption>> GetActorOptionsAsync(CancellationToken ct = default);

    /// <summary>The actor-tag filter's options (as in <see cref="MovieGridSummary.ActorTags"/>), for a page restored from its
    /// prerendered state, which carries the other options but not these.</summary>
    Task<List<MovieActorOption>> GetActorTagOptionsAsync(CancellationToken ct = default);

    /// <summary>The tracked actors linked to a movie GetWithFilesAsync can list, by name — the DeoVR
    /// group editor's actor-name filter.</summary>
    Task<List<MovieActorOption>> GetActorOptionsWithFilesAsync(CancellationToken ct = default);

    /// <summary>One window of the movies marked Missing, sorted by release date (undated counts as oldest,
    /// ties by insertion order), plus how many there are in all — the Missing page's virtualized list
    ///. Only the columns it renders or the grab needs (Id, Code, title, studio, release date,
    /// whether metadata was fetched), untracked — a full row's description and media columns were most of
    /// the page's memory.</summary>
    Task<MissingMoviesPage> GetMissingPageAsync(bool sortDescending, int skip, int take, CancellationToken ct = default);

    /// <summary>Codes of movies with a torrent that's queued, downloading, seeding, or completed
    /// (but not yet imported).</summary>
    Task<HashSet<string>> GetDownloadingCodesAsync(CancellationToken ct = default);

    /// <summary>How many movies matching <paramref name="filter"/> drift in each direction
    /// (directions with no movies are absent) — the bulk .nfo push's confirmation counts.</summary>
    Task<Dictionary<NfoDriftKind, int>> GetNfoDriftCountsAsync(MovieGridFilter filter, CancellationToken ct = default);

    /// <summary>Ids of the movies matching <paramref name="filter"/> whose drift is one of
    /// <paramref name="kinds"/>, in no particular order — the bulk .nfo push's target set.</summary>
    Task<List<int>> GetNfoDriftMovieIdsAsync(MovieGridFilter filter, IReadOnlyCollection<NfoDriftKind> kinds, CancellationToken ct = default);
}

public sealed class MovieGridQueryService(IDbContextFactory<AppDbContext> dbFactory) : IMovieGridQueryService
{
    // int.MaxValue (2^31 - 1) is a Mersenne prime, so multiplying any nonzero seed by a value below
    // it and reducing mod it never collides — see BuildOrderedQuery's "random" case.
    private const long RandomSortModulus = int.MaxValue;

    public async Task<int> CountAsync(MovieGridFilter filter, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await BuildFilterQuery(db, filter).CountAsync(ct);
    }

    public async Task<MovieGridSummary> GetSummaryAsync(MovieGridFilter filter, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Status itself is exactly what these three counts distinguish, so it's left out — "All (N)"
        // still reflects every other active filter instead of the library's unfiltered total.
        var statusCountQuery = BuildFilterQuery(db, filter with { Status = null });
        var totalCount = await statusCountQuery.CountAsync(ct);
        var missingCount = await statusCountQuery.CountAsync(m => m.Status == MovieStatus.Missing, ct);
        var gotCount = await statusCountQuery.CountAsync(m => m.Status == MovieStatus.Got, ct);

        var filesCount = await db.MovieFiles.CountAsync(ct);
        if (filesCount == 0)
        {
            filesCount = await db.Movies.CountAsync(m => m.LocalFileSizeBytes != null, ct);
        }
        var totalFileSizeBytes = await db.Movies
            .Where(m => m.LocalFileSizeBytes != null)
            .SumAsync(m => m.LocalFileSizeBytes!.Value, ct);

        // Filter options are always the library's full set, unaffected by other active filters —
        // matching ActorMissing.razor's availableMakers/availableYears, so the lists don't shrink
        // as other filters narrow the grid.
        var libraryNames = await db.Movies
            .Where(m => m.JellyfinLibraryName != null && m.JellyfinLibraryName != "")
            .Select(m => m.JellyfinLibraryName!)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct);
        var codecs = await db.Movies
            .Where(m => m.MediaVideoCodec != null && m.MediaVideoCodec != "")
            .Select(m => m.MediaVideoCodec!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);
        var studios = await db.Movies
            .Where(m => m.MetaStudio != null && m.MetaStudio != "")
            .Select(m => m.MetaStudio!)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct);

        // Actress attribute options come from actors linked to a movie (an actor with no movies can't match
        // anything), and like the lists above ignore every active filter.
        var actorAttributes = await ActorAttributeOptionsQuery.LoadAsync(
            ActorAttributeOptionsQuery.ForMovies(db), DateOnly.FromDateTime(DateTime.UtcNow), ct);

        return new MovieGridSummary(
            totalCount, missingCount, gotCount, filesCount, totalFileSizeBytes,
            libraryNames, codecs, studios, await LoadGenreOptionsAsync(db, ct),
            actorAttributes)
        {
            Actors = await LoadLinkedActorOptionsAsync(db, ct),
            ActorTags = await LoadActorTagOptionsAsync(db, ct),
        };
    }

    // The actor tags some actor has in a movie or one of its clips (own rows: nothing flows down or rolls up a new tag).
    private static async Task<List<MovieActorOption>> LoadActorTagOptionsAsync(AppDbContext db, CancellationToken ct) =>
        [.. (await ActorTagOptions.LoadAsync(db.Tags.Where(t => t.IsActorTag && (db.MovieActorTags.Any(a => a.TagId == t.Id) || db.SceneActorTags.Any(a => a.TagId == t.Id)
                || db.HighlightActorTags.Any(a => a.TagId == t.Id) || db.ApexActorTags.Any(a => a.TagId == t.Id))), ct))
            .Select(o => new MovieActorOption(o.Id, o.Label))];

    /// <summary>Sourced from the canonical Tags/MovieTags relation, not MetaGenres's comma-joined
    /// cache string — a Tag name containing its own comma can't be told apart from two
    /// separate genres once joined into that string. The SQL DISTINCT returns one row per
    /// linked (tag, parent) pair, not one per movie.</summary>
    private static async Task<List<string>> LoadGenreOptionsAsync(AppDbContext db, CancellationToken ct)
    {
        // Actor tags (they reach every movie as plain tags) have their own "Actor tag" filter, as on the Scenes wall.
        var linkedTags = await db.MovieTags
            .Where(mt => !mt.Tag.IsActorTag)
            .Select(mt => new { mt.Tag.Name, ParentName = mt.Tag.ParentTag != null ? mt.Tag.ParentTag.Name : null })
            .Distinct()
            .ToListAsync(ct);

        return MovieFilterPredicates.BuildGenreOptions(linkedTags.Select(t => (t.Name, t.ParentName)));
    }

    public async Task<MissingMoviesPage> GetMissingPageAsync(bool sortDescending, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var missing = db.Movies
            .AsNoTracking()
            .Where(m => m.Status == MovieStatus.Missing);

        var total = await missing.CountAsync(ct);
        if (take <= 0) return new MissingMoviesPage([], total);

        // SQLite orders NULL below every date, so an undated movie counts as the oldest either way.
        // Id breaks ties so neighboring windows agree on where each movie sits.
        var ordered = sortDescending
            ? missing.OrderByDescending(m => m.MetaReleaseDate).ThenBy(m => m.Id)
            : missing.OrderBy(m => m.MetaReleaseDate).ThenBy(m => m.Id);

        var movies = await ordered
            .Skip(skip)
            .Take(take)
            .Select(m => new Movie
            {
                Id = m.Id,
                Code = m.Code,
                Status = m.Status,
                Title = m.Title,
                MetaTitle = m.MetaTitle,
                MetaStudio = m.MetaStudio,
                MetaReleaseDate = m.MetaReleaseDate,
                MetaFetchedAt = m.MetaFetchedAt,
            })
            .ToListAsync(ct);
        return new MissingMoviesPage(movies, total);
    }

    public async Task<List<Movie>> GetRangeAsync(MovieGridFilter filter, MovieGridSort sort, int skip, int take, CancellationToken ct = default)
    {
        if (take <= 0) return [];

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await BuildOrderedQuery(db, filter, sort)
            .Skip(skip)
            .Take(take)
            .AsNoTracking()
            .Select(m => new
            {
                Movie = new Movie
                {
                    Id = m.Id,
                    Code = m.Code,
                    Status = m.Status,
                    MetaSourceName = m.MetaSourceName,
                    MetaTitle = m.MetaTitle,
                    Title = m.Title,
                    LocalFileSizeBytes = m.LocalFileSizeBytes,
                    MediaWidth = m.MediaWidth,
                    MediaHeight = m.MediaHeight,
                    MediaScanType = m.MediaScanType,
                    FileCount = m.FileCount,
                    VrType = m.VrType,
                    MetaActresses = m.MetaActresses,
                    MetaFetchedAt = m.MetaFetchedAt,
                    HasUnmatchedActors = m.HasUnmatchedActors,
                    UnmatchedActorNames = m.UnmatchedActorNames,
                    IsFavorite = m.IsFavorite,
                },
                PosterCachedAt = db.CachedImages
                    .Where(c => c.Code == m.Code && c.Role == LocalImageCacheService.RolePoster && c.Index == 0)
                    .Max(c => (DateTime?)c.UpdatedAt),
                m.MetaCoverUrl,
            })
            .ToListAsync(ct);

        foreach (var row in rows) row.Movie.PosterVersion = PosterVersion.Compute(row.PosterCachedAt, row.MetaCoverUrl);
        return rows.Select(row => row.Movie).ToList();
    }

    public async Task<List<Movie>> GetWithFilesAsync(MovieGridFilter filter, MovieGridSort sort, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await BuildOrderedQuery(db, filter, sort)
            .Where(m => m.Status == MovieStatus.Got && m.MovieFiles.Any())
            .AsNoTracking()
            .Select(m => new Movie
            {
                Id = m.Id,
                Code = m.Code,
                MetaTitle = m.MetaTitle,
                Title = m.Title,
                MediaDurationSeconds = m.MediaDurationSeconds,
                MetaRuntimeMinutes = m.MetaRuntimeMinutes,
            })
            .ToListAsync(ct);
    }

    public async Task<List<MovieActorOption>> GetActorTagOptionsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await LoadActorTagOptionsAsync(db, ct);
    }

    public async Task<List<MovieActorOption>> GetActorOptionsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await LoadLinkedActorOptionsAsync(db, ct);
    }

    private static Task<List<MovieActorOption>> LoadLinkedActorOptionsAsync(AppDbContext db, CancellationToken ct) =>
        LoadActorOptionsAsync(db.Actors.Where(a => db.MovieActors.Any(ma => ma.ActorId == a.Id)), ct);

    public async Task<List<MovieActorOption>> GetActorOptionsWithFilesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await LoadActorOptionsAsync(
            db.Actors.Where(a => db.MovieActors.Any(ma => ma.ActorId == a.Id && ma.Movie.Status == MovieStatus.Got && ma.Movie.MovieFiles.Any())), ct);
    }

    // DisplayName is computed in C#, so the actors are loaded first and named and sorted here.
    private static async Task<List<MovieActorOption>> LoadActorOptionsAsync(IQueryable<Actor> actors, CancellationToken ct) =>
        (await actors.AsNoTracking().ToListAsync(ct))
            .Select(a => new MovieActorOption(a.Id, a.DisplayName))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<HashSet<string>> GetDownloadingCodesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await QueryDownloadingCodesAsync(db, ct);
    }

    /// <summary>Shared with Actor Detail's poster grid.</summary>
    internal static async Task<HashSet<string>> QueryDownloadingCodesAsync(AppDbContext db, CancellationToken ct)
    {
        var codes = await db.TorrentDownloads
            .Where(t => t.Status == TorrentDownloadStatus.Queued
                || t.Status == TorrentDownloadStatus.Downloading
                || t.Status == TorrentDownloadStatus.Seeding
                || t.Status == TorrentDownloadStatus.Completed)
            .Select(t => t.MovieCode)
            .Distinct()
            .ToListAsync(ct);
        return new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Dictionary<NfoDriftKind, int>> GetNfoDriftCountsAsync(MovieGridFilter filter, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await BuildFilterQuery(db, filter)
            .Where(m => m.NfoDriftKind != NfoDriftKind.None)
            .GroupBy(m => m.NfoDriftKind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Kind, g => g.Count, ct);
    }

    public async Task<List<int>> GetNfoDriftMovieIdsAsync(MovieGridFilter filter, IReadOnlyCollection<NfoDriftKind> kinds, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await BuildFilterQuery(db, filter)
            .Where(m => kinds.Contains(m.NfoDriftKind))
            .Select(m => m.Id)
            .ToListAsync(ct);
    }

    private static IQueryable<Movie> BuildFilterQuery(AppDbContext db, MovieGridFilter filter)
    {
        IQueryable<Movie> query = db.Movies;

        if (filter.Status is { } status)
        {
            query = query.Where(m => m.Status == status);
        }
        if (!string.IsNullOrEmpty(filter.Library))
        {
            var library = filter.Library;
            query = query.Where(m => m.JellyfinLibraryName == library);
        }
        query = MovieFilterPredicates.WhereResolutions(query, filter.Resolutions);
        query = MovieFilterPredicates.WhereScanTypes(query, filter.ScanTypes);
        query = MovieFilterPredicates.WhereCodecs(query, filter.Codecs);
        query = MovieFilterPredicates.WhereStudios(query, filter.Studios);
        query = MovieFilterPredicates.WhereGenres(query, filter.Genres);
        query = MovieFilterPredicates.WhereFeatures(query, filter.Features);
        query = MovieFilterPredicates.WhereNfoDriftKinds(query, filter.NfoDriftKinds);
        if (filter.ActorIds is { Count: > 0 } actorIds)
        {
            // Any selected actor, like the Scenes wall's actor filter.
            query = query.Where(m => m.MovieActors.Any(ma => actorIds.Contains(ma.ActorId)));
        }
        if (filter.ActorTagIds is { Count: > 0 } actorTagIds)
        {
            // An actor tag (Tag.IsActorTag) on the movie or on any of its scenes, highlights or apexes; with the actor
            // filter, one of those actors must have it. Own rows are enough: what flows down or rolls up never adds a pair.
            var anyActor = filter.ActorIds is not { Count: > 0 };
            var onlyActors = filter.ActorIds ?? [];
            // A parent tag matches its subtags, as for plain tags.
            var matching = db.Tags.Where(t => actorTagIds.Contains(t.Id) || (t.ParentTagId != null && actorTagIds.Contains(t.ParentTagId.Value))).Select(t => t.Id);
            var withActorTag = db.MovieActorTags.Where(t => matching.Contains(t.TagId) && (anyActor || onlyActors.Contains(t.ActorId))).Select(t => t.MovieId)
                .Union(db.SceneActorTags.Where(t => matching.Contains(t.TagId) && (anyActor || onlyActors.Contains(t.ActorId))).Select(t => t.MovieId))
                .Union(db.HighlightActorTags.Where(t => matching.Contains(t.TagId) && (anyActor || onlyActors.Contains(t.ActorId))).Select(t => t.MovieId))
                .Union(db.ApexActorTags.Where(t => matching.Contains(t.TagId) && (anyActor || onlyActors.Contains(t.ActorId))).Select(t => t.MovieId));
            query = query.Where(m => withActorTag.Contains(m.Id));
        }
        query = MovieFilterPredicates.WhereActorAttributes(query, filter.ActorAttributes ?? ActorAttributeSelection.Empty, DateOnly.FromDateTime(DateTime.UtcNow));
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var term = filter.Text.Trim();
            query = query.Where(m =>
                (m.Code != null && EF.Functions.Like(m.Code, $"%{term}%"))
                || (m.MetaTitle != null && EF.Functions.Like(m.MetaTitle, $"%{term}%"))
                || (m.Title != null && EF.Functions.Like(m.Title, $"%{term}%")));
        }
        if (!string.IsNullOrWhiteSpace(filter.CodePrefix))
        {
            var codePrefix = filter.CodePrefix;
            query = query.Where(m => m.Code != null && EF.Functions.Like(m.Code, $"{codePrefix}%"));
        }

        return query;
    }

    private static IQueryable<Movie> BuildOrderedQuery(AppDbContext db, MovieGridFilter filter, MovieGridSort sort)
    {
        var query = BuildFilterQuery(db, filter);
        var descending = sort.Descending;

        return sort.Field switch
        {
            "title" => descending
                ? query.OrderByDescending(m => m.Code)
                : query.OrderBy(m => m.Code),
            "size" => descending
                ? query.OrderBy(m => m.LocalFileSizeBytes == null).ThenByDescending(m => m.LocalFileSizeBytes)
                : query.OrderBy(m => m.LocalFileSizeBytes == null).ThenBy(m => m.LocalFileSizeBytes),
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
            // Favorites first when descending (the default — see MovieFilterOptions.DefaultSortDescending),
            // then by code within each group, matching the Actors page's "Favorites" sort.
            "favorites" => descending
                ? query.OrderByDescending(m => m.IsFavorite).ThenBy(m => m.Code)
                : query.OrderBy(m => m.IsFavorite).ThenBy(m => m.Code),
            // A fixed pseudo-random order, not ORDER BY RANDOM(): multiplying by a seed coprime
            // with the modulus (any nonzero seed, since the modulus is prime) and reducing mod that
            // modulus is a bijection over ids in range, i.e. a full shuffle — and unlike RANDOM(),
            // it gives the same order across every Skip/Take call until the seed changes.
            "random" => query.OrderBy(m => (long)m.Id * sort.RandomSeed % RandomSortModulus),
            // Falls back to CreatedAt (DB-insert time) for a movie with no resolved local file yet
            // (manually-added, still Missing, or not yet probed since import) — see Movie.FileAddedAt.
            _ => descending
                ? query.OrderByDescending(m => m.FileAddedAt ?? m.CreatedAt)
                : query.OrderBy(m => m.FileAddedAt ?? m.CreatedAt),
        };
    }
}
