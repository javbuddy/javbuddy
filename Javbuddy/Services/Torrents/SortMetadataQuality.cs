using Javbuddy.Services.Javinizer;

namespace Javbuddy.Services.Torrents;

public enum SortQualityIssue
{
    MissingReleaseDate,
    MissingActresses,
    MissingMaker,
    FallbackTitle
}

/// <summary>At-a-glance completeness checks for a scraped sort result, shown on
/// the Review card and live in the metadata editor.</summary>
public static class SortMetadataQuality
{
    public static IReadOnlyList<SortQualityIssue> Check(MovieViewDto movie)
    {
        var issues = new List<SortQualityIssue>();
        if (movie.ReleaseDate is null && movie.ReleaseYear is null) issues.Add(SortQualityIssue.MissingReleaseDate);
        if (movie.Actresses is not { Count: > 0 }) issues.Add(SortQualityIssue.MissingActresses);
        if (string.IsNullOrWhiteSpace(movie.Maker)) issues.Add(SortQualityIssue.MissingMaker);

        var title = movie.Title?.Trim();
        if (string.IsNullOrEmpty(title)
            || string.Equals(title, movie.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(title, movie.Code, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(SortQualityIssue.FallbackTitle);
        }

        return issues;
    }

    public static string Describe(SortQualityIssue issue) => issue switch
    {
        SortQualityIssue.MissingReleaseDate => "No release date or year",
        SortQualityIssue.MissingActresses => "No actresses",
        SortQualityIssue.MissingMaker => "No maker / studio",
        SortQualityIssue.FallbackTitle => "Title is missing or just the ID",
        _ => issue.ToString()
    };
}
