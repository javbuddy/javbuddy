using Javbuddy.Models;
using Javbuddy.Services.R18Dev;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.R18Dev;

public class R18DevSettingsServiceTests
{
    [Fact]
    public async Task GetAsync_EmptyTable_ReturnsUnsavedDefaults()
    {
        using var factory = new TestDbContextFactory();

        var settings = await new R18DevSettingsService(factory, new ConfigurationBuilder().Build()).GetStoredAsync();

        Assert.Equal(0, settings.Id);
        Assert.False(settings.Enabled);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.R18DevSettings.CountAsync());
    }

    [Fact]
    public async Task SaveAsync_FirstSaveInserts_AndAssignsIdToTheModel()
    {
        using var factory = new TestDbContextFactory();
        var service = new R18DevSettingsService(factory, new ConfigurationBuilder().Build());
        var settings = new R18DevSettings { Enabled = true, DumpSourceOverride = "/dumps/x.sql" };

        await service.SaveAsync(settings);

        Assert.NotEqual(0, settings.Id);
        var loaded = await service.GetStoredAsync();
        Assert.True(loaded.Enabled);
        Assert.Equal("/dumps/x.sql", loaded.DumpSourceOverride);
    }

    [Fact]
    public async Task SaveAsync_OverwritesEveryField_IncludingClearedImportBookkeeping()
    {
        using var factory = new TestDbContextFactory();
        var service = new R18DevSettingsService(factory, new ConfigurationBuilder().Build());
        await service.SaveAsync(new R18DevSettings
        {
            Enabled = true,
            LastImportedAt = DateTime.UtcNow,
            LastImportSourceDate = "2026-09-01",
            LastImportSummary = "10 rows"
        });

        var edited = await service.GetStoredAsync();
        edited.Enabled = false;
        edited.LastImportedAt = null;
        edited.LastImportSourceDate = null;
        edited.LastImportSummary = null;
        await service.SaveAsync(edited);

        var loaded = await service.GetStoredAsync();
        Assert.False(loaded.Enabled);
        Assert.Null(loaded.LastImportedAt);
        Assert.Null(loaded.LastImportSourceDate);
        Assert.Null(loaded.LastImportSummary);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.R18DevSettings.CountAsync());
    }

    private static R18DevSettingsService Service(TestDbContextFactory factory, string? enabledEnv = null) =>
        new(factory, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["R18Dev:Enabled"] = enabledEnv }).Build());

    [Fact]
    public async Task TheEnabledEnvironmentVariable_OverridesTheSavedValue_WithoutChangingTheStoredRow()
    {
        using var factory = new TestDbContextFactory();
        await Service(factory).SaveAsync(new R18DevSettings { Enabled = false });
        var service = Service(factory, "true");

        Assert.True((await service.GetEffectiveAsync()).Enabled);
        Assert.False((await service.GetStoredAsync()).Enabled);
        Assert.True(service.IsEnabledSetByEnvironment);
    }

    [Fact]
    public async Task TheEnabledEnvironmentVariable_CanSwitchTheSourceOff()
    {
        using var factory = new TestDbContextFactory();
        await Service(factory).SaveAsync(new R18DevSettings { Enabled = true });

        Assert.False((await Service(factory, "false").GetEffectiveAsync()).Enabled);
    }

    [Fact]
    public async Task WithoutTheEnvironmentVariable_EffectiveIsTheSavedValue()
    {
        using var factory = new TestDbContextFactory();
        var service = Service(factory);
        await service.SaveAsync(new R18DevSettings { Enabled = true });

        Assert.True((await service.GetEffectiveAsync()).Enabled);
        Assert.False(service.IsEnabledSetByEnvironment);
    }

    [Fact]
    public void AnEnabledValueThatIsNotABool_DoesNotCountAsSet() =>
        Assert.False(Service(new TestDbContextFactory(), "yes").IsEnabledSetByEnvironment);
}
