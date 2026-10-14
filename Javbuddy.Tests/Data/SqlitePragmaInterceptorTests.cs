using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Data;

public class SqlitePragmaInterceptorTests
{
    [Fact]
    public async Task OpeningAFileDatabase_AppliesWalAndTheConnectionPragmas()
    {
        var dir = Directory.CreateTempSubdirectory("javbuddy-pragma-");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(dir.FullName, "test.db")};Pooling=False")
                .AddInterceptors(new SqlitePragmaInterceptor())
                .Options;

            await using var db = new AppDbContext(options);
            await db.Database.OpenConnectionAsync();

            var conn = db.Database.GetDbConnection();
            Assert.Equal("wal", await Scalar(conn, "PRAGMA journal_mode"));
            Assert.Equal("1", await Scalar(conn, "PRAGMA synchronous"));
            Assert.Equal("5000", await Scalar(conn, "PRAGMA busy_timeout"));
            Assert.Equal("-16000", await Scalar(conn, "PRAGMA cache_size"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static async Task<string> Scalar(System.Data.Common.DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
