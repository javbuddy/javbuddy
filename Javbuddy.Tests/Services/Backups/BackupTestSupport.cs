using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Backups;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Javbuddy.Tests.Services.Backups;

/// <summary>A migrated file database in WAL mode plus its backup folder, in a temp dir: what the backup code sees in
/// production, built from scratch instead of from a developer's own database.</summary>
public sealed class BackupTestEnvironment : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("javbuddy-backup-");

    public BackupTestEnvironment(Dictionary<string, string?>? settings = null)
    {
        Location = BackupLocation.Resolve(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = $"Data Source={Path.Combine(root.FullName, "data", "Javbuddy.db")}",
        }).Build());
        Directory.CreateDirectory(Path.GetDirectoryName(Location.DatabasePath)!);

        Configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        Factory = new FileDbFactory(Location.ConnectionString);
        using var db = Factory.CreateDbContext();
        db.Database.Migrate();
        SqlitePragmaInterceptor.EnableWal(db.Database, NullLogger.Instance);

        Time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero));
        Settings = new BackupSettingsService(Factory, Configuration);
        Service = new BackupService(Location, Settings, Time, NullLogger<BackupService>.Instance);
    }

    public BackupLocation Location { get; }

    public IConfiguration Configuration { get; }

    public FileDbFactory Factory { get; }

    public FakeTimeProvider Time { get; }

    public BackupSettingsService Settings { get; }

    public BackupService Service { get; }

    public async Task AddTagsAsync(int count)
    {
        await using var db = Factory.CreateDbContext();
        var start = await db.Tags.CountAsync();
        db.Tags.AddRange(Enumerable.Range(start, count).Select(i => new Tag { Name = $"Tag {i}" }));
        await db.SaveChangesAsync();
    }

    public async Task<int> TagCountAsync(string? databasePath = null)
    {
        if (databasePath is not null)
        {
            return (await BackupDatabase.InspectAsync(databasePath, CancellationToken.None)).Tags;
        }
        await using var db = Factory.CreateDbContext();
        return await db.Tags.CountAsync();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        root.Delete(recursive: true);
    }

    public sealed class FileDbFactory(string connectionString) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options);
    }
}
