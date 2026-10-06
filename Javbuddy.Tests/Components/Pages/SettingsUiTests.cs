using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Javbuddy.Tests.Components.Pages;

public class SettingsUiTests : BunitContext
{
    private readonly TestDbContextFactory dbFactory = new();
    private readonly Dictionary<string, string?> env = [];

    public SettingsUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<string?>("lsTheme.get").SetResult("auto");
        JSInterop.Setup<bool>("lsMovieBackdrop.get").SetResult(false);
        Services.AddSingleton<IApexPlaybackSettingsService>(_ =>
            new ApexPlaybackSettingsService(dbFactory, new ConfigurationBuilder().AddInMemoryCollection(env).Build()));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) dbFactory.Dispose();
        base.Dispose(disposing);
    }

    [Fact]
    public async Task ApexPlayback_ShowsTheStoredWindow_AndSavesEachSide()
    {
        var cut = Render<SettingsUi>();
        Assert.Equal(("5", "5"), (cut.Find("#apex-lead-in-seconds").GetAttribute("value"), cut.Find("#apex-tail-seconds").GetAttribute("value")));

        cut.Find("#apex-lead-in-seconds").Change("3");
        cut.Find("#apex-tail-seconds").Change("12.5");

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = Assert.Single(db.ApexPlaybackSettings);
        Assert.Equal((3d, 12.5), (row.LeadInSeconds, row.TailSeconds));
    }

    [Fact]
    public async Task ApexPlayback_AnInvalidSide_ShowsAnErrorWithoutSaving()
    {
        var cut = Render<SettingsUi>();

        cut.Find("#apex-tail-seconds").Change("0");

        Assert.Contains("tail must be between 1 and 300", cut.Find(".text-danger").TextContent);
        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.Empty(db.ApexPlaybackSettings);
    }

    [Fact]
    public void ApexPlayback_ASideSetByTheEnvironment_IsReadOnly()
    {
        env["ApexPlayback:LeadInSeconds"] = "8";

        var cut = Render<SettingsUi>();

        var leadIn = cut.Find("#apex-lead-in-seconds");
        Assert.True(leadIn.HasAttribute("disabled"));
        Assert.Equal("8", leadIn.GetAttribute("value"));
        Assert.Contains("ApexPlayback__LeadInSeconds", cut.Markup);
        Assert.False(cut.Find("#apex-tail-seconds").HasAttribute("disabled"));
    }

    [Fact]
    public void ApexCountdown_ShowsTheStoredLength_AndIsOffWhenZero()
    {
        JSInterop.Setup<int>("lsApexCountdown.get").SetResult(25);
        var on = Render<SettingsUi>();
        on.WaitForAssertion(() => Assert.Equal("25", on.Find("#apex-countdown-seconds").GetAttribute("value")));
        Assert.True(on.Find("#apex-countdown-check").HasAttribute("checked"));

        JSInterop.Setup<int>("lsApexCountdown.get").SetResult(0);
        var off = Render<SettingsUi>();
        off.WaitForAssertion(() => Assert.True(off.Find("#apex-countdown-seconds").HasAttribute("disabled")));
        Assert.False(off.Find("#apex-countdown-check").HasAttribute("checked"));
        Assert.Equal("10", off.Find("#apex-countdown-seconds").GetAttribute("value"));
    }

    [Fact]
    public void ApexCountdown_TogglingAndChangingTheLength_WritesTheCookieHelper()
    {
        JSInterop.Setup<int>("lsApexCountdown.get").SetResult(10);
        var cut = Render<SettingsUi>();
        cut.WaitForAssertion(() => Assert.True(cut.Find("#apex-countdown-check").HasAttribute("checked")));

        cut.Find("#apex-countdown-seconds").Change("500");
        Assert.Equal(60, JSInterop.Invocations.Last(i => i.Identifier == "lsApexCountdown.set").Arguments[0]);

        cut.Find("#apex-countdown-check").Change(false);
        Assert.Equal(0, JSInterop.Invocations.Last(i => i.Identifier == "lsApexCountdown.set").Arguments[0]);

        cut.Find("#apex-countdown-check").Change(true);
        Assert.Equal(60, JSInterop.Invocations.Last(i => i.Identifier == "lsApexCountdown.set").Arguments[0]);
    }

    [Fact]
    public void ActorDetailSections_ShowTheStoredOrder_AndMovingOneWritesTheCookieHelper()
    {
        JSInterop.Setup<string?>("lsActorDetailSections.get").SetResult("Movies,Photos");
        var cut = Render<SettingsUi>();
        cut.WaitForAssertion(() => Assert.Equal(
            ["Movies", "Photos", "Scenes", "Highlights", "Apexes"],
            cut.FindAll(".actor-detail-section-item").Select(li => li.GetAttribute("data-section"))));
        Assert.True(cut.FindAll(".actor-detail-section-item")[0].QuerySelector("button[title='Move up']")!.HasAttribute("disabled"));

        cut.Find("button[aria-label='Move Apexes up']").Click();
        Assert.Equal("Movies,Photos,Scenes,Apexes,Highlights", JSInterop.Invocations.Last(i => i.Identifier == "lsActorDetailSections.set").Arguments[0]);

        cut.FindAll("button").First(b => b.TextContent == "Reset to default").Click();
        Assert.Equal("Photos,Movies,Scenes,Highlights,Apexes", JSInterop.Invocations.Last(i => i.Identifier == "lsActorDetailSections.set").Arguments[0]);
        Assert.Equal("Photos", cut.FindAll(".actor-detail-section-item")[0].GetAttribute("data-section"));
    }
}
