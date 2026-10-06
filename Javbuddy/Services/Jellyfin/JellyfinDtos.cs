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

    [JsonPropertyName("TotalRecordCount")]
    public int TotalRecordCount { get; set; }
}

public class JellyfinItemDto : MediaServerItemDto
{
    [JsonPropertyName("ProductionYear")]
    public int? ProductionYear { get; set; }
}

public class JellyfinVirtualFolderDto
{
    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("ItemId")]
    public string? ItemId { get; set; }

    [JsonPropertyName("CollectionType")]
    public string? CollectionType { get; set; }

    [JsonPropertyName("Locations")]
    public List<string>? Locations { get; set; }
}

public class JellyfinSystemInfoDto
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("ServerName")]
    public string? ServerName { get; set; }

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

/// <summary>Item projection for the trickplay lookup: the id, runtime, and the per-media-source,
/// per-width trickplay layout Jellyfin reports in an item's <c>Trickplay</c> field.</summary>
public class JellyfinTrickplayQueryResultDto
{
    [JsonPropertyName("Items")]
    public List<JellyfinTrickplayItemDto>? Items { get; set; }
}

public class JellyfinTrickplayItemDto
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("RunTimeTicks")]
    public long? RunTimeTicks { get; set; }

    /// <summary>Keyed by media source id, then by tile-sheet thumbnail width (as a string).</summary>
    [JsonPropertyName("Trickplay")]
    public Dictionary<string, Dictionary<string, JellyfinTrickplayInfoDto>>? Trickplay { get; set; }
}

public class JellyfinTrickplayInfoDto
{
    [JsonPropertyName("Width")]
    public int Width { get; set; }

    [JsonPropertyName("Height")]
    public int Height { get; set; }

    [JsonPropertyName("TileWidth")]
    public int TileWidth { get; set; }

    [JsonPropertyName("TileHeight")]
    public int TileHeight { get; set; }

    [JsonPropertyName("ThumbnailCount")]
    public int ThumbnailCount { get; set; }

    /// <summary>Milliseconds between thumbnails.</summary>
    [JsonPropertyName("Interval")]
    public int Interval { get; set; }
}
