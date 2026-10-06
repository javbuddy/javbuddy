namespace Javbuddy.Services.MinnanoAv;

/// <summary>A candidate performer from a minnano-av.com search. Unlike WAPdB, the site's primary
/// name is the Japanese kanji spelling; kana and romaji come from the same "furigana" line.</summary>
public record MinnanoAvSearchResult(
    string Name,
    string? Kana,
    string? Romaji,
    string PathOrUrl,
    string? ImageUrl,
    string? DebutInfo,
    bool IsExactMatch,
    IReadOnlyList<string> KnownAliases)
{
    public string FullUrl(string baseUrl = MinnanoAvClient.DefaultBaseUrl) =>
        PathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? PathOrUrl
            : $"{baseUrl.TrimEnd('/')}/{PathOrUrl.TrimStart('/')}";
}

public class MinnanoAvPerformerDetail
{
    public string Name { get; set; } = string.Empty;
    public string? Kana { get; set; }
    public string? Romaji { get; set; }
    public string PathOrUrl { get; set; } = string.Empty;
    public int? HeightCm { get; set; }
    public string? CupSize { get; set; }
    public int? Bust { get; set; }
    public int? Waist { get; set; }
    public int? Hips { get; set; }
    public DateTime? BirthDate { get; set; }
    public bool? IsRetired { get; set; }
    public List<string> Aliases { get; set; } = new();

    public bool HasKnownAttributes =>
        HeightCm.HasValue ||
        !string.IsNullOrWhiteSpace(CupSize) ||
        Bust.HasValue || Waist.HasValue || Hips.HasValue ||
        BirthDate.HasValue ||
        IsRetired.HasValue ||
        Aliases.Count > 0;
}

public class MinnanoAvImportOptions
{
    public bool ImportJapaneseName { get; set; } = true;
    public bool ImportHeight { get; set; } = true;
    public bool ImportCupSize { get; set; } = true;
    public bool ImportMeasurements { get; set; } = true;
    public bool ImportBirthDate { get; set; } = true;
    public bool ImportRetiredStatus { get; set; } = true;
    public bool ImportAliases { get; set; } = true;

    public int SelectedCount(MinnanoAvPerformerDetail? detail)
    {
        if (detail is null) return 0;
        var count = 0;
        if (ImportJapaneseName && !string.IsNullOrWhiteSpace(detail.Name)) count++;
        if (ImportHeight && detail.HeightCm.HasValue) count++;
        if (ImportCupSize && !string.IsNullOrWhiteSpace(detail.CupSize)) count++;
        if (ImportMeasurements && (detail.Bust.HasValue || detail.Waist.HasValue || detail.Hips.HasValue)) count++;
        if (ImportBirthDate && detail.BirthDate.HasValue) count++;
        if (ImportRetiredStatus && detail.IsRetired.HasValue) count++;
        if (ImportAliases && detail.Aliases.Count > 0) count++;
        return count;
    }
}
