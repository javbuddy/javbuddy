using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class VideoSeekKeysTests : BunitContext
{
    private const string ModulePath = "./Components/Shared/VideoSeekKeys.razor.js";

    private BunitJSModuleInterop SetUpModule()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Loose;
        return module;
    }

    [Fact]
    public void InitialisesTheModuleWithTheVideoSelector_AndRendersNothing()
    {
        var module = SetUpModule();

        var cut = Render<VideoSeekKeys>(p => p.Add(x => x.VideoSelector, "video.cleanup-video"));

        var init = Assert.Single(module.Invocations, i => i.Identifier == "init");
        Assert.Equal("video.cleanup-video", init.Arguments[1]);
        Assert.Null(init.Arguments[2]);
        Assert.Null(init.Arguments[3]);
        Assert.Equal("", cut.Markup.Trim());
    }

    [Fact]
    public void ARange_IsPassedOn_ToClampTheSeeksToIt()
    {
        var module = SetUpModule();

        Render<VideoSeekKeys>(p => p
            .Add(x => x.VideoSelector, "video.video-player-video")
            .Add(x => x.MinSeconds, 90d)
            .Add(x => x.MaxSeconds, 100.5));

        var init = Assert.Single(module.Invocations, i => i.Identifier == "init");
        Assert.Equal(90d, init.Arguments[2]);
        Assert.Equal(100.5, init.Arguments[3]);
    }

    [Fact]
    public void JumpTargets_ArePassedOn_AndChangesSetTheListenerUpAgain()
    {
        var module = SetUpModule();
        var cut = Render<VideoSeekKeys>(p => p
            .Add(x => x.VideoSelector, "video.video-player-video")
            .Add(x => x.HighlightTargets, [10d, 40d])
            .Add(x => x.ApexTargets, [95d]));

        var init = Assert.Single(module.Invocations, i => i.Identifier == "init");
        Assert.Equal([10d, 40d], (IReadOnlyList<double>)init.Arguments[4]!);
        Assert.Equal([95d], (IReadOnlyList<double>)init.Arguments[5]!);

        // Unchanged values (even in new lists) don't; a new highlight does.
        cut.Render(p => p.Add(x => x.HighlightTargets, [10d, 40d]));
        Assert.Single(module.Invocations, i => i.Identifier == "init");
        cut.Render(p => p.Add(x => x.HighlightTargets, [10d, 40d, 60d]));

        var inits = module.Invocations.Where(i => i.Identifier == "init").ToList();
        Assert.Equal(2, inits.Count);
        Assert.Equal(init.Arguments[0], inits[1].Arguments[0]);
        Assert.Equal([10d, 40d, 60d], (IReadOnlyList<double>)inits[1].Arguments[4]!);
    }

    [Fact]
    public void ANewRange_SetsTheListenerUpAgain()
    {
        var module = SetUpModule();
        var cut = Render<VideoSeekKeys>(p => p
            .Add(x => x.VideoSelector, "video.video-player-video")
            .Add(x => x.MinSeconds, 90d)
            .Add(x => x.MaxSeconds, 100.5));

        cut.Render(p => p.Add(x => x.MinSeconds, 200d).Add(x => x.MaxSeconds, 230d));

        var last = module.Invocations.Last(i => i.Identifier == "init");
        Assert.Equal((200d, 230d), ((double?)last.Arguments[2], (double?)last.Arguments[3]));
    }

    [Fact]
    public async Task Dispose_RemovesTheListenerRegisteredUnderTheSameKey()
    {
        var module = SetUpModule();
        var cut = Render<VideoSeekKeys>(p => p.Add(x => x.VideoSelector, "video.video-player-video"));

        await cut.Instance.DisposeAsync();

        var key = Assert.Single(module.Invocations, i => i.Identifier == "init").Arguments[0];
        Assert.Equal(key, Assert.Single(module.Invocations, i => i.Identifier == "dispose").Arguments[0]);
    }
}
