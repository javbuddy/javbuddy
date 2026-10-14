using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class UptimeClockTests : BunitContext
{
    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(-5, "00:00:00")]
    [InlineData(3_725, "01:02:05")]
    [InlineData(90_061, "1d 01:01:01")]
    public void Format_ShowsHoursMinutesSeconds_WithDaysOnlyWhenThereAreAny(int seconds, string expected) =>
        Assert.Equal(expected, UptimeClock.Format(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void RendersTheTimeSinceTheStart()
    {
        var cut = Render<UptimeClock>(p => p.Add(x => x.Since, DateTime.Now - TimeSpan.FromMinutes(61)));

        Assert.StartsWith("01:01:", cut.Markup.Replace("<span>", "").Trim());
    }

    [Fact]
    public void KeepsTicking_WithoutTheParentRenderingAgain()
    {
        var cut = Render<UptimeClock>(p => p
            .Add(x => x.Since, DateTime.Now)
            .Add(x => x.Interval, TimeSpan.FromMilliseconds(50)));
        var renders = cut.RenderCount;

        cut.WaitForState(() => cut.RenderCount > renders + 1);
    }
}
