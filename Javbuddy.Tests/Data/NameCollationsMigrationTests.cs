using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Javbuddy.Tests.Data;

/// <summary>The NameCollationsAndIndexCleanup migration on a database that already holds the case-only duplicates its
/// NOCASE unique indexes reject: it must upgrade in place without losing a tag or actor.</summary>
public sealed class NameCollationsMigrationTests : IDisposable
{
    private const string PreviousMigration = "20261026175136_CodeColumnsNoCase";

    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public NameCollationsMigrationTests() => connection.Open();

    public void Dispose() => connection.Dispose();

    [Fact]
    public async Task Upgrade_RenamesDuplicateTagsAndActors_AndDropsDuplicateAliases()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(options))
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            // The old schema's columns, so seeded as SQL rather than through the current model.
            await Exec("""
                INSERT INTO "Tags" ("Id", "Name", "NeedsReview", "CreatedAt") VALUES
                    (1, 'Creampie', 0, '2026-01-01'), (2, 'creampie', 0, '2026-01-01'), (3, 'CREAMPIE', 0, '2026-01-01'), (4, 'Solo', 0, '2026-01-01'),
                    (5, 'CREAMPIE (2)', 0, '2026-01-01');
                INSERT INTO "Actors" ("Id", "FirstName", "LastName", "IsFavorite", "IsRetired", "CreatedAt") VALUES
                    (1, 'Yua', NULL, 0, 0, '2026-01-01'), (2, 'yua', NULL, 0, 0, '2026-01-01'),
                    (3, 'Yua', 'Mikami', 0, 0, '2026-01-01'), (4, 'YUA', 'mikami', 0, 0, '2026-01-01'),
                    (5, 'Yua (2)', NULL, 0, 0, '2026-01-01');
                INSERT INTO "ActorAliases" ("ActorId", "Name", "CreatedAt") VALUES
                    (1, 'Alias', '2026-01-01'), (1, 'ALIAS', '2026-01-01'), (2, 'Alias', '2026-01-01');
                INSERT INTO "TagReplacementRules" ("SourceValue", "MatchMode", "TargetTagId", "CreatedAt") VALUES
                    ('bar', 'Exact', 4, '2026-01-01'), ('BAR', 'Exact', 1, '2026-01-01');
                """);

            await migrator.MigrateAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            // A suffix another name already has (ignoring case) is skipped: "CREAMPIE (2)" and "Yua (2)" exist already.
            Assert.Equal(["Creampie", "creampie (3)", "CREAMPIE (4)", "Solo", "CREAMPIE (2)"],
                await db.Tags.OrderBy(t => t.Id).Select(t => t.Name).ToListAsync());
            Assert.Equal(["Yua", "yua (3)", "Yua", "YUA (2)", "Yua (2)"], await db.Actors.OrderBy(a => a.Id).Select(a => a.FirstName).ToListAsync());
            Assert.Equal([(1, "Alias"), (2, "Alias")],
                (await db.ActorAliases.OrderBy(a => a.Id).Select(a => new { a.ActorId, a.Name }).ToListAsync()).Select(a => (a.ActorId, a.Name)));
            // Exact-mode rules are distinct per casing, so they're left alone.
            Assert.Equal(["bar", "BAR"], await db.TagReplacementRules.OrderBy(r => r.Id).Select(r => r.SourceValue).ToListAsync());
        }

        // The upgraded indexes now reject what used to slip through.
        await AssertRejected(db => db.Tags.Add(new Tag { Name = "SOLO" }));
        await AssertRejected(db => db.Actors.Add(new Actor { FirstName = "YUA" }));
        await AssertRejected(db => db.Actors.Add(new Actor { FirstName = "yua", LastName = "MIKAMI" }));

        async Task AssertRejected(Action<AppDbContext> add)
        {
            await using var db = new AppDbContext(options);
            add(db);
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.True(SqliteConstraintErrors.IsDuplicateKey(ex), ex.InnerException?.Message);
        }
    }

    private async Task Exec(string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
