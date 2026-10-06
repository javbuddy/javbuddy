using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class ChipMultiSelectGroupTests : BunitContext
{
    [Fact]
    public void RendersLabelWithoutCount_WhenNothingSelected()
    {
        var cut = Render<ChipMultiSelectGroup>(p => p
            .Add(x => x.GroupLabel, "Cup Size")
            .Add(x => x.Options, [new MultiSelectOption("A", false, default)]));

        var text = cut.Find("button.sort-dropdown-item").TextContent;
        Assert.Contains("Cup Size", text);
        Assert.DoesNotContain("(", text);
    }

    [Fact]
    public void RendersSelectedCountInLabel()
    {
        var cut = Render<ChipMultiSelectGroup>(p => p
            .Add(x => x.GroupLabel, "Cup Size")
            .Add(x => x.Options, [new MultiSelectOption("A", true, default), new MultiSelectOption("B", false, default)]));

        Assert.Contains("Cup Size (1)", cut.Find("button.sort-dropdown-item").TextContent);
    }

    [Fact]
    public void CollapsedByDefault_DoesNotRenderChips()
    {
        var cut = Render<ChipMultiSelectGroup>(p => p
            .Add(x => x.GroupLabel, "Cup Size")
            .Add(x => x.Options, [new MultiSelectOption("A", false, default)]));

        Assert.Empty(cut.FindAll(".chip-grid"));
    }

    [Fact]
    public void ClickingHeader_ExpandsChipGrid()
    {
        var cut = Render<ChipMultiSelectGroup>(p => p
            .Add(x => x.GroupLabel, "Cup Size")
            .Add(x => x.Options, [new MultiSelectOption("A", false, default), new MultiSelectOption("B", true, default)]));

        cut.Find("button.sort-dropdown-item").Click();

        var chips = cut.FindAll(".filter-chip");
        Assert.Equal(2, chips.Count);
        Assert.Contains("A", chips[0].TextContent.Trim());
        Assert.DoesNotContain("active", chips[0].GetAttribute("class"));
        Assert.Contains("active", chips[1].GetAttribute("class"));
    }

    [Fact]
    public void ClickingChip_InvokesOptionOnClick()
    {
        var clicked = false;
        var cut = Render<ChipMultiSelectGroup>(p => p
            .Add(x => x.GroupLabel, "Cup Size")
            .Add(x => x.Options, [new MultiSelectOption("A", false, EventCallback.Factory.Create(this, () => clicked = true))]));

        cut.Find("button.sort-dropdown-item").Click();
        cut.Find(".filter-chip").Click();

        Assert.True(clicked);
    }

    [Fact]
    public void ClickingAll_InvokesOnSelectAll()
    {
        var called = false;
        var cut = Render<ChipMultiSelectGroup>(p => p
            .Add(x => x.GroupLabel, "Cup Size")
            .Add(x => x.Options, [new MultiSelectOption("A", false, default)])
            .Add(x => x.OnSelectAll, EventCallback.Factory.Create(this, () => called = true)));

        cut.Find("button.sort-dropdown-item").Click();
        cut.Find(".multiselect-actions-row button:nth-child(1)").Click();

        Assert.True(called);
    }

    [Fact]
    public void ClickingClear_InvokesOnSelectNone()
    {
        var called = false;
        var cut = Render<ChipMultiSelectGroup>(p => p
            .Add(x => x.GroupLabel, "Cup Size")
            .Add(x => x.Options, [new MultiSelectOption("A", true, default)])
            .Add(x => x.OnSelectNone, EventCallback.Factory.Create(this, () => called = true)));

        cut.Find("button.sort-dropdown-item").Click();
        cut.Find(".multiselect-actions-row button:nth-child(2)").Click();

        Assert.True(called);
    }
}
