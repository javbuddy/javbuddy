using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Statistics;

namespace Javbuddy.Tests.Components.Shared;

public class BarListTests : BunitContext
{
    [Fact]
    public void Render_ScalesBarsToTheLargestRow()
    {
        var cut = Render<BarList>(p => p.Add(x => x.Rows, new[] { new StatRow("A", 10), new StatRow("B", 5) }));

        var fills = cut.FindAll(".bar-list-fill");
        Assert.Contains("width:100%", fills[0].GetAttribute("style"));
        Assert.Contains("width:50%", fills[1].GetAttribute("style"));
    }

    [Fact]
    public void Render_WithShowBytes_AppendsTheSize()
    {
        var cut = Render<BarList>(p => p
            .Add(x => x.Rows, new[] { new StatRow("A", 2, 2048) })
            .Add(x => x.ShowBytes, true));

        Assert.Contains("2 ·", cut.Find(".bar-list-value").TextContent);
    }

    [Fact]
    public void Render_WithNoRows_ShowsEmptyMessage()
    {
        var cut = Render<BarList>(p => p.Add(x => x.Rows, Array.Empty<StatRow>()));

        Assert.Empty(cut.FindAll(".bar-list"));
        Assert.Contains("No data", cut.Markup);
    }
}
