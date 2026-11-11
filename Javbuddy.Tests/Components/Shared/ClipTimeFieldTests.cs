using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class ClipTimeFieldTests : BunitContext
{
    private IRenderedComponent<ClipTimeField> RenderField(Action<string>? changed = null, Action? now = null, bool fill = false, RenderFragment? extra = null) =>
        Render<ClipTimeField>(p =>
        {
            p.Add(x => x.Value, "1:30")
                .Add(x => x.ValueChanged, EventCallback.Factory.Create<string>(this, v => changed?.Invoke(v)))
                .Add(x => x.ClassPrefix, "scene-editor")
                .Add(x => x.InputClass, "scene-editor-start-input")
                .Add(x => x.NowClass, "scene-editor-now-btn scene-editor-start-now-btn")
                .Add(x => x.NowTitle, "Set it")
                .Add(x => x.OnNow, EventCallback.Factory.Create(this, () => now?.Invoke()))
                .Add(x => x.Fill, fill);
            if (extra is not null) p.Add(x => x.ChildContent, extra);
        });

    [Fact]
    public void KeepsTheHostsClassNames_AndShowsTheValue()
    {
        var cut = RenderField();

        var input = cut.Find("input.scene-editor-input.scene-editor-start-input");
        Assert.Equal("1:30", input.GetAttribute("value"));
        Assert.Equal("m:ss", input.GetAttribute("placeholder"));
        var now = cut.Find("button.scene-editor-btn.scene-editor-now-btn.scene-editor-start-now-btn");
        Assert.Equal("Set it", now.GetAttribute("title"));
        Assert.Equal("button", now.GetAttribute("type"));
        Assert.NotNull(cut.Find("span.scene-editor-time-row"));
    }

    [Fact]
    public void TypingRaisesValueChanged_OnEveryKeystroke()
    {
        var values = new List<string>();
        var cut = RenderField(values.Add);

        cut.Find("input").Input("2:0");
        cut.Find("input").Input("2:05");

        Assert.Equal(["2:0", "2:05"], values);
    }

    [Fact]
    public void NowButton_RaisesOnNow()
    {
        var clicks = 0;
        var cut = RenderField(now: () => clicks++);

        cut.Find("button").Click();

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void ExtraContent_FollowsTheNowButton()
    {
        var cut = RenderField(extra: b => b.AddMarkupContent(0, "<button class=\"extra\">×</button>"));

        Assert.Equal(["Now", "×"], cut.FindAll("button").Select(b => b.TextContent));
    }

    [Fact]
    public void Fill_AddsTheSharedRowClass_OnlyWhenSet()
    {
        Assert.Empty(RenderField().FindAll(".clip-time-row-fill"));
        Assert.Single(RenderField(fill: true).FindAll(".clip-time-row-fill"));
    }

    [Fact]
    public void PlaceholderAndAriaLabel_AreParameters()
    {
        var cut = Render<ClipTimeField>(p => p
            .Add(x => x.ClassPrefix, "apex-editor")
            .Add(x => x.Placeholder, "5")
            .Add(x => x.AriaLabel, "Tail seconds"));

        var input = cut.Find("input");
        Assert.Equal("5", input.GetAttribute("placeholder"));
        Assert.Equal("Tail seconds", input.GetAttribute("aria-label"));
    }
}
