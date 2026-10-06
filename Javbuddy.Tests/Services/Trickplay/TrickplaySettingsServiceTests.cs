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

        Assert.Equal((true, false, true), (settings.GenerateForNewFiles, settings.KeyframeOnly, settings.JellyfinFallback));
    }

    [Fact]
    public async Task SaveAsync_UpsertsTheSingleRow()
    {
        var service = Service();
        await service.SaveAsync(new TrickplaySettings { JellyfinFallback = false });
        await service.SaveAsync(new TrickplaySettings { JellyfinFallback = false, KeyframeOnly = true });

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = Assert.Single(db.TrickplaySettings);
        Assert.Equal((true, true, false), (row.GenerateForNewFiles, row.KeyframeOnly, row.JellyfinFallback));
    }

    [Fact]
    public async Task EachEnvironmentVariable_OverridesJustItsOwnField()
    {
        await Service().SaveAsync(new TrickplaySettings { GenerateForNewFiles = false, KeyframeOnly = false, JellyfinFallback = true });
        var service = Service(new() { ["Trickplay:JellyfinFallback"] = "false", ["Trickplay:KeyframeOnly"] = "not a bool" });

        var effective = await service.GetEffectiveAsync();
        var stored = await service.GetStoredAsync();

        Assert.Equal((false, false, false), (effective.GenerateForNewFiles, effective.KeyframeOnly, effective.JellyfinFallback));
        Assert.True(stored.JellyfinFallback);
        Assert.True(service.IsSetByEnvironment(nameof(TrickplaySettings.JellyfinFallback)));
        Assert.False(service.IsSetByEnvironment(nameof(TrickplaySettings.KeyframeOnly)));
    }
}
