using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Components.Shared;

public class ApexCountdownTests : BunitContext
{
    private BunitJSModuleInterop SetUpModule()
    {
        var module = JSInterop.SetupModule("./Components/Shared/ApexCountdown.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        return module;
    }

    private static ApexItem Apex(double seconds) => new(1, 0, seconds, []);

    private IRenderedComponent<ApexCountdown> RenderWith(params double[] seconds) =>
        Render<ApexCountdown>(p => p
            .Add(x => x.VideoSelector, "video.video-player-video")
            .Add(x => x.Apexes, seconds.Select(Apex).ToList()));

    [Fact]
    public void RendersAHiddenOverlay_AndInitialisesTheModuleWithTheApexTimesInOrder()
    {
        var module = SetUpModule();

        var cut = RenderWith(90, 30);

        Assert.True(cut.Find(".apex-countdown").HasAttribute("hidden"));
        var init = Assert.Single(module.Invocations, i => i.Identifier == "init");
        Assert.Equal("video.video-player-video", init.Arguments[2]);
        Assert.Equal(new[] { 30d, 90d }, Assert.IsType<double[]>(init.Arguments[3]));
    }

    [Fact]
    public void ChangedApexes_AreSentToTheModule_UnchangedOnesAreNot()
    {
        var module = SetUpModule();
        var cut = RenderWith(30);

        cut.Render(p => p.Add(x => x.Apexes, new List<ApexItem> { Apex(30) }));
        Assert.DoesNotContain(module.Invocations, i => i.Identifier == "update");

        cut.Render(p => p.Add(x => x.Apexes, new List<ApexItem> { Apex(30), Apex(60) }));
        var update = Assert.Single(module.Invocations, i => i.Identifier == "update");
        Assert.Equal(new[] { 30d, 60d }, Assert.IsType<double[]>(update.Arguments[1]));
    }

    [Fact]
    public async Task Dispose_StopsTheTimerRegisteredUnderTheSameKey()
    {
        var module = SetUpModule();
        var cut = RenderWith(30);

        await cut.Instance.DisposeAsync();

        var key = Assert.Single(module.Invocations, i => i.Identifier == "init").Arguments[0];
        Assert.Equal(key, Assert.Single(module.Invocations, i => i.Identifier == "dispose").Arguments[0]);
    }
}
