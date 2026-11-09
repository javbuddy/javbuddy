using System.Text.Json.Serialization;
using Javbuddy.Services.MediaServer;

namespace Javbuddy.Services.Jellyfin;

// Mirrors Jellyfin's default (PascalCase) JSON API — unlike javinizer-go and
// Prowlarr, Jellyfin serializes as PascalCase by default (camelCase requires
// an explicit Accept header profile), so these DTOs use PascalCase names.

public class JellyfinQueryResultDto
{
    [JsonPropertyName("Items")]
    public List<JellyfinItemDto>? Items { get; set; }
}

public class JellyfinItemDto : MediaServerItemDto;

public class JellyfinSystemInfoDto
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Version")]
    public string? Version { get; set; }
}

public class JellyfinPersonDto
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("ServerId")]
    public string? ServerId { get; set; }
}
