using Javbuddy.Models;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.Settings;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Javbuddy.Tests.Services.Monitoring;

public class SystemStatusServiceTests
{
    private readonly TestDbContextFactory factory = new();
    private readonly IJavinizerClient javinizer = Substitute.For<IJavinizerClient>();
    private readonly IProwlarrClient prowlarr = Substitute.For<IProwlarrClient>();
    private readonly IQBittorrentClient qbittorrent = Substitute.For<IQBittorrentClient>();
    private readonly IJellyfinClient jellyfin = Substitute.For<IJellyfinClient>();

    private SystemStatusService CreateService()
    {
        var pathMappings = Substitute.For<IPathMappingService>();
        pathMappings.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var connectionStatus = new ConnectionStatusService(factory, new ConfigurationBuilder().Build(), pathMappings);
        jellyfin.GetSelectedLibraryNamesAsync(Arg.Any<CancellationToken>()).Returns([]);
        return new SystemStatusService(factory, connectionStatus, javinizer, prowlarr, qbittorrent, jellyfin);
    }

    [Fact]
    public async Task GetConfiguredAsync_NothingSet_AllFalse()
    {
        var configured = await CreateService().GetConfiguredAsync();

        Assert.Equal(ConfiguredIntegrations.None, configured);
    }

    [Fact]
    public async Task GetConfiguredAsync_ReadsEachIntegrationsSavedSettings()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://j", ApiToken = "t" });
            db.QBittorrentSettings.Add(new QBittorrentSettings { BaseUrl = "http://q", Username = "u", Password = "p" });
            db.ProwlarrSettings.Add(new ProwlarrSettings { BaseUrl = "http://p" });
            await db.SaveChangesAsync();
        }

        var configured = await CreateService().GetConfiguredAsync();

        Assert.True(configured.Javinizer);
        Assert.True(configured.QBittorrent);
        Assert.False(configured.Prowlarr);
        Assert.False(configured.Jellyfin);
    }

    [Fact]
    public async Task GetHealthReportAsync_NothingConfigured_ListsEachMissingIntegration()
    {
        var report = await CreateService().GetHealthReportAsync();

        Assert.Contains(report.Messages, m => m.StartsWith("javinizer-go is not configured"));
        Assert.Contains(report.Messages, m => m.StartsWith("Prowlarr is not configured"));
        Assert.Contains(report.Messages, m => m.StartsWith("qBittorrent is not configured"));
        Assert.DoesNotContain(report.Messages, m => m.StartsWith("No Jellyfin libraries"));
        Assert.NotEmpty(report.MigrationVersion);
    }

    [Fact]
    public async Task GetHealthReportAsync_ConfiguredJellyfinWithoutLibraries_Warns()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://jf", ApiKey = "k", Enabled = true });
            await db.SaveChangesAsync();
        }

        var report = await CreateService().GetHealthReportAsync();

        Assert.Contains(report.Messages, m => m.StartsWith("No Jellyfin libraries are selected"));
    }

    [Fact]
    public async Task GetSubServiceHealthAsync_UnconfiguredIsReportedWithoutCallingTheClient()
    {
        var rows = await CreateService().GetSubServiceHealthAsync(ConfiguredIntegrations.None);

        Assert.Equal(["javinizer-go", "Prowlarr", "qBittorrent", "Jellyfin"], rows.Select(r => r.Name));
        Assert.All(rows, r =>
        {
            Assert.False(r.Configured);
            Assert.Equal("Not configured.", r.Message);
        });
        await javinizer.DidNotReceiveWithAnyArgs().TestConnectionAsync(default);
        await prowlarr.DidNotReceiveWithAnyArgs().TestConnectionAsync(default);
    }

    [Fact]
    public async Task GetSubServiceHealthAsync_ConfiguredIsPingedAndItsResultAndUrlReturned()
    {
        prowlarr.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(new ProwlarrTestResult(true, "Connected successfully", "1.2.3"));
        prowlarr.GetExternalUrlAsync(Arg.Any<CancellationToken>()).Returns("https://prowlarr.example");

        var rows = await CreateService().GetSubServiceHealthAsync(ConfiguredIntegrations.None with { Prowlarr = true });

        var row = rows.Single(r => r.Name == "Prowlarr");
        Assert.True(row.Configured);
        Assert.True(row.Success);
        Assert.Equal("Connected successfully", row.Message);
        Assert.Equal("https://prowlarr.example", row.Url);
        Assert.Equal("1.2.3", row.Version);
        await qbittorrent.DidNotReceiveWithAnyArgs().TestConnectionAsync(default);
    }
}
