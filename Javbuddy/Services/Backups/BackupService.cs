using Javbuddy.Services.Common;

namespace Javbuddy.Services.Backups;

public interface IBackupService
{
    Task<IReadOnlyList<BackupEntry>> ListAsync(CancellationToken ct = default);

    /// <summary>Snapshots the database into a new backup archive, then deletes automatic backups past retention.</summary>
    Task<BackupEntry> CreateAsync(BackupKind kind, CancellationToken ct = default);

    Task<OperationResult> DeleteAsync(string fileName, CancellationToken ct = default);

    /// <summary>The path of a listed backup for download, or null when no backup has this exact file name.</summary>
    string? FindPath(string fileName);

    /// <summary>Validates an uploaded archive and adds it to the list as a manual backup.</summary>
    Task<OperationResult> ImportAsync(Stream archive, CancellationToken ct = default);

    /// <summary>Validates a backup and stages it for the next startup to swap in; nothing changes until then.</summary>
    Task<OperationResult> StageRestoreAsync(string fileName, CancellationToken ct = default);

    /// <summary>Whether a staged restore waits for a restart.</summary>
    bool RestorePending { get; }

    /// <summary>Deletes automatic backups older than the retention period, always keeping the newest one.</summary>
    Task<int> PruneAsync(CancellationToken ct = default);
}

public sealed class BackupService(
    BackupLocation location,
    IBackupSettingsService settingsService,
    TimeProvider time,
    ILogger<BackupService> logger) : IBackupService
{
    /// <summary>The largest import accepted, and the largest database taken out of an archive.</summary>
    public const long MaxArchiveBytes = 4L * 1024 * 1024 * 1024;

    private readonly SemaphoreSlim gate = new(1, 1);

    public bool RestorePending => File.Exists(location.PendingDatabasePath);

    public async Task<IReadOnlyList<BackupEntry>> ListAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            return List();
        }
        finally
        {
            gate.Release();
        }
    }

    private List<BackupEntry> List()
    {
        if (!Directory.Exists(location.Folder))
        {
            return [];
        }

        var entries = new List<BackupEntry>();
        foreach (var path in Directory.EnumerateFiles(location.Folder, "*.zip"))
        {
            var fileName = Path.GetFileName(path);
            if (BackupArchive.ParseKind(fileName) is not { } kind || BackupArchive.ReadManifest(path) is not { } manifest)
            {
                continue;
            }
            entries.Add(new BackupEntry(fileName, kind, new FileInfo(path).Length, manifest));
        }
        return [.. entries.OrderByDescending(e => e.Manifest.CreatedUtc).ThenByDescending(e => e.FileName, StringComparer.Ordinal)];
    }

    public async Task<BackupEntry> CreateAsync(BackupKind kind, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var entry = await CreateCoreAsync(kind, ct);
            await PruneCoreAsync();
            return entry;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<BackupEntry> CreateCoreAsync(BackupKind kind, CancellationToken ct)
    {
        Directory.CreateDirectory(location.Folder);
        var work = BackupArchive.CreateWorkFolder(location.Folder);
        try
        {
            var snapshot = Path.Combine(work.FullName, BackupArchive.DatabaseEntryName);
            // The snapshot call blocks while SQLite copies the file, so keep it off the caller's thread.
            await Task.Run(() => BackupDatabase.Snapshot(location.ConnectionString, snapshot), ct);
            var manifest = await BackupRestoreApplier.BuildManifestAsync(snapshot, time.GetUtcNow().UtcDateTime, ct);

            var zipPath = Path.Combine(work.FullName, "backup.zip");
            await BackupArchive.CreateAsync(zipPath, snapshot, manifest, ct);

            var target = UniquePath(location.Folder, kind, manifest.CreatedUtc);
            File.Move(zipPath, target);
            logger.LogInformation("Created {Kind} database backup {File}.", BackupArchive.KindName(kind), Path.GetFileName(target));
            return new BackupEntry(Path.GetFileName(target), kind, new FileInfo(target).Length, manifest);
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    /// <summary>A free file name for a backup made at <paramref name="utc"/> (two in the same second get a suffix).</summary>
    internal static string UniquePath(string folder, BackupKind kind, DateTime utc)
    {
        var path = Path.Combine(folder, BackupArchive.FileName(kind, utc));
        for (var duplicate = 2; File.Exists(path); duplicate++)
        {
            path = Path.Combine(folder, BackupArchive.FileName(kind, utc, duplicate));
        }
        return path;
    }

    public async Task<OperationResult> DeleteAsync(string fileName, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var path = FindPath(fileName);
            if (path is null)
            {
                return OperationResult.Fail("That backup no longer exists.");
            }

            File.Delete(path);
            logger.LogInformation("Deleted database backup {File}.", fileName);
            return OperationResult.Ok();
        }
        finally
        {
            gate.Release();
        }
    }

    public string? FindPath(string fileName)
    {
        // Matched against the folder's own file names, so a request can never name a path outside it.
        if (BackupArchive.ParseKind(fileName) is null || !Directory.Exists(location.Folder))
        {
            return null;
        }

        return Directory.EnumerateFiles(location.Folder, "*.zip")
            .FirstOrDefault(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.Ordinal));
    }

    public async Task<OperationResult> ImportAsync(Stream archive, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(location.Folder);
            var work = BackupArchive.CreateWorkFolder(location.Folder);
            try
            {
                var zipPath = Path.Combine(work.FullName, "import.zip");
                await using (var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await CopyBoundedAsync(archive, file, MaxArchiveBytes, ct);
                }

                if (await ValidateArchiveAsync(zipPath, work.FullName, ct) is { } error)
                {
                    return OperationResult.Fail(error);
                }

                var manifest = BackupArchive.ReadManifest(zipPath)!;
                var target = UniquePath(location.Folder, BackupKind.Manual, time.GetUtcNow().UtcDateTime);
                File.Move(zipPath, target);
                logger.LogInformation("Imported database backup {File} (made {Created:u} by Javbuddy {Version}).",
                    Path.GetFileName(target), manifest.CreatedUtc, manifest.AppVersion);
                return OperationResult.Ok();
            }
            catch (InvalidDataException ex)
            {
                return OperationResult.Fail(ex.Message);
            }
            finally
            {
                work.Delete(recursive: true);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OperationResult> StageRestoreAsync(string fileName, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var path = FindPath(fileName);
            if (path is null)
            {
                return OperationResult.Fail("That backup no longer exists.");
            }

            var work = BackupArchive.CreateWorkFolder(location.Folder);
            try
            {
                if (await ValidateArchiveAsync(path, work.FullName, ct) is { } error)
                {
                    return OperationResult.Fail(error);
                }

                Directory.CreateDirectory(location.PendingFolder);
                File.Move(Path.Combine(work.FullName, BackupArchive.DatabaseEntryName), location.PendingDatabasePath, overwrite: true);
                logger.LogInformation("Staged a restore of database backup {File}; it applies at the next startup.", fileName);
                return OperationResult.Ok();
            }
            catch (InvalidDataException ex)
            {
                return OperationResult.Fail(ex.Message);
            }
            finally
            {
                work.Delete(recursive: true);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> PruneAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            return await PruneCoreAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<int> PruneCoreAsync()
    {
        var retention = TimeSpan.FromDays((await settingsService.GetEffectiveAsync()).RetentionDays);
        var cutoff = time.GetUtcNow().UtcDateTime - retention;
        var automatic = List().Where(e => e.Kind == BackupKind.Auto).ToList();
        var expired = automatic.Skip(1).Where(e => e.Manifest.CreatedUtc < cutoff).ToList();
        foreach (var entry in expired)
        {
            File.Delete(Path.Combine(location.Folder, entry.FileName));
            logger.LogInformation("Deleted automatic database backup {File} past its {Days}-day retention.", entry.FileName, retention.TotalDays);
        }
        return expired.Count;
    }

    /// <summary>Extracts the archive's database into <paramref name="workFolder"/> and checks it; returns why it
    /// can't be used, or null. Leaves the extracted database there for the caller.</summary>
    private static async Task<string?> ValidateArchiveAsync(string zipPath, string workFolder, CancellationToken ct)
    {
        if (BackupArchive.ReadManifest(zipPath) is null)
        {
            return "The file is not a Javbuddy backup.";
        }

        var database = Path.Combine(workFolder, BackupArchive.DatabaseEntryName);
        await BackupArchive.ExtractDatabaseAsync(zipPath, database, MaxArchiveBytes, ct);
        return await BackupDatabase.ValidateAsync(database, ct);
    }

    private static async Task CopyBoundedAsync(Stream source, Stream destination, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException("The file is larger than the 4 GB limit.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}
