using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class PlayerIdleChromeTests : BunitContext
{
    private BunitJSModuleInterop SetUpModule()
    {
        var module = JSInterop.SetupModule("./Components/Shared/PlayerIdleChrome.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        return module;
    }

    [Fact]
    public void InitialisesTheModuleWithTheRootAndVideoSelector_AndRendersNothing()
    {
        var module = SetUpModule();

        var cut = Render<PlayerIdleChrome>(p => p
            .Add(x => x.Root, new Microsoft.AspNetCore.Components.ElementReference("panel-id"))
            .Add(x => x.VideoSelector, "video.video-player-video"));

        var init = Assert.Single(module.Invocations, i => i.Identifier == "init");
        Assert.Equal("video.video-player-video", init.Arguments[2]);
        Assert.Equal("", cut.Markup.Trim());
    }

    [Fact]
    public async Task Dispose_RemovesTheListenersRegisteredUnderTheSameKey()
    {
        var module = SetUpModule();
        var cut = Render<PlayerIdleChrome>(p => p
            .Add(x => x.Root, new Microsoft.AspNetCore.Components.ElementReference("panel-id"))
            .Add(x => x.VideoSelector, "video.video-player-video"));

        await cut.Instance.DisposeAsync();

        var key = Assert.Single(module.Invocations, i => i.Identifier == "init").Arguments[0];
        Assert.Equal(key, Assert.Single(module.Invocations, i => i.Identifier == "dispose").Arguments[0]);
    }
}
