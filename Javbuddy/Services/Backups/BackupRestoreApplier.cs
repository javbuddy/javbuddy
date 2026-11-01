using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Javbuddy.Services.Backups;

/// <summary>Swaps in a restore staged by <see cref="BackupService.StageRestoreAsync"/>. Runs at startup, before
/// migrations and before anything has the database open, because the file can't be replaced under a running app's
/// pooled connections. The database being replaced is first kept as a <c>pre-restore</c> backup; if that or the swap
/// fails, the current database stays as it was.</summary>
public static class BackupRestoreApplier
{
    /// <summary>True when a staged restore replaced the database. A staged restore is consumed either way, so a
    /// failing one doesn't retry on every start.</summary>
    public static async Task<bool> ApplyPendingAsync(BackupLocation location, TimeProvider time, ILogger logger, CancellationToken ct = default)
    {
        if (!File.Exists(location.PendingDatabasePath))
        {
            return false;
        }

        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(location.DatabasePath))
            {
                await KeepPreRestoreBackupAsync(location, time, ct);
            }

            // Staged next to the database so the final move is a same-volume rename, whatever Backup__Path says.
            var swapPath = location.DatabasePath + ".restoring";
            File.Copy(location.PendingDatabasePath, swapPath, overwrite: true);
            // Anything still in the old database's log belongs to the database being replaced.
            File.Delete(location.DatabasePath + "-wal");
            File.Delete(location.DatabasePath + "-shm");
            File.Move(swapPath, location.DatabasePath, overwrite: true);
            logger.LogWarning("Restored the database from a staged backup; the previous database is kept as a pre-restore backup.");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Applying the staged database restore failed; keeping the current database.");
            return false;
        }
        finally
        {
            try
            {
                Directory.Delete(location.PendingFolder, recursive: true);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not remove the staged restore folder {Folder}.", location.PendingFolder);
            }
        }
    }

    internal static async Task KeepPreRestoreBackupAsync(BackupLocation location, TimeProvider time, CancellationToken ct)
    {
        Directory.CreateDirectory(location.Folder);
        var work = BackupArchive.CreateWorkFolder(location.Folder);
        try
        {
            var snapshot = Path.Combine(work.FullName, BackupArchive.DatabaseEntryName);
            BackupDatabase.Snapshot(location.ConnectionString, snapshot);
            var manifest = await BuildManifestAsync(snapshot, time.GetUtcNow().UtcDateTime, ct);
            var target = BackupService.UniquePath(location.Folder, BackupKind.PreRestore, manifest.CreatedUtc);
            await BackupArchive.CreateAsync(target, snapshot, manifest, ct);
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    /// <summary>The manifest for a database snapshot. Counts stay zero for a database too old or broken to count,
    /// since a backup of it is still worth keeping.</summary>
    internal static async Task<BackupManifest> BuildManifestAsync(string snapshotPath, DateTime createdUtc, CancellationToken ct)
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
        // A SemVer informational version carries the commit hash after '+'; the list shows just the release.
        version = version.Split('+')[0];
        try
        {
            var summary = await BackupDatabase.InspectAsync(snapshotPath, ct);
            return new BackupManifest(version, createdUtc, summary.MigrationId, summary.Movies, summary.Actors, summary.Tags, summary.Scenes);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return new BackupManifest(version, createdUtc, "", 0, 0, 0, 0);
        }
    }
}
