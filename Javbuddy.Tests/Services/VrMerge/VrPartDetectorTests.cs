using Javbuddy.Services.VrMerge;

namespace Javbuddy.Tests.Services.VrMerge;

public class VrPartDetectorTests
{
    // The real devr-041 release folder: 3 real
    // VR parts, a 19 MB spam mp4 with an unrelated Japanese filename, and 3 ad-shortcut .url files.
    private static readonly VrFileEntry Part1 = new(@"D:\scratch\torrent\javbuddy\devr-041\masex.tv@h_1711devr00041_1_8k.mp4", "masex.tv@h_1711devr00041_1_8k.mp4", 1_524_289_600);
    private static readonly VrFileEntry Part2 = new(@"D:\scratch\torrent\javbuddy\devr-041\masex.tv@h_1711devr00041_2_8k.mp4", "masex.tv@h_1711devr00041_2_8k.mp4", 2_532_052_096);
    private static readonly VrFileEntry Part3 = new(@"D:\scratch\torrent\javbuddy\devr-041\masex.tv@h_1711devr00041_3_8k.mp4", "masex.tv@h_1711devr00041_3_8k.mp4", 3_269_696_752);
    private static readonly VrFileEntry SpamFile = new(@"D:\scratch\torrent\javbuddy\devr-041\三上悠亚想要跟你决胜负.mp4", "三上悠亚想要跟你决胜负.mp4", 20_188_289);
    private static readonly VrFileEntry AdShortcut1 = new(@"D:\scratch\torrent\javbuddy\devr-041\ad1.url", "ad1.url", 176);
    private static readonly VrFileEntry AdShortcut2 = new(@"D:\scratch\torrent\javbuddy\devr-041\ad2.url", "ad2.url", 175);

    [Fact]
    public void DetectParts_RealDevr041Folder_RejectsSpamAndShortcuts_OrdersByPartNumber()
    {
        var files = new[] { Part2, SpamFile, Part3, AdShortcut1, Part1, AdShortcut2 };

        var result = VrPartDetector.DetectParts(files);

        Assert.Equal([Part1, Part2, Part3], result);
    }

    [Fact]
    public void DetectParts_FewerThanTwoVideoFiles_ReturnsEmpty()
    {
        var result = VrPartDetector.DetectParts([Part1, AdShortcut1, AdShortcut2]);

        Assert.Empty(result);
    }

    [Fact]
    public void DetectParts_UnrelatedVideoFilesWithNoCommonPrefix_ReturnsEmpty()
    {
        var unrelatedA = new VrFileEntry(@"C:\a\movie-one.mp4", "movie-one.mp4", 1_000_000_000);
        var unrelatedB = new VrFileEntry(@"C:\a\another-file.mp4", "another-file.mp4", 1_000_000_000);

        var result = VrPartDetector.DetectParts([unrelatedA, unrelatedB]);

        Assert.Empty(result);
    }

    [Fact]
    public void DetectParts_MixedExtensionsAtSimilarSize_KeepsOnlyDominantExtensionOfLargestFile()
    {
        var mp4Part1 = new VrFileEntry(@"C:\r\release_1.mp4", "release_1.mp4", 1_000_000_000);
        var mp4Part2 = new VrFileEntry(@"C:\r\release_2.mp4", "release_2.mp4", 1_100_000_000);
        var mkvStray = new VrFileEntry(@"C:\r\release_3.mkv", "release_3.mkv", 1_050_000_000);

        var result = VrPartDetector.DetectParts([mp4Part1, mkvStray, mp4Part2]);

        Assert.Equal([mp4Part1, mp4Part2], result);
    }

    [Theory]
    [InlineData(0, "DEVR-041-A")]
    [InlineData(1, "DEVR-041-B")]
    [InlineData(25, "DEVR-041-Z")]
    [InlineData(26, "DEVR-041-27")]
    [InlineData(30, "DEVR-041-31")]
    public void ChapterTitle_UsesLettersThenPartNumbers(int index, string expected)
    {
        Assert.Equal(expected, VrPartDetector.ChapterTitle("DEVR-041", index));
    }

    [Fact]
    public void BuildFfMetadata_ProducesCumulativeMicrosecondBoundaries()
    {
        var parts = new List<(string Title, long DurationMicroseconds)>
        {
            ("DEVR-041-A", 1_140_399_067),
            ("DEVR-041-B", 1_978_432_067),
            ("DEVR-041-C", 1_737_599_933),
        };

        var result = VrPartDetector.BuildFfMetadata(parts);

        Assert.Equal(
            ";FFMETADATA1\n" +
            "[CHAPTER]\nTIMEBASE=1/1000000\nSTART=0\nEND=1140399067\ntitle=DEVR-041-A\n" +
            "[CHAPTER]\nTIMEBASE=1/1000000\nSTART=1140399067\nEND=3118831134\ntitle=DEVR-041-B\n" +
            "[CHAPTER]\nTIMEBASE=1/1000000\nSTART=3118831134\nEND=4856431067\ntitle=DEVR-041-C\n",
            result);
    }

    [Fact]
    public void BuildFfMetadata_EscapesSpecialCharactersInTitle()
    {
        var parts = new List<(string Title, long DurationMicroseconds)> { ("A=B;C#D\\E", 1_000_000) };

        var result = VrPartDetector.BuildFfMetadata(parts);

        Assert.Contains(@"title=A\=B\;C\#D\\E", result);
    }

    [Fact]
    public void BuildConcatList_EscapesSingleQuotes()
    {
        var result = VrPartDetector.BuildConcatList([@"D:\it's a file.mp4", @"D:\plain.mp4"]);

        Assert.Equal(
            "file 'D:\\it'\\''s a file.mp4'\n" +
            "file 'D:\\plain.mp4'\n",
            result);
    }
}
