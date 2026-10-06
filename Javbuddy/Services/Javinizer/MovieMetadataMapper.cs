using Javbuddy.Models;
using Javbuddy.Services.Torrents;

namespace Javbuddy.Services.Javinizer;

/// <summary>Applies a javinizer-go scrape result onto a Movie's Meta* fields.</summary>
public static class MovieMetadataMapper
{
    public static void Apply(Movie movie, MovieViewDto meta)
    {
        movie.MetaTitle = ResolveTitle(meta);
        movie.MetaOriginalTitle = meta.OriginalTitle;
        movie.MetaDescription = meta.Description;
        movie.MetaReleaseDate = meta.ReleaseDate;
        movie.MetaDirector = meta.Director;
        movie.MetaStudio = meta.Maker;
        movie.MetaLabel = meta.Label;
        movie.MetaSeries = meta.Series;
        movie.MetaRatingScore = meta.RatingScore;
        movie.MetaRatingVotes = meta.RatingVotes;
        movie.MetaCoverUrl = ResolveCoverUrl(meta);
        // javinizer-go's live scrape API has no distinct backdrop/fanart field (only
        // poster_url/cover_url — see JavinizerDtos.MovieViewDto), so a Missing/non-local movie has
        // nothing else to use for the hero background. The full cover is already landscape-shaped
        // (see PosterCropGeometry — that's exactly the image RemotePosterCropService crops down
        // for the poster), so it doubles as a perfectly good backdrop as-is, no cropping needed.
        movie.MetaBackdropUrl = movie.MetaCoverUrl;
        movie.MetaActresses = FormatActresses(meta.Actresses);
        movie.MetaGenres = FormatGenres(meta.Genres);
        movie.MetaSourceName = meta.SourceName;
        movie.MetaSourceUrl = meta.SourceUrl;
        movie.MetaFetchedAt = DateTime.UtcNow;
    }

    /// <summary>Shared with the selective "Refresh with Javinizer" field picker, which needs
    /// the same title-resolution fallback when previewing/applying just the Title field.
    /// Prepends the movie's DVD ID / release code if not already present.</summary>
    public static string? ResolveTitle(MovieViewDto meta, string? code = null)
    {
        var rawTitle = string.IsNullOrWhiteSpace(meta.Title) ? meta.DisplayTitle : meta.Title;
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return null;
        }

        var trimmed = rawTitle.Trim();
        var effectiveCode = !string.IsNullOrWhiteSpace(code) ? code.Trim() : meta.Id?.Trim();
        if (string.IsNullOrWhiteSpace(effectiveCode))
        {
            return trimmed;
        }

        if ((!string.IsNullOrWhiteSpace(code) && StartsWithCode(trimmed, code)) ||
            (!string.IsNullOrWhiteSpace(meta.Id) && StartsWithCode(trimmed, meta.Id)))
        {
            return trimmed;
        }

        return $"{effectiveCode} {trimmed}";
    }

    private static bool StartsWithCode(string title, string code)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var trimmedCode = code.Trim();
        if (!title.StartsWith(trimmedCode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (title.Length == trimmedCode.Length)
        {
            return true;
        }

        return !char.IsAsciiLetterOrDigit(title[trimmedCode.Length]);
    }

    /// <summary>Shared with the selective "Refresh with Javinizer" field picker.</summary>
    public static string? ResolveCoverUrl(MovieViewDto meta) =>
        string.IsNullOrWhiteSpace(meta.CoverUrl) ? meta.PosterUrl : meta.CoverUrl;

    /// <summary>Shared with the selective "Refresh with Javinizer" field picker, which needs
    /// the same comma-joined display format to preview the incoming value before it's applied.
    /// javinizer-go aggregates cast from several scrapers, which can list one actress in both name
    /// orderings, as one combined field, or twice: an entry sharing any identity key (DMM id,
    /// either name ordering, Japanese name) with an earlier one is dropped, keeping the first.</summary>
    public static string? FormatActresses(IReadOnlyList<ActressViewDto>? actresses)
    {
        if (actresses is not { Count: > 0 }) return null;

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (var actress in actresses)
        {
            var name = FormatActressName(actress);
            if (name.Length == 0) continue;

            var keys = SortSourceHelper.ActressSourceKeys(actress).ToList();
            var isDuplicate = keys.Any(seenKeys.Contains);
            // A duplicate's keys still count, so a third entry matching only the duplicate is caught.
            seenKeys.UnionWith(keys);
            if (!isDuplicate) names.Add(name);
        }
        return names.Count > 0 ? string.Join(", ", names) : null;
    }

    /// <summary>Shared with the selective "Refresh with Javinizer" field picker.</summary>
    public static string? FormatGenres(IReadOnlyList<GenreViewDto>? genres) =>
        genres is { Count: > 0 }
            ? string.Join(", ", genres.Select(g => g.Name))
            : null;

    private static string FormatActressName(ActressViewDto a)
    {
        if (!string.IsNullOrWhiteSpace(a.LastName) || !string.IsNullOrWhiteSpace(a.FirstName))
        {
            return $"{a.LastName} {a.FirstName}".Trim();
        }
        return a.JapaneseName ?? "";
    }
}
