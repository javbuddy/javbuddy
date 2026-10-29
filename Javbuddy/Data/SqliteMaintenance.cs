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
    /// rebuild runs through in WAL mode) and truncates it, so the space is returned to the disk. Returns false when a
    /// reader kept the log from being truncated; it shrinks at a later checkpoint instead.
    /// The rebuild holds the write lock throughout: other writers wait only up to their command timeout, and a WAL-less
    /// database blocks readers too. Cancellation is only checked before it starts, so keep it to when it's worth it.</summary>
    public static async Task<bool> VacuumAsync(DatabaseFacade database, CancellationToken ct)
    {
        await database.ExecuteSqlRawAsync("VACUUM;", ct);

        await database.OpenConnectionAsync(ct);
        try
        {
            await using var command = database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await using var reader = await command.ExecuteReaderAsync(ct);
            // The first column is 1 when a reader or writer blocked the checkpoint (0 on a database not in WAL mode).
            return !await reader.ReadAsync(ct) || reader.GetInt32(0) == 0;
        }
        finally
        {
            await database.CloseConnectionAsync();
        }
    }

    /// <summary>The database's size in bytes (page count × page size), excluding the write-ahead log.</summary>
    public static async Task<long> GetSizeBytesAsync(DatabaseFacade database, CancellationToken ct) =>
        await database.SqlQueryRaw<long>("SELECT page_count * page_size AS \"Value\" FROM pragma_page_count(), pragma_page_size()")
            .SingleAsync(ct);

    /// <summary>The bytes taken by free pages, which <see cref="VacuumAsync"/> reclaims.</summary>
    public static async Task<long> GetFreeBytesAsync(DatabaseFacade database, CancellationToken ct) =>
        await database.SqlQueryRaw<long>("SELECT freelist_count * page_size AS \"Value\" FROM pragma_freelist_count(), pragma_page_size()")
            .SingleAsync(ct);
}
