using Microsoft.Data.Sqlite;

namespace Javbuddy.Services.Backups;

/// <summary>Where the app's SQLite database lives and where its backups are kept. Backups default to a
/// <c>backups</c> folder next to the database (the <c>/data</c> volume in Docker); <c>Backup__Path</c> moves them.</summary>
public sealed record BackupLocation(string Folder, string DatabasePath, string ConnectionString)
{
    /// <summary>Where a restore waits, hidden from the backup list, until the next startup swaps it in.</summary>
    public string PendingFolder => Path.Combine(Folder, ".restore-pending");

    public string PendingDatabasePath => Path.Combine(PendingFolder, BackupArchive.DatabaseEntryName);

    public static BackupLocation Resolve(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=Javbuddy.db";
        // SQLite opens a relative path against the working directory, so that's what the database path means.
        var databasePath = Path.GetFullPath(new SqliteConnectionStringBuilder(connectionString).DataSource);
        var configured = configuration["Backup:Path"];
        var folder = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetDirectoryName(databasePath)!, "backups")
            : Path.GetFullPath(configured);
        return new BackupLocation(folder, databasePath, connectionString);
    }
}
