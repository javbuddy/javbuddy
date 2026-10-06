using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>One release grabbed from Prowlarr and sent to qBittorrent. Rows are never deleted —
/// they back both the Activity &gt; Queue view (RemovedFromClientAt == null &amp;&amp; SortedAt == null, still in the
/// download/sort pipeline) and the Activity &gt; History view (rows that have left the queue: RemovedFromClientAt != null || SortedAt != null).</summary>
public class TorrentDownload
{
    public int Id { get; set; }

    public int MovieId { get; set; }
    public Movie? Movie { get; set; }

    /// <summary>Denormalized so History still reads sensibly if the movie is ever deleted.</summary>
    [Required]
    [StringLength(50)]
    public string MovieCode { get; set; } = "";

    [StringLength(500)]
    public string? ReleaseTitle { get; set; }

    [StringLength(200)]
    public string? Indexer { get; set; }

    public long? Size { get; set; }

    /// <summary>The magnet/download URL that was sent to qBittorrent.</summary>
    [StringLength(2000)]
    public string? SourceUrl { get; set; }

    /// <summary>qBittorrent's torrent hash, set once QBittorrentSyncTask correlates this row to a
    /// live torrent by its grab tag. Null until then.</summary>
    [StringLength(100)]
    public string? Hash { get; set; }

    /// <summary>qBittorrent's save_path for this torrent, refreshed by QBittorrentSyncTask every
    /// sync tick. Seeds (but does not dictate) the sorting wizard's scan-path field — javinizer-go
    /// may see a different filesystem view than qBittorrent reports. Only used as a fallback when
    /// ContentPath isn't available (e.g. a row synced before that field existed).</summary>
    [StringLength(1000)]
    public string? SavePath { get; set; }

    /// <summary>qBittorrent's content_path for this torrent — the actual content location
    /// (multi-file torrent's root folder, or the file itself for a single-file torrent),
    /// unlike SavePath which is just the download root. Preferred over SavePath for prefilling
    /// the sorting wizard's scan path since it doesn't require the user to hand-append the
    /// per-torrent subfolder.</summary>
    [StringLength(1000)]
    public string? ContentPath { get; set; }

    /// <summary>The active/last javinizer-go batch job id for sorting this torrent, so the
    /// sorting wizard can resume polling after a page reload instead of re-scraping.</summary>
    [StringLength(100)]
    public string? JavinizerBatchJobId { get; set; }

    /// <summary>Set once the javinizer-go batch job for this torrent reaches status "organized".
    /// Non-null switches the Queue/History "Sort" affordance to a "Sorted" badge, but doesn't hard-block re-running.</summary>
    public DateTime? SortedAt { get; set; }

    public TorrentDownloadStatus Status { get; set; } = TorrentDownloadStatus.Queued;

    /// <summary>0-1, from qBittorrent's live progress. Null once RemovedFromClientAt is set.</summary>
    public double? Progress { get; set; }

    public long? DownloadSpeedBytesPerSec { get; set; }
    public long? EtaSeconds { get; set; }

    public DateTime GrabbedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Set once qBittorrent no longer reports this torrent (removed, either by the user
    /// in the Queue page or directly in qBittorrent). Non-null means this row is History-only.</summary>
    public DateTime? RemovedFromClientAt { get; set; }

    [StringLength(2000)]
    public string? ErrorMessage { get; set; }
}
