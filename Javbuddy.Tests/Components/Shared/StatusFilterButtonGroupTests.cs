using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class StatusFilterButtonGroupTests : BunitContext
{
    [Fact]
    public void RendersOneButtonPerOption_WithActiveClassOnTheActiveOne()
    {
        var options = new[]
        {
            new FilterButtonOption("All (5)", true, EventCallback.Empty),
            new FilterButtonOption("Missing (2)", false, EventCallback.Empty),
            new FilterButtonOption("Got (3)", false, EventCallback.Empty),
        };

        var cut = Render<StatusFilterButtonGroup>(p => p.Add(x => x.Options, options));

        var buttons = cut.FindAll("button");
        Assert.Equal(3, buttons.Count);
        Assert.Contains("All (5)", buttons[0].TextContent);
        Assert.Contains("btn-secondary", buttons[0].GetAttribute("class"));
        Assert.Contains("btn-outline-secondary", buttons[1].GetAttribute("class"));
        Assert.Contains("btn-outline-secondary", buttons[2].GetAttribute("class"));
    }

    [Fact]
    public void ClickingAButton_InvokesItsCallback()
    {
        var clicked = false;
        var options = new[]
        {
            new FilterButtonOption("All", true, EventCallback.Factory.Create(this, () => clicked = true)),
        };

        var cut = Render<StatusFilterButtonGroup>(p => p.Add(x => x.Options, options));
        cut.Find("button").Click();

        Assert.True(clicked);
    }
}
