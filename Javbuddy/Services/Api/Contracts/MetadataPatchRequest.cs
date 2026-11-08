namespace Javbuddy.Services.Api.Contracts;

/// <summary>The fields a metadata patch can set; null means "leave unchanged". To empty a field, list it in
/// <see cref="MetadataPatchRequest.Clear"/> instead — a whitespace-only value is rejected.</summary>
public sealed record MetadataFields(
    string? Title = null,
    string? TitleOverride = null,
    string? OriginalTitle = null,
    string? Description = null,
    DateOnly? ReleaseDate = null,
    string? Studio = null,
    string? Label = null,
    string? Series = null,
    string? Director = null,
    string? ContentType = null);

/// <summary>A partial metadata edit: <see cref="Set"/> values replace, names in <see cref="Clear"/> are emptied, everything else is kept.</summary>
public sealed record MetadataPatchRequest(MetadataFields? Set = null, IReadOnlyList<string>? Clear = null);

/// <summary>The edited movie, plus a warning when the edit saved but something around it didn't (e.g. a new .nfo couldn't be written).</summary>
public sealed record MetadataPatchResponse(MovieDetail Movie, string? Warning);
