namespace Javbuddy.Services.ClipExport;

/// <summary>Deletes prepared clips nobody downloaded (the tab closed, the browser blocked the download) and anything a restart
/// left in the clip-exports folder: once at startup, then every <see cref="Interval"/>, so a clip is gone at most
/// <see cref="ClipExportDownloads.Lifetime"/> + <see cref="Interval"/> after it was prepared, with or without another export.</summary>
public sealed class ClipExportSweeper(ClipExportDownloads downloads, ILogger<ClipExportSweeper> logger, TimeProvider? timeProvider = null) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider ?? TimeProvider.System);
        do
        {
            try
            {
                downloads.Sweep();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Sweeping expired clip downloads failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
