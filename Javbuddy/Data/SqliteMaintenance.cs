using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Javbuddy.Data;

/// <summary>Whole-database SQLite upkeep that EF Core has no API for: refreshing the query planner's statistics and
/// rebuilding the file to reclaim the free pages deletes leave behind.</summary>
public static class SqliteMaintenance
{
    /// <summary>Runs <c>PRAGMA optimize</c> over every table (0x10000), analyzing those whose statistics are missing
    /// or stale (0x02). Plain <c>optimize</c> only looks at tables this connection has queried, so on a fresh one it
    /// does nothing. SQLite caps each analysis, so it stays quick on a large table.</summary>
    public static Task OptimizeAsync(DatabaseFacade database, CancellationToken ct) =>
        database.ExecuteSqlRawAsync("PRAGMA optimize=0x10002;", ct);

    /// <summary>Rebuilds the database file without its free pages, then checkpoints the write-ahead log (which the
    /// rebuild runs through in WAL mode) and truncates it, so the space is actually returned to the disk. Readers carry
    /// on meanwhile; writers wait on the lock for the duration.</summary>
    public static async Task VacuumAsync(DatabaseFacade database, CancellationToken ct)
    {
        await database.ExecuteSqlRawAsync("VACUUM;", ct);
        await database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", ct);
    }

    /// <summary>The database's size in bytes (page count × page size), excluding the write-ahead log.</summary>
    public static async Task<long> GetSizeBytesAsync(DatabaseFacade database, CancellationToken ct) =>
        await database.SqlQueryRaw<long>("SELECT page_count * page_size AS \"Value\" FROM pragma_page_count(), pragma_page_size()")
            .SingleAsync(ct);
}
