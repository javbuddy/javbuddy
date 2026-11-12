using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Images;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Javbuddy.Tests.Components.Shared;

public class ImageSourcePickerTests : BunitContext
{
    private readonly List<ImageSourcePicker.PickedImage> picked = [];
    private readonly List<string?> errors = [];
    private readonly List<bool> fetching = [];

    private IRenderedComponent<ImageSourcePicker> RenderPicker(Func<string, Task<ImageSourcePicker.FetchResult>>? fetch = null) =>
        Render<ImageSourcePicker>(p => p
            .Add(x => x.UrlInputId, "my-url")
            .Add(x => x.UrlPlaceholder, "https://example.com/x.jpg")
            .Add(x => x.FetchAsync, fetch ?? (_ => Task.FromResult(new ImageSourcePicker.FetchResult(null, null, null))))
            .Add(x => x.OnPicked, EventCallback.Factory.Create<ImageSourcePicker.PickedImage>(this, i => picked.Add(i)))
            .Add(x => x.OnError, EventCallback.Factory.Create<string?>(this, e => errors.Add(e)))
            .Add(x => x.OnFetchingChanged, EventCallback.Factory.Create<bool>(this, f => fetching.Add(f))));

    [Fact]
    public void RendersTheDropzoneAndTheUrlBox_WithTheCallersIdAndPlaceholder()
    {
        var cut = RenderPicker();

        Assert.Single(cut.FindAll(".upload-dropzone input[type='file']"));
        Assert.Equal("my-url", cut.Find("input[type='url']").Id);
        Assert.Equal("my-url", cut.Find("label").GetAttribute("for"));
        Assert.Equal("https://example.com/x.jpg", cut.Find("input[type='url']").GetAttribute("placeholder"));
        Assert.Contains("up to 20 MB", cut.Find(".upload-dropzone").TextContent);
        Assert.True(cut.Find("button.fetch-btn").HasAttribute("disabled"));
    }

    [Fact]
    public void ShowsTheConfiguredSizeLimit()
    {
        Services.AddSingleton(new ImageUploadSettings(MaxSizeMb: 7));

        var cut = RenderPicker();

        Assert.Contains("up to 7 MB", cut.Find(".upload-dropzone").TextContent);
    }

    [Fact]
    public async Task APickedFile_RaisesItsBytesAndContentType()
    {
        var cut = RenderPicker();

        await cut.InvokeAsync(() => cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "a.png", null, "image/png")));

        var image = Assert.Single(picked);
        Assert.Equal(new byte[] { 1, 2, 3 }, image.Bytes);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal<string?>([null], errors);
    }

    [Fact]
    public async Task APickedFileWithoutAContentType_IsTreatedAsJpeg()
    {
        var cut = RenderPicker();

        await cut.InvokeAsync(() => cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1], "a.jpg", null, "")));

        Assert.Equal("image/jpeg", Assert.Single(picked).ContentType);
    }

    [Fact]
    public async Task AnUnsupportedExtension_ReportsAnErrorAndRaisesNothing()
    {
        var cut = RenderPicker();

        await cut.InvokeAsync(() => cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1], "a.gif", null, "image/gif")));

        Assert.Empty(picked);
        Assert.Equal("Unsupported image format. Please select a JPEG, PNG, or WebP image.", errors.Last());
    }

    [Fact]
    public async Task AFileOverTheSizeLimit_ReportsAnErrorAndRaisesNothing()
    {
        Services.AddSingleton(new ImageUploadSettings(MaxSizeMb: 1));
        var cut = RenderPicker();

        await cut.InvokeAsync(() => cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[1024 * 1024 + 1], "big.png", null, "image/png")));

        Assert.Empty(picked);
        Assert.Equal("Image size exceeds 1 MB limit.", errors.Last());
    }

    [Fact]
    public async Task Fetch_PassesTheTypedUrl_RaisesTheImage_AndReportsBusyAroundIt()
    {
        string? requested = null;
        var cut = RenderPicker(url =>
        {
            requested = url;
            return Task.FromResult(new ImageSourcePicker.FetchResult([9, 8], "image/webp", null));
        });

        cut.Find("input[type='url']").Input("https://example.com/p.webp");
        await cut.Find("button.fetch-btn").ClickAsync(new());

        Assert.Equal("https://example.com/p.webp", requested);
        var image = Assert.Single(picked);
        Assert.Equal(new byte[] { 9, 8 }, image.Bytes);
        Assert.Equal("image/webp", image.ContentType);
        Assert.Equal([true, false], fetching);
    }

    [Fact]
    public async Task Fetch_WithoutAContentType_IsTreatedAsJpeg()
    {
        var cut = RenderPicker(_ => Task.FromResult(new ImageSourcePicker.FetchResult([1], null, null)));

        cut.Find("input[type='url']").Input("https://example.com/p");
        await cut.Find("button.fetch-btn").ClickAsync(new());

        Assert.Equal("image/jpeg", Assert.Single(picked).ContentType);
    }

    [Theory]
    [InlineData("Image not found (HTTP 404).", "Image not found (HTTP 404).")]
    [InlineData(null, "Failed to download image.")]
    public async Task Fetch_Failure_ReportsTheServiceMessageOrADefault(string? serviceMessage, string expected)
    {
        var cut = RenderPicker(_ => Task.FromResult(new ImageSourcePicker.FetchResult(null, null, serviceMessage)));

        cut.Find("input[type='url']").Input("https://example.com/missing.jpg");
        await cut.Find("button.fetch-btn").ClickAsync(new());

        Assert.Empty(picked);
        Assert.Equal(expected, errors.Last());
        Assert.Equal([true, false], fetching);
    }

    [Fact]
    public async Task Fetch_ThatThrows_ReportsTheExceptionAndReleasesTheBusyState()
    {
        var cut = RenderPicker(_ => throw new InvalidOperationException("boom"));

        cut.Find("input[type='url']").Input("https://example.com/x.jpg");
        await cut.Find("button.fetch-btn").ClickAsync(new());

        Assert.Equal("Error downloading image: boom", errors.Last());
        Assert.Equal([true, false], fetching);
        Assert.Equal("Fetch", cut.Find("button.fetch-btn").TextContent.Trim());
    }

    [Fact]
    public async Task EnterInTheUrlBox_Fetches_ButOnlyWhenThereIsAUrl()
    {
        var calls = 0;
        var cut = RenderPicker(_ =>
        {
            calls++;
            return Task.FromResult(new ImageSourcePicker.FetchResult([1], "image/png", null));
        });

        await cut.Find("input[type='url']").TriggerEventAsync("onkeydown", new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.Equal(0, calls);

        cut.Find("input[type='url']").Input("https://example.com/x.png");
        await cut.Find("input[type='url']").TriggerEventAsync("onkeydown", new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task WhileFetching_TheBoxAndButtonAreLocked_AndTheButtonSaysSo()
    {
        var release = new TaskCompletionSource<ImageSourcePicker.FetchResult>();
        var cut = RenderPicker(_ => release.Task);

        cut.Find("input[type='url']").Input("https://example.com/x.png");
        var click = cut.Find("button.fetch-btn").ClickAsync(new());

        Assert.True(cut.Find("input[type='url']").HasAttribute("disabled"));
        Assert.True(cut.Find("button.fetch-btn").HasAttribute("disabled"));
        Assert.Equal("Fetching…", cut.Find("button.fetch-btn").TextContent.Trim());

        release.SetResult(new ImageSourcePicker.FetchResult([1], "image/png", null));
        await click;
        Assert.False(cut.Find("input[type='url']").HasAttribute("disabled"));
    }
}
