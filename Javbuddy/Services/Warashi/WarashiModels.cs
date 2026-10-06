namespace Javbuddy.Services.Warashi;

public record WarashiSearchResult(
    string Name,
    string? JapaneseName,
    string PathOrUrl,
    string? ImageUrl,
    string? CareerActivity,
    bool IsExactMatch,
    IReadOnlyList<string> KnownAliases)
{
    public string FullUrl(string baseUrl = "https://warashi-asian-pornstars.fr") =>
        PathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? PathOrUrl
            : $"{baseUrl.TrimEnd('/')}/{PathOrUrl.TrimStart('/')}";
}

public class WarashiPerformerDetail
{
    public string Name { get; set; } = string.Empty;
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string? JapaneseName { get; set; }
    public string PathOrUrl { get; set; } = string.Empty;
    public int? HeightCm { get; set; }
    public string? CupSize { get; set; }
    public int? Bust { get; set; }
    public int? Waist { get; set; }
    public int? Hips { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? BloodType { get; set; }
    public string? BirthPlace { get; set; }
    public bool? IsRetired { get; set; }
    public int? DebutYear { get; set; }
    public List<string> Aliases { get; set; } = new();
    public string? MainPhotoUrl { get; set; }
    public List<string> AdditionalPhotoUrls { get; set; } = new();

    public bool HasKnownAttributes =>
        HeightCm.HasValue ||
        !string.IsNullOrWhiteSpace(CupSize) ||
        Bust.HasValue || Waist.HasValue || Hips.HasValue ||
        BirthDate.HasValue ||
        IsRetired.HasValue ||
        Aliases.Count > 0 ||
        !string.IsNullOrWhiteSpace(MainPhotoUrl) ||
        AdditionalPhotoUrls.Count > 0 ||
        !string.IsNullOrWhiteSpace(BloodType) ||
        !string.IsNullOrWhiteSpace(BirthPlace);
}

public class WarashiImportOptions
{
    public bool ImportJapaneseName { get; set; } = true;
    public bool ImportHeight { get; set; } = true;
    public bool ImportCupSize { get; set; } = true;
    public bool ImportMeasurements { get; set; } = true;
    public bool ImportBirthDate { get; set; } = true;
    public bool ImportRetiredStatus { get; set; } = true;
    public bool ImportAliases { get; set; } = true;
    public bool ImportPhotos { get; set; }

    public int SelectedCount(WarashiPerformerDetail? detail)
    {
        if (detail is null) return 0;
        var count = 0;
        if (ImportJapaneseName && !string.IsNullOrWhiteSpace(detail.JapaneseName)) count++;
        if (ImportHeight && detail.HeightCm.HasValue) count++;
        if (ImportCupSize && !string.IsNullOrWhiteSpace(detail.CupSize)) count++;
        if (ImportMeasurements && (detail.Bust.HasValue || detail.Waist.HasValue || detail.Hips.HasValue)) count++;
        if (ImportBirthDate && detail.BirthDate.HasValue) count++;
        if (ImportRetiredStatus && detail.IsRetired.HasValue) count++;
        if (ImportAliases && detail.Aliases.Count > 0) count++;
        var totalPhotos = (string.IsNullOrWhiteSpace(detail.MainPhotoUrl) ? 0 : 1) + detail.AdditionalPhotoUrls.Count;
        if (ImportPhotos && totalPhotos > 0) count++;
        return count;
    }
}
