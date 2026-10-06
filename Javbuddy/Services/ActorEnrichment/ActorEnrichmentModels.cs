namespace Javbuddy.Services.ActorEnrichment;

public record ActorEnrichmentContext(
    int ActorId,
    string DisplayName,
    string? FirstName,
    string? LastName,
    string? JapaneseNameKanji,
    string? JapaneseNameKana,
    IReadOnlyList<string> ExistingAliases,
    IReadOnlyList<string> LinkedMovieCodes,
    string? JellyfinPersonId = null,
    int? R18DevId = null,
    string? R18DevName = null)
{
    /// <summary>The batch run this lookup belongs to, whose loaded data it may reuse; null for a single-actor run.</summary>
    public ActorEnrichmentRunCache? RunCache { get; init; }
}

public class ActorMetadataResult
{
    public string SourceName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? JapaneseNameKanji { get; set; }
    public string? JapaneseNameKana { get; set; }
    public List<string> Aliases { get; set; } = new();
    public string? JellyfinPersonId { get; set; }
    public int? R18DevId { get; set; }
    public string? R18DevName { get; set; }

    // Extensible and biographical fields for future provider expansion
    public DateTime? BirthDate { get; set; }
    public string? BloodType { get; set; }
    public int? HeightCm { get; set; }
    public string? CupSize { get; set; }
    public int? Bust { get; set; }
    public int? Waist { get; set; }
    public int? Hips { get; set; }
    public string? Measurements { get; set; }
    public int? DebutYear { get; set; }
    public bool? IsRetired { get; set; }
    public Dictionary<string, string> ExtraAttributes { get; set; } = new();
}

public class ActorEnrichmentOptions
{
    /// <summary>Specific source to use (e.g. "Local"), or null to run all available sources in priority order.</summary>
    public string? Source { get; set; }

    /// <summary>Whether to overwrite fields that already contain non-empty values (default: false).</summary>
    public bool OverwriteExisting { get; set; }

    /// <summary>Whether to refresh the cached WebP avatar after enrichment (default: true).</summary>
    public bool RefreshImageCache { get; set; } = true;
}

public record ActorEnrichmentResult(
    bool Success,
    string? ErrorMessage = null,
    string? SourceName = null,
    int FieldsUpdated = 0,
    IReadOnlyList<string>? UpdatedFieldNames = null,
    IReadOnlyList<string>? AddedAliases = null,
    bool ImageRefreshed = false)
{
    public static ActorEnrichmentResult Ok(
        string sourceName,
        int fieldsUpdated,
        IReadOnlyList<string> updatedFieldNames,
        IReadOnlyList<string> addedAliases,
        bool imageRefreshed) =>
        new(true, null, sourceName, fieldsUpdated, updatedFieldNames, addedAliases, imageRefreshed);

    public static ActorEnrichmentResult Fail(string error) =>
        new(false, error);
}

public record ActorBatchEnrichmentResult(
    int TotalProcessed,
    int Succeeded,
    int Failed,
    int TotalFieldsUpdated);
