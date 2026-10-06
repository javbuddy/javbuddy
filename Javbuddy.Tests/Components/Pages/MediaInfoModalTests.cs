using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Models;

namespace Javbuddy.Tests.Components.Pages;

public class MediaInfoModalTests : BunitContext
{
    [Fact]
    public void ModalClosedByDefault()
    {
        var movie = new Movie { Code = "ABC-123" };
        var cut = Render<MediaInfoModal>(p => p.Add(x => x.Movie, movie));

        Assert.Empty(cut.FindAll(".mediainfo-modal-backdrop"));
    }

    [Fact]
    public async Task OpenAsync_SingleFileOrNoFiles_RendersWithoutTabs()
    {
        var movie = new Movie
        {
            Code = "ABC-123",
            MediaScannedAt = DateTime.UtcNow,
            MediaVideoCodec = "AVC",
            MediaWidth = 1920,
            MediaHeight = 1080,
        };
        var cut = Render<MediaInfoModal>(p => p.Add(x => x.Movie, movie));

        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        Assert.NotEmpty(cut.FindAll(".mediainfo-modal-backdrop"));
        Assert.Empty(cut.FindAll(".mediainfo-version-tabs"));
        Assert.Contains("AVC", cut.Find("pre.mediainfo-full").TextContent);
    }

    [Fact]
    public async Task OpenAsync_MultipleFiles_RendersTabs_AndSwitchesActiveFileOnClick()
    {
        var movie = new Movie { Code = "ABC-123" };
        var file1 = new MovieFile
        {
            FileName = "ABC-123.mp4",
            VersionTag = "Original",
            IsPrimary = true,
            Width = 1920,
            Height = 1080,
            FrameRate = 29.97,
            VideoCodec = "AVC",
            MediaScannedAt = DateTime.UtcNow,
        };
        var file2 = new MovieFile
        {
            FileName = "ABC-123-RIFE-3.1.mkv",
            VersionTag = "RIFE-3.1",
            IsPrimary = false,
            Width = 3840,
            Height = 2160,
            FrameRate = 60,
            VideoCodec = "HEVC",
            MediaScannedAt = DateTime.UtcNow,
        };
        movie.MovieFiles.Add(file1);
        movie.MovieFiles.Add(file2);

        var cut = Render<MediaInfoModal>(p => p.Add(x => x.Movie, movie));

        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        var tabs = cut.FindAll(".mediainfo-version-tab");
        Assert.Equal(2, tabs.Count);

        // Initially, file1 is active because it is primary
        Assert.Contains("active", tabs[0].ClassName);
        Assert.Contains("Original", tabs[0].TextContent);
        Assert.Contains("AVC", cut.Find("pre.mediainfo-full").TextContent);

        // Click second tab (RIFE version)
        await cut.InvokeAsync(() => tabs[1].Click());

        tabs = cut.FindAll(".mediainfo-version-tab");
        Assert.Contains("active", tabs[1].ClassName);
        Assert.Contains("RIFE-3.1", tabs[1].TextContent);
        Assert.Contains("HEVC", cut.Find("pre.mediainfo-full").TextContent);
    }

    [Fact]
    public async Task Close_HidesModal()
    {
        var movie = new Movie { Code = "ABC-123" };
        var cut = Render<MediaInfoModal>(p => p.Add(x => x.Movie, movie));

        await cut.InvokeAsync(() => cut.Instance.OpenAsync());
        Assert.NotEmpty(cut.FindAll(".mediainfo-modal-backdrop"));

        var closeBtn = cut.Find(".mediainfo-modal-close-btn");
        await cut.InvokeAsync(() => closeBtn.Click());

        Assert.Empty(cut.FindAll(".mediainfo-modal-backdrop"));
    }

    [Fact]
    public async Task Tabs_ShowEachVersionsVrFormat()
    {
        var movie = new Movie { Code = "SIVR-059" };
        movie.MovieFiles.Add(new MovieFile { FileName = "SIVR-059.mp4", VersionTag = "Original", IsPrimary = true, VrType = "VR180 SBS", MediaScannedAt = DateTime.UtcNow });
        movie.MovieFiles.Add(new MovieFile { FileName = "SIVR-059-2D.mp4", VersionTag = "2D", MediaScannedAt = DateTime.UtcNow });
        var cut = Render<MediaInfoModal>(p => p.Add(x => x.Movie, movie));

        await cut.InvokeAsync(() => cut.Instance.OpenAsync());

        var tabs = cut.FindAll(".mediainfo-version-tab");
        Assert.Equal("VR180 SBS", tabs[0].QuerySelector(".version-badge-vr")!.TextContent);
        Assert.Null(tabs[1].QuerySelector(".version-badge-vr"));
        Assert.Empty(cut.FindAll("select"));
    }
}
