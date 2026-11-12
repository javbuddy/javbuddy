using Javbuddy.Models;
using Javbuddy.Services.R18Dev;
using Javbuddy.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.R18Dev;

/// <summary>Deleting the r18.dev dump and the settings bookkeeping that goes with it, against a real
/// temp-dir sidecar file and the real settings table.</summary>
[Collection(SqlitePoolCollection.Name)]
public class R18DevDumpServiceTests : IDisposable
{
    private readonly string tempDir = Directory.CreateTempSubdirectory("r18devdumpservice").FullName;
    private readonly TestDbContextFactory factory = new();
    private readonly R18DevDumpStore store;
    private readonly R18DevSettingsService settingsService;
    private readonly R18DevDumpService service;
    private readonly string dumpPath;

    public R18DevDumpServiceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={Path.Combine(tempDir, "Test.db")}"
            })
            .Build();
        dumpPath = R18DevDumpPaths.GetDbPath(configuration);
        store = new R18DevDumpStore(configuration);
        settingsService = new R18DevSettingsService(factory);
        service = new R18DevDumpService(store, settingsService);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        factory.Dispose();
        Directory.Delete(tempDir, recursive: true);
    }

    private void CreateDump() => File.WriteAllText(dumpPath, "not a real dump");

    private static R18DevSettings ImportedSettings(bool enabled) => new()
    {
        Enabled = enabled,
        DumpSourceOverride = "/dumps/x.sql",
        LastImportedAt = DateTime.UtcNow,
        LastImportSourceDate = "2026-09-01",
        LastImportSummary = "10 rows",
    };

    [Fact]
    public void IsDumpAvailable_FollowsTheFileOnDisk()
    {
        Assert.False(service.IsDumpAvailable());

        CreateDump();

        Assert.True(service.IsDumpAvailable());
    }

    [Fact]
    public async Task DeleteDumpAsync_RemovesTheFile_ClearsAndSavesTheBookkeeping_AndKeepsTheRest()
    {
        CreateDump();
        var settings = ImportedSettings(enabled: true);

        await service.DeleteDumpAsync(settings);

        Assert.False(File.Exists(dumpPath));
        var loaded = await settingsService.GetAsync();
        Assert.True(loaded.Enabled);
        Assert.Equal("/dumps/x.sql", loaded.DumpSourceOverride);
        Assert.Null(loaded.LastImportedAt);
        Assert.Null(loaded.LastImportSourceDate);
        Assert.Null(loaded.LastImportSummary);
    }

    [Fact]
    public async Task SaveAsync_DisablingAnEnabledSource_DeletesTheDumpAndClearsTheBookkeeping()
    {
        CreateDump();
        var settings = ImportedSettings(enabled: false);

        await service.SaveAsync(settings, wasEnabled: true);

        Assert.False(File.Exists(dumpPath));
        var loaded = await settingsService.GetAsync();
        Assert.False(loaded.Enabled);
        Assert.Null(loaded.LastImportedAt);
        Assert.Null(loaded.LastImportSummary);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task SaveAsync_WithoutDisablingAnEnabledSource_KeepsTheDumpAndTheBookkeeping(bool wasEnabled, bool enabled)
    {
        CreateDump();
        var settings = ImportedSettings(enabled);

        await service.SaveAsync(settings, wasEnabled);

        Assert.True(File.Exists(dumpPath));
        var loaded = await settingsService.GetAsync();
        Assert.Equal(enabled, loaded.Enabled);
        Assert.Equal("10 rows", loaded.LastImportSummary);
    }
}
