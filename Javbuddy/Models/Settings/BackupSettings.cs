namespace Javbuddy.Models;

/// <summary>Single-row settings for the database backups on System &gt; Backups. Each field can be
/// overridden by a <c>Backup__&lt;Field&gt;</c> environment variable (see BackupSettingsService).</summary>
public class BackupSettings
{
    public int Id { get; set; }

    /// <summary>Whether the Database Backup task creates a backup on its own schedule.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Automatic backups older than this many days are deleted after each backup.</summary>
    public int RetentionDays { get; set; } = 10;

    /// <summary>How often, in days, an automatic backup is created.</summary>
    public int FrequencyDays { get; set; } = 1;
}
