namespace Javbuddy.Services.Backups;

/// <summary>What a backup archive says about itself in <c>backup.json</c>: the app version and the database's
/// latest applied migration (to refuse a backup from a newer build), and row counts for the list's badges.</summary>
public sealed record BackupManifest(
    string AppVersion,
    DateTime CreatedUtc,
    string MigrationId,
    int Movies,
    int Actors,
    int Tags,
    int Scenes);

public enum BackupKind
{
    Auto,
    Manual,
    PreRestore,
}

public sealed record BackupEntry(string FileName, BackupKind Kind, long SizeBytes, BackupManifest Manifest);
