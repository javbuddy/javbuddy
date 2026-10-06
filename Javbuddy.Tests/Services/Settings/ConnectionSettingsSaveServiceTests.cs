using Javbuddy.Models;
using Javbuddy.Services.Settings;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.Settings;

public class ConnectionSettingsServiceTests
{
    [Fact]
    public async Task SaveJavinizerAsync_PersistsBaseUrlExternalUrlAndApiToken()
    {
        using var factory = new TestDbContextFactory();
        var service = new ConnectionSettingsSaveService(factory);

        await service.SaveJavinizerAsync(new JavinizerSettings { BaseUrl = "http://jv.test", ExternalUrl = "http://jv-ext.test", ApiToken = "tok" });

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.JavinizerSettings.SingleAsync();
        Assert.Equal("http://jv.test", row.BaseUrl);
        Assert.Equal("http://jv-ext.test", row.ExternalUrl);
        Assert.Equal("tok", row.ApiToken);
    }

    [Fact]
    public async Task SaveProwlarrAsync_PersistsBaseUrlExternalUrlAndApiKey()
    {
        using var factory = new TestDbContextFactory();
        var service = new ConnectionSettingsSaveService(factory);

        await service.SaveProwlarrAsync(new ProwlarrSettings { BaseUrl = "http://pw.test", ExternalUrl = "http://pw-ext.test", ApiKey = "key" });

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.ProwlarrSettings.SingleAsync();
        Assert.Equal("http://pw.test", row.BaseUrl);
        Assert.Equal("http://pw-ext.test", row.ExternalUrl);
        Assert.Equal("key", row.ApiKey);
    }

    [Fact]
    public async Task SaveJellyfinConnectionAsync_PersistsBaseUrlExternalUrlAndApiKey_LeavesLibrarySelectionAlone()
    {
        using var factory = new TestDbContextFactory();
        await using (var seedDb = await factory.CreateDbContextAsync())
        {
            seedDb.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://old.test", SelectedLibraryNames = "lib-1,lib-2" });
            await seedDb.SaveChangesAsync();
        }

        var service = new ConnectionSettingsSaveService(factory);
        await service.SaveJellyfinConnectionAsync(new JellyfinSettings { BaseUrl = "http://jf.test", ExternalUrl = "http://jf-ext.test", ApiKey = "key" });

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.JellyfinSettings.SingleAsync();
        Assert.Equal("http://jf.test", row.BaseUrl);
        Assert.Equal("http://jf-ext.test", row.ExternalUrl);
        Assert.Equal("key", row.ApiKey);
        Assert.Equal("lib-1,lib-2", row.SelectedLibraryNames);
    }

    [Fact]
    public async Task SaveLocalLibraryAsync_PersistsRootPaths()
    {
        using var factory = new TestDbContextFactory();
        var service = new ConnectionSettingsSaveService(factory);

        await service.SaveLocalLibraryAsync(new LocalLibrarySettings { RootPaths = @"D:\media\jav" });

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.LocalLibrarySettings.SingleAsync();
        Assert.Equal(@"D:\media\jav", row.RootPaths);
    }

    [Fact]
    public async Task SaveQBittorrentAsync_PersistsAllConnectionFields()
    {
        using var factory = new TestDbContextFactory();
        var service = new ConnectionSettingsSaveService(factory);

        await service.SaveQBittorrentAsync(new QBittorrentSettings
        {
            BaseUrl = "http://qb.test",
            ExternalUrl = "http://qb-ext.test",
            Username = "user",
            Password = "pass",
            Category = "javbuddy"
        });

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.QBittorrentSettings.SingleAsync();
        Assert.Equal("http://qb.test", row.BaseUrl);
        Assert.Equal("http://qb-ext.test", row.ExternalUrl);
        Assert.Equal("user", row.Username);
        Assert.Equal("pass", row.Password);
        Assert.Equal("javbuddy", row.Category);
    }

    [Fact]
    public async Task GetAsync_EmptyTables_ReturnUnsavedDefaults()
    {
        using var factory = new TestDbContextFactory();
        var service = new ConnectionSettingsSaveService(factory);

        Assert.Equal(0, (await service.GetJavinizerAsync()).Id);
        Assert.Equal(0, (await service.GetProwlarrAsync()).Id);
        Assert.Equal(0, (await service.GetJellyfinAsync()).Id);
        Assert.Equal(0, (await service.GetLocalLibraryAsync()).Id);
        Assert.Equal(0, (await service.GetQBittorrentAsync()).Id);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.JellyfinSettings.CountAsync());
    }

    [Fact]
    public async Task GetQBittorrentAsync_ReturnsTheSavedRow()
    {
        using var factory = new TestDbContextFactory();
        var service = new ConnectionSettingsSaveService(factory);
        await service.SaveQBittorrentAsync(new QBittorrentSettings { BaseUrl = "http://qb.test", Username = "u", Password = "p", Category = "javbuddy" });

        var row = await service.GetQBittorrentAsync();

        Assert.Equal("http://qb.test", row.BaseUrl);
        Assert.Equal("javbuddy", row.Category);
    }

    [Fact]
    public async Task SaveJellyfinLibrarySelectionAsync_CreatesTheRowWhenNoneExists()
    {
        using var factory = new TestDbContextFactory();
        var service = new ConnectionSettingsSaveService(factory);

        await service.SaveJellyfinLibrarySelectionAsync(["JAV", "VR"]);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal("JAV,VR", (await db.JellyfinSettings.SingleAsync()).SelectedLibraryNames);
    }

    [Fact]
    public async Task SaveJellyfinLibrarySelectionAsync_LeavesConnectionFieldsUntouched()
    {
        using var factory = new TestDbContextFactory();
        var service = new ConnectionSettingsSaveService(factory);
        await service.SaveJellyfinConnectionAsync(new JellyfinSettings { Enabled = true, BaseUrl = "http://jf.test", ApiKey = "key" });

        await service.SaveJellyfinLibrarySelectionAsync(["Movies"]);

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.JellyfinSettings.SingleAsync();
        Assert.Equal("Movies", row.SelectedLibraryNames);
        Assert.Equal("http://jf.test", row.BaseUrl);
        Assert.Equal("key", row.ApiKey);
    }
}
