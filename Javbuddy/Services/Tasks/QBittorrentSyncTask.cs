using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.QBittorrent;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Tasks;

/// <summary>Reconciles TorrentDownload rows against qBittorrent's live torrent list: fills in
/// the Hash for freshly-grabbed rows (correlated by the movie-code tag set at grab time — see
/// TorrentGrabService), updates progress/speed/ETA/status for already-matched rows, and marks a
/// row RemovedFromClientAt once qBittorrent no longer reports it — which is what keeps Activity
/// &gt; History accurate even after a torrent is removed or the app restarts. Runs on the
/// existing shared 1-minute ScheduledTaskHostedService loop, so — regardless of GetInterval below
/// — this effectively runs at most once a minute automatically; the Activity &gt; Queue page also
/// triggers it on demand via its Refresh button (same "run now" pattern as System &gt; Tasks),
/// and separately live-polls qBittorrent directly for sub-minute progress while it's open.</summary>
public class QBittorrentSyncTask(IQBittorrentClient qBittorrentClient, IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration) : IScheduledTask
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(10);

    private readonly IQBittorrentClient qBittorrentClient = qBittorrentClient;
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly IConfiguration configuration = configuration;

    public string Name => "qBittorrent Sync";

    public string Description =>
        "Reconciles tracked torrent downloads against qBittorrent's live torrent list — fills in hashes, " +
        "progress, and removal status for Activity > Queue.";

    // Runs every ~10-30s rather than the hours/minutes-scale interval System > Tasks is meant
    // for — keep it off that page (Activity > Queue is where its effects actually show up).
    public bool ShowInTasksUi => false;

    // Runs on essentially every tick of the shared 1-minute scheduler loop — a ScheduledTaskRun
    // row and a "Running scheduled task" log line for every single run would just be noise.
    public bool RecordRunHistory => false;

    public TimeSpan GetInterval()
    {
        var configured = configuration["QBittorrent:SyncIntervalSeconds"];
        if (double.TryParse(configured, out var seconds) && seconds > 0)
        {
            var interval = TimeSpan.FromSeconds(seconds);
            return interval < MinimumInterval ? MinimumInterval : interval;
        }
        return DefaultInterval;
    }

    public async Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress)
    {
        var liveTorrents = await qBittorrentClient.GetTorrentsAsync(ct);
        var byHash = liveTorrents
            .Where(t => t.Hash is not null)
            .ToDictionary(t => t.Hash!, t => t, StringComparer.OrdinalIgnoreCase);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tracked = await db.TorrentDownloads.Where(d => d.RemovedFromClientAt == null).ToListAsync(ct);

        var correlated = 0;
        var updated = 0;
        var removed = 0;

        foreach (var row in tracked)
        {
            if (row.Hash is null)
            {
                // Freshly grabbed — correlate by the movie-code tag set at grab time.
                var match = liveTorrents.FirstOrDefault(t =>
                    t.Tags is not null
                    && t.Tags.Split(',', StringSplitOptions.TrimEntries).Contains(row.MovieCode, StringComparer.OrdinalIgnoreCase));
                if (match is not null)
                {
                    row.Hash = match.Hash;
                    ApplyLiveState(row, match);
                    correlated++;
                }
                continue;
            }

            if (byHash.TryGetValue(row.Hash, out var live))
            {
                ApplyLiveState(row, live);
                updated++;
            }
            else
            {
                row.RemovedFromClientAt = DateTime.UtcNow;
                removed++;
            }
        }

        await db.SaveChangesAsync(ct);
        return $"correlated {correlated}, updated {updated}, removed {removed}";
    }

    private static void ApplyLiveState(TorrentDownload row, QBittorrentTorrentDto live)
    {
        row.Progress = live.Progress;
        row.DownloadSpeedBytesPerSec = live.DlSpeed;
        row.EtaSeconds = live.Eta;
        row.Status = MapStatus(live.State);
        row.SavePath = live.SavePath;
        row.ContentPath = live.ContentPath;
        if (live.CompletionOn > 0)
        {
            row.CompletedAt = DateTimeOffset.FromUnixTimeSeconds(live.CompletionOn).UtcDateTime;
        }
    }

    // qBittorrent's post-completion states, mapped to two distinct statuses: Seeding while it's
    // still actively (or waiting to) upload, Completed once it's been paused/stopped. Newer
    // qBittorrent versions renamed pausedUP/pausedDL to stoppedUP/stoppedDL — both are handled.
    private static TorrentDownloadStatus MapStatus(string? state) => state switch
    {
        "error" or "missingFiles" => TorrentDownloadStatus.Error,
        "uploading" or "stalledUP" or "forcedUP" or "queuedUP" => TorrentDownloadStatus.Seeding,
        "pausedUP" or "stoppedUP" or "checkingUP" => TorrentDownloadStatus.Completed,
        _ => TorrentDownloadStatus.Downloading
    };
}
