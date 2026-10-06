using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.R18Dev;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Javbuddy.Tests.Services.R18Dev;

/// <summary>Exercises R18DevDumpImporter.ImportAsync end to end against a small synthetic
/// pg_dump-format file (same text shape DumpParserTests builds for the parser alone), routed
/// through R18Dev:DumpSourceOverride so the test never touches the network. Focused on the
/// progress reporting added for the r18.dev import task's System &gt; Tasks live progress —
/// row counts and dump parsing itself are covered separately by DumpParserTests.</summary>
[Collection(R18DevDumpImportCollection.Name)]
public class R18DevDumpImporterTests : IDisposable
{
    private readonly string tempDir;
    private readonly TestDbContextFactory dbFactory = new();

    public R18DevDumpImporterTests()
    {
        tempDir = Directory.CreateTempSubdirectory("r18devimporttest").FullName;
    }

    public void Dispose()
    {
        dbFactory.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(tempDir, recursive: true);
    }

    private const string SampleDump = """
        COPY public.derived_video (content_id, dvd_id) FROM stdin;
        abc123	SIVR-505
        def456	SIVR-506
        \.
        COPY public.derived_actress (id, name_romaji) FROM stdin;
        1	Actress One
        \.
        """;

    private R18DevDumpImporter CreateImporter()
    {
        var dumpPath = Path.Combine(tempDir, "dump.sql");
        File.WriteAllText(dumpPath, SampleDump);

        var mainDbPath = Path.Combine(tempDir, "Test.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={mainDbPath}",
                ["R18Dev:DumpSourceOverride"] = dumpPath,
            })
            .Build();

        return new R18DevDumpImporter(Substitute.For<IHttpClientFactory>(), dbFactory, configuration);
    }

    [Fact]
    public async Task ImportAsync_ReportsDownloadThenImportStages()
    {
        var importer = CreateImporter();
        var reports = new List<TaskProgress>();
        var progress = new ImmediateProgress<TaskProgress>(reports.Add);

        var result = await importer.ImportAsync(progress);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, result.VideoCount);
        Assert.Equal(1, result.ActressCount);

        Assert.Contains(reports, r => r.Stage == "Downloading r18.dev dump");
        Assert.Contains(reports, r => r.Stage == "Importing r18.dev dump");

        var importStages = reports.Where(r => r.Stage == "Importing r18.dev dump").ToList();
        Assert.Equal(3, importStages[^1].Current); // 2 videos + 1 actress row processed
        Assert.Null(importStages[^1].Total); // total row count isn't known up front for a streamed dump
    }
}
