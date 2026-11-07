using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.Movies;

public class NewMovieArtworkSettingsServiceTests : IDisposable
{
    private readonly TestDbContextFactory dbFactory = new();

    public void Dispose() => dbFactory.Dispose();

    private NewMovieArtworkSettingsService Service(Dictionary<string, string?>? env = null) =>
        new(dbFactory, new ConfigurationBuilder().AddInMemoryCollection(env ?? []).Build());

    [Fact]
    public async Task WithNothingSaved_BothAreOn()
    {
        var settings = await Service().GetEffectiveAsync();

        Assert.True(settings.GenerateExtraFanart);
        Assert.True(settings.GeneratePosterAndFanart);
    }

    [Fact]
    public async Task SaveAsync_UpsertsTheSingleRow()
    {
        var service = Service();
        await service.SaveAsync(new NewMovieArtworkSettings { GenerateExtraFanart = false });
        await service.SaveAsync(new NewMovieArtworkSettings { GenerateExtraFanart = false, GeneratePosterAndFanart = false });

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = Assert.Single(db.NewMovieArtworkSettings);
        Assert.False(row.GenerateExtraFanart);
        Assert.False(row.GeneratePosterAndFanart);
    }

    [Fact]
    public async Task EachEnvironmentVariable_OverridesJustItsOwnField()
    {
        await Service().SaveAsync(new NewMovieArtworkSettings { GenerateExtraFanart = true, GeneratePosterAndFanart = true });
        var service = Service(new() { ["NewMovieArtwork:GenerateExtraFanart"] = "false", ["NewMovieArtwork:GeneratePosterAndFanart"] = "nope" });

        var effective = await service.GetEffectiveAsync();

        Assert.False(effective.GenerateExtraFanart);
        Assert.True(effective.GeneratePosterAndFanart);
        Assert.True((await service.GetStoredAsync()).GenerateExtraFanart);
        Assert.True(service.IsSetByEnvironment(nameof(NewMovieArtworkSettings.GenerateExtraFanart)));
        Assert.False(service.IsSetByEnvironment(nameof(NewMovieArtworkSettings.GeneratePosterAndFanart)));
    }
}
