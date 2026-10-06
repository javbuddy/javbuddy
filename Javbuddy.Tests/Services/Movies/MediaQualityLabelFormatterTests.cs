using Javbuddy.Services.Movies;

namespace Javbuddy.Tests.Services.Movies;

public class MediaQualityLabelFormatterTests
{
    [Theory]
    [InlineData(1920, 1080, "Interlaced", "1080i")]
    [InlineData(720, 480, "MBAFF", "480i")]
    [InlineData(720, 576, "Mixed", "576i")]
    [InlineData(1920, 1080, "Progressive", "HD")]
    [InlineData(1920, 1080, " progressive ", "HD")]
    [InlineData(720, 480, null, "SD")]
    public void FormatPoster_UsesInterlacedHeightForNonProgressiveMedia(int width, int height, string? scanType, string expected)
    {
        Assert.Equal(expected, MediaQualityLabelFormatter.FormatPoster(width, height, scanType));
    }

    [Fact]
    public void FormatPoster_WithoutWidth_ReturnsNull()
    {
        Assert.Null(MediaQualityLabelFormatter.FormatPoster(null, 1080, "Interlaced"));
    }

    [Theory]
    [InlineData(1920, 1080, "Interlaced", "1080i")]
    [InlineData(1920, 1080, "Progressive", "1920x1080")]
    [InlineData(720, 480, null, "720x480")]
    public void FormatDetail_PreservesProgressiveDimensionsAndMarksNonProgressiveMedia(int width, int height, string? scanType, string expected)
    {
        Assert.Equal(expected, MediaQualityLabelFormatter.FormatDetail(width, height, scanType));
    }
}
