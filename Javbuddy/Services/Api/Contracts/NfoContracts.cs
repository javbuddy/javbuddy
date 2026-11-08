namespace Javbuddy.Services.Api.Contracts;

/// <summary>The movie's .nfo drift and the .nfo as Javbuddy would write it (null when the movie has no readable .nfo).</summary>
public sealed record NfoPreview(string DriftKind, string? Details, string? ProposedNfo);

public sealed record NfoPushResponse(int Written, int SkippedExternal, int Failed, int StillDrifting);

/// <summary>A read-only javinizer-go scrape for comparison; nothing is stored.</summary>
public sealed record JavinizerPreview(
    string? Code,
    string? Title,
    string? OriginalTitle,
    string? Description,
    DateOnly? ReleaseDate,
    string? Studio,
    string? Label,
    string? Series,
    string? Director,
    int? RuntimeMinutes,
    IReadOnlyList<string> Cast,
    IReadOnlyList<string> Genres,
    string? CoverUrl);

public sealed record IntegrationStatus(bool Javinizer, bool Prowlarr, [property: System.Text.Json.Serialization.JsonPropertyName("qbittorrent")] bool QBittorrent, bool Jellyfin);

public sealed record SystemInfo(string Version, IntegrationStatus Integrations);
