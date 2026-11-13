using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Models;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Components.Pages;

public class MediaInfoModalTests : BunitContext
{
    public MediaInfoModalTests() => JSInterop.AllowModalShell();

    [Fact]
    public void ModalClosedByDefault()
    {
        var movie = new Movie { Code = "ABC-123" };
        var cut = Render<MediaInfoModal>(p => p.Add(x => x.Movie, movie));

        Assert.Empty(cut.FindAll(".modal-shell-backdrop"));
    }

    [Fact]
    public async Task Show_SingleFileOrNoFiles_RendersWithoutTabs()
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

        cut.Render(p => p.Add(x => x.Show, true));

        Assert.NotEmpty(cut.FindAll(".modal-shell-backdrop"));
        Assert.Empty(cut.FindAll(".mediainfo-version-tabs"));
        Assert.Contains("AVC", cut.Find("pre.mediainfo-full").TextContent);
    }

    [Fact]
    public async Task Show_MultipleFiles_RendersTabs_AndSwitchesActiveFileOnClick()
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

        cut.Render(p => p.Add(x => x.Show, true));

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
    public async Task Close_AsksTheParentToClose_AndTheModalHidesWhenShowTurnsFalse()
    {
        var movie = new Movie { Code = "ABC-123" };
        var closeRequests = 0;
        var cut = Render<MediaInfoModal>(p => p.Add(x => x.Movie, movie).Add(x => x.OnClose, () => closeRequests++));

        cut.Render(p => p.Add(x => x.Show, true));
        Assert.NotEmpty(cut.FindAll(".modal-shell-backdrop"));

        var closeBtn = cut.Find(".modal-shell-close-btn");
        await cut.InvokeAsync(() => closeBtn.Click());

        Assert.Equal(1, closeRequests);
        cut.Render(p => p.Add(x => x.Show, false));
        Assert.Empty(cut.FindAll(".modal-shell-backdrop"));
    }

    [Fact]
    public void ReopeningStartsOnThePrimaryFileAgain()
    {
        var movie = new Movie { Code = "ABC-123" };
        movie.MovieFiles.Add(new MovieFile { FileName = "a.mp4", VersionTag = "A", MediaScannedAt = DateTime.UtcNow });
        movie.MovieFiles.Add(new MovieFile { FileName = "z.mp4", VersionTag = "Z", IsPrimary = true, MediaScannedAt = DateTime.UtcNow });
        var cut = Render<MediaInfoModal>(p => p.Add(x => x.Movie, movie).Add(x => x.Show, true));
        cut.FindAll(".mediainfo-version-tab")[1].Click();
        Assert.Contains("active", cut.FindAll(".mediainfo-version-tab")[1].ClassName);

        cut.Render(p => p.Add(x => x.Show, false));
        cut.Render(p => p.Add(x => x.Show, true));

        Assert.Contains("active", cut.FindAll(".mediainfo-version-tab")[0].ClassName);
    }

    [Fact]
    public async Task Tabs_ShowEachVersionsVrFormat()
    {
        var movie = new Movie { Code = "SIVR-059" };
        movie.MovieFiles.Add(new MovieFile { FileName = "SIVR-059.mp4", VersionTag = "Original", IsPrimary = true, VrType = "VR180 SBS", MediaScannedAt = DateTime.UtcNow });
        movie.MovieFiles.Add(new MovieFile { FileName = "SIVR-059-2D.mp4", VersionTag = "2D", MediaScannedAt = DateTime.UtcNow });
        var cut = Render<MediaInfoModal>(p => p.Add(x => x.Movie, movie));

        cut.Render(p => p.Add(x => x.Show, true));

        var tabs = cut.FindAll(".mediainfo-version-tab");
        Assert.Equal("VR180 SBS", tabs[0].QuerySelector(".version-badge-vr")!.TextContent);
        Assert.Null(tabs[1].QuerySelector(".version-badge-vr"));
        Assert.Empty(cut.FindAll("select"));
    }

    [Fact]
    public async Task Tabs_ListPrimaryFirstThenByFileName_AndPickUpFilesAddedByAParentRerender()
    {
        var movie = new Movie { Code = "ABC-123" };
        movie.MovieFiles.Add(new MovieFile { FileName = "b.mp4", VersionTag = "B", MediaScannedAt = DateTime.UtcNow });
        movie.MovieFiles.Add(new MovieFile { FileName = "a.mp4", VersionTag = "A", MediaScannedAt = DateTime.UtcNow });
        movie.MovieFiles.Add(new MovieFile { FileName = "z.mp4", VersionTag = "Z", IsPrimary = true, MediaScannedAt = DateTime.UtcNow });
        var cut = Render<MediaInfoModal>(p => p.Add(x => x.Movie, movie));

        cut.Render(p => p.Add(x => x.Show, true));
        Assert.Equal(["Z", "A", "B"], cut.FindAll(".version-tag").Select(e => e.TextContent));

        movie.MovieFiles.Add(new MovieFile { FileName = "c.mp4", VersionTag = "C", MediaScannedAt = DateTime.UtcNow });
        cut.Render(p => p.Add(x => x.Movie, movie));
        Assert.Equal(["Z", "A", "B", "C"], cut.FindAll(".version-tag").Select(e => e.TextContent));
    }
}
