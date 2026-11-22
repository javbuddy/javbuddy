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
    public void AlwaysExpanded_ShowsTheOptionsWithNoHeader()
    {
        var cut = Render<MultiSelectFilterGroup>(p => p.Add(x => x.GroupLabel, "Features").Add(x => x.Options, [Option("VR", false)]).Add(x => x.AlwaysExpanded, true));

        Assert.Equal(["VR"], cut.FindAll("button").Select(b => b.TextContent.Trim()));
        cut.Find("button").Click();
        Assert.Equal(["VR"], clicked);
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

    private IRenderedComponent<MultiSelectFilterGroup> RenderWithActions(Action<ComponentParameterCollectionBuilder<MultiSelectFilterGroup>>? configure = null, IReadOnlyList<MultiSelectOption>? options = null) =>
        Render<MultiSelectFilterGroup>(p =>
        {
            p.Add(x => x.GroupLabel, "Actor tag").Add(x => x.Options, options ?? HairOptions()).Add(x => x.Searchable, true);
            configure?.Invoke(p);
        });

    [Fact]
    public void SelectAll_PassesTheLabelsVisibleUnderTheSearch()
    {
        IReadOnlyList<string>? received = null;
        var cut = RenderWithActions(p => p.Add(x => x.OnSelectAll, EventCallback.Factory.Create<IReadOnlyList<string>>(this, labels => received = labels)));
        cut.Find(".sort-dropdown-item").Click();

        cut.FindAll(".multiselect-action-link")[0].Click();
        Assert.Equal(["Hair", "Hair › Long", "Hair › Short", "Blonde"], received);

        cut.Find(".multiselect-search-input").Input("long");
        cut.FindAll(".multiselect-action-link")[0].Click();
        Assert.Equal(["Hair › Long"], received);
    }

    [Fact]
    public void None_ClearsTheWholeSelection_EvenWhileSearching()
    {
        var cleared = 0;
        var cut = RenderWithActions(p => p.Add(x => x.OnSelectNone, EventCallback.Factory.Create(this, () => cleared++)));
        cut.Find(".sort-dropdown-item").Click();
        cut.Find(".multiselect-search-input").Input("long");

        cut.FindAll(".multiselect-action-link")[1].Click();

        Assert.Equal(1, cleared);
        Assert.Equal("long", cut.Find(".multiselect-search-input").GetAttribute("value"));
    }

    [Fact]
    public void SearchWithNoMatch_ShowsTheEmptyMessage()
    {
        var cut = RenderWithActions();
        cut.Find(".sort-dropdown-item").Click();

        cut.Find(".multiselect-search-input").Input("zzz");

        Assert.Equal("No matches.", cut.Find(".multiselect-empty").TextContent.Trim());
        Assert.Empty(cut.FindAll(".multiselect-scroll-area .sort-dropdown-item"));
    }

    [Fact]
    public void MatchAll_ShowsOnlyWithTwoSelectedAndABoundCallback_AndReportsTheToggle()
    {
        static MultiSelectOption Selected(string label) => new(label, true, EventCallback.Empty);
        bool? changed = null;
        var matchAllChanged = EventCallback.Factory.Create<bool>(this, v => changed = v);

        var one = RenderWithActions(p => p.Add(x => x.OnMatchAllChanged, matchAllChanged), [Selected("A"), Option("B", false)]);
        one.Find(".sort-dropdown-item").Click();
        Assert.Empty(one.FindAll(".match-all-check"));

        var unbound = RenderWithActions(null, [Selected("A"), Selected("B")]);
        unbound.Find(".sort-dropdown-item").Click();
        Assert.Empty(unbound.FindAll(".match-all-check"));

        var two = RenderWithActions(p => p.Add(x => x.OnMatchAllChanged, matchAllChanged).Add(x => x.MatchAll, false), [Selected("A"), Selected("B")]);
        two.Find(".sort-dropdown-item").Click();
        two.Find(".match-all-check input").Change(true);
        Assert.True(changed);
    }

    [Fact]
    public void Header_ShowsTheActiveCount_ExcludedOptionsIncluded()
    {
        var cut = RenderWithActions(null, [new("A", true, EventCallback.Empty), new("B", false, EventCallback.Empty, IsExcluded: true), Option("C", false)]);

        Assert.Contains("Actor tag (2)", cut.Find(".sort-dropdown-item").TextContent);
        cut.Find(".sort-dropdown-item").Click();
        Assert.Contains("multiselect-excluded", cut.FindAll(".multiselect-scroll-area .sort-dropdown-item")[1].ClassName);
    }

    [Fact]
    public void Excludable_OnlyChangesTheHoverHintOfEachOption()
    {
        var options = new List<MultiSelectOption>
        {
            new("Selected", true, EventCallback.Empty),
            new("Excluded", false, EventCallback.Empty, IsExcluded: true),
            Option("Plain", false),
        };
        var cut = Render<MultiSelectFilterGroup>(p => p.Add(x => x.GroupLabel, "Genre").Add(x => x.Options, options).Add(x => x.AlwaysExpanded, true).Add(x => x.Excludable, true));

        var titles = cut.FindAll("button").Select(b => b.GetAttribute("title"));

        Assert.Equal(["Click to exclude", "Click to clear", "Click to include"], titles);

        var plain = Render<MultiSelectFilterGroup>(p => p.Add(x => x.GroupLabel, "Genre").Add(x => x.Options, options).Add(x => x.AlwaysExpanded, true));
        Assert.All(plain.FindAll("button"), b => Assert.Null(b.GetAttribute("title")));
    }
}
