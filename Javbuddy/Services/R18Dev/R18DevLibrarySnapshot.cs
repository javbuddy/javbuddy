using Javbuddy.Models;
using Javbuddy.Services.Movies;

namespace Javbuddy.Services.R18Dev;

/// <summary>The local library as the catalog matching sees it: tracked movies' codes and statuses plus the delete history,
/// with the canonical-key sets the status filters need. Immutable once built, so it can be shared across requests.</summary>
public sealed class R18DevLibrarySnapshot
{
    public R18DevLibrarySnapshot(List<Movie> movies, List<DeletedMovie> deleted)
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
