namespace Javbuddy.Models;

public enum TorrentDownloadStatus
{
    Queued,
    Downloading,
    /// <summary>Finished downloading and actively (or waiting to) seed — distinct from
    /// Completed, which is finished but paused/stopped, not participating in seeding.</summary>
    Seeding,
    Completed,
    Removed,
    Error
}
