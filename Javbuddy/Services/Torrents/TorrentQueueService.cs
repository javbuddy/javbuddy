using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Torrents;

public interface ITorrentQueueService
{
    Task<List<TorrentDownload>> GetActiveAsync(CancellationToken ct = default);
    Task<int> GetActiveCountAsync(CancellationToken ct = default);
    Task RemoveAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);

    /// <summary>Runs the qBittorrent sync now, through the scheduled-task launcher, and returns once it has ended
    /// (at once when a sync is already running).</summary>
    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>Copies qBittorrent's live progress, speed and ETA onto <paramref name="rows"/> in memory (nothing is
    /// saved). False, leaving the rows untouched, when qBittorrent can't be reached.</summary>
    Task<bool> ApplyLiveProgressAsync(IEnumerable<TorrentDownload> rows, CancellationToken ct = default);
}

/// <summary>Activity &gt; Queue's reads and removals: the active (not sorted, not removed) downloads,
/// and removing selected ones from qBittorrent (torrent only, files kept) and marking them Removed.</summary>
public class TorrentQueueService(
    IDbContextFactory<AppDbContext> dbFactory,
    IQBittorrentClient qBittorrentClient,
    ScheduledTaskLauncher taskLauncher,
    TorrentChangeNotifier changeNotifier) : ITorrentQueueService
{
    // RemovedFromClientAt/SortedAt (not Status) is the source of truth for "in the queue": an
    // errored torrent stays in it until removed or sorted.
    // Shared with the movie grids' Downloading badge so the two can't drift apart.
    internal static IQueryable<TorrentDownload> Active(IQueryable<TorrentDownload> downloads) =>
        downloads.Where(d => d.RemovedFromClientAt == null && d.SortedAt == null);

    private static IQueryable<TorrentDownload> Active(AppDbContext db) => Active(db.TorrentDownloads);

    public async Task<List<TorrentDownload>> GetActiveAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Active(db).AsNoTracking().OrderByDescending(d => d.GrabbedAt).ToListAsync(ct);
    }

    public async Task<int> GetActiveCountAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Active(db).CountAsync(ct);
    }

    public async Task RemoveAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.TorrentDownloads.Where(d => ids.Contains(d.Id)).ToListAsync(ct);

        foreach (var row in rows.Where(r => r.Hash is not null))
        {
            await qBittorrentClient.DeleteTorrentAsync(row.Hash!, ct: ct);
        }

        var removedAt = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.RemovedFromClientAt = removedAt;
            row.Status = TorrentDownloadStatus.Removed;
        }
        await db.SaveChangesAsync(ct);

        changeNotifier.NotifyChanged();
    }

    public Task RefreshAsync(CancellationToken ct = default) =>
        taskLauncher.Queue(QBittorrentSyncTask.TaskName).WaitAsync(ct);

    public async Task<bool> ApplyLiveProgressAsync(IEnumerable<TorrentDownload> rows, CancellationToken ct = default)
    {
        var result = await qBittorrentClient.GetTorrentsAsync(ct);
        if (!result.Success) return false;

        TorrentLiveProgress.Apply(rows, result.Torrents);
        return true;
    }
}
