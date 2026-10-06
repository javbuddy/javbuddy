using System.ComponentModel.DataAnnotations;
using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Models;

/// <summary>Single-row settings for connecting to a Jellyfin instance.</summary>
public class JellyfinSettings : IHasConnectionUrls
{
    public int Id { get; set; }

    public bool Enabled { get; set; } = true;

    [StringLength(500)]
    public string? BaseUrl { get; set; }

    [StringLength(500)]
    public string? ExternalUrl { get; set; }

    [StringLength(200)]
    public string? ApiKey { get; set; }

    /// <summary>Comma-separated Jellyfin library (VirtualFolder) Names to restrict the "Scan Jellyfin" bulk sync to. Empty means all libraries.</summary>
    [StringLength(2000)]
    public string? SelectedLibraryNames { get; set; }

    // Stats from the most recent automatic link-sync run (JellyfinLinkSyncService).
    public DateTime? LastLinkSyncAt { get; set; }
    public int? LastLinkSyncChecked { get; set; }
    public int? LastLinkSyncMatched { get; set; }
}
