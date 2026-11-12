using Javbuddy.Models;
using Javbuddy.Services.QBittorrent;

namespace Javbuddy.Services.Torrents;

/// <summary>Merges qBittorrent's live torrent list into the Queue page's already-loaded rows
/// (in memory only; nothing is saved).</summary>
public static class TorrentLiveProgress
{
    /// <summary>Copies progress, download speed and ETA onto each row whose hash (case-insensitive)
    /// matches a live torrent. Rows without a hash or without a live match are left as they were.</summary>
    public static void Apply(IEnumerable<TorrentDownload> rows, IEnumerable<QBittorrentTorrentDto> live)
    {
        var byHash = live
            .Where(t => t.Hash is not null)
            .ToDictionary(t => t.Hash!, t => t, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (row.Hash is not null && byHash.TryGetValue(row.Hash, out var liveTorrent))
            {
                row.Progress = liveTorrent.Progress;
                row.DownloadSpeedBytesPerSec = liveTorrent.DlSpeed;
                row.EtaSeconds = liveTorrent.Eta;
            }
        }
    }
}
