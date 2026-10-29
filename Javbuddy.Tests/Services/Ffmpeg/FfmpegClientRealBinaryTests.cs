using System.Diagnostics;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.VrMerge;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.Ffmpeg;

/// <summary>Runs <see cref="FfmpegClient"/> against the real ffmpeg/ffprobe on PATH, using clips
/// synthesized with lavfi in a temp dir (no fixture files). Catches an ffmpeg upgrade changing a
/// flag or output format that the argument-builder and parser unit tests can't see. Each test
/// returns early when the binaries aren't installed, same as the Windows guards elsewhere in the
/// suite; CI installs ffmpeg so they run there.
/// The client under test resolves its binaries like the app does, so Ffmpeg__Path / Ffmpeg__ProbePath
/// can point it at a specific build (CI points them at a build of the Dockerfile's pinned FFmpeg).
/// The test clips are always made with the ffmpeg on PATH: that build is stripped down to the
/// encoders Javbuddy needs and can't produce H.264/AAC.</summary>
[Trait("Category", "RealFfmpeg")]
public sealed class FfmpegClientRealBinaryTests(FfmpegClientRealBinaryTests.Clips clips) : IClassFixture<FfmpegClientRealBinaryTests.Clips>
{
    private readonly FfmpegClient client = new(clips.Resolver);

    public sealed class Clips : IDisposable
    {
        public IFfmpegBinaryResolver Resolver { get; } = new FfmpegBinaryResolver(new ConfigurationBuilder().AddEnvironmentVariables().Build());
        private IFfmpegBinaryResolver Generator { get; } = new FfmpegBinaryResolver(new ConfigurationBuilder().Build());
        public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("javbuddy-ffmpeg-real-").FullName;
        public bool Available => Resolver.IsAvailable && Generator.FfmpegPath is not null;

        /// <summary>2 s, 320x240 @ 25 fps, H.264 + stereo AAC.</summary>
        public string Plain { get; }

        /// <summary>3 s: 1 s test pattern, 1 s pure black, 1 s test pattern; no audio.</summary>
        public string WithBlack { get; }

        public Clips()
        {
            Plain = Path.Combine(Directory, "plain.mp4");
            WithBlack = Path.Combine(Directory, "with-black.mp4");
            if (!Available) return;

            Run("-f", "lavfi", "-i", "testsrc=d=2:s=320x240:r=25",
                "-f", "lavfi", "-i", "sine=d=2:r=44100",
                "-ac", "2", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", Plain);
            Run("-f", "lavfi", "-i", "testsrc=d=1:s=320x240:r=25",
                "-f", "lavfi", "-i", "color=c=black:d=1:s=320x240:r=25",
                "-f", "lavfi", "-i", "testsrc=d=1:s=320x240:r=25",
                "-filter_complex", "[0][1][2]concat=n=3:v=1:a=0",
                "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", WithBlack);
        }

        private void Run(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo(Generator.FfmpegPath!) { RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(arguments)) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo)!;
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException($"Could not synthesize test clip: {stderr}");
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private string OutputPath(string fileName) => Path.Combine(clips.Directory, $"{Guid.NewGuid():N}-{fileName}");

    [Fact]
    public async Task ProbeAsync_ReadsStreamShapeAndDuration()
    {
        if (!clips.Available) return;

        var info = await client.ProbeAsync(clips.Plain);

        Assert.NotNull(info);
        Assert.InRange(info.DurationSeconds, 1.9, 2.2);
        Assert.Equal("h264", info.VideoCodec);
        Assert.Equal(320, info.Width);
        Assert.Equal(240, info.Height);
        Assert.Equal(25, info.FrameRate);
        Assert.Equal("aac", info.AudioCodec);
        Assert.Equal(2, info.AudioChannels);
        Assert.Equal(0, info.ChapterCount);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsNull_ForMissingFile()
    {
        if (!clips.Available) return;

        Assert.Null(await client.ProbeAsync(Path.Combine(clips.Directory, "does-not-exist.mp4")));
    }

    [Fact]
    public async Task ExtractStillWebpAsync_WritesWebpImage()
    {
        if (!clips.Available) return;
        var output = OutputPath("still.webp");

        var result = await client.ExtractStillWebpAsync(clips.Plain, 1, leftEyeOnly: false, output);

        Assert.True(result.Success, result.ErrorMessage);
        var header = (await File.ReadAllBytesAsync(output)).Take(12).ToArray();
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(header, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(header, 8, 4));
    }

    [Fact]
    public async Task ExtractPreviewVideoAsync_WritesSilentVp9Webm()
    {
        if (!clips.Available) return;
        var output = OutputPath("preview.webm");

        var result = await client.ExtractPreviewVideoAsync(clips.Plain, 0, 1, leftEyeOnly: false, output);

        Assert.True(result.Success, result.ErrorMessage);
        var info = await client.ProbeAsync(output);
        Assert.NotNull(info);
        Assert.Equal("vp9", info.VideoCodec);
        Assert.Null(info.AudioCodec);
    }

    [Fact]
    public async Task ConcatWithChaptersAsync_JoinsPartsWithOneChapterEach()
    {
        if (!clips.Available) return;
        var output = OutputPath("merged.mp4");
        var micros = 2_000_000L;
        var request = new FfmpegConcatRequest(
            [new FfmpegConcatPart(clips.Plain, "Part 1", micros), new FfmpegConcatPart(clips.Plain, "Part 2", micros)],
            output);

        var result = await client.ConcatWithChaptersAsync(request, progress: null);

        Assert.True(result.Success, result.ErrorMessage);
        var info = await client.ProbeAsync(output);
        Assert.NotNull(info);
        Assert.InRange(info.DurationSeconds, 3.8, 4.4);
        Assert.Equal(["Part 1", "Part 2"], info.Chapters.Select(c => c.Title));
        Assert.InRange(info.Chapters[1].StartSeconds, 1.9, 2.1);
    }

    [Fact]
    public async Task RemuxWithChaptersAsync_ReplacesChaptersAndKeepsStreams()
    {
        if (!clips.Available) return;
        var output = OutputPath("chaptered.mp4");
        var metadata = VrPartDetector.BuildFfMetadata([("Intro", 1_000_000L), ("Main", 1_000_000L)]);

        var result = await client.RemuxWithChaptersAsync(clips.Plain, metadata, output, TimeSpan.FromSeconds(2), progress: null);

        Assert.True(result.Success, result.ErrorMessage);
        var info = await client.ProbeAsync(output);
        Assert.NotNull(info);
        Assert.Equal(["Intro", "Main"], info.Chapters.Select(c => c.Title));
        Assert.Equal("h264", info.VideoCodec);
        Assert.Equal("aac", info.AudioCodec);
    }

    [Fact]
    public async Task RemuxDts2PtsAsync_WritesPlayableCopy()
    {
        if (!clips.Available) return;
        var output = OutputPath("dts2pts.mp4");

        var result = await client.RemuxDts2PtsAsync(clips.Plain, output, TimeSpan.FromSeconds(2), progress: null);

        Assert.True(result.Success, result.ErrorMessage);
        var info = await client.ProbeAsync(output);
        Assert.NotNull(info);
        Assert.Equal("h264", info.VideoCodec);
        Assert.InRange(info.DurationSeconds, 1.9, 2.2);
    }

    [Fact]
    public async Task GenerateTrickplayAsync_WritesNumberedWebpSheets()
    {
        if (!clips.Available) return;
        var outputDirectory = Directory.CreateDirectory(OutputPath("trickplay")).FullName;
        var request = new FfmpegTrickplayRequest(IntervalSeconds: 1, ThumbnailWidth: 160, ThumbnailHeight: 120, TileWidth: 2, TileHeight: 1, LeftEyeOnly: false, KeyframeOnly: false);

        var result = await client.GenerateTrickplayAsync(clips.Plain, request, outputDirectory, TimeSpan.FromSeconds(2), progress: null);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "0.webp")));
    }

    [Fact]
    public async Task DetectSceneSignalsAsync_FindsBlackStretchAndCut()
    {
        if (!clips.Available) return;

        var signals = await client.DetectSceneSignalsAsync(clips.WithBlack, TimeSpan.FromSeconds(3), progress: null);

        Assert.NotNull(signals);
        var black = Assert.Single(signals.BlackStretches);
        Assert.InRange(black.StartSeconds, 0.9, 1.1);
        Assert.InRange(black.EndSeconds, 1.9, 2.1);
        Assert.Contains(signals.Cuts, c => Math.Abs(c.Seconds - 1) < 0.2 || Math.Abs(c.Seconds - 2) < 0.2);
    }
}
