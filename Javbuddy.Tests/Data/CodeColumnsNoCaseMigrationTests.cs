using Javbuddy.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Javbuddy.Tests.Data;

/// <summary>The CodeColumnsNoCase migration rebuilds unique indexes under NOCASE, which fails on rows whose codes differ
/// only by case: an existing database (e.g. an older backup) must still upgrade, keeping the first row of each pair.</summary>
public sealed class CodeColumnsNoCaseMigrationTests : IDisposable
{
    private const string PreviousMigration = "20261019053120_ActorIsTransgender";

    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public CodeColumnsNoCaseMigrationTests() => connection.Open();

    public void Dispose() => connection.Dispose();

    [Fact]
    public async Task Upgrade_DropsLaterCaseDuplicatesOfCachedImagesAndCandidates_AndKeepsTheRest()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(options))
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            await Image(1, "abp-123", "Cover", 0, "Original");
            await Image(2, "ABP-123", "Cover", 0, "Original");  // case-duplicate of 1
            await Image(3, "ABP-123", "Cover", 0, "Thumb");     // different variant
            await Image(4, "ABP-123", "Gallery", 0, "Original"); // different role
            await Candidate(1, "abp-123");
            await Candidate(2, "ABP-123");                       // case-duplicate of 1
            await Candidate(3, "XYZ-001");

            await migrator.MigrateAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            Assert.Equal([1, 3, 4], await db.CachedImages.OrderBy(i => i.Id).Select(i => i.Id).ToListAsync());
            Assert.Equal(["abp-123", "XYZ-001"], await db.DiscoveredMovieCandidates.OrderBy(c => c.Id).Select(c => c.Code).ToListAsync());
        }
    }

    private Task Image(int id, string code, string role, int index, string variant) =>
        Exec($"""
            INSERT INTO "CachedImages" ("Id", "Code", "Role", "Index", "Variant", "StorageId", "SourceUrl", "SourceIdentity", "SourceLength", "SourceLastWriteUtc", "UpdatedAt")
            VALUES ({id}, '{code}', '{role}', {index}, '{variant}', '00000000-0000-0000-0000-00000000000{id}', '', '', 0, '2026-01-01', '2026-01-01')
            """);

    private Task Candidate(int id, string code) =>
        Exec($"""
            INSERT INTO "DiscoveredMovieCandidates" ("Id", "Code", "Title", "Studio", "ActressNames", "CoverImageUrl", "GalleryImageUrls", "SourceName", "Status", "IsUpcoming", "FirstSeenAt", "LastSeenAt")
            VALUES ({id}, '{code}', 't', '', '', '', '', 's', 'New', 0, '2026-01-01', '2026-01-01')
            """);

    private async Task Exec(string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
