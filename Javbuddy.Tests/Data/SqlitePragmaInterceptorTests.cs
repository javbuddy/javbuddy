using System.Data.Common;
using System.Globalization;
using Javbuddy.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Tests.Data;

public sealed class SqlitePragmaInterceptorTests : IDisposable
{
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("javbuddy-pragma-");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        dir.Delete(recursive: true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AfterEnableWal_ANewConnection_GetsWalAndAllConnectionPragmas(bool openAsync)
    {
        var options = Options(pooling: false);
        await using (var setup = new AppDbContext(options))
            SqlitePragmaInterceptor.EnableWal(setup.Database, NullLogger.Instance);

        await using var db = new AppDbContext(options);
        if (openAsync)
            await db.Database.OpenConnectionAsync();
        else
            db.Database.OpenConnection();

        var conn = db.Database.GetDbConnection();
        Assert.Equal("wal", await Scalar(conn, "PRAGMA journal_mode"));
        Assert.Equal("1", await Scalar(conn, "PRAGMA synchronous"));
        Assert.Equal("5000", await Scalar(conn, "PRAGMA busy_timeout"));
        Assert.Equal("-8000", await Scalar(conn, "PRAGMA cache_size"));
        Assert.Equal("67108864", await Scalar(conn, "PRAGMA journal_size_limit"));
    }

    [Fact]
    public async Task EnableWal_AppliesTheWalOnlyPragmasToItsOwnConnectionToo()
    {
        await using var db = new AppDbContext(Options(pooling: false));
        await db.Database.OpenConnectionAsync();

        SqlitePragmaInterceptor.EnableWal(db.Database, NullLogger.Instance);

        // EnableWal doesn't close a connection the caller already opened, so this is the one it configured.
        var conn = db.Database.GetDbConnection();
        Assert.Equal("wal", await Scalar(conn, "PRAGMA journal_mode"));
        Assert.Equal("1", await Scalar(conn, "PRAGMA synchronous"));
    }

    [Fact]
    public async Task WithoutWal_SynchronousStaysAtTheSafeDefault()
    {
        await using var db = new AppDbContext(Options(pooling: false));
        await db.Database.OpenConnectionAsync();

        var conn = db.Database.GetDbConnection();
        Assert.Equal("delete", await Scalar(conn, "PRAGMA journal_mode"));
        Assert.Equal("2", await Scalar(conn, "PRAGMA synchronous"));
        Assert.Equal("5000", await Scalar(conn, "PRAGMA busy_timeout"));
    }

    [Fact]
    public async Task APooledConnection_IsConfiguredOnlyOnItsFirstOpen()
    {
        var options = Options(pooling: true);
        await using var db = new AppDbContext(options);
        await db.Database.OpenConnectionAsync();
        var conn = db.Database.GetDbConnection();
        await Scalar(conn, "PRAGMA cache_size=-1234");
        await db.Database.CloseConnectionAsync();

        // Reopening reuses the pooled handle, so the interceptor leaves the value set above alone.
        await db.Database.OpenConnectionAsync();
        Assert.Equal("-1234", await Scalar(conn, "PRAGMA cache_size"));
    }

    [Fact]
    public void EnableWal_WhenTheDatabaseCantUseWal_KeepsItsJournalModeInsteadOfThrowing()
    {
        // An in-memory database can't use WAL; SQLite keeps "memory", like a filesystem without shared memory keeps "delete".
        using var keepAlive = new SqliteConnection("Data Source=pragma-no-wal;Mode=Memory;Cache=Shared");
        keepAlive.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=pragma-no-wal;Mode=Memory;Cache=Shared")
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
        using var db = new AppDbContext(options);

        SqlitePragmaInterceptor.EnableWal(db.Database, NullLogger.Instance);

        using var command = keepAlive.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        Assert.Equal("memory", command.ExecuteScalar());
    }

    private DbContextOptions<AppDbContext> Options(bool pooling) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(dir.FullName, "test.db")};Pooling={pooling}")
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

    private static async Task<string> Scalar(DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture)!;
    }
}
