using System.Text;
using System.Text.Json;
using Javbuddy.Services.Javinizer;

namespace Javbuddy.Services.Torrents;

/// <summary>Scraper-provenance helpers for the sort editor. Field keys are
/// javinizer-go's field-override keys (internal/worker/field_override.go).</summary>
public static class SortSourceHelper
{
    public static readonly IReadOnlyList<string> OverridableFields =
        ["title", "original_title", "description", "director", "maker", "label", "series", "runtime", "release_date", "actresses", "genres"];

    /// <summary>Reads a result's field_sources/actress_sources object (field → source name).</summary>
    public static Dictionary<string, string> ParseSources(JsonElement? element)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (element is not { ValueKind: JsonValueKind.Object } obj) return map;

        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() is { Length: > 0 } source)
            {
                map[prop.Name] = source;
            }
        }

        return map;
    }

    /// <summary>One source's value for <paramref name="field"/> as display text, or null when it has none.</summary>
    public static string? FormatValue(ScraperSourceResultDto source, string field)
    {
        var text = field switch
        {
            "title" => source.Title,
            "original_title" => source.OriginalTitle,
            "description" => source.Description,
            "director" => source.Director,
            "maker" => source.Maker,
            "label" => source.Label,
            "series" => source.Series,
            "runtime" => source.Runtime is > 0 ? $"{source.Runtime} min" : null,
            "release_date" => source.ReleaseDate?.ToString("yyyy-MM-dd"),
            "actresses" => source.Actresses is { Count: > 0 } a ? string.Join(", ", a.Select(SortEditorFormat.ActressPrimaryName).Where(n => n.Length > 0)) : null,
            "genres" => source.Genres is { Count: > 0 } g ? string.Join(", ", g) : null,
            _ => null
        };
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>The scraper that provided <paramref name="actress"/>, from a result's
    /// actress_sources, or null when it isn't attributed.</summary>
    public static string? ActressSource(IReadOnlyDictionary<string, string> actressSources, ActressViewDto actress) =>
        actressSources.Count == 0
            ? null
            : ActressSourceKeys(actress).Select(actressSources.GetValueOrDefault).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

    /// <summary>actress_sources keys that can name <paramref name="actress"/>, best first:
    /// javinizer-go's own key (scrape.ActressSourceKey, internal/scrape/provenance.go), then the
    /// alternate name keys its review UI also tries after an edit (ActressEditor.svelte).</summary>
    public static IEnumerable<string> ActressSourceKeys(ActressViewDto actress)
    {
        if (actress.DmmId is > 0) yield return $"dmmid:{actress.DmmId}";
        string?[] names = [actress.JapaneseName, $"{actress.FirstName} {actress.LastName}", $"{actress.LastName} {actress.FirstName}"];
        foreach (var name in names)
        {
            if (NormalizeActressNameKey(name) is { Length: > 0 } key) yield return "name:" + key;
        }
    }

    /// <summary>javinizer-go's models.NormalizeActressNameKey: NFKC, lowercase, whitespace runs
    /// collapsed to one space (NFKC already turns an ideographic space into a plain one).</summary>
    public static string NormalizeActressNameKey(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? ""
            : string.Join(' ', name.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Copies one field from javinizer-go's post-override movie into the edit buffer,
    /// leaving every other (possibly unsaved) field alone.</summary>
    public static void CopyField(string field, MovieViewDto from, MovieViewDto to)
    {
        switch (field)
        {
            case "title": to.Title = from.Title; break;
            case "original_title": to.OriginalTitle = from.OriginalTitle; break;
            case "description": to.Description = from.Description; break;
            case "director": to.Director = from.Director; break;
            case "maker": to.Maker = from.Maker; break;
            case "label": to.Label = from.Label; break;
            case "series": to.Series = from.Series; break;
            case "runtime": to.Runtime = from.Runtime; break;
            case "release_date":
                to.ReleaseDate = from.ReleaseDate;
                to.ReleaseYear = from.ReleaseYear;
                break;
            case "actresses": to.Actresses = from.Actresses is { } a ? new List<ActressViewDto>(a) : []; break;
            case "genres": to.Genres = from.Genres is { } g ? new List<GenreViewDto>(g) : []; break;
        }
    }
}
