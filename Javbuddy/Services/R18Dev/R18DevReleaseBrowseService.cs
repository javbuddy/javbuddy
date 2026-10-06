using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.R18Dev;

/// <summary>One page of the catalog browse. <paramref name="NextOffset"/> is where the next page
/// starts; <paramref name="HasMore"/> is false once the catalog has no more matches.</summary>
public sealed record R18DevReleaseBrowseResult(
    bool Enabled,
    R18DevCatalogAvailability Availability,
    IReadOnlyList<R18DevReleaseRow> Rows,
    int NextOffset = 0,
    bool HasMore = false)
{
    public bool DumpAvailable => Availability != R18DevCatalogAvailability.NoDump;
}

/// <summary>How many distinct releases match a catalog filter, in total and per library status.
/// <paramref name="Total"/> and <paramref name="Untracked"/> leave out the
/// <paramref name="PreviouslyDeleted"/> ones when the filter hides them.</summary>
public sealed record R18DevCatalogCounts(int Total, int Untracked, int Wanted, int Got, int PreviouslyDeleted = 0)
{
    public int For(R18DevCatalogStatus status) => status switch
    {
        R18DevCatalogStatus.Untracked => Untracked,
        R18DevCatalogStatus.Wanted => Wanted,
        R18DevCatalogStatus.Got => Got,
        _ => Total,
    };
}

/// <summary>Releases sharing a series with a movie, and how many there are in all — an r18.dev
/// preview's "More from this series" shelf. <paramref name="Series"/> and <paramref name="Label"/> also
/// back Movie Detail's series and label badges, tracked or not: a movie without a series has no shelf.</summary>
public sealed record R18DevRelatedReleases(R18DevCatalogRef? Series, R18DevCatalogRef? Label, IReadOnlyList<R18DevReleaseRow> Rows, int Total)
{
    public static readonly R18DevRelatedReleases None = new(null, null, [], 0);
}

public interface IR18DevReleaseBrowseService
{
    /// <summary>One page of r18.dev releases matching <paramref name="filter"/>, newest first, each
    /// marked Got / Wanted / Missing against the local library. The filter's library status is applied
    /// over the whole catalog, not just this page. Retailer/distributor variants of one release are
    /// collapsed and distributor-only releases hidden, as on the actor Missing page's defaults;
    /// releases whose canonical code is in <paramref name="alreadyShown"/> (shown on an earlier page)
    /// are skipped.</summary>
    Task<R18DevReleaseBrowseResult> BrowseAsync(R18DevCatalogFilter filter, int offset, IReadOnlySet<string>? alreadyShown = null, CancellationToken ct = default);

    /// <summary>How many releases match <paramref name="filter"/> (ignoring its status), per status,
    /// and how many of them match a previously deleted movie. All zero when the catalog can't be
    /// queried.</summary>
    Task<R18DevCatalogCounts> CountAsync(R18DevCatalogFilter filter, CancellationToken ct = default);

    /// <summary>The filter menu's genres and year range. <see cref="R18DevCatalogOptions.None"/> when
    /// the catalog can't be queried.</summary>
    Task<R18DevCatalogOptions> GetFilterOptionsAsync(CancellationToken ct = default);

    /// <summary>Up to <paramref name="limit"/> releases from <paramref name="code"/>'s series in the
    /// dump, newest first, without the movie itself; none when it has no series. Also returns the
    /// movie's series and label ids for Movie Detail's badges.</summary>
    Task<R18DevRelatedReleases> GetRelatedAsync(string code, int limit, CancellationToken ct = default);

    /// <summary>Catalog releases whose code starts with <paramref name="term"/>, marked against the
    /// local library — the header search's and Add New's suggestions. Empty while r18.dev is disabled
    /// or its catalog can't be queried.</summary>
    Task<IReadOnlyList<R18DevReleaseRow>> SuggestAsync(string term, int limit, CancellationToken ct = default);
}

/// <summary>The Movies page's "All releases (r18.dev)" browse and the other catalog lookups: joins
/// the sidecar dump store (which can't see the main database) with the local library.</summary>
public class R18DevReleaseBrowseService(IDbContextFactory<AppDbContext> dbFactory, IR18DevDumpStore dumpStore, TimeProvider timeProvider) : IR18DevReleaseBrowseService
{
    /// <summary>Rows fetched per page — what the Movies page shows per "Show more".</summary>
    public const int PageSize = 200;

    public async Task<R18DevReleaseBrowseResult> BrowseAsync(R18DevCatalogFilter filter, int offset, IReadOnlySet<string>? alreadyShown = null, CancellationToken ct = default)
    {
        var (enabled, availability) = await GetAvailabilityAsync(ct);
        if (!enabled || availability != R18DevCatalogAvailability.Ready)
        {
            return new R18DevReleaseBrowseResult(enabled, availability, []);
        }

        var library = await LoadLibraryAsync(ct);
        var keys = library.KeyFilterFor(filter.Status, filter.HidePreviouslyDeleted);
        if (keys is { Exclude: false, Keys.Count: 0 })
        {
            // "Wanted"/"Got" with nothing in the library under that status.
            return new R18DevReleaseBrowseResult(true, availability, []);
        }

        var entries = await dumpStore.GetCatalogPageAsync(filter, keys, offset, PageSize, ct);
        var rows = R18DevReleaseMatcher.CollapseDuplicates(library.Classify(entries))
            .Where(r => alreadyShown is null || !alreadyShown.Contains(r.CanonicalKey))
            .ToList();

        return new R18DevReleaseBrowseResult(true, availability, rows, offset + entries.Count, entries.Count == PageSize);
    }

    public async Task<R18DevCatalogCounts> CountAsync(R18DevCatalogFilter filter, CancellationToken ct = default)
    {
        var (enabled, availability) = await GetAvailabilityAsync(ct);
        if (!enabled || availability != R18DevCatalogAvailability.Ready)
        {
            return new R18DevCatalogCounts(0, 0, 0, 0);
        }

        var library = await LoadLibraryAsync(ct);
        var total = await dumpStore.CountCatalogAsync(filter, null, ct);
        var wanted = await CountWithAsync(filter, library.WantedKeys, ct);
        var got = await CountWithAsync(filter, library.GotKeys, ct);
        var deleted = await CountWithAsync(filter, library.DeletedOnlyKeys, ct);
        var shown = filter.HidePreviouslyDeleted ? total - deleted : total;
        return new R18DevCatalogCounts(shown, Math.Max(0, shown - wanted - got), wanted, got, deleted);
    }

    private async Task<int> CountWithAsync(R18DevCatalogFilter filter, IReadOnlyCollection<string> keys, CancellationToken ct) =>
        keys.Count == 0 ? 0 : await dumpStore.CountCatalogAsync(filter, new R18DevCanonicalKeyFilter(keys, Exclude: false), ct);

    public async Task<R18DevCatalogOptions> GetFilterOptionsAsync(CancellationToken ct = default)
    {
        var (enabled, availability) = await GetAvailabilityAsync(ct);
        if (!enabled || availability != R18DevCatalogAvailability.Ready) return R18DevCatalogOptions.None;

        var categories = await dumpStore.GetCategoriesAsync(ct);
        var years = await dumpStore.GetReleaseYearBoundsAsync(ct);
        // The dump has placeholder dates years ahead (e.g. 2030-12-31); the slider stops at next
        // year, and its top end means "no upper limit" anyway.
        var maxYear = years is { } y ? Math.Max(y.Min, Math.Min(y.Max, timeProvider.GetUtcNow().Year + 1)) : (int?)null;
        return new R18DevCatalogOptions(categories, years?.Min, maxYear);
    }

    public async Task<R18DevRelatedReleases> GetRelatedAsync(string code, int limit, CancellationToken ct = default)
    {
        var (enabled, availability) = await GetAvailabilityAsync(ct);
        if (!enabled || availability != R18DevCatalogAvailability.Ready || string.IsNullOrWhiteSpace(code))
        {
            return R18DevRelatedReleases.None;
        }

        var detail = await dumpStore.GetMovieByCodeAsync(code, ct);
        if (detail is null) return R18DevRelatedReleases.None;
        if (detail.SeriesRef is null) return new R18DevRelatedReleases(null, detail.LabelRef, [], 0);

        var filter = new R18DevCatalogFilter { Series = detail.SeriesRef };
        var self = new R18DevCanonicalKeyFilter([CodeNormalization.GetCanonicalKey(code)], Exclude: true);

        var library = await LoadLibraryAsync(ct);
        // Over-fetched, since collapsing variants can shrink the page.
        var entries = await dumpStore.GetCatalogPageAsync(filter, self, 0, limit * 2, ct);
        var rows = R18DevReleaseMatcher.CollapseDuplicates(library.Classify(entries)).Take(limit).ToList();
        var total = await dumpStore.CountCatalogAsync(filter, self, ct);
        return new R18DevRelatedReleases(detail.SeriesRef, detail.LabelRef, rows, total);
    }

    public async Task<IReadOnlyList<R18DevReleaseRow>> SuggestAsync(string term, int limit, CancellationToken ct = default)
    {
        var (enabled, availability) = await GetAvailabilityAsync(ct);
        if (!enabled || availability != R18DevCatalogAvailability.Ready || string.IsNullOrWhiteSpace(term))
        {
            return [];
        }

        var entries = await dumpStore.SuggestAsync(term, limit, ct);
        if (entries.Count == 0) return [];

        var library = await LoadLibraryAsync(ct);
        return library.Classify(entries);
    }

    private async Task<(bool Enabled, R18DevCatalogAvailability Availability)> GetAvailabilityAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var enabled = await db.R18DevSettings.OrderBy(s => s.Id).Select(s => s.Enabled).FirstOrDefaultAsync(ct);
        return enabled ? (true, await dumpStore.GetCatalogAvailabilityAsync(ct)) : (false, R18DevCatalogAvailability.NoDump);
    }

    private async Task<LibrarySnapshot> LoadLibraryAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Only what matching reads (Code, Status), untracked — the full Movie rows carry
        // description/genre text that's dead weight here.
        var movies = await db.Movies
            .Where(m => m.Code != null)
            .AsNoTracking()
            .Select(m => new Movie { Code = m.Code, Status = m.Status })
            .ToListAsync(ct);
        // The whole history: it only grows by one row per deleted movie, and the status filters need
        // every key, not just the page's.
        var deleted = await db.DeletedMovies.AsNoTracking().ToListAsync(ct);
        return new LibrarySnapshot(movies, deleted);
    }

    private sealed class LibrarySnapshot
    {
        public LibrarySnapshot(List<Movie> movies, List<DeletedMovie> deleted)
        {
            Movies = movies;
            Deleted = deleted;
            WantedKeys = KeysWith(MovieStatus.Missing);
            GotKeys = KeysWith(MovieStatus.Got);
            // A movie that's tracked again counts as tracked, as in R18DevReleaseMatcher.Classify.
            var tracked = WantedKeys.Concat(GotKeys).ToHashSet(StringComparer.Ordinal);
            DeletedOnlyKeys = deleted.Select(d => d.CanonicalKey).Where(k => k.Length > 0 && !tracked.Contains(k)).Distinct().ToList();
        }

        public List<Movie> Movies { get; }

        public List<DeletedMovie> Deleted { get; }

        public IReadOnlyCollection<string> WantedKeys { get; }

        public IReadOnlyCollection<string> GotKeys { get; }

        public IReadOnlyCollection<string> DeletedOnlyKeys { get; }

        public List<R18DevReleaseRow> Classify(IEnumerable<R18DevFilmographyEntry> entries) =>
            R18DevReleaseMatcher.Classify(entries, Movies, Deleted);

        public R18DevCanonicalKeyFilter? KeyFilterFor(R18DevCatalogStatus status, bool hidePreviouslyDeleted)
        {
            var hidden = hidePreviouslyDeleted ? DeletedOnlyKeys : [];
            return status switch
            {
                R18DevCatalogStatus.Untracked => new R18DevCanonicalKeyFilter(WantedKeys.Concat(GotKeys).Concat(hidden).Distinct().ToList(), Exclude: true),
                R18DevCatalogStatus.Wanted => new R18DevCanonicalKeyFilter(WantedKeys, Exclude: false),
                R18DevCatalogStatus.Got => new R18DevCanonicalKeyFilter(GotKeys, Exclude: false),
                _ => hidden.Count > 0 ? new R18DevCanonicalKeyFilter(hidden, Exclude: true) : null,
            };
        }

        private List<string> KeysWith(MovieStatus status) => Movies
            .Where(m => m.Status == status)
            .Select(m => CodeNormalization.GetCanonicalKey(m.Code))
            .Where(k => k.Length > 0)
            .Distinct()
            .ToList();
    }
}
