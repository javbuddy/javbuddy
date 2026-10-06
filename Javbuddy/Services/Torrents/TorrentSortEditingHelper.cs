using Javbuddy.Services.Javinizer;

namespace Javbuddy.Services.Torrents;

/// <summary>Pure helpers for TorrentSort.razor's metadata editor: cloning a batch-scrape result
/// into an editable buffer, and comparing/formatting its structured actresses.
/// Deliberately separate from MovieMetadataMapper, which flattens a scrape result onto a Movie
/// entity's Meta* fields for display — a different shape (this produces the structured
/// MovieViewDto javinizer-go's UpdateResultAsync expects) for a different purpose. The two
/// FormatActressName-style helpers also format names differently by design: this one shows
/// "First Last (Japanese)" for the Review card; MovieMetadataMapper's collapses to a single
/// "Last First" (or Japanese-only) name for the Movie.MetaActresses summary field. They are not
/// meant to be unified.</summary>
public static class TorrentSortEditingHelper
{
    /// <summary>Compare the selected source with the scrape's original image. The backend crop
    /// flag alone cannot identify a user replacement: it may be false for the default too.</summary>
    public static bool HasCustomPoster(MovieViewDto movie)
    {
        var original = !string.IsNullOrWhiteSpace(movie.OriginalPosterUrl)
            ? movie.OriginalPosterUrl
            : movie.OriginalCoverUrl;
        return !string.IsNullOrWhiteSpace(movie.PosterUrl)
            && !string.IsNullOrWhiteSpace(original)
            && !string.Equals(movie.PosterUrl, original, StringComparison.Ordinal);
    }

    /// <summary>Deep-enough clone for an edit buffer — new list instances for Actresses/Genres/
    /// ScreenshotUrls so edits don't mutate the original job-data result until saved.
    /// Note: SourceName/SourceUrl aren't carried over (matches the modal's pre-extraction
    /// behavior — this dialog has never surfaced or round-tripped those two fields).</summary>
    public static MovieViewDto Clone(MovieViewDto movie) => new()
    {
        Id = movie.Id,
        Code = movie.Code,
        DisplayTitle = movie.DisplayTitle,
        Title = movie.Title,
        OriginalTitle = movie.OriginalTitle,
        Description = movie.Description,
        ReleaseDate = movie.ReleaseDate,
        ReleaseYear = movie.ReleaseYear,
        Director = movie.Director,
        Maker = movie.Maker,
        Label = movie.Label,
        Series = movie.Series,
        Runtime = movie.Runtime,
        RatingScore = movie.RatingScore,
        RatingVotes = movie.RatingVotes,
        PosterUrl = movie.PosterUrl,
        CoverUrl = movie.CoverUrl,
        CroppedPosterUrl = movie.CroppedPosterUrl,
        ShouldCropPoster = movie.ShouldCropPoster,
        OriginalShouldCropPoster = movie.OriginalShouldCropPoster,
        PosterCropBounds = movie.PosterCropBounds,
        PosterCropSourceFull = movie.PosterCropSourceFull,
        OriginalPosterUrl = movie.OriginalPosterUrl,
        OriginalCoverUrl = movie.OriginalCoverUrl,
        TrailerUrl = movie.TrailerUrl,
        ScreenshotUrls = movie.ScreenshotUrls != null ? new List<string>(movie.ScreenshotUrls) : new List<string>(),
        CastVersion = movie.CastVersion,
        Actresses = movie.Actresses != null ? new List<ActressViewDto>(movie.Actresses) : new List<ActressViewDto>(),
        Genres = movie.Genres != null ? new List<GenreViewDto>(movie.Genres) : new List<GenreViewDto>()
    };

    /// <summary>Copies the image fields (poster, crop, cover, screenshots) from the live result
    /// onto the edit buffer before a save. The editor's Media actions change these in
    /// javinizer-go directly, so the buffer cloned at open is stale for them; PATCHing it would
    /// revert a crop or poster pick (an explicit null poster_crop_bounds clears the crop).</summary>
    public static void CopyMediaFields(MovieViewDto from, MovieViewDto to)
    {
        to.PosterUrl = from.PosterUrl;
        to.CoverUrl = from.CoverUrl;
        to.CroppedPosterUrl = from.CroppedPosterUrl;
        to.ShouldCropPoster = from.ShouldCropPoster;
        to.OriginalShouldCropPoster = from.OriginalShouldCropPoster;
        to.PosterCropBounds = from.PosterCropBounds;
        to.PosterCropSourceFull = from.PosterCropSourceFull;
        to.OriginalPosterUrl = from.OriginalPosterUrl;
        to.OriginalCoverUrl = from.OriginalCoverUrl;
        to.ScreenshotUrls = from.ScreenshotUrls != null ? new List<string>(from.ScreenshotUrls) : new List<string>();
    }

    public static string FormatActressName(ActressViewDto a)
    {
        var fullName = $"{a.FirstName} {a.LastName}".Trim();
        if (!string.IsNullOrWhiteSpace(fullName) && !string.IsNullOrWhiteSpace(a.JapaneseName) && fullName != a.JapaneseName)
        {
            return $"{fullName} ({a.JapaneseName})";
        }
        return !string.IsNullOrWhiteSpace(fullName) ? fullName : (a.JapaneseName ?? "");
    }

    /// <summary>Same person, for the editor's duplicate check: same javinizer id
    /// when both have one, else same Japanese name (spaces ignored), else same first + last name,
    /// case-insensitively.</summary>
    public static bool IsSameActress(ActressViewDto a, ActressViewDto b)
    {
        if (a.Id is > 0 && b.Id is > 0) return a.Id == b.Id;
        if (CompactJapanese(a.JapaneseName) is { } ja && CompactJapanese(b.JapaneseName) is { } jb) return ja == jb;
        var nameA = $"{a.FirstName} {a.LastName}".Trim();
        return nameA.Length > 0 && string.Equals(nameA, $"{b.FirstName} {b.LastName}".Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? CompactJapanese(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Replace(" ", "").Replace("\u3000", "");
}

/// <summary>Chip labels for the sort editor's cast: romaji "First Last" first and
/// the Japanese name second; a Japanese-only actress shows her Japanese name as the main label.</summary>
public static class SortEditorFormat
{
    public static string ActressPrimaryName(ActressViewDto a)
    {
        var full = $"{a.FirstName} {a.LastName}".Trim();
        return full.Length > 0 ? full : a.JapaneseName ?? "";
    }

    public static string? ActressSecondaryName(ActressViewDto a) =>
        !string.IsNullOrWhiteSpace(a.JapaneseName) && ActressPrimaryName(a) != a.JapaneseName ? a.JapaneseName : null;
}
