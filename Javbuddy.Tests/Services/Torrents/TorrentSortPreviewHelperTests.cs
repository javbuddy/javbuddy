using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Torrents;

namespace Javbuddy.Tests.Services.Torrents;

public class TorrentSortPreviewHelperTests
{
    [Fact]
    public void BuildFileTree_SingleFileMovie_ListsVideoNfoPosterFanart()
    {
        var preview = new OrganizePreviewResponseDto
        {
            FolderName = "START-591",
            FullPath = "/media/jav/START-591/START-591.mp4",
            VideoFiles = ["/media/jav/START-591/START-591.mp4"],
            NfoPath = "/media/jav/START-591/START-591.nfo",
            PosterPath = "/media/jav/START-591/poster.jpg",
            FanartPath = "/media/jav/START-591/fanart.jpg"
        };

        var entries = TorrentSortPreviewHelper.BuildFileTree(preview);

        Assert.Equal(
            ["START-591.mp4", "START-591.nfo", "poster.jpg", "fanart.jpg"],
            entries.Select(e => e.RelativePath));
        Assert.All(entries, e => Assert.False(e.IsNested));
    }

    [Fact]
    public void BuildFileTree_MultiPartMovie_ListsEveryVideoFileAndPerFileNfo()
    {
        var preview = new OrganizePreviewResponseDto
        {
            FolderName = "SIVR-505",
            VideoFiles = ["/media/vr/SIVR-505/SIVR-505-A.mp4", "/media/vr/SIVR-505/SIVR-505-B.mp4"],
            NfoPaths = ["/media/vr/SIVR-505/SIVR-505-A.nfo", "/media/vr/SIVR-505/SIVR-505-B.nfo"],
            NfoPath = "/media/vr/SIVR-505/SIVR-505.nfo" // deprecated singular — NfoPaths should win
        };

        var entries = TorrentSortPreviewHelper.BuildFileTree(preview);

        Assert.Equal(
            ["SIVR-505-A.mp4", "SIVR-505-B.mp4", "SIVR-505-A.nfo", "SIVR-505-B.nfo"],
            entries.Select(e => e.RelativePath));
    }

    [Fact]
    public void BuildFileTree_ScreenshotsNestUnderExtrafanart_WhenExtrafanartPathPresent()
    {
        var preview = new OrganizePreviewResponseDto
        {
            FolderName = "SIVR-505",
            VideoFiles = ["/media/vr/SIVR-505/SIVR-505.mp4"],
            ExtraFanartPath = "/media/vr/SIVR-505/extrafanart",
            Screenshots = ["fanart1.jpg", "fanart2.jpg"]
        };

        var entries = TorrentSortPreviewHelper.BuildFileTree(preview);

        var nested = entries.Where(e => e.IsNested).ToList();
        Assert.Equal(["fanart1.jpg", "fanart2.jpg"], nested.Select(e => e.RelativePath));
    }

    [Fact]
    public void BuildFileTree_ScreenshotsIgnored_WhenExtrafanartPathBlank()
    {
        var preview = new OrganizePreviewResponseDto
        {
            FolderName = "SIVR-505",
            VideoFiles = ["/media/vr/SIVR-505/SIVR-505.mp4"],
            ExtraFanartPath = "",
            Screenshots = ["fanart1.jpg"]
        };

        var entries = TorrentSortPreviewHelper.BuildFileTree(preview);

        Assert.DoesNotContain(entries, e => e.IsNested);
    }

    [Fact]
    public void BuildFileTree_BlankOptionalFields_AreSkippedGracefully()
    {
        var preview = new OrganizePreviewResponseDto
        {
            FolderName = "START-591",
            FullPath = "/media/jav/START-591/START-591.mp4",
            PosterPath = "",
            FanartPath = null,
            TrailerPath = null,
            ExtraFanartPath = null,
            Screenshots = null
        };

        var entries = TorrentSortPreviewHelper.BuildFileTree(preview);

        Assert.Equal(["START-591.mp4"], entries.Select(e => e.RelativePath));
    }

    [Fact]
    public void BuildFileTree_NoDataAtAll_ReturnsEmptyList()
    {
        var preview = new OrganizePreviewResponseDto { FolderName = "START-591" };

        var entries = TorrentSortPreviewHelper.BuildFileTree(preview);

        Assert.Empty(entries);
    }
}
