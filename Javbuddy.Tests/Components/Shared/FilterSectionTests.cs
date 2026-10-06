using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class FilterSectionTests : BunitContext
{
    [Fact]
    public void IsCollapsedByDefault_AndTheHeaderTogglesItsNestedContent()
    {
        var cut = Render<FilterSection>(p => p
            .Add(x => x.Label, "Actor")
            .Add(x => x.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<span class=\"inside\">nested</span>"))));

        Assert.Empty(cut.FindAll(".inside"));
        Assert.StartsWith("Actor", cut.Find("button.sort-dropdown-item").TextContent.Trim());
        Assert.Contains("▼", cut.Find("button.sort-dropdown-item").TextContent);

        cut.Find("button.sort-dropdown-item").Click();
        Assert.Single(cut.FindAll(".filter-section-body .inside"));
        Assert.Contains("▲", cut.Find("button.sort-dropdown-item").TextContent);

        cut.Find("button.sort-dropdown-item").Click();
        Assert.Empty(cut.FindAll(".inside"));
    }

    [Fact]
    public void ShowsHowManyFiltersInsideAreActive()
    {
        var cut = Render<FilterSection>(p => p.Add(x => x.Label, "Actor").Add(x => x.ActiveCount, 3));

        Assert.Contains("Actor (3)", cut.Find("button.sort-dropdown-item").TextContent);
        Assert.Contains("active", cut.Find("button.sort-dropdown-item").ClassList);

        var none = Render<FilterSection>(p => p.Add(x => x.Label, "Actor"));
        Assert.DoesNotContain("(", none.Find("button.sort-dropdown-item").TextContent);
    }
}
