using System.Text.Json.Serialization;

namespace Javbuddy.Services.Prowlarr;

// Mirrors Prowlarr's Prowlarr.Api.V1.Search.ReleaseResource (GET /api/v1/search).

public class ReleaseResourceDto
{
    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("age")]
    public int Age { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("grabs")]
    public int? Grabs { get; set; }

    [JsonPropertyName("indexerId")]
    public int IndexerId { get; set; }

    [JsonPropertyName("indexer")]
    public string? Indexer { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("publishDate")]
    public DateTime PublishDate { get; set; }

    [JsonPropertyName("commentUrl")]
    public string? CommentUrl { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("infoUrl")]
    public string? InfoUrl { get; set; }

    [JsonPropertyName("magnetUrl")]
    public string? MagnetUrl { get; set; }

    [JsonPropertyName("seeders")]
    public int? Seeders { get; set; }

    [JsonPropertyName("leechers")]
    public int? Leechers { get; set; }

    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }
}

public class ProwlarrErrorDto
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class ProwlarrSystemStatusDto
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}


// Mirror Prowlarr's Prowlarr.Api.V1.Indexers.IndexerResource (GET /api/v1/indexer) and IndexerStatusResource
// (GET /api/v1/indexerstatus), reduced to the fields Javbuddy reads.

public class ProwlarrIndexerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("enable")]
    public bool Enable { get; set; }
}

public class ProwlarrIndexerStatusDto
{
    [JsonPropertyName("indexerId")]
    public int IndexerId { get; set; }

    [JsonPropertyName("disabledTill")]
    public DateTime? DisabledTill { get; set; }
}
