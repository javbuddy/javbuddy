using Javbuddy.Models;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.Trickplay;

public class TrickplaySettingsServiceTests : IDisposable
{
    private readonly TestDbContextFactory dbFactory = new();

    public void Dispose() => dbFactory.Dispose();

    private TrickplaySettingsService Service(Dictionary<string, string?>? env = null) =>
        new(dbFactory, new ConfigurationBuilder().AddInMemoryCollection(env ?? []).Build());

    [Fact]
    public async Task WithNothingSaved_UsesTheDefaults()
    {
        var settings = await Service().GetEffectiveAsync();

        Assert.Equal((true, false), (settings.GenerateForNewFiles, settings.KeyframeOnly));
    }

    [Fact]
    public async Task SaveAsync_UpsertsTheSingleRow()
    {
        var service = Service();
        await service.SaveAsync(new TrickplaySettings { GenerateForNewFiles = false });
        await service.SaveAsync(new TrickplaySettings { KeyframeOnly = true });

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = Assert.Single(db.TrickplaySettings);
        Assert.Equal((true, true), (row.GenerateForNewFiles, row.KeyframeOnly));
    }

    [Fact]
    public async Task EachEnvironmentVariable_OverridesJustItsOwnField()
    {
        await Service().SaveAsync(new TrickplaySettings { GenerateForNewFiles = false, KeyframeOnly = false });
        var service = Service(new() { ["Trickplay:GenerateForNewFiles"] = "true", ["Trickplay:KeyframeOnly"] = "not a bool" });

        var effective = await service.GetEffectiveAsync();
        var stored = await service.GetStoredAsync();

        Assert.Equal((true, false), (effective.GenerateForNewFiles, effective.KeyframeOnly));
        Assert.False(stored.GenerateForNewFiles);
        Assert.True(service.IsSetByEnvironment(nameof(TrickplaySettings.GenerateForNewFiles)));
        Assert.False(service.IsSetByEnvironment(nameof(TrickplaySettings.KeyframeOnly)));
    }
}
