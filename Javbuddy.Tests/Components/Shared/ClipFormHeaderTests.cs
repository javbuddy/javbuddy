using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Tests.Components.Shared;

public class ClipFormHeaderTests : BunitContext
{
    [Fact]
    public void ShowsTheTitle_AndTheDownloadButtonWithTheHostsClass_WhichCallsOnDownload()
    {
        var clicks = 0;
        var cut = Render<ClipFormHeader>(p => p
            .Add(x => x.Title, "Edit scene 2")
            .Add(x => x.ClassPrefix, "scene-editor")
            .Add(x => x.OnDownload, EventCallback.Factory.Create(this, () => clicks++)));

        Assert.Equal("Edit scene 2", cut.Find(".clip-form-head-title").TextContent);
        cut.Find("button.scene-editor-download-btn").Click();
        Assert.Equal(1, clicks);
    }
}
