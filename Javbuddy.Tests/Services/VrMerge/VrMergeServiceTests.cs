using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.VrMerge;
using NSubstitute;

namespace Javbuddy.Tests.Services.VrMerge;

public class VrMergeServiceTests : IDisposable
{
    private readonly string tempFolder = Directory.CreateTempSubdirectory("javbuddy-vrmerge-test-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempFolder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string WritePart(string name, int sizeBytes = 1024)
    {
        var path = Path.Combine(tempFolder, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    private static VrMergeCandidatePart Part(string path, string name, long sizeBytes = 1024, double durationSeconds = 10, string chapterTitle = "X-A") =>
        new()
        {
            Path = path,
            Name = name,
            SizeBytes = sizeBytes,
            DurationSeconds = durationSeconds,
            ChapterTitle = chapterTitle,
        };

    [Fact]
    public async Task DetectAsync_FolderDoesNotExist_ReturnsNull()
    {
        var ffmpegClient = Substitute.For<IFfmpegClient>();
        var service = new VrMergeService(ffmpegClient);

        var result = await service.DetectAsync(Path.Combine(tempFolder, "does-not-exist"));

        Assert.Null(result);
    }

    [Fact]
    public async Task DetectAsync_FewerThanTwoParts_ReturnsNull()
    {
        WritePart("lone-file.mp4");

        var ffmpegClient = Substitute.For<IFfmpegClient>();
        var service = new VrMergeService(ffmpegClient);

        var result = await service.DetectAsync(tempFolder);

        Assert.Null(result);
    }

    [Fact]
    public async Task DetectAsync_TwoParts_ProbesEachAndFlagsCodecMismatch()
    {
        var p1 = WritePart("release_1.mp4", 1_000_000);
        var p2 = WritePart("release_2.mp4", 1_000_000);

        var ffmpegClient = Substitute.For<IFfmpegClient>();
        ffmpegClient.ProbeAsync(p1, Arg.Any<CancellationToken>())
            .Returns(new FfprobeMediaInfo { DurationSeconds = 100, VideoCodec = "hevc", Width = 1920, Height = 1080 });
        ffmpegClient.ProbeAsync(p2, Arg.Any<CancellationToken>())
            .Returns(new FfprobeMediaInfo { DurationSeconds = 100, VideoCodec = "h264", Width = 1920, Height = 1080 });

        var service = new VrMergeService(ffmpegClient);
        var result = await service.DetectAsync(tempFolder);

        Assert.NotNull(result);
        Assert.Equal(2, result.Parts.Count);
        Assert.Equal("X-A", VrPartDetector.ChapterTitle("X", 0));
        Assert.Contains(result.Warnings, w => w.Contains("different video codec"));
    }

    [Fact]
    public async Task MergeAsync_PartNoLongerExists_FailsPreflight()
    {
        var ffmpegClient = Substitute.For<IFfmpegClient>();
        var service = new VrMergeService(ffmpegClient);

        var request = new VrMergeRequest
        {
            FolderPath = tempFolder,
            CodeBase = "TEST-001",
            OrderedParts =
            [
                Part(Path.Combine(tempFolder, "missing_1.mp4"), "missing_1.mp4"),
                Part(Path.Combine(tempFolder, "missing_2.mp4"), "missing_2.mp4"),
            ],
        };

        var result = await service.MergeAsync(request, progress: null);

        Assert.False(result.Success);
        Assert.Contains("no longer exists", result.ErrorMessage);
        await ffmpegClient.DidNotReceive().ConcatWithChaptersAsync(Arg.Any<FfmpegConcatRequest>(), Arg.Any<IProgress<FfmpegProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MergeAsync_OutputAlreadyExists_FailsPreflight()
    {
        var p1 = WritePart("release_1.mp4");
        var p2 = WritePart("release_2.mp4");
        var folderName = Path.GetFileName(tempFolder);
        File.WriteAllText(Path.Combine(tempFolder, folderName + ".mp4"), "");

        var ffmpegClient = Substitute.For<IFfmpegClient>();
        var service = new VrMergeService(ffmpegClient);

        var request = new VrMergeRequest
        {
            FolderPath = tempFolder,
            CodeBase = "TEST-001",
            OrderedParts = [Part(p1, "release_1.mp4"), Part(p2, "release_2.mp4")],
        };

        var result = await service.MergeAsync(request, progress: null);

        Assert.False(result.Success);
        Assert.Contains("already exists", result.ErrorMessage);
    }

    [Fact]
    public async Task MergeAsync_FfmpegFails_DeletesPartialOutput()
    {
        var p1 = WritePart("release_1.mp4");
        var p2 = WritePart("release_2.mp4");
        var folderName = Path.GetFileName(tempFolder);
        var mergingPath = Path.Combine(tempFolder, folderName + ".merging.mp4");

        var ffmpegClient = Substitute.For<IFfmpegClient>();
        ffmpegClient.ConcatWithChaptersAsync(Arg.Any<FfmpegConcatRequest>(), Arg.Any<IProgress<FfmpegProgress>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                File.WriteAllBytes(mergingPath, [1, 2, 3]); // ffmpeg can leave a partial file behind on failure
                return Task.FromResult(FfmpegRunResult.Failed("ffmpeg exited with code 1."));
            });

        var service = new VrMergeService(ffmpegClient);
        var request = new VrMergeRequest
        {
            FolderPath = tempFolder,
            CodeBase = "TEST-001",
            OrderedParts = [Part(p1, "release_1.mp4"), Part(p2, "release_2.mp4")],
        };

        var result = await service.MergeAsync(request, progress: null);

        Assert.False(result.Success);
        Assert.False(File.Exists(mergingPath));
    }

    [Fact]
    public async Task MergeAsync_ChapterCountMismatchOnVerify_FailsAndDeletesPartial()
    {
        var p1 = WritePart("release_1.mp4");
        var p2 = WritePart("release_2.mp4");
        var folderName = Path.GetFileName(tempFolder);
        var mergingPath = Path.Combine(tempFolder, folderName + ".merging.mp4");

        var ffmpegClient = Substitute.For<IFfmpegClient>();
        ffmpegClient.ConcatWithChaptersAsync(Arg.Any<FfmpegConcatRequest>(), Arg.Any<IProgress<FfmpegProgress>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                File.WriteAllBytes(mergingPath, [1, 2, 3]);
                return Task.FromResult(FfmpegRunResult.Ok());
            });
        ffmpegClient.ProbeAsync(mergingPath, Arg.Any<CancellationToken>())
            .Returns(new FfprobeMediaInfo { ChapterCount = 1 }); // expected 2

        var service = new VrMergeService(ffmpegClient);
        var request = new VrMergeRequest
        {
            FolderPath = tempFolder,
            CodeBase = "TEST-001",
            OrderedParts = [Part(p1, "release_1.mp4"), Part(p2, "release_2.mp4")],
        };

        var result = await service.MergeAsync(request, progress: null);

        Assert.False(result.Success);
        Assert.Contains("verification", result.ErrorMessage);
        Assert.False(File.Exists(mergingPath));
    }

    [Fact]
    public async Task MergeAsync_Success_RenamesMergingFileToFinalOutput()
    {
        var p1 = WritePart("release_1.mp4");
        var p2 = WritePart("release_2.mp4");
        var folderName = Path.GetFileName(tempFolder);
        var mergingPath = Path.Combine(tempFolder, folderName + ".merging.mp4");
        var finalPath = Path.Combine(tempFolder, folderName + ".mp4");

        var ffmpegClient = Substitute.For<IFfmpegClient>();
        ffmpegClient.ConcatWithChaptersAsync(Arg.Any<FfmpegConcatRequest>(), Arg.Any<IProgress<FfmpegProgress>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                File.WriteAllBytes(mergingPath, [1, 2, 3]);
                return Task.FromResult(FfmpegRunResult.Ok());
            });
        ffmpegClient.ProbeAsync(mergingPath, Arg.Any<CancellationToken>())
            .Returns(new FfprobeMediaInfo { ChapterCount = 2 });

        var service = new VrMergeService(ffmpegClient);
        var request = new VrMergeRequest
        {
            FolderPath = tempFolder,
            CodeBase = "TEST-001",
            OrderedParts = [Part(p1, "release_1.mp4"), Part(p2, "release_2.mp4")],
        };

        var result = await service.MergeAsync(request, progress: null);

        Assert.True(result.Success);
        Assert.Equal(finalPath, result.MergedFilePath);
        Assert.True(File.Exists(finalPath));
        Assert.False(File.Exists(mergingPath));
    }
}
