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
