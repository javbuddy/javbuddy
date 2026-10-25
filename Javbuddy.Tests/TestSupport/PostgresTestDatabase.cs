using System.Collections.Concurrent;
using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Javbuddy.Tests.TestSupport;

/// <summary>
/// Runs the suite against a real Postgres server when the <c>JAVBUDDY_TEST_POSTGRES</c> environment variable holds an
/// admin connection string (e.g. <c>Host=localhost;Username=postgres;Password=dev</c>); unset, tests use SQLite as before.
/// Once per test run a template database is created and brought up to date with the app's Postgres migrations (so they get
/// tested too); each <see cref="TestDbContextFactory"/> then clones it with <c>CREATE DATABASE ... TEMPLATE</c>, which
/// takes tens of milliseconds, and drops its clone on dispose. The template is dropped when the process exits.
/// The admin statements here are test-support DDL, not app-data SQL.
/// </summary>
public static class PostgresTestDatabase
{
    public const string EnvironmentVariable = "JAVBUDDY_TEST_POSTGRES";

    private static readonly Lazy<string?> Template = new(CreateTemplate);

    // Databases not yet dropped, so a test that never disposes its factory doesn't leave one behind: the rest are dropped at process exit.
    private static readonly ConcurrentDictionary<string, bool> Live = new();

    /// <summary>True when the suite is running against Postgres.</summary>
    public static bool IsEnabled => !string.IsNullOrWhiteSpace(AdminConnectionString);

    private static string? AdminConnectionString => Environment.GetEnvironmentVariable(EnvironmentVariable);

    /// <summary>Creates a fresh, fully migrated database and returns its connection string.</summary>
    public static (string ConnectionString, string Name) CreateDatabase()
    {
        var template = Template.Value ?? throw new InvalidOperationException($"{EnvironmentVariable} is not set.");
        var name = $"test_{Stamp()}_{Guid.NewGuid():N}";
        ExecuteAdmin($"""CREATE DATABASE "{name}" TEMPLATE "{template}" """);
        Live[name] = true;
        return (ConnectionStringFor(name), name);
    }

    public static void DropDatabase(string name)
    {
        Live.TryRemove(name, out _);
        ExecuteAdmin($"""DROP DATABASE IF EXISTS "{name}" WITH (FORCE)""");
    }

    public static DbContextOptionsBuilder<PostgresAppDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<PostgresAppDbContext>()
            .UseNpgsql(connectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));

    private static string? CreateTemplate()
    {
        if (!IsEnabled)
        {
            return null;
        }

        DropStaleDatabases();
        var name = $"javbuddy_test_template_{Stamp()}_{Guid.NewGuid():N}";
        ExecuteAdmin($"""CREATE DATABASE "{name}" """);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Parallel.ForEach(Live.Keys, new ParallelOptions { MaxDegreeOfParallelism = 8 }, DropDatabase);
            DropDatabase(name);
        };

        using (var context = new PostgresAppDbContext(Options(ConnectionStringFor(name)).Options))
        {
            context.Database.Migrate();
        }
        return name;
    }

    private static string Stamp() => DateTime.UtcNow.ToString("yyyyMMddHHmmss");

    /// <summary>Drops databases an earlier, killed run left behind (their names carry the creation time), so a crashed or
    /// interrupted run doesn't fill the server. Only ones over 30 minutes old are touched, in case another run is active.</summary>
    private static void DropStaleDatabases()
    {
        var names = new List<string>();
        using (var connection = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = "postgres", Pooling = false }.ConnectionString))
        {
            connection.Open();
            using var command = new NpgsqlCommand(
                "SELECT datname FROM pg_database WHERE datname LIKE 'test\\_%' OR datname LIKE 'javbuddy\\_test\\_template\\_%'", connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }
        }

        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        foreach (var name in names)
        {
            var stamp = name.Split('_') is { Length: >= 3 } parts ? parts[^2] : "";
            if (DateTime.TryParseExact(stamp, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var created)
                && created < cutoff)
            {
                DropDatabase(name);
            }
        }
    }

    // Pooling is off so no idle connection can block CREATE DATABASE ... TEMPLATE or DROP DATABASE, and thousands of
    // short-lived per-test databases don't each leave a connection pool behind.
    private static string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = database, Pooling = false, Timezone = "UTC" }.ConnectionString;

    private static void ExecuteAdmin(string sql)
    {
        using var connection = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = "postgres", Pooling = false }.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}
