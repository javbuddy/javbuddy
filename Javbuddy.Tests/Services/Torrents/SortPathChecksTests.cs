using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Torrents;

namespace Javbuddy.Tests.Services.Torrents;

public class SortPathChecksTests
{
    [Fact]
    public void FindOverlongSegments_FlagsFolderAndFileNamesOver255Utf8Bytes()
    {
        var longJapanese = new string('あ', 90); // 270 UTF-8 bytes in only 90 characters
        var preview = new OrganizePreviewResponseDto
        {
            FolderName = $"ABC-123 {longJapanese}",
            FileName = "ABC-123",
            VideoFiles = ["/out/ABC-123/ABC-123.mp4", $"/out/ABC-123/{longJapanese}.mp4"]
        };

        var overlong = SortPathChecks.FindOverlongSegments(preview);

        Assert.Equal(2, overlong.Count);
        Assert.Contains(preview.FolderName, overlong);
        Assert.Contains($"{longJapanese}.mp4", overlong);
    }

    [Fact]
    public void FindOverlongSegments_ShortNames_ReturnsEmpty()
    {
        var preview = new OrganizePreviewResponseDto { FolderName = new string('a', 255), FileName = "ABC-123" };

        Assert.Empty(SortPathChecks.FindOverlongSegments(preview));
    }
}
