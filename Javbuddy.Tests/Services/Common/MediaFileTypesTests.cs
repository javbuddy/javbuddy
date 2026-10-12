using Javbuddy.Services.Common;

namespace Javbuddy.Tests.Services.Common;

public class MediaFileTypesTests
{
    [Theory]
    [InlineData("a.mkv", true)]
    [InlineData("A.M2TS", true)]
    [InlineData("/dl/x.iso", true)]
    [InlineData("a.txt", false)]
    [InlineData("noext", false)]
    [InlineData(null, false)]
    [InlineData(" ", false)]
    public void IsReleaseVideo_ClassifiesByExtension(string? name, bool expected) =>
        Assert.Equal(expected, MediaFileTypes.IsReleaseVideo(name));

    [Theory]
    [InlineData(".JPG", true)]
    [InlineData("poster.webp", true)]
    [InlineData(".gif", false)]
    [InlineData(null, false)]
    public void IsImage_AcceptsExtensionOrPath(string? value, bool expected) =>
        Assert.Equal(expected, MediaFileTypes.IsImage(value));
}
