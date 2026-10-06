using Javbuddy.Services.MovieDiscovery;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.MovieDiscovery;

public class DiscoverySettingsServiceTests
{
    private static IStudioDiscoverySource Source(string name, string? logo = null)
    {
        var source = Substitute.For<IStudioDiscoverySource>();
        source.SourceName.Returns(name);
        source.Logo.Returns(logo is null ? null : new StudioLogo(logo, 10, 10));
        return source;
    }

    [Fact]
    public async Task GetSourcesAsync_NoRows_EverySourceIsEnabled()
    {
        using var factory = new TestDbContextFactory();
        var service = new DiscoverySettingsService([Source("S1", "/s1.png"), Source("Moodyz")], factory);

        var sources = await service.GetSourcesAsync();

        Assert.Equal(["S1", "Moodyz"], sources.Select(s => s.SourceName));
        Assert.All(sources, s => Assert.True(s.Enabled));
        Assert.Equal("/s1.png", sources[0].LogoUrl);
    }

    [Fact]
    public async Task SaveAsync_DisablesAndReEnablesSources_ThroughASingleRowPerSource()
    {
        using var factory = new TestDbContextFactory();
        var service = new DiscoverySettingsService([Source("S1"), Source("Moodyz")], factory);

        await service.SaveAsync(new Dictionary<string, bool> { ["S1"] = false, ["Moodyz"] = true });
        var afterDisable = await service.GetSourcesAsync();
        await service.SaveAsync(new Dictionary<string, bool> { ["S1"] = true });
        var afterEnable = await service.GetSourcesAsync();

        Assert.False(afterDisable.Single(s => s.SourceName == "S1").Enabled);
        Assert.True(afterDisable.Single(s => s.SourceName == "Moodyz").Enabled);
        Assert.All(afterEnable, s => Assert.True(s.Enabled));
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2, await db.DiscoverySourceSettings.CountAsync());
    }

    [Fact]
    public async Task SaveAsync_IgnoresNamesThatAreNotRegisteredSources()
    {
        using var factory = new TestDbContextFactory();
        var service = new DiscoverySettingsService([Source("S1")], factory);

        await service.SaveAsync(new Dictionary<string, bool> { ["Ghost"] = false });

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.DiscoverySourceSettings.ToListAsync());
    }

    [Fact]
    public async Task RunScanAsync_SkipsDisabledSources_AndReportsProgressOverTheEnabledOnes()
    {
        using var factory = new TestDbContextFactory();
        var disabled = Source("Off");
        var enabled = Source("On");
        enabled.ScanAsync(Arg.Any<CancellationToken>()).Returns([new DiscoveredMovieItem("SIVR-501", "Title", "S1", "https://example.com/c.jpg", [], [], null, false)]);
        await new DiscoverySettingsService([disabled, enabled], factory).SaveAsync(new Dictionary<string, bool> { ["Off"] = false });
        var service = new MovieDiscoveryService([disabled, enabled], factory, Substitute.For<Javbuddy.Services.Movies.IMovieAddService>(), TimeProvider.System);

        var result = await service.RunScanAsync();

        Assert.Equal(1, result.NewCount);
        await disabled.DidNotReceiveWithAnyArgs().ScanAsync(default);
        await enabled.Received(1).ScanAsync(Arg.Any<CancellationToken>());
    }
}
