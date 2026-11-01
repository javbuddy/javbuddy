using Javbuddy.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Backups;

/// <summary>What <see cref="BackupDatabase.InspectAsync"/> reads out of a database file.</summary>
public sealed record DatabaseSummary(IReadOnlyList<string> AppliedMigrations, int Movies, int Actors, int Tags, int Scenes)
{
    public string MigrationId => AppliedMigrations.LastOrDefault() ?? "";
}

/// <summary>The SQLite file operations behind backups, all on a standalone connection (never the app's pooled one):
/// a consistent snapshot through SQLite's online backup API, and reading or checking a snapshot or staged copy.</summary>
public static class BackupDatabase
{
    private static string Standalone(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Pooling = false,
    }.ToString();

    /// <summary>Copies the live database to <paramref name="destinationPath"/> as one consistent snapshot, including
    /// whatever is still in the write-ahead log, while the app keeps reading and writing.</summary>
    public static void Snapshot(string sourceConnectionString, string destinationPath)
    {
        var source = new SqliteConnectionStringBuilder(sourceConnectionString) { Pooling = false };
        // The mode would make the copy refuse to create the file, or open it read-only.
        source.Remove("Mode");
        using var from = new SqliteConnection(source.ToString());
        using var to = new SqliteConnection(Standalone(destinationPath));
        from.Open();
        to.Open();
        from.BackupDatabase(to);
    }

    private static AppDbContext OpenContext(string path) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Standalone(path)).Options);

    /// <summary>The migrations a database has applied and its main row counts. Throws when the file isn't a
    /// readable Javbuddy database.</summary>
    public static async Task<DatabaseSummary> InspectAsync(string path, CancellationToken ct)
    {
        await using var db = OpenContext(path);
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
        return new DatabaseSummary(
            applied,
            await db.Movies.CountAsync(ct),
            await db.Actors.CountAsync(ct),
            await db.Tags.CountAsync(ct),
            await db.Scenes.CountAsync(ct));
    }

    /// <summary>Null when <paramref name="path"/> is a sound Javbuddy database this build can open, else why not:
    /// not a database, failing SQLite's integrity check, or migrated by a newer build than this one.</summary>
    public static async Task<string?> ValidateAsync(string path, CancellationToken ct)
    {
        try
        {
            await using (var connection = new SqliteConnection(Standalone(path)))
            {
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA integrity_check;";
                if (await command.ExecuteScalarAsync(ct) as string != "ok")
                {
                    return "The backup's database failed SQLite's integrity check.";
                }
            }

            await using var db = OpenContext(path);
            var applied = await db.Database.GetAppliedMigrationsAsync(ct);
            var known = db.Database.GetMigrations().ToHashSet();
            if (!applied.Any())
            {
                return "The backup doesn't contain a Javbuddy database.";
            }

            return applied.Any(m => !known.Contains(m))
                ? "The backup was made by a newer version of Javbuddy than this one; update Javbuddy to restore it."
                : null;
        }
        catch (SqliteException)
        {
            return "The backup doesn't contain a readable SQLite database.";
        }
        catch (InvalidOperationException)
        {
            return "The backup doesn't contain a Javbuddy database.";
        }
    }
}
