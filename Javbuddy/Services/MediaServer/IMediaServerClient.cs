using System.Text.Json.Serialization;

namespace Javbuddy.Services.MediaServer;

public class MediaServerItemDto
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("ServerId")]
    public string? ServerId { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonIgnore]
    public string? LibraryId { get; set; }

    [JsonIgnore]
    public string? LibraryName { get; set; }
}

public record MediaServerLookupResult(bool Success, IReadOnlyList<MediaServerItemDto>? Items, string? ErrorMessage);
public record MediaServerTestResult(bool Success, string Message, string? Version = null, string? ServerId = null);
public record MediaServerActionResult(bool Success, string? ErrorMessage);

/// <summary>Generic media server abstraction enabling media servers (Jellyfin, and
/// in the future Plex, Emby) to be used interchangeably for library ownership checks,
/// item links, and web playback.</summary>
public interface IMediaServerClient
{
    string DisplayName { get; }
    Task<bool> IsEnabledAsync(CancellationToken ct = default);
    Task<MediaServerLookupResult> LookupInSelectedLibrariesAsync(string query, CancellationToken ct = default);
    Task<string?> GetWebUrlAsync(string itemId, string? serverId = null, CancellationToken ct = default);
    Task<MediaServerActionResult> RefreshItemAsync(string itemId, CancellationToken ct = default);
    Task<MediaServerTestResult> TestConnectionAsync(CancellationToken ct = default);
    Task<string?> GetExternalUrlAsync(CancellationToken ct = default);
}
