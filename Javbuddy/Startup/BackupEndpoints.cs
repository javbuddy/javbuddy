using Javbuddy.Services.Backups;

namespace Javbuddy.Startup;

/// <summary>The database backup download. Like the rest of the app it has no authentication of its own.</summary>
public static class BackupEndpoints
{
    public static void MapBackupEndpoints(this WebApplication app) =>
        // The name is only ever looked up among the backup folder's own files (never joined into a path), so
        // it can't reach anything else.
        app.MapGet("/api/backups/{fileName}/download", (string fileName, IBackupService backups) =>
            backups.FindPath(fileName) is { } path
                ? Results.File(path, "application/zip", fileName)
                : Results.NotFound());
}
