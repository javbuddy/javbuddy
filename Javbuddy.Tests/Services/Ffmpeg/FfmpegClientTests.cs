using Javbuddy.Services.Ffmpeg;
using NSubstitute;

namespace Javbuddy.Tests.Services.Ffmpeg;

public class FfmpegClientTests
{
    [Fact]
    public void ParseProgressLine_RealStderrLine_ComputesPercentAgainstTotalDuration()
    {
        var line = "frame=15411 fps=2758 q=-1.0 size=  306432kB time=00:04:17.42 bitrate=9751.4kbits/s speed=46.1x";
        var totalDuration = TimeSpan.FromMinutes(80.933333); // ~4856.43s, the devr-041 combined duration

        var progress = FfmpegClient.ParseProgressLine(line, totalDuration);

        Assert.NotNull(progress);
        Assert.Equal(new TimeSpan(0, 0, 4, 17, 420), progress.Elapsed);
        Assert.InRange(progress.PercentComplete, 5.29, 5.31);
    }

    [Fact]
    public void ParseProgressLine_NoTimeField_ReturnsNull()
    {
        var line = "Stream mapping:\n  Stream #0:0 -> #0:0 (copy)";

        var progress = FfmpegClient.ParseProgressLine(line, TimeSpan.FromMinutes(10));

        Assert.Null(progress);
    }

    [Fact]
    public void ParseProgressLine_ZeroTotalDuration_ReturnsZeroPercentInsteadOfDividingByZero()
    {
        var line = "frame=1 time=00:00:01.00 bitrate=100kbits/s";

        var progress = FfmpegClient.ParseProgressLine(line, TimeSpan.Zero);

        Assert.NotNull(progress);
        Assert.Equal(0, progress.PercentComplete);
    }

    [Fact]
    public void ParseProgressLine_ElapsedPastTotalDuration_ClampsPercentTo100()
    {
        var line = "frame=1 time=00:10:00.00 bitrate=100kbits/s";

        var progress = FfmpegClient.ParseProgressLine(line, TimeSpan.FromMinutes(5));

        Assert.Equal(100, progress!.PercentComplete);
    }

    [Fact]
    public void ParseProbeOutput_MapsChaptersWithTitles()
    {
        // Shape as printed by ffprobe (checked against a real run) for a file muxed from an FFMETADATA chapter file (the VR
        // merge's output), plus a second file's uppercase TITLE and an untitled chapter.
        const string json = """
            {
                "chapters": [
                    { "id": 0, "time_base": "1/1000", "start": 0, "start_time": "0.000000", "end": 4000, "end_time": "4.000000", "tags": { "title": "DEVR-041-A" } },
                    { "id": 1, "time_base": "1/1000", "start": 4000, "start_time": "4.000000", "end": 7500, "end_time": "7.500000", "tags": { "TITLE": " Part B " } },
                    { "id": 2, "time_base": "1/1000", "start": 7500, "start_time": "7.500000", "end": 10000, "end_time": "10.000000" }
                ],
                "format": { "duration": "10.000000" }
            }
            """;

        var info = FfmpegClient.ParseProbeOutput(json);

        Assert.NotNull(info);
        Assert.Equal(3, info.ChapterCount);
        Assert.Equal(
            [new FfprobeChapterInfo(0, 4, "DEVR-041-A"), new FfprobeChapterInfo(4, 7.5, "Part B"), new FfprobeChapterInfo(7.5, 10, null)],
            info.Chapters);
        Assert.Equal(10, info.DurationSeconds);
    }

    [Fact]
    public void ParseProbeOutput_NoChaptersOrInvalidJson()
    {
        Assert.Empty(FfmpegClient.ParseProbeOutput("""{ "format": { "duration": "5" } }""")!.Chapters);
        Assert.Null(FfmpegClient.ParseProbeOutput("not json"));
    }

    [Fact]
    public void StillArguments_SeekBeforeInput_ScaleTo360pAsBt601_LibWebpQ85()
    {
        Assert.Equal(
            ["-hide_banner", "-nostdin", "-y", "-ss", "603.5", "-i", "in.mp4", "-frames:v", "1", "-an",
             "-vf", "scale=-2:360:flags=lanczos+accurate_rnd+full_chroma_int:out_color_matrix=bt601:out_range=tv",
             "-c:v", "libwebp", "-quality", "85", "-compression_level", "6", "out.webp"],
            FfmpegClient.BuildStillArguments("in.mp4", 603.5, leftEyeOnly: false, "out.webp"));
    }

    [Fact]
    public void PreviewVideoArguments_CropLeftEye_SourceFrameRate_Vp9Crf32_TaggedBt709()
    {
        Assert.Equal(
            ["-hide_banner", "-nostdin", "-y", "-ss", "603", "-t", "4", "-i", "in.mp4", "-an", "-sn", "-dn",
             "-vf", "crop=iw/2:ih:0:0,scale=-2:360:flags=lanczos+accurate_rnd+full_chroma_int:out_color_matrix=bt709:out_range=tv,"
                 + "setparams=colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv",
             "-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "32", "-deadline", "good", "-cpu-used", "2", "-row-mt", "1",
             "-pix_fmt", "yuv420p", "-f", "webm", "out.webm"],
            FfmpegClient.BuildPreviewVideoArguments("in.mp4", 603, 4, leftEyeOnly: true, "out.webm"));
    }

    [Fact]
    public void ChapterRemuxArguments_ReplaceChapters_DropOldChapterTrack_KeepTags()
    {
        Assert.Equal(
            ["-hide_banner", "-nostdin", "-y", "-i", "in.mp4", "-i", "meta.txt", "-map", "0", "-map", "-0:d?",
             "-map_metadata", "0", "-map_chapters", "1", "-c", "copy", "-movflags", "+faststart", "out.mp4"],
            FfmpegClient.BuildChapterRemuxArguments("in.mp4", "meta.txt", "out.mp4"));
        Assert.DoesNotContain("-movflags", FfmpegClient.BuildChapterRemuxArguments("in.mkv", "meta.txt", "out.mkv"));
    }

    [Fact]
    public void Dts2PtsRemuxArguments_KeepAllStreamsButData_FaststartOnlyForMp4Family()
    {
        // -map -0:d?: MP4 files can carry RTP hint tracks (codec "unknown" data streams), which the
        // MP4 muxer can't write, failing the whole repair with exit 234.
        Assert.Equal(
            ["-hide_banner", "-nostdin", "-y", "-fflags", "+genpts", "-i", "in.mp4", "-map", "0", "-map", "-0:d?",
             "-c", "copy", "-bsf:v", "dts2pts", "-avoid_negative_ts", "make_zero", "-movflags", "+faststart", "out.mp4"],
            FfmpegClient.BuildDts2PtsRemuxArguments("in.mp4", "out.mp4"));
        Assert.DoesNotContain("-movflags", FfmpegClient.BuildDts2PtsRemuxArguments("in.mkv", "out.mkv"));
    }

    [Fact]
    public void ParseSceneSignalLine_ReadsBlackdetectAndScdetReports()
    {
        // Lines as printed by ffmpeg 8.1.2.
        Assert.Equal(
            new FfmpegBlackStretch(0, 9.910178),
            FfmpegClient.ParseSceneSignalLine("[Parsed_blackdetect_1 @ 0x7f058000fa40] black_start:0 black_end:9.910178 black_duration:9.910178"));
        Assert.Equal(
            new FfmpegSceneCut(33.6336, 24.126),
            FfmpegClient.ParseSceneSignalLine("[Parsed_scdet_2 @ 0x7f085c00fcc0] lavfi.scd.score: 24.126, lavfi.scd.time: 33.6336"));
        Assert.Null(FfmpegClient.ParseSceneSignalLine("frame= 1016 fps=120 q=-0.0 size=N/A time=00:00:33.90 bitrate=N/A speed=3.99x"));
    }

    [Fact]
    public void SceneDetectionArguments_VideoOnly_Downscaled_BothFilters()
    {
        Assert.Equal(
            ["-hide_banner", "-nostdin", "-i", "in.mkv", "-an", "-sn", "-dn",
             "-vf", "scale=480:-2,blackdetect=d=0.3:pix_th=0.10,scdet=threshold=8", "-f", "null", "-"],
            FfmpegClient.BuildSceneDetectionArguments("in.mkv"));
    }

    [Fact]
    public async Task RemuxDts2PtsAsync_WhenFfmpegNotAvailable_ReturnsFailed()
    {
        var binaryResolver = Substitute.For<IFfmpegBinaryResolver>();
        binaryResolver.FfmpegPath.Returns((string?)null);
        var client = new FfmpegClient(binaryResolver);

        var result = await client.RemuxDts2PtsAsync("input.mp4", "output.mp4", TimeSpan.FromMinutes(1), null);

        Assert.False(result.Success);
        Assert.Contains("ffmpeg is not available", result.ErrorMessage);
    }

    [Fact]
    public void BuildTrickplayArguments_DecodesTheWholeVideo_IntoTiledWebpSheets()
    {
        var request = new FfmpegTrickplayRequest(10, 320, 180, 10, 10, LeftEyeOnly: false, KeyframeOnly: false);

        Assert.Equal(
            ["-hide_banner", "-nostdin", "-y", "-i", "in.mp4", "-an", "-sn", "-dn",
             "-vf", "fps=1/10,scale=320:180:flags=lanczos+accurate_rnd+full_chroma_int:out_color_matrix=bt601:out_range=tv,showinfo,tile=10x10", "-c:v", "libwebp", "-quality", "75",
             "-start_number", "0", Path.Combine("out", "%d.webp")],
            FfmpegClient.BuildTrickplayArguments("in.mp4", request, "out"));
    }

    [Fact]
    public void BuildTrickplayArguments_KeyframeOnlyAndVr_SkipNonKeyframes_AndCropToTheLeftEye()
    {
        var request = new FfmpegTrickplayRequest(10, 320, 320, 10, 10, LeftEyeOnly: true, KeyframeOnly: true);

        var arguments = FfmpegClient.BuildTrickplayArguments("in.mp4", request, "out");

        Assert.Equal(["-skip_frame", "nokey", "-i", "in.mp4"], arguments[3..7]);
        Assert.Equal("crop=iw/2:ih:0:0,fps=1/10,scale=320:320:flags=lanczos+accurate_rnd+full_chroma_int:out_color_matrix=bt601:out_range=tv,showinfo,tile=10x10", arguments[arguments.IndexOf("-vf") + 1]);
    }

    [Fact]
    public void BuildTrickplayArguments_Clip_SeeksToItsStart_AndStopsAfterItsLength()
    {
        var request = new FfmpegTrickplayRequest(1, 320, 180, 10, 10, LeftEyeOnly: false, KeyframeOnly: false, StartSeconds: 125.5, LengthSeconds: 30);

        var arguments = FfmpegClient.BuildTrickplayArguments("in.mp4", request, "out");

        Assert.Equal(["-ss", "125.5", "-t", "30", "-i", "in.mp4"], arguments[3..9]);
        Assert.StartsWith("fps=1/1,", arguments[arguments.IndexOf("-vf") + 1]);
    }

    [Fact]
    public void ParseShowinfoLine_RealStderrLine_ComputesPercentAgainstTotalDuration()
    {
        // Captured from ffmpeg 8.1 generating trickplay for a real 1080p file.
        var line = "[Parsed_showinfo_2 @ 0x7f7de800fdc0] n:  54 pts:     54 pts_time:540     duration:      1 duration_time:10      fmt:yuv420p cl:unspecified sar:1/1 s:320x180 i:P iskey:0 type:B checksum:6A6EC358";

        var progress = FfmpegClient.ParseShowinfoLine(line, TimeSpan.FromSeconds(7200));

        Assert.NotNull(progress);
        Assert.Equal(TimeSpan.FromSeconds(540), progress.Elapsed);
        Assert.Equal(7.5, progress.PercentComplete, 3);
    }

    [Theory]
    [InlineData("[Parsed_showinfo_2 @ 0x7f7de800fdc0] config in time_base: 10/1, frame_rate: 1/10")]
    [InlineData("[Parsed_showinfo_2 @ 0x7f7de800fdc0] color_range:tv color_space:bt470bg color_primaries:unknown color_trc:unknown")]
    [InlineData("frame=    1 fps=0.0 q=-0.0 Lsize=N/A time=00:16:40.00 bitrate=N/A speed=39.9x")]
    public void ParseShowinfoLine_OtherLines_ReturnNull(string line) =>
        Assert.Null(FfmpegClient.ParseShowinfoLine(line, TimeSpan.FromSeconds(7200)));

    [Fact]
    public async Task GenerateTrickplayAsync_WithoutFfmpeg_Fails()
    {
        var resolver = Substitute.For<IFfmpegBinaryResolver>();
        resolver.FfmpegPath.Returns((string?)null);

        var result = await new FfmpegClient(resolver).GenerateTrickplayAsync(
            "in.mp4", new FfmpegTrickplayRequest(10, 320, 180, 10, 10, false, false), "out", TimeSpan.FromMinutes(1), progress: null);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Runs_StartAtBelowNormalPriority()
    {
        if (OperatingSystem.IsWindows()) return;
        // A stand-in ffmpeg that reports its own niceness on stderr and fails, so the value lands in
        // the error message. The sleep gives the client time to lower the priority after starting it.
        var dir = Directory.CreateTempSubdirectory("javbuddy-ffmpeg-priority-test-").FullName;
        try
        {
            var script = Path.Combine(dir, "ffmpeg");
            await File.WriteAllTextAsync(script, "#!/bin/sh\nsleep 0.5\necho \"niceness $(nice)\" >&2\nexit 1\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var binaryResolver = Substitute.For<IFfmpegBinaryResolver>();
            binaryResolver.FfmpegPath.Returns(script);

            var result = await new FfmpegClient(binaryResolver).ExtractStillWebpAsync("in.mp4", 0, leftEyeOnly: false, "out.webp");

            var niceness = int.Parse(result.ErrorMessage!.Split(' ').Last());
            Assert.True(niceness >= 10, result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
