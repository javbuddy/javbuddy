using Javbuddy.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Javbuddy.Tests.Data;

/// <summary>The AddActorPlaceholder migration adds a foreign key to Actors, which SQLite can only do by rebuilding the
/// table most actor data points at: an existing database must come through with every actor and the rows that
/// reference one (aliases, images, photos, cast links, actor tags) intact.</summary>
public sealed class ActorPlaceholderMigrationTests : IDisposable
{
    private const string PreviousMigration = "20261109173851_AddCachedImageBlurHash";

    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");

    public ActorPlaceholderMigrationTests() => connection.Open();

    public void Dispose() => connection.Dispose();

    [Fact]
    public async Task Upgrade_KeepsEveryActorAndTheRowsThatReferenceThem_AndAddsTheNullablePlaceholderColumn()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(options))
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            // The old schema's columns, so seeded as SQL rather than through the current model.
            await Insert("Movies", ("Id", 1), ("Code", "ABC-123"));
            await Insert("Actors", ("Id", 1), ("FirstName", "Yua"), ("LastName", "Mikami"), ("IsFavorite", 1));
            await Insert("Actors", ("Id", 2), ("FirstName", "Remu"), ("IsRetired", 1));
            await Insert("ActorAliases", ("ActorId", 1), ("Name", "Mikami Yua"));
            await Insert("ActorAliases", ("ActorId", 2), ("Name", "R"));
            await Insert("MovieActors", ("MovieId", 1), ("ActorId", 1));
            await Insert("MovieActors", ("MovieId", 1), ("ActorId", 2));
            var before = await Counts();

            await migrator.MigrateAsync();

            Assert.Equal(before, await Counts());
        }

        await using (var db = new AppDbContext(options))
        {
            var actors = await db.Actors.OrderBy(a => a.Id).ToListAsync();
            Assert.Equal(["Yua", "Remu"], actors.Select(a => a.FirstName));
            Assert.All(actors, a => Assert.Null(a.PlaceholderMovieId));
            Assert.True(actors[0].IsFavorite);
            Assert.True(actors[1].IsRetired);

            // The new foreign key works: a placeholder goes with its movie, and nothing else does.
            await Insert("Actors", ("Id", 3), ("FirstName", "Redhead"), ("LastName", "ABC-123"), ("PlaceholderMovieId", 1));
            await Exec("""DELETE FROM "Movies" WHERE "Id" = 1""");
            Assert.Equal([1, 2], await db.Actors.OrderBy(a => a.Id).Select(a => a.Id).ToListAsync());
            Assert.Equal(2, await db.ActorAliases.CountAsync());
        }
    }

    private async Task<(long Actors, long Aliases, long CastLinks)> Counts() =>
        (await Scalar("Actors"), await Scalar("ActorAliases"), await Scalar("MovieActors"));

    /// <summary>Inserts a row into the old schema: the given columns, and a zero or empty value for every other column
    /// that is NOT NULL and has no default, so the test names only what it cares about.</summary>
    private async Task Insert(string table, params (string Column, object Value)[] values)
    {
        var given = values.ToDictionary(v => v.Column, v => v.Value);
        var columns = new List<string>();
        var parameters = new List<object>();
        await using (var info = connection.CreateCommand())
        {
            info.CommandText = $"""PRAGMA table_info("{table}")""";
            await using var reader = await info.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (given.TryGetValue(name, out var value))
                {
                    columns.Add(name);
                    parameters.Add(value);
                }
                else if (reader.GetInt32(3) == 1 && reader.IsDBNull(4) && reader.GetInt32(5) == 0)
                {
                    columns.Add(name);
                    parameters.Add(reader.GetString(2).Contains("TEXT", StringComparison.OrdinalIgnoreCase) ? "2026-01-01" : 0);
                }
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""INSERT INTO "{table}" ({string.Join(", ", columns.Select(c => $"\"{c}\""))}) VALUES ({string.Join(", ", columns.Select((_, i) => $"$p{i}"))})""";
        for (var i = 0; i < parameters.Count; i++) command.Parameters.AddWithValue($"$p{i}", parameters[i]);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> Scalar(string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT COUNT(*) FROM "{table}" """;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task Exec(string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
