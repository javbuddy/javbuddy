using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Javbuddy.Data;

/// <summary>Applies the connection PRAGMAs every SQLite connection opened for <see cref="AppDbContext"/> should have.
/// WAL lets the UI keep reading while a background task writes (the default rollback journal blocks readers during a
/// commit), and with it <c>synchronous=NORMAL</c> is safe: a crash can lose the last commits but never corrupts the file.
/// <c>busy_timeout</c> makes a writer wait for the lock instead of failing with SQLITE_BUSY.</summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public const string Pragmas =
        "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000; PRAGMA cache_size=-16000;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        Apply(connection);

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }
}
