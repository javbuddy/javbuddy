using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Javbuddy.Data;

/// <summary>Applies the connection PRAGMAs every SQLite connection opened for <see cref="AppDbContext"/> should have.
/// WAL (set once at startup by <see cref="EnableWal"/>, since it's stored in the database file) lets the UI keep reading
/// while a background task writes; the default rollback journal blocks readers during a commit. With WAL,
/// <c>synchronous=NORMAL</c> is safe: an app crash loses nothing, and an OS crash or power loss can lose the last commits
/// but never corrupts the file. Without WAL it isn't, so a connection on a rollback-journal database keeps the default.
/// <c>busy_timeout</c> lets SQLite wait for the lock itself; Microsoft.Data.Sqlite already retries a locked command until
/// its CommandTimeout, so it mainly makes the wait finer-grained. <c>cache_size</c> is raised only moderately (8 MB,
/// default 2 MB) because pooled connections each keep their own. <c>journal_size_limit</c> truncates the <c>-wal</c> file
/// back to 64 MB after a checkpoint, which it otherwise never does.
/// These all stick to the physical connection, and EF opens and closes a pooled connection around every query, so they
/// run once per underlying SQLite handle rather than on every open.</summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public const string ConnectionPragmas =
        "PRAGMA busy_timeout=5000; PRAGMA cache_size=-8000; PRAGMA journal_size_limit=67108864;";

    private const string WalPragmas = "PRAGMA synchronous=NORMAL;";

    // Keyed by the native handle, which a pooled connection keeps across opens; an entry goes away with its handle.
    private static readonly ConditionalWeakTable<object, object> ConfiguredHandles = new();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        ApplyAsync(connection, async: false, CancellationToken.None).GetAwaiter().GetResult();

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default) =>
        ApplyAsync(connection, async: true, cancellationToken);

    /// <summary>Switches the database to WAL. Called once at startup: the mode is stored in the database file, so
    /// every later connection opens in it. Needs shared memory next to the file, so on a filesystem that can't provide
    /// it (NFS, some host bind mounts) it logs a warning and leaves the database on the rollback journal rather than
    /// failing startup.</summary>
    public static void EnableWal(DatabaseFacade database, ILogger logger)
    {
        database.OpenConnection();
        try
        {
            var connection = database.GetDbConnection();
            string? mode;
            try
            {
                mode = Convert.ToString(Execute(connection, "PRAGMA journal_mode=WAL;", scalar: true),
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (SqliteException ex)
            {
                logger.LogWarning(ex, "Could not switch the SQLite database to WAL mode; it stays on the rollback journal.");
                return;
            }

            if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("SQLite kept journal mode {JournalMode} instead of WAL; the database stays on it.", mode);
                return;
            }

            // This connection was configured by the interceptor before WAL was on, so it missed the WAL-only PRAGMAs.
            Execute(connection, WalPragmas, scalar: false);
        }
        finally
        {
            database.CloseConnection();
        }
    }

    private static async Task ApplyAsync(DbConnection connection, bool async, CancellationToken ct)
    {
        if (connection is not SqliteConnection { Handle: { } handle } || ConfiguredHandles.TryGetValue(handle, out _))
            return;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode;";
            var mode = Convert.ToString(async ? await command.ExecuteScalarAsync(ct) : command.ExecuteScalar(),
                System.Globalization.CultureInfo.InvariantCulture);
            command.CommandText = string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase)
                ? ConnectionPragmas + " " + WalPragmas
                : ConnectionPragmas;
            if (async)
                await command.ExecuteNonQueryAsync(ct);
            else
                command.ExecuteNonQuery();
        }

        // Only after the PRAGMAs succeeded, so a failed attempt is retried on the next open.
        ConfiguredHandles.AddOrUpdate(handle, handle);
    }

    private static object? Execute(DbConnection connection, string sql, bool scalar)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return scalar ? command.ExecuteScalar() : command.ExecuteNonQuery();
    }
}
