using Javbuddy.Services.Backups;
using Javbuddy.Services.Common;

namespace Javbuddy.Services.Tasks;

/// <summary>Creates the automatic database backups (System &gt; Backups sets whether, how often and for how long to
/// keep them). Retention only ever removes automatic backups; manual and pre-restore ones are left alone.</summary>
public sealed class DatabaseBackupTask(IBackupService backups, IBackupSettingsService settings) : IScheduledTask
{
    public string Name => "Database Backup";

    public string Description => "Backs up the database on the schedule set under System > Backups and removes automatic backups past their retention.";

    /// <summary>The configured frequency, or a daily check while automatic backups are off so that turning them on
    /// takes effect within a day.</summary>
    public TimeSpan GetInterval()
    {
        var effective = settings.GetEffectiveCached();
        return TimeSpan.FromDays(effective.Enabled ? effective.FrequencyDays : 1);
    }

    public async Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress)
    {
        if (!(await settings.GetEffectiveAsync(ct)).Enabled)
        {
            return "automatic backups are off";
        }

        progress.Report(new TaskProgress(0, 1, "Backing up the database"));
        var entry = await backups.CreateAsync(BackupKind.Auto, ct);
        progress.Report(new TaskProgress(1, 1, "Backing up the database"));
        return $"created {entry.FileName} ({ByteSizeFormatter.Format(entry.SizeBytes)})";
    }
}
