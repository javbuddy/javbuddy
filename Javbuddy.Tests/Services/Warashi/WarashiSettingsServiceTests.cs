using Javbuddy.Models;
using Javbuddy.Services.Warashi;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.Warashi;

public class WarashiSettingsServiceTests
{
    [Fact]
    public async Task SaveAsync_InsertsFirstRow()
    {
        using var factory = new TestDbContextFactory();
        var service = new WarashiSettingsService(factory);

        await service.SaveAsync(new WarashiSettings
        {
            Enabled = true,
            BaseUrl = "https://wapdb.test",
            RequestDelayMs = 500,
            OverwriteExisting = true
        });

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.WarashiSettings.SingleAsync();
        Assert.True(row.Enabled);
        Assert.Equal("https://wapdb.test", row.BaseUrl);
        Assert.Equal(500, row.RequestDelayMs);
        Assert.True(row.OverwriteExisting);
    }

    [Fact]
    public async Task SaveAsync_UpdatesExistingRowRatherThanInsertingASecondOne()
    {
        using var factory = new TestDbContextFactory();
        await using (var seedDb = await factory.CreateDbContextAsync())
        {
            seedDb.WarashiSettings.Add(new WarashiSettings { Enabled = false, RequestDelayMs = 750 });
            await seedDb.SaveChangesAsync();
        }

        var service = new WarashiSettingsService(factory);
        await service.SaveAsync(new WarashiSettings { Enabled = true, RequestDelayMs = 250, OverwriteExisting = true });

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.WarashiSettings.SingleAsync();
        Assert.True(row.Enabled);
        Assert.Equal(250, row.RequestDelayMs);
        Assert.True(row.OverwriteExisting);
    }

    [Fact]
    public async Task SaveEnrichmentOutcomeAsync_UpdatesOnlyTheOutcomeColumns()
    {
        using var factory = new TestDbContextFactory();
        await using (var seedDb = await factory.CreateDbContextAsync())
        {
            seedDb.WarashiSettings.Add(new WarashiSettings { Enabled = true, BaseUrl = "https://kept.test", RequestDelayMs = 250, OverwriteExisting = true });
            await seedDb.SaveChangesAsync();
        }

        var enrichedAt = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
        await new WarashiSettingsService(factory).SaveEnrichmentOutcomeAsync(enrichedAt, "Enriched 1 of 1 actors (2 fields updated).");

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.WarashiSettings.SingleAsync();
        Assert.True(row.Enabled);
        Assert.Equal("https://kept.test", row.BaseUrl);
        Assert.Equal(250, row.RequestDelayMs);
        Assert.True(row.OverwriteExisting);
        Assert.Equal(enrichedAt, row.LastEnrichedAt);
        Assert.Equal("Enriched 1 of 1 actors (2 fields updated).", row.LastEnrichSummary);
    }

    [Fact]
    public async Task SaveAsync_FormLoadedBeforeABatchFinished_DoesNotRollBackTheBatchOutcome()
    {
        using var factory = new TestDbContextFactory();
        var service = new WarashiSettingsService(factory);
        var staleForm = new WarashiSettings { Enabled = true, RequestDelayMs = 750 };
        await service.SaveAsync(staleForm);

        await service.SaveEnrichmentOutcomeAsync(DateTime.UtcNow, "fresh outcome");
        staleForm.RequestDelayMs = 500;
        await service.SaveAsync(staleForm);

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.WarashiSettings.SingleAsync();
        Assert.Equal(500, row.RequestDelayMs);
        Assert.Equal("fresh outcome", row.LastEnrichSummary);
        Assert.NotNull(row.LastEnrichedAt);
    }
}
