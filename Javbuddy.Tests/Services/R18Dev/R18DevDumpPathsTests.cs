using Javbuddy.Services.R18Dev;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.R18Dev;

public class R18DevDumpPathsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public void Sqlite_DefaultsToASiblingOfTheMainDatabase()
    {
        var dir = Path.Combine(Path.GetTempPath(), "r18-paths");
        var path = R18DevDumpPaths.GetDbPath(Config(("ConnectionStrings:Default", $"Data Source={Path.Combine(dir, "Main.db")}")));

        Assert.Equal(Path.Combine(dir, "Main.r18dev.db"), path);
    }

    [Fact]
    public void ConfiguredPath_WinsOnSqlite()
    {
        var configured = Path.Combine(Path.GetTempPath(), "elsewhere", "dump.db");
        var path = R18DevDumpPaths.GetDbPath(Config(
            ("ConnectionStrings:Default", "Data Source=/data/Main.db"), ("R18Dev:DbPath", configured)));

        Assert.Equal(configured, path);
    }

    [Fact]
    public void Postgres_WithoutAConfiguredPath_UsesTheWorkingDirectory()
    {
        var path = R18DevDumpPaths.GetDbPath(Config(
            ("Database:Provider", "Postgres"), ("ConnectionStrings:Default", "Host=db;Database=javbuddy")));

        Assert.Equal(Path.GetFullPath("Javbuddy.r18dev.db"), path);
    }

    [Fact]
    public void Postgres_ConfiguredPath_IsUsed()
    {
        var configured = Path.Combine(Path.GetTempPath(), "data", "Javbuddy.r18dev.db");
        var path = R18DevDumpPaths.GetDbPath(Config(
            ("Database:Provider", "Postgres"), ("ConnectionStrings:Default", "Host=db"), ("R18Dev:DbPath", configured)));

        Assert.Equal(configured, path);
    }
}
