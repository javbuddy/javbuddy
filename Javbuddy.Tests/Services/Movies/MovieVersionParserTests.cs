using Javbuddy.Models;
using Javbuddy.Services.Movies;

namespace Javbuddy.Tests.Services.Movies;

public class MovieVersionParserTests
{
    [Theory]
    [InlineData("MIDE-400.mkv", "MIDE-400", "Original")]
    [InlineData("mide-400.mp4", "MIDE-400", "Original")]
    [InlineData("MIDE-400-RIFE-3.1.webm", "MIDE-400", "RIFE-3.1")]
    [InlineData("MIDE-400 - 4K.mkv", "MIDE-400", "4K")]
    [InlineData("MIDE-400 [1080p].mp4", "MIDE-400", "1080p")]
    [InlineData("MIDE-400 [4K Remux].mkv", "MIDE-400", "4K Remux")]
    [InlineData("MIDE-400 (AI Upscale 60fps).mp4", "MIDE-400", "AI Upscale 60fps")]
    [InlineData("MIDE-400.Upscale.mp4", "MIDE-400", "Upscale")]
    [InlineData("MIDE-400_60fps.webm", "MIDE-400", "60fps")]
    [InlineData("MIDE400-RIFE-3.1.webm", "MIDE-400", "RIFE-3.1")]
    [InlineData("4K-Release.mkv", "MIDE-400", "4K-Release")]
    [InlineData("", "MIDE-400", "Original")]
    public void ExtractVersionTag_ExtractsExpectedTag(string fileName, string code, string expectedTag)
    {
        var result = MovieVersionParser.ExtractVersionTag(fileName, code);
        Assert.Equal(expectedTag, result);
    }

    [Fact]
    public void DeterminePrimaryFile_SelectsExactCodeMatchFirst()
    {
        var files = new List<MovieFile>
        {
            new() { FileName = "MIDE-400-RIFE-3.1.webm", VersionTag = "RIFE-3.1", Width = 1920, Height = 1080, FileSizeBytes = 8_000_000_000 },
            new() { FileName = "MIDE-400.mkv", VersionTag = "Original", Width = 1920, Height = 1080, FileSizeBytes = 4_000_000_000 },
        };

        var primary = MovieVersionParser.DeterminePrimaryFile(files, "MIDE-400");
        Assert.NotNull(primary);
        Assert.Equal("MIDE-400.mkv", primary.FileName);
    }

    [Fact]
    public void DeterminePrimaryFile_SelectsOriginalTagIfNoExactCodeMatch()
    {
        var files = new List<MovieFile>
        {
            new() { FileName = "MIDE-400-RIFE-3.1.webm", VersionTag = "RIFE-3.1", Width = 1920, Height = 1080, FileSizeBytes = 8_000_000_000 },
            new() { FileName = "MIDE-400-Original.mkv", VersionTag = "Original", Width = 1920, Height = 1080, FileSizeBytes = 4_000_000_000 },
        };

        var primary = MovieVersionParser.DeterminePrimaryFile(files, "MIDE-400");
        Assert.NotNull(primary);
        Assert.Equal("MIDE-400-Original.mkv", primary.FileName);
    }

    [Fact]
    public void DeterminePrimaryFile_SelectsHighestResolutionWhenNoOriginal()
    {
        var files = new List<MovieFile>
        {
            new() { FileName = "MIDE-400-1080p.mkv", VersionTag = "1080p", Width = 1920, Height = 1080, FileSizeBytes = 4_000_000_000 },
            new() { FileName = "MIDE-400-4K.mkv", VersionTag = "4K", Width = 3840, Height = 2160, FileSizeBytes = 12_000_000_000 },
        };

        var primary = MovieVersionParser.DeterminePrimaryFile(files, "MIDE-400");
        Assert.NotNull(primary);
        Assert.Equal("MIDE-400-4K.mkv", primary.FileName);
    }

    [Fact]
    public void DeterminePrimaryFile_KeepsTheVersionTheUserPinned()
    {
        var files = new List<MovieFile>
        {
            new() { FileName = "MIDE-400.mkv", VersionTag = "Original", Width = 1920, Height = 1080 },
            new() { FileName = "MIDE-400-RIFE-3.1.webm", VersionTag = "RIFE-3.1", Width = 1920, Height = 1080, IsPrimaryPinned = true },
        };

        Assert.Equal("MIDE-400-RIFE-3.1.webm", MovieVersionParser.DeterminePrimaryFile(files, "MIDE-400")?.FileName);
    }

    [Fact]
    public void FormatVersionLabel_JoinsTagResolutionFpsAndSize()
    {
        var file = new MovieFile { VersionTag = "RIFE-3.1", Width = 1920, Height = 1080, ScanType = "Progressive", FrameRate = 60, FileSizeBytes = 4_509_715_660 };

        Assert.Equal($"RIFE-3.1 · {file.ResolutionDisplay} · 60 fps · {file.FileSizeDisplay}", MovieVersionParser.FormatVersionLabel(file));
    }

    [Fact]
    public void FormatVersionLabel_LeavesOutWhatWasntProbed()
    {
        Assert.Equal("Original", MovieVersionParser.FormatVersionLabel(new MovieFile { VersionTag = "Original" }));
    }

    [Theory]
    [InlineData(60.0, "60 fps")]
    [InlineData(59.94, "59.94 fps")]
    [InlineData(29.97, "29.97 fps")]
    [InlineData(24.0, "24 fps")]
    [InlineData(0.0, null)]
    [InlineData(-1.0, null)]
    [InlineData(null, null)]
    public void FormatFps_FormatsExpected(double? input, string? expected)
    {
        var result = MovieVersionParser.FormatFps(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("SIVR-059.3d.hsbs.mp4", "Original")]
    [InlineData("SIVR-059_180_sbs.mp4", "Original")]
    [InlineData("SIVR-059-4K_180_sbs.mp4", "4K")]
    [InlineData("SIVR-059-RIFE-3.1.3d.hsbs.webm", "RIFE-3.1")]
    public void ExtractVersionTag_LeavesOutVrFormatTokens(string fileName, string expected) =>
        Assert.Equal(expected, MovieVersionParser.ExtractVersionTag(fileName, "SIVR-059"));
}
