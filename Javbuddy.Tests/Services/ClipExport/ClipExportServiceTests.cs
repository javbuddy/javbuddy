using Javbuddy.Models;
using Javbuddy.Services.ClipExport;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.ClipExport;

public sealed class ClipExportServiceTests : IDisposable
{
    private const double MovieSeconds = 3600;

    private readonly TestDbContextFactory factory = new();
    private readonly string root = Path.Combine(Path.GetTempPath(), $"javbuddy-clip-export-{Guid.NewGuid():N}");
    private readonly string sourcePath;
    private readonly IFfmpegClient ffmpeg = Substitute.For<IFfmpegClient>();
    private readonly IApexPlaybackSettingsService apexPlayback = Substitute.For<IApexPlaybackSettingsService>();
    private readonly ClipExportDownloads downloads;
    private readonly ClipExportService service;
    private readonly int movieId;

    /// <summary>What ffmpeg was asked to copy: (start, duration).</summary>
    private (double Start, double Duration)? copied;

    /// <summary>Every muxer ffmpeg was asked for, in order.</summary>
    private readonly List<string> muxers = [];

    /// <summary>Muxers the fake ffmpeg fails with.</summary>
    private readonly HashSet<string> failingMuxers = [];

    /// <summary>How far before the requested start the fake copy begins (a keyframe that far back).</summary>
    private double keyframeLead;

    public ClipExportServiceTests()
    {
        var movieFolder = Path.Combine(root, "library", "ABC-123");
        Directory.CreateDirectory(movieFolder);
        sourcePath = Path.Combine(movieFolder, "ABC-123.mp4");
        File.WriteAllText(sourcePath, "source");

        var library = Substitute.For<ILocalLibraryClient>();
        library.ResolveMovieFolderPathAsync("ABC-123", Arg.Any<CancellationToken>()).Returns(movieFolder);

        var cache = Substitute.For<ILocalImageCache>();
        cache.RootPath.Returns(Path.Combine(root, "cache"));
        downloads = new ClipExportDownloads(cache);

        apexPlayback.GetEffectiveAsync(Arg.Any<CancellationToken>()).Returns(new ApexWindow(5, 5));
        SourceCodecs("h264", "aac");
        ffmpeg.ExportClipCopyAsync(sourcePath, Arg.Any<double>(), Arg.Any<double>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var muxer = call.ArgAt<string>(3);
                muxers.Add(muxer);
                File.WriteAllText(call.ArgAt<string>(4), "clip");
                if (failingMuxers.Contains(muxer)) return FfmpegRunResult.Failed($"ffmpeg exited with code 1: {muxer} can't hold that");
                copied = (call.ArgAt<double>(1), call.ArgAt<double>(2));
                return FfmpegRunResult.Ok();
            });
        ffmpeg.ProbeAsync(Arg.Is<string>(p => p != sourcePath), Arg.Any<CancellationToken>())
            .Returns(_ => new FfprobeMediaInfo { DurationSeconds = copied!.Value.Duration + keyframeLead });

        var binaries = Substitute.For<IFfmpegBinaryResolver>();
        binaries.IsAvailable.Returns(true);
        service = new ClipExportService(factory, library, ffmpeg, binaries, apexPlayback, downloads, NullLogger<ClipExportService>.Instance);

        using var db = factory.CreateDbContext();
        var movie = new Movie { Code = "ABC-123", MediaDurationSeconds = MovieSeconds };
        db.Movies.Add(movie);
        db.SaveChanges();
        movieId = movie.Id;
    }

    private void SourceCodecs(string video, string? audio) =>
        ffmpeg.ProbeAsync(sourcePath, Arg.Any<CancellationToken>())
            .Returns(new FfprobeMediaInfo { DurationSeconds = MovieSeconds, VideoCodec = video, AudioCodec = audio });

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private T Add<T>(T entity) where T : class
    {
        using var db = factory.CreateDbContext();
        db.Add(entity);
        db.SaveChanges();
        return entity;
    }

    [Fact]
    public async Task Scene_WithoutEnd_RunsToTheNextScene_AndIsNamedByPosition()
    {
        var first = Add(new Scene { MovieId = movieId, StartSeconds = 60 });
        Add(new Scene { MovieId = movieId, StartSeconds = 600 });

        var result = await service.PrepareAsync(ClipKind.Scene, first.Id);

        Assert.True(result.Success, result.Error);
        Assert.Equal((60d, 540d), copied);
        Assert.Equal("ABC-123 - Scene 1 [00-01-00 - 00-10-00].mp4", result.FileName);
    }

    [Fact]
    public async Task LastScene_WithoutEnd_RunsToTheMovieEnd_AndUsesItsTitle()
    {
        Add(new Scene { MovieId = movieId, StartSeconds = 0 });
        var last = Add(new Scene { MovieId = movieId, StartSeconds = 3000, Title = "Finale: bath" });

        var result = await service.PrepareAsync(ClipKind.Scene, last.Id);

        Assert.True(result.Success, result.Error);
        Assert.Equal((3000d, 600d), copied);
        Assert.Equal("ABC-123 - Finale_ bath [00-50-00 - 01-00-00].mp4", result.FileName);
    }

    [Fact]
    public async Task Highlight_UsesItsRange_AndPositionWhenUntitled()
    {
        Add(new MovieHighlight { MovieId = movieId, StartSeconds = 10, EndSeconds = 20 });
        var second = Add(new MovieHighlight { MovieId = movieId, StartSeconds = 100, EndSeconds = 130 });

        var result = await service.PrepareAsync(ClipKind.Highlight, second.Id);

        Assert.True(result.Success, result.Error);
        Assert.Equal((100d, 30d), copied);
        Assert.StartsWith("ABC-123 - Highlight 2 [", result.FileName);
    }

    [Fact]
    public async Task Apex_UsesTheDefaultWindow_WhenItHasNoOverrides()
    {
        var apex = Add(new MovieApex { MovieId = movieId, Seconds = 1000 });

        var result = await service.PrepareAsync(ClipKind.Apex, apex.Id);

        Assert.True(result.Success, result.Error);
        Assert.Equal((995d, 10d), copied);
        Assert.StartsWith("ABC-123 - Apex 1 [", result.FileName);
    }

    [Fact]
    public async Task Apex_UsesItsOwnWindow_AndIsCutAtTheMovieEnd()
    {
        var apex = Add(new MovieApex { MovieId = movieId, Seconds = MovieSeconds - 2, LeadInSeconds = 20, TailSeconds = 30 });

        var result = await service.PrepareAsync(ClipKind.Apex, apex.Id);

        Assert.True(result.Success, result.Error);
        Assert.Equal((MovieSeconds - 22, 22d), copied);
    }

    [Fact]
    public async Task ActualStart_IsReadBackFromTheOutputLength_AndNamesTheFile()
    {
        keyframeLead = 7.5;
        var highlight = Add(new MovieHighlight { MovieId = movieId, StartSeconds = 100, EndSeconds = 130 });

        var result = await service.PrepareAsync(ClipKind.Highlight, highlight.Id);

        Assert.Equal(92.5, result.ActualStartSeconds, 3);
        Assert.Equal(7.5, result.LeadSeconds, 3);
        Assert.Equal("ABC-123 - Highlight 1 [00-01-32 - 00-02-10].mp4", result.FileName);
    }

    [Fact]
    public async Task Success_HandsTheTempFileToASingleUseDownload()
    {
        var highlight = Add(new MovieHighlight { MovieId = movieId, StartSeconds = 100, EndSeconds = 130 });

        var result = await service.PrepareAsync(ClipKind.Highlight, highlight.Id);

        Assert.Equal($"/api/clip-exports/{result.Token}", result.DownloadUrl);
        var download = downloads.Take(result.Token!);
        Assert.NotNull(download);
        Assert.Equal(result.FileName, download.FileName);
        Assert.True(File.Exists(download.Path));
        Assert.StartsWith(downloads.Folder, download.Path);
    }

    [Theory]
    [InlineData("h264", "aac", "mp4", ".mp4", "video/mp4")]
    [InlineData("vp9", "opus", "webm", ".webm", "video/webm")]
    [InlineData("h264", "flac", "matroska", ".mkv", "video/x-matroska")]
    public async Task Container_FollowsTheSourceCodecs(string video, string audio, string muxer, string extension, string contentType)
    {
        SourceCodecs(video, audio);
        var highlight = Add(new MovieHighlight { MovieId = movieId, StartSeconds = 100, EndSeconds = 130 });

        var result = await service.PrepareAsync(ClipKind.Highlight, highlight.Id);

        Assert.Equal([muxer], muxers);
        Assert.EndsWith(extension, result.FileName);
        Assert.Equal(contentType, downloads.Take(result.Token!)!.ContentType);
    }

    [Fact]
    public async Task FailedMp4Copy_IsRetriedAsMatroska_AndLeavesOnlyTheGoodFile()
    {
        failingMuxers.Add("mp4");
        var highlight = Add(new MovieHighlight { MovieId = movieId, StartSeconds = 100, EndSeconds = 130 });

        var result = await service.PrepareAsync(ClipKind.Highlight, highlight.Id);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["mp4", "matroska"], muxers);
        Assert.EndsWith(".mkv", result.FileName);
        Assert.Single(Directory.EnumerateFiles(downloads.Folder));
    }

    [Fact]
    public async Task FfmpegFailure_ReportsTheError_AndLeavesNoTempFile()
    {
        failingMuxers.UnionWith(["mp4", "matroska"]);
        var highlight = Add(new MovieHighlight { MovieId = movieId, StartSeconds = 100, EndSeconds = 130 });

        var result = await service.PrepareAsync(ClipKind.Highlight, highlight.Id);

        Assert.False(result.Success);
        Assert.Equal("ffmpeg exited with code 1: matroska can't hold that", result.Error);
        Assert.Empty(Directory.EnumerateFiles(downloads.Folder));
    }

    [Fact]
    public async Task MissingClip_Fails()
    {
        var result = await service.PrepareAsync(ClipKind.Apex, 999);

        Assert.False(result.Success);
        Assert.Null(copied);
    }

    [Fact]
    public async Task MissingVideoFile_Fails_WithoutRunningFfmpeg()
    {
        File.Delete(sourcePath);
        var highlight = Add(new MovieHighlight { MovieId = movieId, StartSeconds = 100, EndSeconds = 130 });

        var result = await service.PrepareAsync(ClipKind.Highlight, highlight.Id);

        Assert.False(result.Success);
        Assert.Null(copied);
    }

    [Fact]
    public async Task UnexpectedError_IsAFailedExport_NotAnException()
    {
        ffmpeg.ProbeAsync(sourcePath, Arg.Any<CancellationToken>()).Returns<FfprobeMediaInfo?>(_ => throw new IOException("No space left on device"));
        var highlight = Add(new MovieHighlight { MovieId = movieId, StartSeconds = 100, EndSeconds = 130 });

        var result = await service.PrepareAsync(ClipKind.Highlight, highlight.Id);

        Assert.False(result.Success);
        Assert.Equal("IOException: No space left on device", result.Error);
    }

    [Fact]
    public async Task Cancellation_StopsTheCopy_AndLeavesNoTempFile()
    {
        using var cts = new CancellationTokenSource();
        ffmpeg.ExportClipCopyAsync(sourcePath, Arg.Any<double>(), Arg.Any<double>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<FfmpegRunResult>(call =>
            {
                File.WriteAllText(call.ArgAt<string>(4), "partial");
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });
        var highlight = Add(new MovieHighlight { MovieId = movieId, StartSeconds = 100, EndSeconds = 130 });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PrepareAsync(ClipKind.Highlight, highlight.Id, cts.Token));
        Assert.Empty(Directory.EnumerateFiles(downloads.Folder));
    }

    [Fact]
    public async Task DeletedScene_Fails()
    {
        var result = await service.PrepareAsync(ClipKind.Scene, 12345);

        Assert.False(result.Success);
        Assert.Null(copied);
    }
}
