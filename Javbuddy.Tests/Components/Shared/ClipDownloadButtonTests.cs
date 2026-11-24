using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class ClipDownloadButtonTests : BunitContext
{
    private IRenderedComponent<ClipDownloadButton> RenderButton(bool downloading = false, bool disabled = false, Action? clicked = null) =>
        Render<ClipDownloadButton>(p => p
            .Add(x => x.ClassPrefix, "highlight-editor")
            .Add(x => x.Downloading, downloading)
            .Add(x => x.Disabled, disabled)
            .Add(x => x.OnDownload, EventCallback.Factory.Create(this, () => clicked?.Invoke())));

    [Fact]
    public void Idle_IsAnEnabledButton_WithTheHostsClass_AndCallsOnDownload()
    {
        var clicks = 0;
        var cut = RenderButton(clicked: () => clicks++);

        var button = cut.Find("button.highlight-editor-download-btn");
        Assert.Equal("Download clip", button.GetAttribute("aria-label"));
        Assert.Equal("false", button.GetAttribute("aria-busy"));
        Assert.Empty(cut.FindAll(".clip-download-spinner"));
        button.Click();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Downloading_SpinsAndIsDisabled()
    {
        var button = RenderButton(downloading: true).Find("button");

        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal("true", button.GetAttribute("aria-busy"));
        Assert.NotNull(button.QuerySelector(".clip-download-spinner"));
    }

    [Fact]
    public void Disabled_WhileAnotherClipIsPrepared()
    {
        var button = RenderButton(disabled: true).Find("button");

        Assert.True(button.HasAttribute("disabled"));
        Assert.Null(button.QuerySelector(".clip-download-spinner"));
    }
}
