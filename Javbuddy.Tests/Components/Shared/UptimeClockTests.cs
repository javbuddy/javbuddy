using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class UptimeClockTests : BunitContext
{
    private const string ModulePath = "./Components/Shared/UptimeClock.razor.js";

    private readonly BunitJSModuleInterop module;

    public UptimeClockTests()
    {
        module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("start", _ => true);
        module.SetupVoid("dispose", _ => true);
    }

    [Fact]
    public void RendersTheTimeSinceTheStart()
    {
        var cut = Render<UptimeClock>(p => p.Add(x => x.Since, DateTime.Now - TimeSpan.FromMinutes(61)));

        Assert.StartsWith("01:01:", cut.Find("[data-testid=uptime-clock]").TextContent);
    }

    [Fact]
    public void ComparesInUtc_SoAUtcStartReadsTheSameAsALocalOne()
    {
        // A local-vs-UTC mix-up would be off by the machine's UTC offset; comparing in UTC is also what
        // keeps a DST change from shifting the clock by an hour.
        var cut = Render<UptimeClock>(p => p.Add(x => x.Since, DateTime.UtcNow - TimeSpan.FromMinutes(61)));

        Assert.StartsWith("01:01:", cut.Find("[data-testid=uptime-clock]").TextContent);
    }

    [Fact]
    public void HandsTheTickingToTheBrowser_WithTheElapsedTimeAtRender()
    {
        var cut = Render<UptimeClock>(p => p.Add(x => x.Since, DateTime.UtcNow - TimeSpan.FromMinutes(61)));

        var start = Assert.Single(module.Invocations, i => i.Identifier == "start");
        var elapsedMs = Assert.IsType<double>(start.Arguments[2]);
        Assert.InRange(elapsedMs, TimeSpan.FromMinutes(61).TotalMilliseconds, TimeSpan.FromMinutes(62).TotalMilliseconds);
        // No server-side timer: the component doesn't render again on its own.
        Assert.Equal(1, cut.RenderCount);
    }

    [Fact]
    public async Task StopsTheBrowserTimer_WhenDisposed()
    {
        Render<UptimeClock>(p => p.Add(x => x.Since, DateTime.UtcNow));

        await DisposeComponentsAsync();

        Assert.Single(module.Invocations, i => i.Identifier == "dispose");
    }
}
