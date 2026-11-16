using Javbuddy.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Javbuddy.Tests.Data;

/// <summary>The AddActorJellyfinServerId migration adds a nullable column: an existing database must keep every
/// actor's Jellyfin person link, with the server id empty until the next link sync fills it in.</summary>
public sealed class ActorJellyfinServerIdMigrationTests : IDisposable
{
    private const string PreviousMigration = "20261115143215_AddActorPlaceholder";

    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");

    public ActorJellyfinServerIdMigrationTests() => connection.Open();

    public void Dispose() => connection.Dispose();

    [Fact]
    public async Task Upgrade_KeepsJellyfinPersonIds_AndLeavesTheServerIdEmpty()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(options))
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            await Insert("Actors", ("Id", 1), ("FirstName", "Yua"), ("JellyfinPersonId", "person-1"));
            await Insert("Actors", ("Id", 2), ("FirstName", "Remu"));

            await migrator.MigrateAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            var actors = await db.Actors.OrderBy(a => a.Id).ToListAsync();
            Assert.Equal(["person-1", null], actors.Select(a => a.JellyfinPersonId));
            Assert.All(actors, a => Assert.Null(a.JellyfinServerId));
        }
    }

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
}
