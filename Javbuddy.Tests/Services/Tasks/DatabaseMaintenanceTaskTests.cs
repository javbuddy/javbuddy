using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Tests.Services.Tasks;

public sealed class DatabaseMaintenanceTaskTests : IDisposable
{
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("javbuddy-dbmaint-");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        dir.Delete(recursive: true);
    }

    [Fact]
    public async Task RunAsync_WritesPlannerStatistics_AndReclaimsFreePages()
    {
        // A file database in WAL mode, as the app runs it, so the vacuum also has to checkpoint the log back down.
        var dbPath = Path.Combine(dir.FullName, "test.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath};Pooling=False")
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
        var factory = new Factory(options);
        long sizeWithFreePages;
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            SqlitePragmaInterceptor.EnableWal(db.Database, NullLogger.Instance);
            db.Tags.AddRange(Enumerable.Range(0, 2000).Select(i => new Tag { Name = $"Tag {i} {new string('x', 150)}" }));
            await db.SaveChangesAsync();
            await db.Tags.Where(t => t.Id > 100).ExecuteDeleteAsync();
            sizeWithFreePages = await SqliteMaintenance.GetSizeBytesAsync(db.Database, CancellationToken.None);
        }

        var summary = await new DatabaseMaintenanceTask(factory).RunAsync(CancellationToken.None, new Progress<TaskProgress>());

        await using (var db = factory.CreateDbContext())
        {
            var size = await SqliteMaintenance.GetSizeBytesAsync(db.Database, CancellationToken.None);
            Assert.True(size < sizeWithFreePages / 2, $"vacuum should reclaim the deleted rows' pages ({sizeWithFreePages} → {size} bytes)");
            var statistics = await db.Database.SqlQueryRaw<string>("SELECT tbl AS \"Value\" FROM sqlite_stat1").ToListAsync();
            Assert.Contains("Tags", statistics);
        }
        Assert.True(new FileInfo(dbPath).Length < sizeWithFreePages / 2, "the space should be returned to the disk too");
        Assert.StartsWith("optimized statistics, vacuumed ", summary);
    }

    [Fact]
    public async Task RunAsync_WithFewFreePages_SkipsTheVacuum()
    {
        var dbPath = Path.Combine(dir.FullName, "small.db");
        var factory = new Factory(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath};Pooling=False")
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options);
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Tags.AddRange(Enumerable.Range(0, 2000).Select(i => new Tag { Name = $"Tag {i} {new string('x', 150)}" }));
            await db.SaveChangesAsync();
            await db.Tags.Where(t => t.Id <= 100).ExecuteDeleteAsync();
        }

        var summary = await new DatabaseMaintenanceTask(factory).RunAsync(CancellationToken.None, new Progress<TaskProgress>());

        Assert.StartsWith("optimized statistics, skipped vacuum (", summary);
        await using (var db = factory.CreateDbContext())
            Assert.True(await SqliteMaintenance.GetFreeBytesAsync(db.Database, CancellationToken.None) > 0, "the free pages should be left alone");
    }

    [Theory]
    // Database and temp dir on one volume: the temp copy and the log both land there, so it needs the live size twice.
    [InlineData("/data", "/data", 250, null)]
    [InlineData("/data", "/data", 150, "/data")]
    // On separate volumes, each needs it once.
    [InlineData("/data", "/tmp", 150, null)]
    [InlineData("/data", "/tmp", 50, "/data")]
    public void FindDiskSpaceShortfall_NeedsTheLiveSizeOncePerUseOfEachVolume(string dbRoot, string tempRoot, long available, string? expected)
    {
        (string, long) Space(string path) => (path.StartsWith("/data/", StringComparison.Ordinal) ? dbRoot : tempRoot, available);

        var shortfall = DatabaseMaintenanceTask.FindDiskSpaceShortfall(100, "/data/javbuddy.db", "/tmp/", Space);

        Assert.Equal(expected, shortfall?.Root);
    }

    private sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
