using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class DetailSectionTests : BunitContext
{
    [Fact]
    public void RendersTitleAndBody_WithoutAButtonByDefault()
    {
        var cut = Render<DetailSection>(p => p
            .Add(x => x.Title, "Cast")
            .Add(x => x.Class, "extra")
            .AddChildContent("<p class=\"body\">hi</p>"));

        Assert.Equal("Cast", cut.Find(".detail-section-header h2").TextContent);
        Assert.Contains("extra", cut.Find(".detail-section").ClassList);
        Assert.NotNull(cut.Find(".detail-section > .body"));
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void ActionButton_ShowsItsLabelAndClasses_AndRaisesOnAction()
    {
        var clicks = 0;
        var cut = Render<DetailSection>(p => p
            .Add(x => x.Title, "Genres")
            .Add(x => x.ActionText, "Edit Tags")
            .Add(x => x.ActionClass, "mine")
            .Add(x => x.ActionTitle, "Do it")
            .Add(x => x.OnAction, EventCallback.Factory.Create(this, () => clicks++)));

        var button = cut.Find("button");
        Assert.Equal("Edit Tags", button.TextContent);
        Assert.Contains("detail-section-btn", button.ClassList);
        Assert.Contains("mine", button.ClassList);
        Assert.Equal("Do it", button.GetAttribute("title"));
        button.Click();
        Assert.Equal(1, clicks);
    }
}
