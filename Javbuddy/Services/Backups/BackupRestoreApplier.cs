using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Javbuddy.Services.Backups;

/// <summary>Swaps in a restore staged by <see cref="BackupService.StageRestoreAsync"/>. Runs at startup, before
/// migrations and before anything has the database open, because the file can't be replaced under a running app's
/// pooled connections. The database being replaced is first kept as a <c>pre-restore</c> backup; if that or the swap
/// fails, the current database stays as it was.</summary>
public static class BackupRestoreApplier
{
    /// <summary>True when a staged restore replaced the database. A restore that fails is kept staged (and logged), so
    /// it is retried at the next start instead of silently vanishing; one that succeeds is consumed.</summary>
    public static async Task<bool> ApplyPendingAsync(BackupLocation location, TimeProvider time, ILogger logger, CancellationToken ct = default)
    {
        if (!File.Exists(location.PendingDatabasePath))
        {
            return false;
        }

        // Staged next to the database so the final move is a same-volume rename, whatever Backup__Path says.
        var swapPath = location.DatabasePath + ".restoring";
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(location.DatabasePath))
            {
                await KeepPreRestoreBackupAsync(location, time, logger, ct);
            }

            File.Copy(location.PendingDatabasePath, swapPath, overwrite: true);
            // Anything still in the old database's log belongs to the database being replaced, and is in the
            // pre-restore backup.
            File.Delete(location.DatabasePath + "-wal");
            File.Delete(location.DatabasePath + "-shm");
            File.Move(swapPath, location.DatabasePath, overwrite: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Applying the staged database restore failed; keeping the current database and the staged restore.");
            TryDelete(swapPath, logger);
            return false;
        }

        logger.LogWarning("Restored the database from a staged backup; the previous database is kept as a pre-restore backup.");
        try
        {
            Directory.Delete(location.PendingFolder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not remove the staged restore folder {Folder}.", location.PendingFolder);
        }
        return true;
    }

    /// <summary>Keeps what is about to be replaced: a pre-restore backup archive, or when SQLite can't read the
    /// database (the case where someone restores because it is damaged) a plain copy of its files in a
    /// <c>pre-restore-files-*</c> folder. Throws only when neither could be made.</summary>
    internal static async Task KeepPreRestoreBackupAsync(BackupLocation location, TimeProvider time, ILogger logger, CancellationToken ct)
    {
        Directory.CreateDirectory(location.Folder);
        var work = BackupArchive.CreateWorkFolder(location.Folder);
        try
        {
            var snapshot = Path.Combine(work.FullName, BackupArchive.DatabaseEntryName);
            BackupDatabase.Snapshot(location.ConnectionString, snapshot);
            var manifest = await BuildManifestAsync(snapshot, time.GetUtcNow().UtcDateTime, ct);

            // Built in the scratch folder and moved, so an interrupted write never leaves a half archive in the list.
            var zip = Path.Combine(work.FullName, "backup.zip");
            await BackupArchive.CreateAsync(zip, snapshot, manifest, ct);
            File.Move(zip, BackupService.UniquePath(location.Folder, BackupKind.PreRestore, manifest.CreatedUtc));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not snapshot the current database; keeping a plain copy of its files before the restore.");
            var copy = Directory.CreateDirectory(Path.Combine(location.Folder, $"pre-restore-files-{time.GetUtcNow().UtcDateTime:yyyyMMdd-HHmmss}"));
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                if (File.Exists(location.DatabasePath + suffix))
                {
                    File.Copy(location.DatabasePath + suffix, Path.Combine(copy.FullName, Path.GetFileName(location.DatabasePath) + suffix), overwrite: true);
                }
            }
            logger.LogWarning("The current database files were copied to {Folder}.", copy.FullName);
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    private static void TryDelete(string path, ILogger logger)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not remove {Path}.", path);
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
