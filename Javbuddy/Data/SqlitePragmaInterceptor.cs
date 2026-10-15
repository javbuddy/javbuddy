using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Javbuddy.Data;

/// <summary>Applies the connection PRAGMAs every SQLite connection opened for <see cref="AppDbContext"/> should have.
/// WAL lets the UI keep reading while a background task writes (the default rollback journal blocks readers during a
/// commit), and with it <c>synchronous=NORMAL</c> is safe: an app crash loses nothing, and an OS crash or power loss can
/// lose the last commits but never corrupts the file. <c>busy_timeout</c> lets SQLite wait for the lock itself;
/// Microsoft.Data.Sqlite already retries a locked command until its CommandTimeout, so it mainly makes the wait finer-grained.
/// <c>cache_size</c> is per connection and pooled connections keep theirs, so it's raised only moderately (8 MB, default 2 MB).
/// <c>journal_size_limit</c> truncates the <c>-wal</c> file back to 64 MB after a checkpoint, which it otherwise never does.</summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public const string Pragmas =
        "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000; PRAGMA cache_size=-8000; PRAGMA journal_size_limit=67108864;";

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
