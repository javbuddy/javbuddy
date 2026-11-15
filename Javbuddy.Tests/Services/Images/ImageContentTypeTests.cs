using Javbuddy.Services.Images;

namespace Javbuddy.Tests.Services.Images;

public class ImageContentTypeTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D }, "image/png")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38 }, "image/gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46 }, "image/webp")]
    [InlineData(new byte[] { }, "image/webp")]
    public void Sniff_ReadsTheFormatFromTheFirstBytes(byte[] bytes, string expected) =>
        Assert.Equal(expected, ImageContentType.Sniff(bytes));
}
