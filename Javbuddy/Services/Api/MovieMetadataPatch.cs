using Javbuddy.Models;
using Javbuddy.Services.Api.Contracts;
using Javbuddy.Services.Movies;

namespace Javbuddy.Services.Api;

/// <summary>Turns a partial <see cref="MetadataPatchRequest"/> into the full <see cref="MovieMetadataUpdate"/> that
/// <c>IMovieService.UpdateMetadataAsync</c> replaces a movie's metadata with: the movie's current values, overlaid with
/// the set values, with the cleared fields emptied. Pure, so REST and MCP validate identically.
/// Public field names: title (MetaTitle), titleOverride (Title), originalTitle, description, releaseDate (date only),
/// studio, label, series, director, contentType (censored|uncensored|decensored, any case).
/// A whitespace-only set value is rejected ("use clear") rather than silently treated as a clear; set values are trimmed.
/// Content type can't be emptied: it is not clearable.</summary>
public static class MovieMetadataPatch
{
    /// <summary>The public names of the clearable fields.</summary>
    public static readonly IReadOnlyList<string> ClearableFields =
        ["title", "titleOverride", "originalTitle", "description", "releaseDate", "studio", "label", "series", "director"];

    public static ApiResult<MovieMetadataUpdate> Apply(Movie current, MetadataPatchRequest patch)
    {
        var set = patch.Set;
        var clearRaw = patch.Clear ?? [];
        var errors = new List<string>();

        var clear = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in clearRaw)
        {
            var canonical = ClearableFields.FirstOrDefault(f => string.Equals(f, name?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (canonical is null)
            {
                errors.Add($"Unknown field to clear: '{name}'. Clearable fields: {string.Join(", ", ClearableFields)}.");
            }
            else
            {
                clear.Add(canonical);
            }
        }

        var hasSet = set is not null && (set.Title is not null || set.TitleOverride is not null || set.OriginalTitle is not null
            || set.Description is not null || set.ReleaseDate is not null || set.Studio is not null || set.Label is not null
            || set.Series is not null || set.Director is not null || set.ContentType is not null);
        if (!hasSet && clearRaw.Count == 0)
        {
            return ApiResult<MovieMetadataUpdate>.Invalid("Nothing to change: provide at least one field in 'set' or 'clear'.");
        }

        set ??= new MetadataFields();
        string? Text(string field, string? value)
        {
            if (value is null) return null;
            var trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                errors.Add($"'{field}' is empty or whitespace; use 'clear' to empty a field.");
                return null;
            }
            if (clear.Contains(field))
            {
                errors.Add($"'{field}' is in both 'set' and 'clear'.");
            }
            return trimmed;
        }

        var title = Text("title", set.Title);
        var titleOverride = Text("titleOverride", set.TitleOverride);
        var originalTitle = Text("originalTitle", set.OriginalTitle);
        var description = Text("description", set.Description);
        var studio = Text("studio", set.Studio);
        var label = Text("label", set.Label);
        var series = Text("series", set.Series);
        var director = Text("director", set.Director);
        if (set.ReleaseDate is not null && clear.Contains("releaseDate"))
        {
            errors.Add("'releaseDate' is in both 'set' and 'clear'.");
        }

        var contentType = current.ContentType;
        if (set.ContentType is not null)
        {
            if (!TryParseContentType(set.ContentType, out contentType))
            {
                errors.Add($"Invalid contentType '{set.ContentType}'. Use censored, uncensored or decensored.");
            }
        }

        if (errors.Count > 0)
        {
            return ApiResult<MovieMetadataUpdate>.Invalid(errors);
        }

        string? Resolve(string field, string? value, string? existing) => clear.Contains(field) ? null : value ?? existing;

        DateTime? releaseDate = clear.Contains("releaseDate")
            ? null
            : set.ReleaseDate is { } date ? date.ToDateTime(TimeOnly.MinValue) : current.MetaReleaseDate;

        return ApiResult<MovieMetadataUpdate>.Ok(new MovieMetadataUpdate(
            Title: Resolve("titleOverride", titleOverride, current.Title),
            MetaTitle: Resolve("title", title, current.MetaTitle),
            MetaOriginalTitle: Resolve("originalTitle", originalTitle, current.MetaOriginalTitle),
            MetaReleaseDate: releaseDate,
            MetaStudio: Resolve("studio", studio, current.MetaStudio),
            MetaLabel: Resolve("label", label, current.MetaLabel),
            MetaSeries: Resolve("series", series, current.MetaSeries),
            MetaDirector: Resolve("director", director, current.MetaDirector),
            MetaDescription: Resolve("description", description, current.MetaDescription),
            ContentType: contentType));
    }

    private static bool TryParseContentType(string value, out MovieContentType contentType)
    {
        contentType = default;
        var trimmed = value.Trim();
        // Enum.TryParse would also accept "1" or "Censored, Uncensored"; only the three names are public.
        foreach (var candidate in new[] { MovieContentType.Censored, MovieContentType.Uncensored, MovieContentType.Decensored })
        {
            if (string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                contentType = candidate;
                return true;
            }
        }
        return false;
    }
}
