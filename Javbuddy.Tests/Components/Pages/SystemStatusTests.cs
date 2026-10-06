using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.Settings;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

/// <summary>Covers the System &gt; Status page's "Sub-services" health section: an unconfigured
/// integration is reported as such without ever calling out to it, and a configured one is
/// actually pinged via TestConnectionAsync and its result rendered.</summary>
public class SystemStatusTests : BunitContext
{
    private (TestDbContextFactory Factory, IJavinizerClient Javinizer, IProwlarrClient Prowlarr, IQBittorrentClient QBittorrent, IJellyfinClient Jellyfin) SetUpServices(
        string? connectionString = null, string contentRootPath = "", ImageCacheSettings? imageCacheSettings = null)
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);

        var javinizer = Substitute.For<IJavinizerClient>();
        var prowlarr = Substitute.For<IProwlarrClient>();
        var qbittorrent = Substitute.For<IQBittorrentClient>();
        var jellyfin = Substitute.For<IJellyfinClient>();
        jellyfin.GetSelectedLibraryNamesAsync(Arg.Any<CancellationToken>()).Returns([]);

        var imageCacheMaintenance = Substitute.For<IImageCacheMaintenanceService>();
        imageCacheMaintenance.GetStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new ImageCacheStats(0, 0, 0, 0, 0, 0));

        var diskSpace = Substitute.For<IDiskSpaceService>();
        diskSpace.GetDiskSpaceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var pathMappings = Substitute.For<IPathMappingService>();
        pathMappings.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        Services.AddSingleton(pathMappings);
        Services.AddScoped<IConnectionStatusService, ConnectionStatusService>();
        Services.AddScoped<ISystemStatusService, SystemStatusService>();

        Services.AddSingleton(javinizer);
        Services.AddSingleton(prowlarr);
        Services.AddSingleton(qbittorrent);
        Services.AddSingleton(jellyfin);
        Services.AddSingleton(imageCacheMaintenance);
        Services.AddSingleton(diskSpace);

        var imageCache = Substitute.For<ILocalImageCache>();
        imageCache.Settings.Returns(imageCacheSettings ?? ImageCacheSettings.Default);
        Services.AddSingleton(imageCache);

        var configBuilder = new ConfigurationBuilder();
        if (connectionString is not null)
        {
            configBuilder.AddInMemoryCollection([new("ConnectionStrings:Default", connectionString)]);
        }
        Services.AddSingleton<IConfiguration>(configBuilder.Build());

        var env = Substitute.For<IWebHostEnvironment>();
        env.ContentRootPath.Returns(contentRootPath);
        Services.AddSingleton(env);

        return (factory, javinizer, prowlarr, qbittorrent, jellyfin);
    }

    [Fact]
    public void AllServicesUnconfigured_ShowsNoSubServicesConfigured_AndNeverCallsTestConnection()
    {
        var (_, javinizer, prowlarr, qbittorrent, jellyfin) = SetUpServices();

        var cut = Render<SystemStatus>();

        Assert.Contains("javinizer-go", cut.Markup);
        Assert.Contains("not configured", cut.Markup);
        Assert.Contains("No sub-services configured.", cut.Markup);
        Assert.DoesNotContain("Connected successfully", cut.Markup);

        javinizer.DidNotReceive().TestConnectionAsync(Arg.Any<CancellationToken>());
        prowlarr.DidNotReceive().TestConnectionAsync(Arg.Any<CancellationToken>());
        qbittorrent.DidNotReceive().TestConnectionAsync(Arg.Any<CancellationToken>());
        jellyfin.DidNotReceive().TestConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void HealthyConfiguredService_LinksToItsExternalUrl_WithoutTheRedundantSuccessMessage()
    {
        var (factory, javinizer, _, _, _) = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://127.0.0.1:50730", ApiToken = "jv_test" });
            db.SaveChanges();
        }
        javinizer.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new JavinizerTestResult(true, "Connected successfully."));
        javinizer.GetExternalUrlAsync(Arg.Any<CancellationToken>()).Returns("https://javinizer.example.test");

        var cut = Render<SystemStatus>();

        var link = cut.Find(".health-service-link");
        Assert.Equal("javinizer-go", link.TextContent);
        Assert.Equal("https://javinizer.example.test", link.GetAttribute("href"));
        Assert.DoesNotContain("Connected successfully.", cut.Markup);
        javinizer.Received(1).TestConnectionAsync(Arg.Any<CancellationToken>());
        javinizer.Received(1).GetExternalUrlAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void HealthyConfiguredServices_RendersVersions_WhenAvailable()
    {
        var (factory, javinizer, prowlarr, qbittorrent, jellyfin) = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://127.0.0.1:50730", ApiToken = "jv_test" });
            db.ProwlarrSettings.Add(new ProwlarrSettings { BaseUrl = "http://127.0.0.1:9696", ApiKey = "prowlarr_key" });
            db.QBittorrentSettings.Add(new QBittorrentSettings { BaseUrl = "http://127.0.0.1:8080", Username = "user", Password = "password" });
            db.JellyfinSettings.Add(new JellyfinSettings { BaseUrl = "http://127.0.0.1:8096", ApiKey = "jf_key", Enabled = true });
            db.SaveChanges();
        }

        javinizer.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new JavinizerTestResult(true, "Connected successfully.", "v0.5.2"));
        prowlarr.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new ProwlarrTestResult(true, "Connected successfully.", "1.31.2.4975"));
        qbittorrent.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new QBittorrentTestResult(true, "Connected successfully.", "v4.6.0"));
        jellyfin.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new JellyfinTestResult(true, "Connected successfully.", "10.10.6", "jf_srv_id"));

        var cut = Render<SystemStatus>();

        var subServicesTable = cut.FindAll(".health-table").Last();
        Assert.Contains("(v0.5.2)", subServicesTable.TextContent);
        Assert.Contains("(1.31.2.4975)", subServicesTable.TextContent);
        Assert.Contains("(v4.6.0)", subServicesTable.TextContent);
        Assert.Contains("(10.10.6)", subServicesTable.TextContent);

        var versions = cut.FindAll(".health-service-version");
        Assert.Equal(4, versions.Count);
        Assert.Equal("(v0.5.2)", versions[0].TextContent.Trim());
        Assert.Equal("(1.31.2.4975)", versions[1].TextContent.Trim());
        Assert.Equal("(v4.6.0)", versions[2].TextContent.Trim());
        Assert.Equal("(10.10.6)", versions[3].TextContent.Trim());
    }

    [Fact]
    public void HealthyConfiguredService_OmitsVersion_WhenVersionIsNull()
    {
        var (factory, javinizer, _, qbittorrent, _) = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://127.0.0.1:50730", ApiToken = "jv_test" });
            db.QBittorrentSettings.Add(new QBittorrentSettings { BaseUrl = "http://127.0.0.1:8080", Username = "user", Password = "password" });
            db.SaveChanges();
        }
        javinizer.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new JavinizerTestResult(true, "Connected successfully.", null));
        qbittorrent.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new QBittorrentTestResult(true, "Connected successfully.", null));

        var cut = Render<SystemStatus>();

        var subServicesTable = cut.FindAll(".health-table").Last();
        Assert.Contains("javinizer-go", subServicesTable.TextContent);
        Assert.Contains("qBittorrent", subServicesTable.TextContent);
        Assert.Empty(cut.FindAll(".health-service-version"));
    }

    [Fact]
    public void UnconfiguredService_IsOmittedFromSubServicesList_WhenOthersAreConfigured()
    {
        var (factory, javinizer, _, _, _) = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://127.0.0.1:50730", ApiToken = "jv_test" });
            db.SaveChanges();
        }
        javinizer.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new JavinizerTestResult(true, "Connected successfully."));

        var cut = Render<SystemStatus>();

        // The Health section above also renders a "health-table" div, so the Sub-services
        // list (which only lists configured integrations) is the last one in DOM order.
        var subServicesTable = cut.FindAll(".health-table").Last();
        Assert.DoesNotContain("Prowlarr", subServicesTable.TextContent);
        Assert.DoesNotContain("qBittorrent", subServicesTable.TextContent);
        Assert.Contains("javinizer-go", subServicesTable.TextContent);
    }

    [Fact]
    public async Task ClickingRefresh_ReRunsTheChecks()
    {
        var (factory, javinizer, _, _, _) = SetUpServices();
        using (var db = factory.CreateDbContext())
        {
            db.JavinizerSettings.Add(new JavinizerSettings { BaseUrl = "http://127.0.0.1:50730", ApiToken = "jv_test" });
            db.SaveChanges();
        }
        javinizer.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new JavinizerTestResult(true, "Connected successfully."));

        var cut = Render<SystemStatus>();
        await javinizer.Received(1).TestConnectionAsync(Arg.Any<CancellationToken>());

        var refreshButton = cut.FindAll("button").Single(b => b.TextContent.Contains("Refresh"));
        // Awaited: a plain Click() only queues the event when the page's 1-second uptime tick holds
        // the renderer's dispatcher, so the check below could run before the refresh did.
        await refreshButton.ClickAsync(new());

        await javinizer.Received(2).TestConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DataDirectory_ResolvesFromConnectionString_WhenDataSourceIsAbsolute()
    {
        var dbPath = OperatingSystem.IsWindows() ? @"C:\data\Javbuddy.db" : "/data/Javbuddy.db";
        var expectedDirectory = OperatingSystem.IsWindows() ? @"C:\data" : "/data";
        SetUpServices(connectionString: $"Data Source={dbPath}");

        var cut = Render<SystemStatus>();

        Assert.Contains("Data Directory", cut.Markup);
        Assert.Contains(expectedDirectory, cut.Markup);
    }

    [Fact]
    public void DataDirectory_FallsBackToContentRoot_WhenConnectionStringHasNoDirectory()
    {
        SetUpServices(connectionString: "Data Source=Javbuddy.db", contentRootPath: "/app");

        var cut = Render<SystemStatus>();

        Assert.Contains("Data Directory", cut.Markup);
        Assert.Contains("/app", cut.Markup);
    }

    [Fact]
    public void DiskSpace_RendersLocationsFromTheService()
    {
        SetUpServices();
        var diskSpace = Services.GetRequiredService<IDiskSpaceService>();
        diskSpace.GetDiskSpaceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([new DiskSpaceInfo("/data", 50L * 1024 * 1024 * 1024, 200L * 1024 * 1024 * 1024)]);

        var cut = Render<SystemStatus>();

        Assert.Contains("Disk Space", cut.Markup);
        Assert.Contains("/data", cut.Markup);
        Assert.Contains("50 GB", cut.Markup);
        Assert.Contains("200 GB", cut.Markup);
    }

    [Fact]
    public void DiskSpace_ShowsUsedSpaceBeforeFreeSpace()
    {
        SetUpServices();
        var diskSpace = Services.GetRequiredService<IDiskSpaceService>();
        diskSpace.GetDiskSpaceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([new DiskSpaceInfo("/data", 50L * 1024 * 1024 * 1024, 200L * 1024 * 1024 * 1024)]);

        var cut = Render<SystemStatus>();

        var header = cut.Find(".disk-space-header").Children.Select(c => c.TextContent).ToList();
        Assert.Equal(["Location", "Used Space", "Free Space", "Total Space", ""], header);
        var row = cut.FindAll(".disk-space-row")[1].Children.Select(c => c.TextContent.Trim()).ToList();
        Assert.Equal(["/data", "150 GB", "50 GB", "200 GB", ""], row);
    }

    [Fact]
    public void DiskSpace_RendersLabelForImageCacheAndActorImagesLocations()
    {
        SetUpServices();
        var diskSpace = Services.GetRequiredService<IDiskSpaceService>();
        diskSpace.GetDiskSpaceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([
                new DiskSpaceInfo("/cache", 1L * 1024 * 1024 * 1024, 10L * 1024 * 1024 * 1024, "Image Cache"),
                new DiskSpaceInfo("/images", 2L * 1024 * 1024 * 1024, 20L * 1024 * 1024 * 1024, "Actor Images"),
            ]);

        var cut = Render<SystemStatus>();

        Assert.Contains("Image Cache", cut.Markup);
        Assert.Contains("/cache", cut.Markup);
        Assert.Contains("Actor Images", cut.Markup);
        Assert.Contains("/images", cut.Markup);
    }

    [Fact]
    public void DiskSpace_PassesTheImageCachePathToTheService()
    {
        SetUpServices();
        var imageCache = Services.GetRequiredService<ILocalImageCache>();
        imageCache.RootPath.Returns("/cache");
        var diskSpace = Services.GetRequiredService<IDiskSpaceService>();

        Render<SystemStatus>();

        diskSpace.Received().GetDiskSpaceAsync(Arg.Any<string>(), "/cache", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ImageCacheSettings_AreShownOnTheStatusPage()
    {
        SetUpServices(imageCacheSettings: new ImageCacheSettings(ImageCacheMode.Thumbnails, TimeSpan.FromHours(24), 512L * 1024 * 1024));

        var cut = Render<SystemStatus>();

        Assert.Contains("Cache Mode", cut.Markup);
        Assert.Contains("Thumbnails", cut.Markup);
        Assert.Contains("Cache TTL", cut.Markup);
        Assert.Contains("24 hours", cut.Markup);
        Assert.Contains("Cache Limit", cut.Markup);
        Assert.Contains("512 MB", cut.Markup);
    }

    [Fact]
    public void ImageCacheStats_BreakDownStillImagesWebMPreviewsAndTimelines()
    {
        SetUpServices();
        var imageCacheMaintenance = Services.GetRequiredService<IImageCacheMaintenanceService>();
        imageCacheMaintenance.GetStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new ImageCacheStats(7, 0,
                StillImageFileCount: 3_000, StillImageBytes: 512L * 1024 * 1024,
                PreviewFileCount: 1_420, PreviewBytes: 8L * 1024 * 1024 * 1024,
                TimelineFileCount: 10, TimelineBytes: 24L * 1024 * 1024));

        var cut = Render<SystemStatus>();

        var rows = cut.FindAll(".about-row")
            .Select(row => row.Children.Select(c => c.TextContent.Trim()).ToList())
            .ToDictionary(cells => cells[0], cells => cells[1]);
        Assert.Equal("4,430 (8.5 GB)", rows["Cached Files"]);
        Assert.Equal("3,000 (512 MB)", rows["Still Images"]);
        Assert.Equal("1,420 (8 GB)", rows["WebM Previews"]);
        Assert.Equal("10 (24 MB)", rows["DeoVR Timelines"]);
    }
}
