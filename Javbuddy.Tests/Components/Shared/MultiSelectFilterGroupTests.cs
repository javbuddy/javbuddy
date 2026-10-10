using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class MultiSelectFilterGroupTests : BunitContext
{
    private readonly List<string> clicked = [];

    private IReadOnlyList<MultiSelectOption> HairOptions() =>
    [
        Option("Hair", false),
        Option("Hair › Long", true),
        Option("Hair › Short", true),
        Option("Blonde", false),
    ];

    private MultiSelectOption Option(string label, bool nested) =>
        new(label, false, EventCallback.Factory.Create(this, () => clicked.Add(label)), nested);

    private IRenderedComponent<MultiSelectFilterGroup> RenderGroup() =>
        Render<MultiSelectFilterGroup>(p => p.Add(x => x.GroupLabel, "Actor tag").Add(x => x.Options, HairOptions()).Add(x => x.Searchable, true));

    [Fact]
    public void Subtags_AreIndentedUnderTheirParent_ShowingOnlyTheirName_AndTheParentStaysSelectable()
    {
        var cut = RenderGroup();
        cut.Find(".sort-dropdown-item").Click();

        var items = cut.FindAll(".multiselect-scroll-area .sort-dropdown-item");
        Assert.Equal(["Hair", "Long", "Short", "Blonde"], items.Select(i => i.TextContent.Trim()));
        Assert.Equal([false, true, true, false], items.Select(i => i.ClassList.Contains("sort-dropdown-nested")));

        items[0].Click();
        Assert.Equal(["Hair"], clicked);
    }

    [Fact]
    public void WhileSearching_TheMatchesShowFlatWithTheirFullLabel()
    {
        var cut = RenderGroup();
        cut.Find(".sort-dropdown-item").Click();

        cut.Find(".multiselect-search-input").Input("long");

        var item = Assert.Single(cut.FindAll(".multiselect-scroll-area .sort-dropdown-item"));
        Assert.Equal("Hair › Long", item.TextContent.Trim());
        Assert.DoesNotContain("sort-dropdown-nested", item.ClassName);
    }
}
