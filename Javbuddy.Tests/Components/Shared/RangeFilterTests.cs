using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class RangeFilterTests : BunitContext
{
    [Fact]
    public void RendersLabelWithoutRange_WhenAtFullBounds()
    {
        var cut = Render<RangeFilter>(p => p
            .Add(x => x.BoundsMin, 143)
            .Add(x => x.BoundsMax, 171)
            .Add(x => x.SelectedMin, 143)
            .Add(x => x.SelectedMax, 171));

        var text = cut.Find("button.sort-dropdown-item").TextContent;
        Assert.Contains("Height", text);
        Assert.DoesNotContain("(", text);
    }

    [Fact]
    public void RendersSelectedRangeInLabel_WhenNarrowed()
    {
        var cut = Render<RangeFilter>(p => p
            .Add(x => x.BoundsMin, 143)
            .Add(x => x.BoundsMax, 171)
            .Add(x => x.SelectedMin, 150)
            .Add(x => x.SelectedMax, 165));

        Assert.Contains("Height (150–165 cm)", cut.Find("button.sort-dropdown-item").TextContent);
    }

    [Fact]
    public void CollapsedByDefault_DoesNotRenderSlider()
    {
        var cut = Render<RangeFilter>(p => p
            .Add(x => x.BoundsMin, 143)
            .Add(x => x.BoundsMax, 171)
            .Add(x => x.SelectedMin, 143)
            .Add(x => x.SelectedMax, 171));

        Assert.Empty(cut.FindAll(".range-slider-track"));
    }

    [Fact]
    public void ClickingHeader_ExpandsSliderWithTwoThumbs()
    {
        var cut = Render<RangeFilter>(p => p
            .Add(x => x.BoundsMin, 143)
            .Add(x => x.BoundsMax, 171)
            .Add(x => x.SelectedMin, 143)
            .Add(x => x.SelectedMax, 171));

        cut.Find("button.sort-dropdown-item").Click();

        Assert.Equal(2, cut.FindAll(".range-thumb-input").Count);
        Assert.Contains("143", cut.Find(".height-range-values").TextContent);
        Assert.Contains("171", cut.Find(".height-range-values").TextContent);
    }

    [Fact]
    public void ChangingMinThumb_CommitsClampedRange()
    {
        (int Min, int Max)? committed = null;
        var cut = Render<RangeFilter>(p => p
            .Add(x => x.BoundsMin, 143)
            .Add(x => x.BoundsMax, 171)
            .Add(x => x.SelectedMin, 143)
            .Add(x => x.SelectedMax, 171)
            .Add(x => x.OnRangeCommitted, EventCallback.Factory.Create<(int, int)>(this, r => committed = r)));

        cut.Find("button.sort-dropdown-item").Click();
        var minInput = cut.FindAll(".range-thumb-input")[0];
        minInput.Change("155");

        Assert.NotNull(committed);
        Assert.Equal(155, committed!.Value.Min);
        Assert.Equal(171, committed.Value.Max);
    }

    private IRenderedComponent<RangeFilter> RenderExpanded(int selectedMin, int selectedMax, Action<(int Min, int Max)> onCommit,
        string label = "Height", string unit = "cm")
    {
        var cut = Render<RangeFilter>(p => p
            .Add(x => x.Label, label).Add(x => x.Unit, unit)
            .Add(x => x.BoundsMin, 143)
            .Add(x => x.BoundsMax, 171)
            .Add(x => x.SelectedMin, selectedMin)
            .Add(x => x.SelectedMax, selectedMax)
            .Add(x => x.OnRangeCommitted, EventCallback.Factory.Create<(int, int)>(this, onCommit)));
        cut.Find("button.sort-dropdown-item").Click();
        return cut;
    }

    [Fact]
    public void ChangingMinThumb_OntoMaxThumb_CommitsSingleValue()
    {
        (int Min, int Max)? committed = null;
        var cut = RenderExpanded(143, 150, r => committed = r);

        cut.FindAll(".range-thumb-input")[0].Change("150");

        Assert.Equal((150, 150), committed);
    }

    [Fact]
    public void ChangingMaxThumb_OntoMinThumb_CommitsSingleValue()
    {
        (int Min, int Max)? committed = null;
        var cut = RenderExpanded(150, 171, r => committed = r);

        cut.FindAll(".range-thumb-input")[1].Change("150");

        Assert.Equal((150, 150), committed);
    }

    [Fact]
    public void ChangingMinThumb_PastMaxThumb_CommitsTheSpanBetweenTheThumbs()
    {
        (int Min, int Max)? committed = null;
        var cut = RenderExpanded(143, 150, r => committed = r);

        cut.FindAll(".range-thumb-input")[0].Change("160");

        Assert.Equal((150, 160), committed);
    }

    [Theory]
    [InlineData(143)] // both at the low end
    [InlineData(157)] // both mid-range
    [InlineData(171)] // both at the high end
    public void MeetingThumbs_CanBeDraggedApartInEitherDirection_WithEitherThumb(int value)
    {
        foreach (var thumb in new[] { 0, 1 })
        {
            foreach (var target in new[] { 143, 171 })
            {
                (int Min, int Max)? committed = null;
                var cut = RenderExpanded(value, value, r => committed = r);

                cut.FindAll(".range-thumb-input")[thumb].Change(target.ToString(System.Globalization.CultureInfo.InvariantCulture));

                Assert.Equal((Math.Min(value, target), Math.Max(value, target)), committed);
            }
        }
    }

    [Fact]
    public void DraggingThumbsTogether_ShowsSingleLiveValue()
    {
        var cut = RenderExpanded(143, 171, _ => { });
        var thumbs = cut.FindAll(".range-thumb-input");

        thumbs[0].Input("160");
        cut.FindAll(".range-thumb-input")[1].Input("160");

        Assert.Equal("160 cm", cut.Find(".height-range-values").TextContent.Trim());
    }

    [Fact]
    public void RendersSingleValueInLabel_WhenMinEqualsMax()
    {
        var age = Render<RangeFilter>(p => p
            .Add(x => x.Label, "Age").Add(x => x.Unit, "yrs")
            .Add(x => x.BoundsMin, 19).Add(x => x.BoundsMax, 40)
            .Add(x => x.SelectedMin, 25).Add(x => x.SelectedMax, 25));

        Assert.Contains("Age (25 yrs)", age.Find("button.sort-dropdown-item").TextContent);
        Assert.DoesNotContain("–", age.Find("button.sort-dropdown-item").TextContent);

        age.Find("button.sort-dropdown-item").Click();
        Assert.Equal("25 yrs", age.Find(".height-range-values").TextContent.Trim());
    }

    [Fact]
    public void UsesTheGivenLabelAndUnit()
    {
        var bust = Render<RangeFilter>(p => p
            .Add(x => x.Label, "Bust").Add(x => x.Unit, "cm")
            .Add(x => x.BoundsMin, 70).Add(x => x.BoundsMax, 100)
            .Add(x => x.SelectedMin, 80).Add(x => x.SelectedMax, 100));
        Assert.Contains("Bust (80–100 cm)", bust.Find("button.sort-dropdown-item").TextContent);

        var age = Render<RangeFilter>(p => p
            .Add(x => x.Label, "Age").Add(x => x.Unit, "yrs")
            .Add(x => x.BoundsMin, 19).Add(x => x.BoundsMax, 40)
            .Add(x => x.SelectedMin, 19).Add(x => x.SelectedMax, 30));
        Assert.Contains("Age (19–30 yrs)", age.Find("button.sort-dropdown-item").TextContent);
    }
}
