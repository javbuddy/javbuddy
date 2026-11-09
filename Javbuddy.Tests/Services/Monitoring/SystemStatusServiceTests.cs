using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
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
    private readonly IChallengeSolverClient challengeSolver = Substitute.For<IChallengeSolverClient>();

    private SystemStatusService CreateService()
    {
        var pathMappings = Substitute.For<IPathMappingService>();
        pathMappings.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var connectionStatus = new ConnectionStatusService(factory, new ConfigurationBuilder().Build(), pathMappings);
        return new SystemStatusService(factory, connectionStatus, javinizer, prowlarr, qbittorrent, jellyfin, challengeSolver);
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
        Assert.NotEmpty(report.MigrationVersion);
    }

    [Fact]
    public async Task GetHealthReportAsync_ProwlarrWithDisabledIndexers_WarnsPerIndexer()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ProwlarrSettings.Add(new ProwlarrSettings { BaseUrl = "http://p", ApiKey = "k" });
            await db.SaveChangesAsync();
        }
        prowlarr.GetDisabledIndexersAsync(Arg.Any<CancellationToken>()).Returns([
            new ProwlarrDisabledIndexer(1, "Beta"),
            new ProwlarrDisabledIndexer(2, "Gamma", DateTime.UtcNow.AddHours(1))]);

        var report = await CreateService().GetHealthReportAsync();

        Assert.Contains(report.Messages, m => m.Contains("Beta") && m.Contains("disabled in Prowlarr"));
        Assert.Contains(report.Messages, m => m.Contains("Gamma") && m.Contains("repeated failures"));
    }

    [Fact]
    public async Task GetHealthReportAsync_ProwlarrNotConfigured_DoesNotQueryIndexers()
    {
        await CreateService().GetHealthReportAsync();

        await prowlarr.DidNotReceive().GetDisabledIndexersAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubServiceHealthAsync_UnconfiguredIsReportedWithoutCallingTheClient()
    {
        var rows = await CreateService().GetSubServiceHealthAsync(ConfiguredIntegrations.None);

        Assert.Equal(["javinizer-go", "Prowlarr", "qBittorrent", "Jellyfin", "Challenge solver"], rows.Select(r => r.Name));
        Assert.All(rows, r =>
        {
            Assert.False(r.Configured);
            Assert.Equal("Not configured.", r.Message);
        });
        await javinizer.DidNotReceiveWithAnyArgs().TestConnectionAsync(default);
        await prowlarr.DidNotReceiveWithAnyArgs().TestConnectionAsync(default);
        await challengeSolver.DidNotReceiveWithAnyArgs().TestConnectionAsync(default);
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

    [Fact]
    public async Task GetSubServiceHealthAsync_ConfiguredChallengeSolverIsPingedAndItsResultAndUrlReturned()
    {
        challengeSolver.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(new SolverTestResult(true, "Connected to the challenge solver.", "3.4.5"));
        challengeSolver.GetExternalUrlAsync(Arg.Any<CancellationToken>()).Returns("http://solver:8191");

        var rows = await CreateService().GetSubServiceHealthAsync(ConfiguredIntegrations.None with { ChallengeSolver = true });

        var row = rows.Single(r => r.Name == "Challenge solver");
        Assert.True(row.Configured);
        Assert.True(row.Success);
        Assert.Equal("http://solver:8191", row.Url);
        Assert.Equal("3.4.5", row.Version);
        await prowlarr.DidNotReceiveWithAnyArgs().TestConnectionAsync(default);
    }

    [Fact]
    public async Task GetConfiguredAsync_ReadsTheSavedChallengeSolverAddress()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.FlareSolverrSettings.Add(new FlareSolverrSettings { BaseUrl = "http://solver:8191" });
            await db.SaveChangesAsync();
        }

        var configured = await CreateService().GetConfiguredAsync();

        Assert.True(configured.ChallengeSolver);
        Assert.False(configured.Javinizer);
    }

    [Fact]
    public async Task GetHealthReportAsync_NoChallengeSolver_DoesNotWarn()
    {
        var report = await CreateService().GetHealthReportAsync();

        Assert.DoesNotContain(report.Messages, m => m.StartsWith("The challenge solver", StringComparison.OrdinalIgnoreCase) || m.StartsWith("Challenge solver", StringComparison.OrdinalIgnoreCase));
    }
}
