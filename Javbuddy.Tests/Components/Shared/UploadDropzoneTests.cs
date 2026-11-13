using Bunit;
using Javbuddy.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Javbuddy.Tests.Components.Shared;

public class UploadDropzoneTests : BunitContext
{
    private IRenderedComponent<UploadDropzone> RenderZone(bool multiple = false, bool disabled = false, Action<InputFileChangeEventArgs>? onChange = null) =>
        Render<UploadDropzone>(p => p
            .Add(x => x.OnChange, EventCallback.Factory.Create<InputFileChangeEventArgs>(this, e => onChange?.Invoke(e)))
            .Add(x => x.Multiple, multiple)
            .Add(x => x.Disabled, disabled)
            .Add(x => x.ChildContent, (RenderFragment)(b => b.AddContent(0, "Drop it here"))));

    [Fact]
    public void RendersTheCallersTextAndABrowseButton_OverATransparentFileInput()
    {
        var cut = RenderZone();

        Assert.Contains("Drop it here", cut.Find(".upload-dropzone").TextContent);
        Assert.Equal("Browse…", cut.Find(".upload-dropzone-browse").TextContent.Trim());
        var input = cut.Find(".upload-dropzone input[type='file'].upload-dropzone-input");
        Assert.Contains(".png", input.GetAttribute("accept"));
        Assert.False(input.HasAttribute("multiple"));
        Assert.False(input.HasAttribute("disabled"));
    }

    [Fact]
    public void MultipleAndDisabled_ReachTheInput()
    {
        var cut = RenderZone(multiple: true, disabled: true);

        var input = cut.Find("input[type='file']");
        Assert.True(input.HasAttribute("multiple"));
        Assert.True(input.HasAttribute("disabled"));
        Assert.Contains("upload-dropzone-disabled", cut.Find(".upload-dropzone").ClassList);
    }

    [Fact]
    public void PickingAFile_RaisesOnChange()
    {
        InputFileChangeEventArgs? received = null;
        var cut = RenderZone(onChange: e => received = e);

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("x", "a.jpg"));

        Assert.NotNull(received);
        Assert.Equal("a.jpg", received!.File.Name);
    }
}
