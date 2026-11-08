namespace Javbuddy.Services.Api.Contracts;

/// <summary>A Prowlarr search result as the API shows it. Deliberately has no download or magnet URL: a client grabs by
/// <see cref="Guid"/> alone and the server uses the URL it kept from the search.</summary>
public sealed record ReleaseDto(
    string Guid,
    string? Title,
    string? Indexer,
    long Size,
    int? Seeders,
    int? Leechers,
    int? Grabs,
    int AgeDays,
    DateTime? PublishDate,
    string? Protocol);

/// <summary>The releases found (most seeded first) and a warning for each indexer Prowlarr skipped.</summary>
public sealed record ReleaseSearchResponse(IReadOnlyList<ReleaseDto> Releases, IReadOnlyList<string> Warnings);

/// <summary>Body of <c>POST /movies/{code}/releases/grab</c>.</summary>
public sealed record ReleaseGrabRequest(string? Guid = null);

/// <summary><see cref="Added"/> is true when the grab had to add the movie to the library first.</summary>
public sealed record ReleaseGrabResponse(MovieSummary Movie, bool Added, string? ReleaseTitle);
