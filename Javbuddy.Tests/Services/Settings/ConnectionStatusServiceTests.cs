using Javbuddy.Models;
using Javbuddy.Services.Settings;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Javbuddy.Tests.Services.Settings;

public class ConnectionStatusServiceTests
{
    private static (TestDbContextFactory Db, IConfiguration Config, IPathMappingService PathMappings) SetUp(
        Dictionary<string, string?>? envOverrides = null,
        List<PathMapping>? pathMappings = null)
    {
        var db = new TestDbContextFactory();
        var config = new ConfigurationBuilder().AddInMemoryCollection(envOverrides ?? []).Build();
        var pathMappingService = Substitute.For<IPathMappingService>();
        pathMappingService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(pathMappings ?? []);
        return (db, config, pathMappingService);
    }

    [Fact]
    public async Task NothingConfigured_AllStatusesNotConfigured()
    {
        var (db, config, pathMappings) = SetUp();
        using (db)
        {
            var service = new ConnectionStatusService(db, config, pathMappings);

            var statuses = await service.GetStatusesAsync();

            Assert.Equal(5, statuses.Count);
            Assert.All(statuses, s => Assert.False(s.Configured));
            Assert.All(statuses, s => Assert.False(s.EnvConfigured));
        }
    }

    [Fact]
    public async Task GetStatusesAsync_ExcludesLocalLibrary()
    {
        var (db, config, pathMappings) = SetUp();
        using (db)
        {
            var service = new ConnectionStatusService(db, config, pathMappings);

            var statuses = await service.GetStatusesAsync();

            Assert.DoesNotContain(statuses, s => s.Key == "locallibrary");
        }
    }

    [Fact]
    public async Task LocalLibraryNothingConfigured_IsNotConfigured()
    {
        var (db, config, pathMappings) = SetUp();
        using (db)
        {
            var service = new ConnectionStatusService(db, config, pathMappings);

            var localLibrary = await service.GetLocalLibraryStatusAsync();

            Assert.False(localLibrary.Configured);
            Assert.False(localLibrary.EnvConfigured);
            Assert.Equal("Not configured", localLibrary.Summary);
        }
    }

    [Fact]
    public async Task JavinizerDbRowWithBaseUrlAndToken_IsConfigured()
    {
        var (db, config, pathMappings) = SetUp();
        using (db)
        {
            await using (var ctx = await db.CreateDbContextAsync())
            {
                ctx.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://localhost:8765", ApiToken = "tok" });
                await ctx.SaveChangesAsync();
            }

            var service = new ConnectionStatusService(db, config, pathMappings);
            var statuses = await service.GetStatusesAsync();

            var javinizer = statuses.Single(s => s.Key == "javinizer");
            Assert.True(javinizer.Configured);
            Assert.False(javinizer.EnvConfigured);
            Assert.Equal("http://localhost:8765", javinizer.Summary);
        }
    }

    [Fact]
    public async Task ProwlarrEnvConfigured_TakesPrecedenceAndMarksEnvConfigured()
    {
        var (db, config, pathMappings) = SetUp(new Dictionary<string, string?>
        {
            ["Prowlarr:BaseUrl"] = "http://localhost:9696",
            ["Prowlarr:ApiKey"] = "envkey"
        });
        using (db)
        {
            var service = new ConnectionStatusService(db, config, pathMappings);
            var statuses = await service.GetStatusesAsync();

            var prowlarr = statuses.Single(s => s.Key == "prowlarr");
            Assert.True(prowlarr.Configured);
            Assert.True(prowlarr.EnvConfigured);
        }
    }

    [Fact]
    public async Task JellyfinConfiguredWithNoLibrariesSelected_ShowsNoLibrariesSummary()
    {
        var (db, config, pathMappings) = SetUp();
        using (db)
        {
            await using (var ctx = await db.CreateDbContextAsync())
            {
                ctx.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost:8096", ApiKey = "key" });
                await ctx.SaveChangesAsync();
            }

            var service = new ConnectionStatusService(db, config, pathMappings);
            var statuses = await service.GetStatusesAsync();

            var jellyfin = statuses.Single(s => s.Key == "jellyfin");
            Assert.True(jellyfin.Configured);
            Assert.Equal("No libraries selected", jellyfin.Summary);
        }
    }

    [Fact]
    public async Task JellyfinConfiguredWithSelectedLibraries_ShowsCount()
    {
        var (db, config, pathMappings) = SetUp();
        using (db)
        {
            await using (var ctx = await db.CreateDbContextAsync())
            {
                ctx.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://localhost:8096", ApiKey = "key", SelectedLibraryNames = "JAV,West" });
                await ctx.SaveChangesAsync();
            }

            var service = new ConnectionStatusService(db, config, pathMappings);
            var statuses = await service.GetStatusesAsync();

            var jellyfin = statuses.Single(s => s.Key == "jellyfin");
            Assert.Equal("2 libraries selected", jellyfin.Summary);
        }
    }

    [Fact]
    public async Task LocalLibraryWithRootPaths_IsConfigured()
    {
        var (db, config, pathMappings) = SetUp();
        using (db)
        {
            await using (var ctx = await db.CreateDbContextAsync())
            {
                ctx.LocalLibrarySettings.Add(new LocalLibrarySettings { RootPaths = "D:\\media\\jav\nD:\\media\\vr" });
                await ctx.SaveChangesAsync();
            }

            var service = new ConnectionStatusService(db, config, pathMappings);

            var localLibrary = await service.GetLocalLibraryStatusAsync();
            Assert.True(localLibrary.Configured);
            Assert.Equal("2 root paths configured", localLibrary.Summary);
        }
    }

    [Fact]
    public async Task QBittorrentPartiallyConfigured_IsNotConfigured()
    {
        var (db, config, pathMappings) = SetUp();
        using (db)
        {
            await using (var ctx = await db.CreateDbContextAsync())
            {
                ctx.QBittorrentSettings.Add(new QBittorrentSettings { BaseUrl = "http://localhost:8080" });
                await ctx.SaveChangesAsync();
            }

            var service = new ConnectionStatusService(db, config, pathMappings);
            var statuses = await service.GetStatusesAsync();

            var qbittorrent = statuses.Single(s => s.Key == "qbittorrent");
            Assert.False(qbittorrent.Configured);
        }
    }

    [Fact]
    public async Task PathMappingsWithRules_ShowsRuleCount()
    {
        var (db, config, pathMappings) = SetUp(pathMappings:
        [
            new PathMapping { QBittorrentPrefix = "/downloads/a", JavinizerPrefix = "/scratch/a" },
            new PathMapping { QBittorrentPrefix = "/downloads/b", JavinizerPrefix = "/scratch/b" }
        ]);
        using (db)
        {
            var service = new ConnectionStatusService(db, config, pathMappings);
            var statuses = await service.GetStatusesAsync();

            var mappings = statuses.Single(s => s.Key == "pathmappings");
            Assert.True(mappings.Configured);
            Assert.Equal("2 rules", mappings.Summary);
        }
    }

    [Fact]
    public async Task JellyfinDisabledInDatabase_IsNotConfiguredAndSummaryIsDisabled()
    {
        var (db, config, pathMappings) = SetUp();
        using (db)
        {
            await using (var ctx = await db.CreateDbContextAsync())
            {
                ctx.JellyfinSettings.Add(new JellyfinSettings
                {
                    Enabled = false,
                    BaseUrl = "http://localhost:8096",
                    ApiKey = "key",
                    SelectedLibraryNames = "JAV"
                });
                await ctx.SaveChangesAsync();
            }

            var service = new ConnectionStatusService(db, config, pathMappings);
            var statuses = await service.GetStatusesAsync();

            var jellyfin = statuses.Single(s => s.Key == "jellyfin");
            Assert.False(jellyfin.Configured);
            Assert.Equal("Disabled", jellyfin.Summary);
        }
    }

    [Fact]
    public async Task JellyfinDisabledViaEnv_OverridesDbAndSummaryIsDisabled()
    {
        var (db, config, pathMappings) = SetUp(new Dictionary<string, string?>
        {
            ["Jellyfin:Enabled"] = "false"
        });
        using (db)
        {
            await using (var ctx = await db.CreateDbContextAsync())
            {
                ctx.JellyfinSettings.Add(new JellyfinSettings
                {
                    Enabled = true,
                    BaseUrl = "http://localhost:8096",
                    ApiKey = "key",
                    SelectedLibraryNames = "JAV"
                });
                await ctx.SaveChangesAsync();
            }

            var service = new ConnectionStatusService(db, config, pathMappings);
            var statuses = await service.GetStatusesAsync();

            var jellyfin = statuses.Single(s => s.Key == "jellyfin");
            Assert.False(jellyfin.Configured);
            Assert.Equal("Disabled", jellyfin.Summary);
        }
    }
}
