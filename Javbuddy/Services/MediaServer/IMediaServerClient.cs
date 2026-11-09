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
}

public record MediaServerLookupResult(bool Success, IReadOnlyList<MediaServerItemDto>? Items, string? ErrorMessage);
public record MediaServerTestResult(bool Success, string Message, string? Version = null, string? ServerId = null);

/// <summary>Generic media server abstraction enabling media servers (Jellyfin, and
/// in the future Plex, Emby) to be used interchangeably for "Open in …" item links.</summary>
public interface IMediaServerClient
{
    Task<bool> IsEnabledAsync(CancellationToken ct = default);
    /// <summary>Searches the whole server for a movie by code. An empty result (not an error) when the integration is disabled.</summary>
    Task<MediaServerLookupResult> LookupMovieAsync(string query, CancellationToken ct = default);
    Task<string?> GetWebUrlAsync(string itemId, string? serverId = null, CancellationToken ct = default);
    Task<MediaServerTestResult> TestConnectionAsync(CancellationToken ct = default);
    Task<string?> GetExternalUrlAsync(CancellationToken ct = default);
}
