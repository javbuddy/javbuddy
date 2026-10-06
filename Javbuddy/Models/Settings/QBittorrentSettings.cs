using System.ComponentModel.DataAnnotations;
using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Models;

/// <summary>Single-row settings for connecting to a qBittorrent WebUI instance.</summary>
public class QBittorrentSettings : IHasConnectionUrls
{
    public int Id { get; set; }

    [StringLength(500)]
    public string? BaseUrl { get; set; }

    [StringLength(500)]
    public string? ExternalUrl { get; set; }

    [StringLength(200)]
    public string? Username { get; set; }

    [StringLength(200)]
    public string? Password { get; set; }

    /// <summary>qBittorrent category applied to every torrent this app adds, so they're kept
    /// separate from unrelated torrents in the same qBittorrent instance. Optional.</summary>
    [StringLength(100)]
    public string? Category { get; set; }
}
