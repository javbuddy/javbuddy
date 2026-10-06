using Javbuddy.Models;
using Javbuddy.Services.DeoVr;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.DeoVr;

public class DeoVrSettingsServiceTests : IDisposable
{
    private readonly TestDbContextFactory dbFactory = new();

    public void Dispose() => dbFactory.Dispose();

    private DeoVrSettingsService Service(Dictionary<string, string?>? env = null) =>
        new(dbFactory, new ConfigurationBuilder().AddInMemoryCollection(env ?? []).Build());

    [Fact]
    public async Task WithNothingSaved_IsOff() =>
        Assert.False((await Service().GetEffectiveAsync()).Enabled);

    [Fact]
    public async Task SaveAsync_StoresOneRow()
    {
        var service = Service();
        await service.SaveAsync(new DeoVrSettings { Enabled = true });
        await service.SaveAsync(new DeoVrSettings { Enabled = false });

        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.False(Assert.Single(db.DeoVrSettings).Enabled);
    }

    [Fact]
    public async Task TheEnabledEnvironmentVariable_OverridesTheSavedValue()
    {
        await Service().SaveAsync(new DeoVrSettings { Enabled = false });
        var service = Service(new() { ["DeoVr:Enabled"] = "true" });

        Assert.True((await service.GetEffectiveAsync()).Enabled);
        Assert.False((await service.GetStoredAsync()).Enabled);
        Assert.True(service.IsSetByEnvironment(nameof(DeoVrSettings.Enabled)));
    }

    [Fact]
    public void AnEnabledValueThatIsNotABool_DoesNotCountAsSet() =>
        Assert.False(Service(new() { ["DeoVr:Enabled"] = "yes" }).IsSetByEnvironment(nameof(DeoVrSettings.Enabled)));
}
