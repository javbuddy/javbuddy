using Microsoft.Data.Sqlite;

namespace Javbuddy.Services.R18Dev;

/// <summary>Resolves the file path for the r18.dev dump's local SQLite side-database — never referenced by the main
/// AppDbContext, and always SQLite whichever provider the main database uses. <c>R18Dev:DbPath</c> wins when set.
/// Otherwise on SQLite it is a sibling of the main DB (see ConnectionStrings:Default); on Postgres, which has no
/// database file to sit beside, it is <c>Javbuddy.r18dev.db</c> in the working directory (the Docker image sets
/// R18Dev__DbPath to keep it on the data volume). Purely a regenerable cache: deleting it and re-importing is always
/// safe, same as the main DB per CLAUDE.md's "recreate from scratch" guidance.</summary>
public static class R18DevDumpPaths
{
    private const string DefaultFileName = "Javbuddy.r18dev.db";

    public static string GetDbPath(IConfiguration configuration)
    {
        var configuredPath = configuration["R18Dev:DbPath"];
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        if (!IsSqlite(configuration))
        {
            return Path.GetFullPath(DefaultFileName);
        }

        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=Javbuddy.db";
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        var fullDataSourcePath = Path.GetFullPath(dataSource);
        var directory = Path.GetDirectoryName(fullDataSourcePath) ?? ".";
        var fileName = Path.GetFileNameWithoutExtension(fullDataSourcePath) + ".r18dev.db";
        return Path.Combine(directory, fileName);
    }

    /// <summary>The main database's provider (Sqlite unless Database:Provider says otherwise).</summary>
    public static bool IsSqlite(IConfiguration configuration) =>
        string.Equals(configuration.GetValue("Database:Provider", "Sqlite"), "Sqlite", StringComparison.OrdinalIgnoreCase);
}
