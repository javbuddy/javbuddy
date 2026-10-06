using Bunit;
using Javbuddy.Components.Pages.SettingsConnectionSections;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.Settings;
using Javbuddy.Services.Torrents;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.SettingsConnectionSections;

/// <summary>One smoke test per Settings &gt; Connections section component, per the refactor
/// plan's "the six usages just need a smoke render test each" — these only assert the component
/// renders its title without throwing when no env vars/DB rows are configured (the common,
/// no-connection-yet state), not full save/test-connection behavior (already covered by
/// ConnectionSettingsSaveService/SingleRowSettingsRepository/client unit tests).</summary>
public class SectionSmokeTests : BunitContext
{
    private TestDbContextFactory CreateDbFactory()
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        return factory;
    }

    [Fact]
    public void JavinizerConnectionSection_RendersTitle()
    {
        using var dbFactory = CreateDbFactory();
        Services.AddSingleton(Substitute.For<IJavinizerClient>());
        Services.AddSingleton<IConnectionSettingsSaveService>(new ConnectionSettingsSaveService(dbFactory));
        Services.AddSingleton(Substitute.For<IConfiguration>());

        var cut = Render<JavinizerConnectionSection>();

        Assert.Contains("javinizer-go", cut.Find(".card-title").TextContent);
    }

    [Fact]
    public void ProwlarrConnectionSection_RendersTitle()
    {
        using var dbFactory = CreateDbFactory();
        Services.AddSingleton(Substitute.For<IProwlarrClient>());
        Services.AddSingleton<IConnectionSettingsSaveService>(new ConnectionSettingsSaveService(dbFactory));
        Services.AddSingleton(Substitute.For<IConfiguration>());

        var cut = Render<ProwlarrConnectionSection>();

        Assert.Contains("Prowlarr", cut.Find(".card-title").TextContent);
    }

    [Fact]
    public void JellyfinConnectionSection_RendersTitle()
    {
        using var dbFactory = CreateDbFactory();
        Services.AddSingleton(Substitute.For<IJellyfinClient>());
        var trickplaySettings = Substitute.For<ITrickplaySettingsService>();
        trickplaySettings.GetEffectiveAsync(Arg.Any<CancellationToken>()).Returns(new TrickplaySettings());
        Services.AddSingleton(trickplaySettings);
        Services.AddSingleton<IConnectionSettingsSaveService>(new ConnectionSettingsSaveService(dbFactory));
        Services.AddSingleton(Substitute.For<IConfiguration>());

        var cut = Render<JellyfinConnectionSection>();

        Assert.Contains("Jellyfin", cut.Find(".card-title").TextContent);
    }

    [Fact]
    public void QBittorrentConnectionSection_RendersTitle()
    {
        using var dbFactory = CreateDbFactory();
        Services.AddSingleton(Substitute.For<IQBittorrentClient>());
        Services.AddSingleton<IConnectionSettingsSaveService>(new ConnectionSettingsSaveService(dbFactory));
        Services.AddSingleton(Substitute.For<IConfiguration>());

        var cut = Render<QBittorrentConnectionSection>();

        Assert.Contains("qBittorrent", cut.Find(".card-title").TextContent);
    }

    [Fact]
    public void PathMappingsSection_RendersTitle()
    {
        var pathMappingService = Substitute.For<IPathMappingService>();
        pathMappingService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PathMapping>());
        Services.AddSingleton(pathMappingService);
        Services.AddSingleton(Substitute.For<IConfiguration>());

        var cut = Render<PathMappingsSection>();

        Assert.Contains("Path Mappings", cut.Find(".card-title").TextContent);
    }
}
