using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class VirtualizedGridTests : BunitContext
{
    private readonly BunitJSModuleInterop module;
    private readonly List<(int Start, int Count, int Columns)> requested = [];
    private TaskCompletionSource? blockLoads;

    public VirtualizedGridTests()
    {
        module = JSInterop.SetupModule("./Components/Shared/VirtualizedGrid.razor.js");
        module.Setup<bool>("init", _ => true).SetResult(true);
        module.SetupVoid("scrollToTop", _ => true).SetVoidResult();
        module.SetupVoid("dispose", _ => true).SetVoidResult();
    }

    private IRenderedComponent<VirtualizedGrid> RenderGrid(bool ready = true) => Render<VirtualizedGrid>(p => p
        .Add(g => g.WindowStart, 12)
        .Add(g => g.WindowCount, 3)
        .Add(g => g.TotalCount, 200)
        .Add(g => g.GridSelector, ".grid")
        .Add(g => g.CardSelector, ".card")
        .Add(g => g.ActivePath, "/things")
        .Add(g => g.Ready, ready)
        .Add(g => g.LoadRange, async (start, count, columns) =>
        {
            requested.Add((start, count, columns));
            if (blockLoads is not null) await blockLoads.Task;
        })
        .AddChildContent("<div class=\"grid\"><div class=\"card\">a</div></div>"));

    [Fact]
    public void RendersTheWindowOnTheLeadingSpacer_AroundTheContent()
    {
        var cut = RenderGrid();

        var spacers = cut.FindAll(".virtualized-grid-spacer");
        Assert.Equal(2, spacers.Count);
        Assert.Equal("12", spacers[0].GetAttribute("data-window-start"));
        Assert.Equal("3", spacers[0].GetAttribute("data-window-count"));
        Assert.Equal("200", spacers[0].GetAttribute("data-total-count"));
        Assert.Single(cut.FindAll(".virtualized-grid .grid .card"));
    }

    [Fact]
    public void StartsTheJs_OnlyOnceReady()
    {
        var cut = RenderGrid(ready: false);
        Assert.DoesNotContain(module.Invocations, i => i.Identifier == "init");

        cut.Render(p => p.Add(g => g.Ready, true));

        var init = Assert.Single(module.Invocations, i => i.Identifier == "init");
        Assert.Equal("/things", init.Arguments[5]!.GetType().GetProperty("activePath")!.GetValue(init.Arguments[5]));
    }

    [Fact]
    public async Task RangesAskedForMidLoad_CoalesceIntoTheNewest()
    {
        var cut = RenderGrid();
        blockLoads = new TaskCompletionSource();

        var first = cut.InvokeAsync(() => cut.Instance.SetVisibleRange(0, 10, 2));
        await cut.InvokeAsync(() => cut.Instance.SetVisibleRange(20, 10, 2));
        await cut.InvokeAsync(() => cut.Instance.SetVisibleRange(40, 10, 2));
        blockLoads.SetResult();
        await first;

        Assert.Equal([(0, 10, 2), (40, 10, 2)], requested);
    }

    [Fact]
    public async Task ScrollToTop_BeforeTheJsStarted_RunsOnceItHas()
    {
        var cut = RenderGrid(ready: false);

        await cut.InvokeAsync(() => cut.Instance.ScrollToTopAsync());
        Assert.DoesNotContain(module.Invocations, i => i.Identifier == "scrollToTop");

        cut.Render(p => p.Add(g => g.Ready, true));
        Assert.Single(module.Invocations, i => i.Identifier == "scrollToTop");
    }

    [Fact]
    public async Task Dispose_DetachesThisGridsJsState()
    {
        var cut = RenderGrid();
        var gridId = module.Invocations.Single(i => i.Identifier == "init").Arguments[0];

        await DisposeComponentsAsync();

        var dispose = Assert.Single(module.Invocations, i => i.Identifier == "dispose");
        Assert.Equal(gridId, dispose.Arguments[0]);
    }
}
