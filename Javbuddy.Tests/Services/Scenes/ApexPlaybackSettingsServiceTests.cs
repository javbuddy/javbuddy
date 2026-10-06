using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.Scenes;

public class ApexPlaybackSettingsServiceTests : IDisposable
{
    private readonly TestDbContextFactory dbFactory = new();

    public void Dispose() => dbFactory.Dispose();

    private ApexPlaybackSettingsService Service(Dictionary<string, string?>? env = null) =>
        new(dbFactory, new ConfigurationBuilder().AddInMemoryCollection(env ?? []).Build());

    [Fact]
    public async Task WithNothingSaved_PlaysFiveSecondsEitherSide()
    {
        Assert.Equal(new ApexWindow(5, 5), await Service().GetEffectiveAsync());
    }

    [Fact]
    public async Task SaveAsync_UpsertsTheSingleRow()
    {
        var service = Service();
        Assert.Null(await service.SaveAsync(new ApexPlaybackSettings { LeadInSeconds = 3, TailSeconds = 5 }));
        Assert.Null(await service.SaveAsync(new ApexPlaybackSettings { LeadInSeconds = 3, TailSeconds = 10 }));

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = Assert.Single(db.ApexPlaybackSettings);
        Assert.Equal((3d, 10d), (row.LeadInSeconds, row.TailSeconds));
        Assert.Equal(new ApexWindow(3, 10), await service.GetEffectiveAsync());
    }

    [Fact]
    public async Task SaveAsync_RejectsAnInvalidWindow_WithoutSaving()
    {
        var service = Service();

        Assert.NotNull(await service.SaveAsync(new ApexPlaybackSettings { LeadInSeconds = -1, TailSeconds = 5 }));
        Assert.NotNull(await service.SaveAsync(new ApexPlaybackSettings { LeadInSeconds = 5, TailSeconds = 0 }));

        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.Empty(db.ApexPlaybackSettings);
    }

    [Fact]
    public async Task EachEnvironmentVariable_OverridesJustItsOwnField_AndAnInvalidOneIsIgnored()
    {
        await Service().SaveAsync(new ApexPlaybackSettings { LeadInSeconds = 3, TailSeconds = 10 });

        var tail = Service(new() { ["ApexPlayback:TailSeconds"] = "7.5" });
        Assert.Equal(new ApexWindow(3, 7.5), await tail.GetEffectiveAsync());
        Assert.True(tail.IsSetByEnvironment(nameof(ApexPlaybackSettings.TailSeconds)));
        Assert.False(tail.IsSetByEnvironment(nameof(ApexPlaybackSettings.LeadInSeconds)));

        var invalid = Service(new() { ["ApexPlayback:LeadInSeconds"] = "-2", ["ApexPlayback:TailSeconds"] = "soon" });
        Assert.Equal(new ApexWindow(3, 10), await invalid.GetEffectiveAsync());
        Assert.False(invalid.IsSetByEnvironment(nameof(ApexPlaybackSettings.LeadInSeconds)));
    }
}
