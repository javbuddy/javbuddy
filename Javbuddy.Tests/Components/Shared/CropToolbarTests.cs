using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class CropToolbarTests : BunitContext
{
    private static readonly CropToolbar.RatioPreset[] Presets =
    [
        new("A", 0.5, "Half"),
        new("B", 2.0 / 3.0, "Two thirds"),
    ];

    private readonly List<double?> selected = [];
    private readonly List<bool> rotations = [];
    private int resets;

    private IRenderedComponent<CropToolbar> RenderToolbar(double? current = 0.5, bool straightenable = false, bool disabled = false) =>
        Render<CropToolbar>(p => p
            .Add(x => x.Ratios, Presets)
            .Add(x => x.CurrentRatio, current)
            .Add(x => x.RatioSelected, EventCallback.Factory.Create<double?>(this, r => selected.Add(r)))
            .Add(x => x.Straightenable, straightenable)
            .Add(x => x.OnRotate, EventCallback.Factory.Create<bool>(this, c => rotations.Add(c)))
            .Add(x => x.ZoomInputId, "z")
            .Add(x => x.StraightenInputId, "s")
            .Add(x => x.OnReset, EventCallback.Factory.Create(this, () => resets++))
            .Add(x => x.Disabled, disabled));

    [Fact]
    public void RendersThePresetsThenFree_WithTheirTitles_AndMarksTheActiveRatio()
    {
        var cut = RenderToolbar(current: 0.5);

        var buttons = cut.FindAll(".ratio-group .btn-tool");
        Assert.Equal(["A", "B", "Free"], buttons.Select(b => b.TextContent.Trim()));
        Assert.Equal("Two thirds", buttons[1].GetAttribute("title"));
        Assert.Equal([true, false, false], buttons.Select(b => b.ClassList.Contains("active")));
    }

    [Fact]
    public void FreeIsActive_WhenThereIsNoRatio_AndARatioMatchesWithinATolerance()
    {
        Assert.Contains("active", RenderToolbar(current: null).FindAll(".ratio-group .btn-tool").Last().ClassList);
        Assert.Contains("active", RenderToolbar(current: 2.0 / 3.0 + 0.004).FindAll(".ratio-group .btn-tool")[1].ClassList);
    }

    [Fact]
    public void ClickingAButton_RaisesItsRatio_OrNullForFree()
    {
        var cut = RenderToolbar();

        var buttons = cut.FindAll(".ratio-group .btn-tool");
        buttons[1].Click();
        buttons[2].Click();

        Assert.Equal([2.0 / 3.0, (double?)null], selected);
    }

    [Fact]
    public void WithoutStraighten_ThereAreNoRotateButtonsOrStraightenSlider()
    {
        var cut = RenderToolbar();

        Assert.Empty(cut.FindAll(".rotate-controls, .straighten-controls"));
        Assert.Equal("z", cut.Find(".zoom-slider").Id);
        Assert.Equal("1.0×", cut.Find(".zoom-controls .slider-value").TextContent);
    }

    [Fact]
    public void Straighten_AddsRotateButtonsAndTheSliderBeforeZoom_AndRaisesTheTurnDirection()
    {
        var cut = RenderToolbar(straightenable: true);

        var html = cut.Find(".cropper-toolbar").InnerHtml;
        Assert.True(html.IndexOf("rotate-controls", StringComparison.Ordinal) < html.IndexOf("straighten-controls", StringComparison.Ordinal));
        Assert.True(html.IndexOf("straighten-controls", StringComparison.Ordinal) < html.IndexOf("zoom-controls", StringComparison.Ordinal));
        Assert.Equal("s", cut.Find(".straighten-slider").Id);
        Assert.Equal("0.0°", cut.Find(".straighten-controls .slider-value").TextContent);

        cut.Find(".rotate-right-btn").Click();
        cut.Find(".rotate-left-btn").Click();
        Assert.Equal([true, false], rotations);
    }

    [Fact]
    public void Reset_IsADirectChildOfTheToolbar_AndRaisesTheCallback()
    {
        var cut = RenderToolbar();

        cut.FindAll(".cropper-toolbar > .btn-tool").Single(b => b.TextContent == "Reset").Click();

        Assert.Equal(1, resets);
    }

    [Fact]
    public void Disabled_LocksRotateStraightenAndReset_ButNotTheRatiosOrZoom()
    {
        var cut = RenderToolbar(straightenable: true, disabled: true);

        Assert.True(cut.Find(".rotate-left-btn").HasAttribute("disabled"));
        Assert.True(cut.Find(".rotate-right-btn").HasAttribute("disabled"));
        Assert.True(cut.Find(".straighten-slider").HasAttribute("disabled"));
        Assert.True(cut.FindAll(".cropper-toolbar > .btn-tool").Single().HasAttribute("disabled"));
        Assert.False(cut.Find(".zoom-slider").HasAttribute("disabled"));
        Assert.All(cut.FindAll(".ratio-group .btn-tool"), b => Assert.False(b.HasAttribute("disabled")));
    }
}
