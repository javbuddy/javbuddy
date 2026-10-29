using Javbuddy.Data;
using Javbuddy.Services.Common;
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

        var logTruncated = await SqliteMaintenance.VacuumAsync(db.Database, ct);
        var sizeAfter = await SqliteMaintenance.GetSizeBytesAsync(db.Database, ct);
        progress.Report(new TaskProgress(2, 2, "Vacuuming"));

        var summary = $"optimized statistics, vacuumed {ByteSizeFormatter.Format(sizeBefore)} → {ByteSizeFormatter.Format(sizeAfter)}";
        return logTruncated ? summary : summary + "; the write-ahead log was in use and shrinks at its next checkpoint";
    }
}
