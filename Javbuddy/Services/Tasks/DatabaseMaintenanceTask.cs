using Javbuddy.Data;
using Javbuddy.Services.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Tasks;

/// <summary>Keeps the SQLite database healthy: refreshes the query planner's statistics, without which it guesses how
/// selective each index is, and vacuums the file to return the free pages that deleted rows leave behind (nothing
/// else ever shrinks it). The vacuum rewrites the whole file and holds the write lock meanwhile, so it only runs once
/// free pages make up a worthwhile share of the file.</summary>
public sealed class DatabaseMaintenanceTask(IDbContextFactory<AppDbContext> dbFactory) : IScheduledTask
{
    private const double MinFreeShareToVacuum = 0.10;

    public string Name => "Database Maintenance";

    public string Description => "Refreshes the database's query planner statistics and vacuums it to reclaim unused space.";

    public TimeSpan GetInterval() => TimeSpan.FromDays(7);

    public async Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        progress.Report(new TaskProgress(0, 2, "Optimizing query planner statistics"));
        await SqliteMaintenance.OptimizeAsync(db.Database, ct);

        progress.Report(new TaskProgress(1, 2, "Vacuuming"));
        var sizeBefore = await SqliteMaintenance.GetSizeBytesAsync(db.Database, ct);
        var freeBytes = await SqliteMaintenance.GetFreeBytesAsync(db.Database, ct);
        if (freeBytes < sizeBefore * MinFreeShareToVacuum)
        {
            progress.Report(new TaskProgress(2, 2, "Vacuuming"));
            return $"optimized statistics, skipped vacuum ({ByteSizeFormatter.Format(freeBytes)} free of {ByteSizeFormatter.Format(sizeBefore)})";
        }

        if (new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource is { Length: > 0 } dataSource
            && dataSource != ":memory:"
            && FindDiskSpaceShortfall(sizeBefore - freeBytes, Path.GetFullPath(dataSource), Path.GetTempPath(), AvailableSpace) is { } shortfall)
        {
            progress.Report(new TaskProgress(2, 2, "Vacuuming"));
            return $"optimized statistics, skipped vacuum: it needs {ByteSizeFormatter.Format(shortfall.Needed)} free on {shortfall.Root}, "
                + $"which has {ByteSizeFormatter.Format(shortfall.Available)}";
        }

        var logTruncated = await SqliteMaintenance.VacuumAsync(db.Database, ct);
        var sizeAfter = await SqliteMaintenance.GetSizeBytesAsync(db.Database, ct);
        progress.Report(new TaskProgress(2, 2, "Vacuuming"));

        var summary = $"optimized statistics, vacuumed {ByteSizeFormatter.Format(sizeBefore)} → {ByteSizeFormatter.Format(sizeAfter)}";
        return logTruncated ? summary : summary + "; the write-ahead log was in use and shrinks at its next checkpoint";
    }

    /// <summary>The volume that lacks room for a vacuum of <paramref name="liveBytes"/> of data, or null when both have
    /// it. A vacuum writes a copy of the live pages to a temp file and, in WAL mode, all of them again to the log next
    /// to the database, so each volume needs the live size once per use (twice when they're the same one).
    /// <paramref name="availableSpace"/> maps a path to its volume's root and free bytes.</summary>
    public static (string Root, long Needed, long Available)? FindDiskSpaceShortfall(
        long liveBytes, string databasePath, string tempPath, Func<string, (string Root, long Available)> availableSpace)
    {
        var volumes = new[] { availableSpace(databasePath), availableSpace(tempPath) };
        return volumes
            .GroupBy(v => v.Root, StringComparer.Ordinal)
            .Select(g => (Root: g.Key, Needed: liveBytes * g.Count(), g.First().Available))
            .Where(v => v.Available < v.Needed)
            .Select(v => ((string, long, long)?)v)
            .FirstOrDefault();
    }

    // The mounted volume holding the path: the drive whose root is the longest prefix of it.
    private static (string Root, long Available) AvailableSpace(string path)
    {
        var drive = DriveInfo.GetDrives()
            .Where(d => d.IsReady && path.StartsWith(d.RootDirectory.FullName,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .MaxBy(d => d.RootDirectory.FullName.Length);
        return drive is null ? (path, long.MaxValue) : (drive.RootDirectory.FullName, drive.AvailableFreeSpace);
    }
}
