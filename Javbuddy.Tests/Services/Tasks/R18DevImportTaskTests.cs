using Javbuddy.Services.R18Dev;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tasks;

public class R18DevImportTaskTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private readonly IR18DevDumpImporter importer = Substitute.For<IR18DevDumpImporter>();

    public void Dispose() => factory.Dispose();

    private R18DevImportTask Task(string? enabledEnv = null) => new(importer,
        new R18DevSettingsService(factory, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["R18Dev:Enabled"] = enabledEnv }).Build()));

    [Fact]
    public async Task WhileDisabled_SkipsWithoutImporting()
    {
        var result = await Task().RunAsync(default, new Progress<TaskProgress>());

        Assert.Contains("skipped", result);
        await importer.DidNotReceiveWithAnyArgs().ImportAsync(default!, default);
    }

    [Fact]
    public async Task EnabledOnlyByEnvironment_Imports_AndRecordsTheImportWithoutStoringEnabled()
    {
        importer.ImportAsync(default!, default).ReturnsForAnyArgs(new R18DevImportResult(true, null, "2026-11-17", 5, 2));

        await Task("true").RunAsync(default, new Progress<TaskProgress>());

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.R18DevSettings.SingleAsync();
        Assert.False(row.Enabled);
        Assert.Equal("2026-11-17", row.LastImportSourceDate);
        Assert.NotNull(row.LastImportedAt);
    }
}
