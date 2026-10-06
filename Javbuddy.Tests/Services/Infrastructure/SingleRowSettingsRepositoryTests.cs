using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.Infrastructure;

public class SettingsRepositoryTests
{
    private static SingleRowSettingsRepository<JavinizerSettings> CreateRepository(TestDbContextFactory factory) =>
        new(
            factory,
            db => db.JavinizerSettings,
            x => x.Id,
            (existing, form) =>
            {
                existing.BaseUrl = form.BaseUrl;
                existing.ExternalUrl = form.ExternalUrl;
                existing.ApiToken = form.ApiToken;
            });

    [Fact]
    public async Task SaveAsync_NoExistingRow_InsertsFormModel()
    {
        using var factory = new TestDbContextFactory();
        var repository = CreateRepository(factory);

        await repository.SaveAsync(new JavinizerSettings { BaseUrl = "http://a.test", ApiToken = "tok" });

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.JavinizerSettings.SingleAsync();
        Assert.Equal("http://a.test", row.BaseUrl);
        Assert.Equal("tok", row.ApiToken);
    }

    [Fact]
    public async Task SaveAsync_ExistingRow_CopiesFieldsOntoIt_PreservingId()
    {
        using var factory = new TestDbContextFactory();
        int existingId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = new JavinizerSettings { BaseUrl = "http://old.test", ApiToken = "old-tok" };
            db.JavinizerSettings.Add(row);
            await db.SaveChangesAsync();
            existingId = row.Id;
        }

        var repository = CreateRepository(factory);
        await repository.SaveAsync(new JavinizerSettings { BaseUrl = "http://new.test", ExternalUrl = "http://ext.test", ApiToken = "new-tok" });

        await using var readDb = await factory.CreateDbContextAsync();
        var row2 = await readDb.JavinizerSettings.SingleAsync();
        Assert.Equal(existingId, row2.Id);
        Assert.Equal("http://new.test", row2.BaseUrl);
        Assert.Equal("http://ext.test", row2.ExternalUrl);
        Assert.Equal("new-tok", row2.ApiToken);
    }
}
