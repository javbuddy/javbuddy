using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.Torrents;

public class PathMappingServiceTests
{
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    [Fact]
    public async Task UpdateAsync_RewritesAllThreePrefixes_TrimmingTrailingSlashes()
    {
        using var factory = new TestDbContextFactory();
        var service = new PathMappingService(factory, EmptyConfiguration);
        var mapping = await service.AddAsync("/downloads", "/scratch", appPrefix: null);

        await service.UpdateAsync(mapping.Id, "/downloads/javbuddy/", "/scratch/javbuddy/", @"D:\scratch\javbuddy\");

        var updated = Assert.Single(await service.GetAllAsync());
        Assert.Equal("/downloads/javbuddy", updated.QBittorrentPrefix);
        Assert.Equal("/scratch/javbuddy", updated.JavinizerPrefix);
        Assert.Equal(@"D:\scratch\javbuddy", updated.AppPrefix);
    }

    [Fact]
    public async Task UpdateAsync_BlankAppPrefix_ClearsIt()
    {
        using var factory = new TestDbContextFactory();
        var service = new PathMappingService(factory, EmptyConfiguration);
        var mapping = await service.AddAsync("/downloads", "/scratch", @"D:\scratch");

        await service.UpdateAsync(mapping.Id, "/downloads", "/scratch", "  ");

        Assert.Null(Assert.Single(await service.GetAllAsync()).AppPrefix);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateQBittorrentPrefix_Throws()
    {
        using var factory = new TestDbContextFactory();
        var service = new PathMappingService(factory, EmptyConfiguration);
        await service.AddAsync("/downloads/a", "/scratch/a", appPrefix: null);
        var second = await service.AddAsync("/downloads/b", "/scratch/b", appPrefix: null);

        await Assert.ThrowsAsync<DbUpdateException>(() => service.UpdateAsync(second.Id, "/downloads/a", "/scratch/b", null));
    }

    [Fact]
    public async Task TranslateToAppPathAsync_UsesLongestMatchingPrefix()
    {
        using var factory = new TestDbContextFactory();
        var service = new PathMappingService(factory, EmptyConfiguration);

        await service.AddAsync("/downloads", "/scratch", @"D:\scratch");
        await service.AddAsync("/downloads/javbuddy", "/scratch/javbuddy", @"D:\scratch\torrent\javbuddy");

        var result = await service.TranslateToAppPathAsync("/downloads/javbuddy/devr-041");

        Assert.Equal(@"D:\scratch\torrent\javbuddy/devr-041", result);
    }

    [Fact]
    public async Task TranslateToAppPathAsync_RequiresSegmentBoundaryMatch()
    {
        using var factory = new TestDbContextFactory();
        var service = new PathMappingService(factory, EmptyConfiguration);

        await service.AddAsync("/downloads/javbuddy", "/scratch/javbuddy", @"D:\scratch\javbuddy");

        var result = await service.TranslateToAppPathAsync("/downloads/javbuddy2/devr-041");

        Assert.Equal("/downloads/javbuddy2/devr-041", result);
    }

    [Fact]
    public async Task TranslateToAppPathAsync_NoMappingHasAppPrefix_ReturnsInputUnchanged()
    {
        using var factory = new TestDbContextFactory();
        var service = new PathMappingService(factory, EmptyConfiguration);

        await service.AddAsync("/downloads", "/scratch", appPrefix: null);

        var result = await service.TranslateToAppPathAsync("/downloads/devr-041");

        Assert.Equal("/downloads/devr-041", result);
    }

    [Fact]
    public async Task TranslateToAppPathAsync_IgnoresQBittorrentOnlyMapping_FallsThroughToOneWithAppPrefix()
    {
        using var factory = new TestDbContextFactory();
        var service = new PathMappingService(factory, EmptyConfiguration);

        await service.AddAsync("/downloads/javbuddy/sub", "/scratch/javbuddy/sub", appPrefix: null);
        await service.AddAsync("/downloads", "/scratch", @"D:\scratch");

        var result = await service.TranslateToAppPathAsync("/downloads/javbuddy/sub/devr-041");

        Assert.Equal(@"D:\scratch/javbuddy/sub/devr-041", result);
    }

    [Fact]
    public async Task TranslateAsync_StillWorksForJavinizerPrefix_UnaffectedByAppPrefix()
    {
        using var factory = new TestDbContextFactory();
        var service = new PathMappingService(factory, EmptyConfiguration);

        await service.AddAsync("/downloads", "/scratch", @"D:\scratch");

        var result = await service.TranslateAsync("/downloads/devr-041");

        Assert.Equal("/scratch/devr-041", result);
    }

    [Fact]
    public async Task TranslateJavinizerPathToAppPathAsync_TranslatesJavinizerPrefixToAppPrefix()
    {
        using var factory = new TestDbContextFactory();
        var service = new PathMappingService(factory, EmptyConfiguration);

        await service.AddAsync("/downloads", "/media", "/mnt/library");

        var result = await service.TranslateJavinizerPathToAppPathAsync("/media/movie1");

        Assert.Equal("/mnt/library/movie1", result);
    }

    [Fact]
    public async Task TranslateJavinizerPathToAppPathAsync_NoMatchingJavinizerPrefix_ReturnsInputUnchanged()
    {
        using var factory = new TestDbContextFactory();
        var service = new PathMappingService(factory, EmptyConfiguration);

        await service.AddAsync("/downloads", "/media", "/mnt/library");

        var result = await service.TranslateJavinizerPathToAppPathAsync("/other/movie1");

        Assert.Equal("/other/movie1", result);
    }

    [Fact]
    public async Task TranslateJavinizerPathToAppPathAsync_UsesLongestMatchingPrefix()
    {
        using var factory = new TestDbContextFactory();
        var service = new PathMappingService(factory, EmptyConfiguration);

        await service.AddAsync("/downloads", "/media", "/mnt/library");
        await service.AddAsync("/downloads/javbuddy", "/media/javbuddy", "/mnt/library/javbuddy-specific");

        var result = await service.TranslateJavinizerPathToAppPathAsync("/media/javbuddy/devr-041");

        Assert.Equal("/mnt/library/javbuddy-specific/devr-041", result);
    }

    [Fact]
    public async Task TranslateJavinizerPathToAppPathAsync_NullAppPrefix_SkipsMapping()
    {
        using var factory = new TestDbContextFactory();
        var service = new PathMappingService(factory, EmptyConfiguration);

        await service.AddAsync("/downloads", "/media", appPrefix: null);

        var result = await service.TranslateJavinizerPathToAppPathAsync("/media/devr-041");

        Assert.Equal("/media/devr-041", result);
    }

    [Fact]
    public async Task TranslateJavinizerPathToAppPathAsync_ConfiguredPrefixLongerThanPath_ReturnsInputUnchanged()
    {
        // Regression test for the IndexOutOfRangeException crash: a configured
        // mapping's source prefix longer than the path being translated must fail to match
        // instead of indexing past the end of path.
        using var factory = new TestDbContextFactory();
        var service = new PathMappingService(factory, EmptyConfiguration);

        await service.AddAsync("/downloads", "/media/javbuddy/very/long/prefix", "/mnt/library");

        var result = await service.TranslateJavinizerPathToAppPathAsync("/media");

        Assert.Equal("/media", result);
    }

    [Fact]
    public void PathMappingEnvConfig_GetMapping_ReadsAllThreePrefixes()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["PathMapping:QBittorrentPrefix"] = "/downloads",
            ["PathMapping:JavinizerPrefix"] = "/scratch",
            ["PathMapping:AppPrefix"] = @"D:\scratch"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var mapping = PathMappingEnvConfig.GetMapping(config);

        Assert.NotNull(mapping);
        Assert.Equal("/downloads", mapping.QBittorrentPrefix);
        Assert.Equal("/scratch", mapping.JavinizerPrefix);
        Assert.Equal(@"D:\scratch", mapping.AppPrefix);
    }

    [Fact]
    public void PathMappingEnvConfig_GetMapping_AppPrefixOptional()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["PathMapping:QBittorrentPrefix"] = "/downloads",
            ["PathMapping:JavinizerPrefix"] = "/scratch"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var mapping = PathMappingEnvConfig.GetMapping(config);

        Assert.NotNull(mapping);
        Assert.Null(mapping.AppPrefix);
    }

    [Fact]
    public void PathMappingEnvConfig_GetMapping_MissingRequiredPrefix_ReturnsNull()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["PathMapping:QBittorrentPrefix"] = "/downloads"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var mapping = PathMappingEnvConfig.GetMapping(config);

        Assert.Null(mapping);
    }

    [Fact]
    public void PathMappingEnvConfig_GetMapping_EmptyReturnsNull()
    {
        var mapping = PathMappingEnvConfig.GetMapping(EmptyConfiguration);

        Assert.Null(mapping);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PathMappingEnvConfig_GetMapping_EmptyAppPrefixNormalizesToNull(string? appPrefix)
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["PathMapping:QBittorrentPrefix"] = "/downloads",
            ["PathMapping:JavinizerPrefix"] = "/scratch",
            ["PathMapping:AppPrefix"] = appPrefix,
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var mapping = PathMappingEnvConfig.GetMapping(config);

        Assert.NotNull(mapping);
        Assert.Null(mapping.AppPrefix);
    }

    [Fact]
    public async Task GetAllAsync_EnvConfigured_OverridesDbMappings()
    {
        using var factory = new TestDbContextFactory();
        var service = new PathMappingService(factory, EmptyConfiguration);
        await service.AddAsync("/downloads", "/scratch", @"D:\scratch");

        var inMemory = new Dictionary<string, string?>
        {
            ["PathMapping:QBittorrentPrefix"] = "/env-downloads",
            ["PathMapping:JavinizerPrefix"] = "/env-scratch"
        };
        var envConfig = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var envService = new PathMappingService(factory, envConfig);

        var mappings = await envService.GetAllAsync();

        Assert.Single(mappings);
        Assert.Equal("/env-downloads", mappings[0].QBittorrentPrefix);
        Assert.Equal("/env-scratch", mappings[0].JavinizerPrefix);
    }

    [Fact]
    public async Task TranslateAsync_EnvConfigured_UsesEnvMappingInsteadOfDb()
    {
        using var factory = new TestDbContextFactory();
        var dbService = new PathMappingService(factory, EmptyConfiguration);
        await dbService.AddAsync("/downloads", "/db-scratch", appPrefix: null);

        var inMemory = new Dictionary<string, string?>
        {
            ["PathMapping:QBittorrentPrefix"] = "/downloads",
            ["PathMapping:JavinizerPrefix"] = "/env-scratch"
        };
        var envConfig = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var envService = new PathMappingService(factory, envConfig);

        var result = await envService.TranslateAsync("/downloads/devr-041");

        Assert.Equal("/env-scratch/devr-041", result);
    }
}
