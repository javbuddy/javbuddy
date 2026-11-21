using Javbuddy.Services.Metrics;

namespace Javbuddy.Tests.Services.Metrics;

public class LibraryMediaLabelsTests
{
    [Theory]
    [InlineData("AVC", "h264")]
    [InlineData(" avc ", "h264")]
    [InlineData("HEVC", "hevc")]
    [InlineData("AV1", "av1")]
    [InlineData("VP9", "vp9")]
    [InlineData("MPEG-4 Visual", "mpeg4")]
    [InlineData("MPEG Video", "mpeg2")]
    [InlineData("VC-1", "vc1")]
    [InlineData("WMV3", "wmv")]
    [InlineData("Windows Media Video", "wmv")]
    [InlineData("Some New Codec", "other")]
    [InlineData(null, "unknown")]
    [InlineData("  ", "unknown")]
    public void Codec_NormalizesToABoundedSet(string? input, string expected) =>
        Assert.Equal(expected, LibraryMediaLabels.Codec(input));

    [Theory]
    [InlineData("MPEG-4", "mp4")]
    [InlineData("Matroska", "mkv")]
    [InlineData("WebM", "webm")]
    [InlineData("AVI", "avi")]
    [InlineData("MPEG-TS", "ts")]
    [InlineData("Windows Media", "wmv")]
    [InlineData("Flash Video", "flv")]
    [InlineData("RealMedia", "other")]
    [InlineData(null, "unknown")]
    public void Container_NormalizesToABoundedSet(string? input, string expected) =>
        Assert.Equal(expected, LibraryMediaLabels.Container(input));

    [Theory]
    [InlineData(null, "unknown")]
    [InlineData(0, "unknown")]
    [InlineData(640, "sd")]
    [InlineData(1199, "sd")]
    [InlineData(1280, "720p")]
    [InlineData(1916, "1080p")]
    [InlineData(1920, "1080p")]
    [InlineData(2560, "1440p")]
    [InlineData(3840, "4k")]
    [InlineData(5760, "4k")]
    [InlineData(7680, "8k_plus")]
    [InlineData(11520, "8k_plus")]
    public void Resolution_BucketsByFrameWidth(int? width, string expected) =>
        Assert.Equal(expected, LibraryMediaLabels.Resolution(width));
}
