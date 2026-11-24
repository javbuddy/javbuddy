using Javbuddy.Services.ClipExport;

namespace Javbuddy.Tests.Services.ClipExport;

public class ClipContainerTests
{
    [Theory]
    [InlineData("h264", "aac", ".mp4")]
    [InlineData("hevc", "eac3", ".mp4")]
    [InlineData("h264", null, ".mp4")]
    [InlineData("vp9", "aac", ".mp4")]
    [InlineData("vp9", "opus", ".webm")]
    [InlineData("av1", "vorbis", ".webm")]
    [InlineData("vp9", null, ".webm")]
    [InlineData("h264", "flac", ".mkv")]
    [InlineData("h264", "pcm_s16le", ".mkv")]
    [InlineData("mpeg2video", "mp2", ".mkv")]
    [InlineData("wmv3", "wmav2", ".mkv")]
    [InlineData(null, "aac", ".mkv")]
    public void For_PicksTheContainerTheSourceCodecsFit(string? video, string? audio, string expectedExtension)
    {
        Assert.Equal(expectedExtension, ClipContainer.For(video, audio).Extension);
    }

    [Fact]
    public void Containers_CarryTheirMuxerAndContentType()
    {
        Assert.Equal(("mp4", "video/mp4"), (ClipContainer.Mp4.Muxer, ClipContainer.Mp4.ContentType));
        Assert.Equal(("webm", "video/webm"), (ClipContainer.WebM.Muxer, ClipContainer.WebM.ContentType));
        Assert.Equal(("matroska", "video/x-matroska"), (ClipContainer.Matroska.Muxer, ClipContainer.Matroska.ContentType));
    }
}
