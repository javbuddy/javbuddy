using Javbuddy.Models;
using Javbuddy.Services.Movies;

namespace Javbuddy.Services.R18Dev;

public sealed record R18DevReleaseRow(
    R18DevFilmographyEntry Movie,
    string StatusClass,
    string StatusLabel,
    string LinkUrl,
    string CanonicalKey)
{
    /// <summary>True when the local library tracks this release (Got or Wanted); false for one
    /// only r18.dev knows about.</summary>
    public bool InLibrary => StatusClass is "got" or "wanted";
    public bool PreviouslyDeleted => StatusClass == "deleted";
}

/// <summary>Cross-references r18.dev releases against the local library (which needs the main
/// AppDbContext, so the sidecar-backed store can't do it) — shared by the actor Missing page and
/// the Movies page's "All releases" browse.</summary>
public static class R18DevReleaseMatcher
{
    /// <summary>Matches each entry to a local movie by exact normalized code first, then by
    /// canonical key (tolerating zero-padding and distributor/retailer prefixes and suffixes).
    /// Unmatched entries are "missing".</summary>
    public static List<R18DevReleaseRow> Classify(IEnumerable<R18DevFilmographyEntry> entries, IEnumerable<Movie> localMovies, IEnumerable<DeletedMovie>? deletedMovies = null)
    {
        var withCode = localMovies.Where(m => m.Code is not null).ToList();
        var localByCode = withCode
            .GroupBy(m => CodeNormalization.Normalize(m.Code))
            .ToDictionary(g => g.Key, g => g.First());
        var localByCanonical = withCode
            .GroupBy(m => CodeNormalization.GetCanonicalKey(m.Code))
            .ToDictionary(g => g.Key, g => g.First());
        var deleted = (deletedMovies ?? []).ToList();
        var deletedCodes = deleted.Select(m => m.NormalizedCode).ToHashSet(StringComparer.Ordinal);
        var deletedCanonical = deleted.Select(m => m.CanonicalKey).ToHashSet(StringComparer.Ordinal);

        return entries.Select(entry =>
        {
            var canon = CodeNormalization.GetCanonicalKey(entry.DvdId);

            if (localByCode.TryGetValue(CodeNormalization.Normalize(entry.DvdId), out var localMovie) ||
                localByCanonical.TryGetValue(canon, out localMovie))
            {
                return new R18DevReleaseRow(entry, localMovie.Status.ToPosterStatusSuffix(), localMovie.Status.ToString(), MovieLink(localMovie.Code!), canon);
            }

            if (deletedCodes.Contains(CodeNormalization.Normalize(entry.DvdId)) || deletedCanonical.Contains(canon))
            {
                return new R18DevReleaseRow(entry, "deleted", "Previously Deleted", MovieLink(entry.DvdId), canon);
            }

            var label = entry.ReleaseDate?.ToString("yyyy-MM-dd") ?? "Missing";
            return new R18DevReleaseRow(entry, "missing", label, MovieLink(entry.DvdId), canon);
        }).ToList();
    }

    /// <summary>Updates the row(s) matching a just-added movie in place — the same exact-code-
    /// then-canonical matching <see cref="Classify"/> uses, so duplicate releases under one
    /// canonical key all flip over too, not just the one that was acted on.</summary>
    public static List<R18DevReleaseRow> ApplyAddedMovie(IEnumerable<R18DevReleaseRow> rows, Movie movie)
    {
        if (movie.Code is null) return rows.ToList();

        var norm = CodeNormalization.Normalize(movie.Code);
        var canon = CodeNormalization.GetCanonicalKey(movie.Code);

        return rows.Select(r =>
            CodeNormalization.Normalize(r.Movie.DvdId) == norm || r.CanonicalKey == canon
                ? r with { StatusClass = movie.Status.ToPosterStatusSuffix(), StatusLabel = movie.Status.ToString(), LinkUrl = MovieLink(movie.Code) }
                : r).ToList();
    }

    /// <summary>Collapses rows sharing a canonical key (retailer/distributor variants of one
    /// release) down to the cleanest-looking code. If any variant is already tracked locally, the
    /// survivor shows that status instead of "missing".</summary>
    public static List<R18DevReleaseRow> CollapseDuplicates(IEnumerable<R18DevReleaseRow> rows)
    {
        var collapsed = new List<R18DevReleaseRow>();
        foreach (var group in rows.GroupBy(r => r.CanonicalKey))
        {
            var localMatch = group.FirstOrDefault(r => r.InLibrary) ?? group.FirstOrDefault(r => r.PreviouslyDeleted);

            var best = group.OrderByDescending(r => CodeNormalization.ScoreDvdIdForCanonical(
                    r.Movie.DvdId,
                    !string.IsNullOrWhiteSpace(r.Movie.PosterUrl),
                    !string.IsNullOrWhiteSpace(r.Movie.TitleEn) || !string.IsNullOrWhiteSpace(r.Movie.TitleJa)))
                .First();

            if (localMatch is not null && !best.InLibrary)
            {
                best = best with
                {
                    StatusClass = localMatch.StatusClass,
                    StatusLabel = localMatch.StatusLabel,
                    LinkUrl = localMatch.LinkUrl,
                };
            }

            collapsed.Add(best);
        }
        return collapsed;
    }

    private static string MovieLink(string code) => $"/movies/{Uri.EscapeDataString(code)}";
}
