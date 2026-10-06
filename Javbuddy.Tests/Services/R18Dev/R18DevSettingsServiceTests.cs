using Javbuddy.Models;
using Javbuddy.Services.R18Dev;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.R18Dev;

public class R18DevSettingsServiceTests
{
    [Fact]
    public async Task GetAsync_EmptyTable_ReturnsUnsavedDefaults()
    {
        using var factory = new TestDbContextFactory();

        var settings = await new R18DevSettingsService(factory).GetAsync();

        Assert.Equal(0, settings.Id);
        Assert.False(settings.Enabled);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.R18DevSettings.CountAsync());
    }

    [Fact]
    public async Task SaveAsync_FirstSaveInserts_AndAssignsIdToTheModel()
    {
        using var factory = new TestDbContextFactory();
        var service = new R18DevSettingsService(factory);
        var settings = new R18DevSettings { Enabled = true, DumpSourceOverride = "/dumps/x.sql" };

        await service.SaveAsync(settings);

        Assert.NotEqual(0, settings.Id);
        var loaded = await service.GetAsync();
        Assert.True(loaded.Enabled);
        Assert.Equal("/dumps/x.sql", loaded.DumpSourceOverride);
    }

    [Fact]
    public async Task SaveAsync_OverwritesEveryField_IncludingClearedImportBookkeeping()
    {
        using var factory = new TestDbContextFactory();
        var service = new R18DevSettingsService(factory);
        await service.SaveAsync(new R18DevSettings
        {
            Enabled = true,
            LastImportedAt = DateTime.UtcNow,
            LastImportSourceDate = "2026-09-01",
            LastImportSummary = "10 rows"
        });

        var edited = await service.GetAsync();
        edited.Enabled = false;
        edited.LastImportedAt = null;
        edited.LastImportSourceDate = null;
        edited.LastImportSummary = null;
        await service.SaveAsync(edited);

        var loaded = await service.GetAsync();
        Assert.False(loaded.Enabled);
        Assert.Null(loaded.LastImportedAt);
        Assert.Null(loaded.LastImportSourceDate);
        Assert.Null(loaded.LastImportSummary);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.R18DevSettings.CountAsync());
    }
}
