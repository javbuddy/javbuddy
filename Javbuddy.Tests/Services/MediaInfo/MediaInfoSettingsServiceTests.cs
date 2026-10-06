using Javbuddy.Models;
using Javbuddy.Services.MediaInfo;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.MediaInfo;

public class MediaInfoSettingsServiceTests
{
    [Fact]
    public async Task GetAsync_EmptyTable_ReturnsEnabledDefault()
    {
        using var factory = new TestDbContextFactory();

        var settings = await new MediaInfoSettingsService(factory).GetAsync();

        Assert.True(settings.Enabled);
    }

    [Fact]
    public async Task SaveAsync_UpsertsTheSingleRow()
    {
        using var factory = new TestDbContextFactory();
        var service = new MediaInfoSettingsService(factory);

        await service.SaveAsync(new MediaInfoSettings { Enabled = false });
        var edited = await service.GetAsync();
        Assert.False(edited.Enabled);
        edited.Enabled = true;
        await service.SaveAsync(edited);

        Assert.True((await service.GetAsync()).Enabled);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.MediaInfoSettings.CountAsync());
    }
}
