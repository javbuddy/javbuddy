using System.Text.Json.Serialization;

namespace Javbuddy.Services.QBittorrent;

// Mirrors qBittorrent's WebUI API v2 GET /api/v2/torrents/info response.
// Field names verified against the qBittorrent WebUI API spec — confirm against a live
// instance if a running version behaves differently.

public class QBittorrentTorrentDto
{
    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    [JsonPropertyName("dlspeed")]
    public long DlSpeed { get; set; }

    [JsonPropertyName("eta")]
    public long Eta { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("save_path")]
    public string? SavePath { get; set; }

    /// <summary>Absolute path of the torrent's actual content — the root folder for a
    /// multi-file torrent, or the file itself for a single-file torrent. Unlike save_path
    /// (the download root), this already includes any per-torrent subfolder qBittorrent
    /// created, so it's the right value to prefill the sorting wizard's scan path with.</summary>
    [JsonPropertyName("content_path")]
    public string? ContentPath { get; set; }

    [JsonPropertyName("tags")]
    public string? Tags { get; set; }

    [JsonPropertyName("added_on")]
    public long AddedOn { get; set; }

    [JsonPropertyName("completion_on")]
    public long CompletionOn { get; set; }
}
