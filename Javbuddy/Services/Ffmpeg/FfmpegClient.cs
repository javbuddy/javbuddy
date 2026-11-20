using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Javbuddy.Services.Common;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Metrics;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Services.Ffmpeg;

public interface IFfmpegClient
{
    /// <summary>Probes one file with ffprobe (-show_format -show_streams as JSON) for its duration
    /// and video/audio stream shape. Returns null if ffprobe fails to open or parse the file.</summary>
    Task<FfprobeMediaInfo?> ProbeAsync(string path, CancellationToken ct = default);

    /// <summary>Concatenates the given parts via ffmpeg's concat demuxer with stream copy, writing
    /// an FFMETADATA chapter file so the output gets one chapter per source part. Writes the concat
    /// list and metadata file to the OS temp directory (not the source folder, which stays clean for
    /// javinizer-go's next scan) and deletes them afterward regardless of outcome.</summary>
    Task<FfmpegRunResult> ConcatWithChaptersAsync(FfmpegConcatRequest request, IProgress<FfmpegProgress>? progress, CancellationToken ct = default);

    /// <summary>Performs a lossless stream-copy remux with FFmpeg's dts2pts bitstream filter to
    /// reconstruct monotonic PTS timestamps from Picture Order Count (POC) and write CTTS offsets,
    /// fixing broken B-frame playback in web browsers.</summary>
    Task<FfmpegRunResult> RemuxDts2PtsAsync(string inputPath, string outputPath, TimeSpan totalDuration, IProgress<FfmpegProgress>? progress, CancellationToken ct = default);

    /// <summary>Writes one frame at <paramref name="seconds"/> as a 360p WebP still (a scene
    /// screenshot). leftEyeOnly crops a side-by-side VR frame to its left half.</summary>
    Task<FfmpegRunResult> ExtractStillWebpAsync(string inputPath, double seconds, bool leftEyeOnly, string outputPath, CancellationToken ct = default);

    /// <summary>Writes one full-resolution frame at <paramref name="seconds"/> as a high-quality WebP (the source for a
    /// generated poster or fanart, which a 360p still is too small for). leftEyeOnly crops a side-by-side VR frame to its left half. WebP rather than JPEG because the bundled
    /// ffmpeg is built with only the libwebp and libvpx_vp9 encoders (see the Dockerfile); the caller converts it.</summary>
    Task<FfmpegRunResult> ExtractFrameWebpAsync(string inputPath, double seconds, bool leftEyeOnly, string outputPath, CancellationToken ct = default);

    /// <summary>Writes a short, silent 360p VP9 WebM at the source frame rate (a scene hover preview, played muted
    /// and looping) starting at <paramref name="seconds"/>.</summary>
    Task<FfmpegRunResult> ExtractPreviewVideoAsync(string inputPath, double seconds, double durationSeconds, bool leftEyeOnly, string outputPath, CancellationToken ct = default);

    /// <summary>Stream-copies the input to outputPath with its chapters replaced by the ones in
    /// ffMetadata (FFMETADATA1 text), keeping every stream and the global tags.</summary>
    Task<FfmpegRunResult> RemuxWithChaptersAsync(string inputPath, string ffMetadata, string outputPath, TimeSpan totalDuration, IProgress<FfmpegProgress>? progress, CancellationToken ct = default);

    /// <summary>Decodes the whole file (downscaled) through blackdetect and scdet and returns every
    /// black stretch and hard cut they report; null when ffmpeg fails. Slow: a full
    /// decode, so callers run it as a background job. sink, when given, gets each signal as soon as
    /// ffmpeg reports it.</summary>
    Task<FfmpegSceneSignals?> DetectSceneSignalsAsync(string inputPath, TimeSpan totalDuration, IProgress<FfmpegProgress>? progress, IFfmpegSceneSignalSink? sink = null, CancellationToken ct = default);

    /// <summary>Writes the video's trickplay tile sheets as 0.webp, 1.webp, … into outputDirectory
    ///. Decodes the whole file (or its keyframes).</summary>
    Task<FfmpegRunResult> GenerateTrickplayAsync(string inputPath, FfmpegTrickplayRequest request, string outputDirectory, TimeSpan totalDuration, IProgress<FfmpegProgress>? progress, CancellationToken ct = default);
}

/// <summary>Every ffmpeg/ffprobe run goes through <see cref="RunInstrumentedAsync{T}"/>, which takes the gate slot (if any)
/// and records the run's metrics when <c>metrics</c> is given (it is optional so a client can be built without them).
/// A run that never started a process (binary missing, or cancelled while still queued on a gate) is not counted.</summary>
public partial class FfmpegClient(IFfmpegBinaryResolver binaryResolver, FfmpegGate? longGate = null, FfmpegGate? shortGate = null, JavbuddyMetrics? metrics = null) : IFfmpegClient
{
    private readonly FfmpegGate longGate = longGate ?? FfmpegGate.Long;
    private readonly FfmpegGate shortGate = shortGate ?? FfmpegGate.Short;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [GeneratedRegex(@"time=(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex TimeProgressPattern();

    public async Task<FfprobeMediaInfo?> ProbeAsync(string path, CancellationToken ct = default)
    {
        if (binaryResolver.FfprobePath is not { } ffprobePath) return null;

        // ffprobe is cheap and not gated, so scanning keeps going while a long decode holds the lane.
        return await RunInstrumentedAsync("probe", null, TimeSpan.Zero, RunIo.None, ct, scope => ProbeCoreAsync(scope, ffprobePath, path, ct), r => r is not null);
    }

    private static async Task<FfprobeMediaInfo?> ProbeCoreAsync(FfmpegRunScope scope, string ffprobePath, string path, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(ffprobePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // "error" rather than "quiet" so a failure's reason (moov atom not found, ...) reaches stderr for the failure metric; stdout is unaffected.
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-print_format");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("-show_format");
        startInfo.ArgumentList.Add("-show_streams");
        startInfo.ArgumentList.Add("-show_chapters");
        startInfo.ArgumentList.Add(path);

        using var process = new Process { StartInfo = startInfo };
        scope.StartAtLowPriority(process);
        // stderr is redirected, so it has to be read too: ffprobe blocks once the pipe buffer fills.
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        string stdout;
        try
        {
            stdout = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            scope.Observe(await stderrTask);
        }
        catch (OperationCanceledException)
        {
            scope.Kill(process);
            throw;
        }

        if (process.ExitCode != 0) return null;

        return ParseProbeOutput(stdout);
    }

    /// <summary>Maps ffprobe's JSON (-show_format -show_streams -show_chapters) onto
    /// FfprobeMediaInfo; null when the JSON can't be parsed.</summary>
    public static FfprobeMediaInfo? ParseProbeOutput(string json)
    {
        FfprobeOutput? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<FfprobeOutput>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (parsed is null) return null;

        var durationSeconds = double.TryParse(parsed.Format?.Duration, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
        var videoStream = parsed.Streams?.FirstOrDefault(s => s.CodecType == "video");
        var audioStream = parsed.Streams?.FirstOrDefault(s => s.CodecType == "audio");

        return new FfprobeMediaInfo
        {
            DurationSeconds = durationSeconds,
            VideoCodec = videoStream?.CodecName,
            Width = videoStream?.Width,
            Height = videoStream?.Height,
            FrameRate = ParseFrameRate(videoStream?.RFrameRate),
            AudioCodec = audioStream?.CodecName,
            AudioChannels = audioStream?.Channels,
            ChapterCount = parsed.Chapters?.Count ?? 0,
            Chapters = (parsed.Chapters ?? [])
                .Select(c => new FfprobeChapterInfo(
                    ParseSeconds(c.StartTime),
                    ParseSeconds(c.EndTime),
                    ChapterTitle(c.Tags)))
                .ToList(),
        };
    }

    // Tag keys keep the container's casing (MKV can carry TITLE), so match case-insensitively.
    private static string? ChapterTitle(Dictionary<string, string>? tags)
    {
        var title = tags?.FirstOrDefault(t => string.Equals(t.Key, "title", StringComparison.OrdinalIgnoreCase)).Value;
        return title.TrimToNull();
    }

    private static double ParseSeconds(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;

    public async Task<FfmpegRunResult> ConcatWithChaptersAsync(FfmpegConcatRequest request, IProgress<FfmpegProgress>? progress, CancellationToken ct = default)
    {
        if (binaryResolver.FfmpegPath is not { } ffmpegPath)
        {
            return FfmpegRunResult.Failed("ffmpeg is not available (see Settings → Connections → Path Mappings for how the merge step locates it).");
        }

        var totalDuration = TimeSpan.FromMicroseconds(request.Parts.Sum(p => p.DurationMicroseconds));
        return await RunInstrumentedAsync("concat", longGate, totalDuration, new RunIo([.. request.Parts.Select(p => p.AbsolutePath)], request.OutputPath), ct, scope => ConcatCoreAsync(scope, ffmpegPath, request, totalDuration, progress, ct), r => r.Success);
    }

    private static async Task<FfmpegRunResult> ConcatCoreAsync(FfmpegRunScope scope, string ffmpegPath, FfmpegConcatRequest request, TimeSpan totalDuration, IProgress<FfmpegProgress>? progress, CancellationToken ct)
    {
        var concatListPath = Path.Combine(Path.GetTempPath(), $"javbuddy-vr-merge-{Guid.NewGuid():N}.concat.txt");
        var metadataPath = Path.Combine(Path.GetTempPath(), $"javbuddy-vr-merge-{Guid.NewGuid():N}.metadata.txt");

        try
        {
            await File.WriteAllTextAsync(concatListPath, VrMerge.VrPartDetector.BuildConcatList(request.Parts.Select(p => p.AbsolutePath).ToList()), ct);
            await File.WriteAllTextAsync(metadataPath, VrMerge.VrPartDetector.BuildFfMetadata(request.Parts.Select(p => (p.ChapterTitle, p.DurationMicroseconds)).ToList()), ct);

            var startInfo = new ProcessStartInfo(ffmpegPath)
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[]
                     {
                         "-hide_banner", "-nostdin", "-y", "-fflags", "+genpts",
                         "-f", "concat", "-safe", "0", "-i", concatListPath,
                         "-i", metadataPath, "-c", "copy", "-map_metadata", "1",
                         "-avoid_negative_ts", "make_zero", request.OutputPath,
                     })
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = startInfo };
            scope.StartAtLowPriority(process);

            var stderrTask = ReadStderrForProgressAsync(scope, process, totalDuration, progress, ct);

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                scope.Kill(process);
                throw;
            }

            await stderrTask;

            return process.ExitCode == 0
                ? FfmpegRunResult.Ok()
                : FfmpegRunResult.Failed($"ffmpeg exited with code {process.ExitCode}.");
        }
        finally
        {
            BestEffortFile.TryDeleteFile(concatListPath);
            BestEffortFile.TryDeleteFile(metadataPath);
        }
    }

    public Task<FfmpegRunResult> ExtractStillWebpAsync(string inputPath, double seconds, bool leftEyeOnly, string outputPath, CancellationToken ct = default) =>
        RunAsync("still", inputPath, outputPath, BuildStillArguments(inputPath, seconds, leftEyeOnly, outputPath), ct);

    public Task<FfmpegRunResult> ExtractFrameWebpAsync(string inputPath, double seconds, bool leftEyeOnly, string outputPath, CancellationToken ct = default) =>
        RunAsync("still", inputPath, outputPath, BuildFrameWebpArguments(inputPath, seconds, leftEyeOnly, outputPath), ct);

    public static List<string> BuildFrameWebpArguments(string inputPath, double seconds, bool leftEyeOnly, string outputPath) =>
    [
        "-hide_banner", "-nostdin", "-y",
        "-ss", FormatSeconds(seconds), "-i", inputPath,
        "-frames:v", "1", "-an", "-sn",
        // Full size, but the same BT.601 conversion every other WebP here uses (see WebpScaleOptions): without it a
        // BT.709 source comes out with darker, pinker skin tones.
        "-vf", (leftEyeOnly ? "crop=iw/2:ih:0:0," : "") + $"scale=iw:ih:{WebpScaleOptions}",
        "-c:v", "libwebp", "-quality", "95", "-compression_level", "4",
        outputPath,
    ];

    public Task<FfmpegRunResult> ExtractPreviewVideoAsync(string inputPath, double seconds, double durationSeconds, bool leftEyeOnly, string outputPath, CancellationToken ct = default) =>
        RunAsync("preview", inputPath, outputPath, BuildPreviewVideoArguments(inputPath, seconds, durationSeconds, leftEyeOnly, outputPath), ct);

    public async Task<FfmpegRunResult> RemuxWithChaptersAsync(string inputPath, string ffMetadata, string outputPath, TimeSpan totalDuration, IProgress<FfmpegProgress>? progress, CancellationToken ct = default)
    {
        if (binaryResolver.FfmpegPath is not { } ffmpegPath)
        {
            return FfmpegRunResult.Failed("ffmpeg is not available.");
        }

        return await RunInstrumentedAsync("remux_chapters", longGate, totalDuration, new RunIo([inputPath], outputPath), ct, scope => RemuxChaptersCoreAsync(scope, ffmpegPath, inputPath, ffMetadata, outputPath, totalDuration, progress, ct), r => r.Success);
    }

    private static async Task<FfmpegRunResult> RemuxChaptersCoreAsync(FfmpegRunScope scope, string ffmpegPath, string inputPath, string ffMetadata, string outputPath, TimeSpan totalDuration, IProgress<FfmpegProgress>? progress, CancellationToken ct)
    {
        var metadataPath = Path.Combine(Path.GetTempPath(), $"javbuddy-chapters-{Guid.NewGuid():N}.metadata.txt");
        try
        {
            await File.WriteAllTextAsync(metadataPath, ffMetadata, ct);
            var startInfo = new ProcessStartInfo(ffmpegPath)
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in BuildChapterRemuxArguments(inputPath, metadataPath, outputPath))
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            scope.StartAtLowPriority(process);
            var stderrTask = ReadStderrForProgressAsync(scope, process, totalDuration, progress, ct);
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                scope.Kill(process);
                throw;
            }

            await stderrTask;
            return process.ExitCode == 0
                ? FfmpegRunResult.Ok()
                : FfmpegRunResult.Failed($"ffmpeg exited with code {process.ExitCode} while writing chapters.");
        }
        finally
        {
            BestEffortFile.TryDeleteFile(metadataPath);
        }
    }

    public async Task<FfmpegSceneSignals?> DetectSceneSignalsAsync(string inputPath, TimeSpan totalDuration, IProgress<FfmpegProgress>? progress, IFfmpegSceneSignalSink? sink = null, CancellationToken ct = default)
    {
        if (binaryResolver.FfmpegPath is not { } ffmpegPath) return null;

        return await RunInstrumentedAsync("scene_detect", longGate, totalDuration, new RunIo([inputPath]), ct, scope => DetectSceneSignalsCoreAsync(scope, ffmpegPath, inputPath, totalDuration, progress, sink, ct), r => r is not null);
    }

    private static async Task<FfmpegSceneSignals?> DetectSceneSignalsCoreAsync(FfmpegRunScope scope, string ffmpegPath, string inputPath, TimeSpan totalDuration, IProgress<FfmpegProgress>? progress, IFfmpegSceneSignalSink? sink, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in BuildSceneDetectionArguments(inputPath))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        scope.StartAtLowPriority(process);

        var blacks = new List<FfmpegBlackStretch>();
        var cuts = new List<FfmpegSceneCut>();
        try
        {
            // Stats lines end in \r, which ReadLineAsync also treats as a line break, so progress and
            // filter reports arrive as separate lines.
            string? line;
            while ((line = await process.StandardError.ReadLineAsync(ct)) is not null)
            {
                scope.Observe(line);
                switch (ParseSceneSignalLine(line))
                {
                    case FfmpegBlackStretch black:
                        blacks.Add(black);
                        if (sink is not null) await sink.OnBlackStretchAsync(black, ct);
                        break;
                    case FfmpegSceneCut cut:
                        cuts.Add(cut);
                        if (sink is not null) await sink.OnCutAsync(cut, ct);
                        break;
                    default:
                        if (ParseProgressLine(line, totalDuration) is { } p)
                        {
                            progress?.Report(p);
                            if (sink is not null) await sink.OnDecodedAsync(p.Elapsed.TotalSeconds, ct);
                        }
                        break;
                }
            }
            await process.WaitForExitAsync(ct);
        }
        catch
        {
            // Cancelled, or the sink failed: don't leave the decode running.
            scope.Kill(process);
            throw;
        }

        return process.ExitCode == 0 ? new FfmpegSceneSignals(blacks, cuts) : null;
    }

    /// <summary>Audio/subtitles skipped and the picture scaled to 480 px wide before the filters, which
    /// makes them cheap next to the decode itself (the dominating cost). blackdetect reports black
    /// stretches of at least 0.3 s (pixels under 10% luma); scdet reports cuts scoring 8+ — both
    /// deliberately permissive, SceneBoundaryDetector filters them.</summary>
    public static List<string> BuildSceneDetectionArguments(string inputPath) =>
    [
        "-hide_banner", "-nostdin",
        "-i", inputPath,
        "-an", "-sn", "-dn",
        "-vf", "scale=480:-2,blackdetect=d=0.3:pix_th=0.10,scdet=threshold=8",
        "-f", "null", "-",
    ];

    [GeneratedRegex(@"black_start:\s*([\d.]+)\s+black_end:\s*([\d.]+)")]
    private static partial Regex BlackDetectPattern();

    [GeneratedRegex(@"lavfi\.scd\.score:\s*([\d.]+),\s*lavfi\.scd\.time:\s*([\d.]+)")]
    private static partial Regex SceneCutPattern();

    /// <summary>A blackdetect or scdet report in one ffmpeg stderr line, or null.</summary>
    public static object? ParseSceneSignalLine(string line)
    {
        if (BlackDetectPattern().Match(line) is { Success: true } black)
        {
            return new FfmpegBlackStretch(
                double.Parse(black.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(black.Groups[2].Value, CultureInfo.InvariantCulture));
        }
        if (SceneCutPattern().Match(line) is { Success: true } cut)
        {
            return new FfmpegSceneCut(
                double.Parse(cut.Groups[2].Value, CultureInfo.InvariantCulture),
                double.Parse(cut.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        return null;
    }

    /// <summary>-map 0 keeps every stream but -map -0:d? drops data streams: an MP4's existing
    /// chapters also live in a QuickTime chapter data track, which would otherwise be copied along
    /// (duplicate chapters in MP4, and a hard failure writing Matroska). Chapters come from the
    /// metadata input (-map_chapters 1), global tags from the original (-map_metadata 0).</summary>
    public static List<string> BuildChapterRemuxArguments(string inputPath, string metadataPath, string outputPath)
    {
        var arguments = new List<string>
        {
            "-hide_banner", "-nostdin", "-y",
            "-i", inputPath, "-i", metadataPath,
            "-map", "0", "-map", "-0:d?",
            "-map_metadata", "0", "-map_chapters", "1",
            "-c", "copy",
        };
        if (SceneChapterPlan.IsMp4Family(outputPath))
        {
            arguments.AddRange(["-movflags", "+faststart"]);
        }
        arguments.Add(outputPath);
        return arguments;
    }

    // WebP (VP8) is always decoded as limited-range BT.601, but sources are usually BT.709, and ffmpeg
    // hands libwebp the YUV planes as-is — without out_color_matrix skin tones came out darker and
    // pinker. The input matrix comes from the source's colour tags (untagged is treated as BT.601).
    // Measured on scene media and trickplay sheets.
    private const string WebpScaleOptions = "flags=lanczos+accurate_rnd+full_chroma_int:out_color_matrix=bt601:out_range=tv";

    // A real video codec carries its colour tags in the stream, so the preview stays BT.709
    //. setparams tags every frame: -colorspace and friends on the encoder lose to an
    // untagged source's frame properties, leaving primaries and transfer "unknown" in the file.
    private const string VideoScaleOptions = "flags=lanczos+accurate_rnd+full_chroma_int:out_color_matrix=bt709:out_range=tv";
    private const string Bt709Tags = "setparams=colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv";

    // One thumbnail per interval (the fps filter takes the frame nearest each interval's middle),
    // scaled to the exact thumbnail size the manifest records, then packed into sheets. Verified on
    // a real 2 h 1080p file: full decode ~9 min on 4 cores, keyframe-only ~41 s.
    // showinfo logs each thumbnail's timestamp for progress: ffmpeg's own time= follows the output,
    // which only moves once a whole sheet (1000 s of video) is written. Measured on a
    // real 1080p file: identical sheets, no measurable slowdown.
    public static List<string> BuildTrickplayArguments(string inputPath, FfmpegTrickplayRequest request, string outputDirectory)
    {
        var arguments = new List<string> { "-hide_banner", "-nostdin", "-y" };
        if (request.KeyframeOnly)
        {
            arguments.AddRange(["-skip_frame", "nokey"]);
        }
        // A clip seeks on the input, which is frame-accurate when decoding and restarts the
        // timestamps at 0, so the fps filter samples the clip just as it does a whole video.
        if (request.StartSeconds is { } start)
        {
            arguments.AddRange(["-ss", start.ToString("0.###", CultureInfo.InvariantCulture)]);
        }
        if (request.LengthSeconds is { } length)
        {
            arguments.AddRange(["-t", length.ToString("0.###", CultureInfo.InvariantCulture)]);
        }
        var filters = new List<string>();
        if (request.LeftEyeOnly) filters.Add("crop=iw/2:ih:0:0");
        filters.Add(string.Create(CultureInfo.InvariantCulture, $"fps=1/{request.IntervalSeconds}"));
        filters.Add(string.Create(CultureInfo.InvariantCulture, $"scale={request.ThumbnailWidth}:{request.ThumbnailHeight}:{WebpScaleOptions}"));
        filters.Add("showinfo");
        filters.Add(string.Create(CultureInfo.InvariantCulture, $"tile={request.TileWidth}x{request.TileHeight}"));
        arguments.AddRange([
            "-i", inputPath,
            "-an", "-sn", "-dn",
            "-vf", string.Join(',', filters),
            "-c:v", "libwebp", "-quality", "75",
            "-start_number", "0",
            Path.Combine(outputDirectory, "%d.webp"),
        ]);
        return arguments;
    }

    public async Task<FfmpegRunResult> GenerateTrickplayAsync(string inputPath, FfmpegTrickplayRequest request, string outputDirectory, TimeSpan totalDuration, IProgress<FfmpegProgress>? progress, CancellationToken ct = default)
    {
        if (binaryResolver.FfmpegPath is not { } ffmpegPath)
        {
            return FfmpegRunResult.Failed("ffmpeg is not available.");
        }

        // A highlight's clip is a few seconds of video, so it goes in the short lane rather than waiting behind a full decode.
        var gate = request.StartSeconds is null ? longGate : shortGate;
        // A clip only covers LengthSeconds of the video, so that is the media it processes.
        var inputDuration = request.LengthSeconds is { } length ? TimeSpan.FromSeconds(length) : totalDuration;
        return await RunInstrumentedAsync("trickplay", gate, inputDuration, new RunIo([inputPath], OutputDirectory: outputDirectory), ct, scope => GenerateTrickplayCoreAsync(scope, ffmpegPath, inputPath, request, outputDirectory, totalDuration, progress, ct), r => r.Success);
    }

    private static async Task<FfmpegRunResult> GenerateTrickplayCoreAsync(FfmpegRunScope scope, string ffmpegPath, string inputPath, FfmpegTrickplayRequest request, string outputDirectory, TimeSpan totalDuration, IProgress<FfmpegProgress>? progress, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in BuildTrickplayArguments(inputPath, request, outputDirectory))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        scope.StartAtLowPriority(process);
        var stderrTask = ReadStderrForProgressAsync(scope, process, totalDuration, progress, ct, ParseShowinfoLine);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            scope.Kill(process);
            throw;
        }
        await stderrTask;

        return process.ExitCode == 0
            ? FfmpegRunResult.Ok()
            : FfmpegRunResult.Failed($"ffmpeg exited with code {process.ExitCode}.");
    }

    /// <summary>Parses one showinfo filter line (e.g. "[Parsed_showinfo_2 @ 0x…] n:  54 pts:     54
    /// pts_time:540 duration: …") into a percent-complete against the known total duration, like
    /// ParseProgressLine. Returns null for any other line.</summary>
    public static FfmpegProgress? ParseShowinfoLine(string line, TimeSpan totalDuration)
    {
        var match = ShowinfoTimePattern().Match(line);
        if (!match.Success) return null;

        var elapsed = TimeSpan.FromSeconds(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        var percent = totalDuration > TimeSpan.Zero
            ? Math.Clamp(elapsed.TotalSeconds / totalDuration.TotalSeconds * 100.0, 0, 100)
            : 0;
        return new FfmpegProgress(percent, elapsed);
    }

    [GeneratedRegex(@"^\[Parsed_showinfo_\d+ @ [^\]]+\] n:\s*\d+ pts:\s*-?\d+ pts_time:(\d+(?:\.\d+)?)")]
    private static partial Regex ShowinfoTimePattern();

    // Input seeking (-ss before -i) jumps straight to the nearest keyframe instead of decoding from
    // the start — essential on multi-GB 8K VR files over a network share.
    public static List<string> BuildStillArguments(string inputPath, double seconds, bool leftEyeOnly, string outputPath) =>
    [
        "-hide_banner", "-nostdin", "-y",
        "-ss", FormatSeconds(seconds), "-i", inputPath,
        "-frames:v", "1", "-an",
        "-vf", SceneVideoFilter(leftEyeOnly, fps: null),
        "-c:v", "libwebp", "-quality", "85", "-compression_level", "6",
        outputPath,
    ];

    // VP9 over animated WebP: motion compensation makes it ~120 KB at 10 fps instead of ~408 KB
    // at higher quality. Picked over H.264 from a comparison on five real 1080p clips (VP9 crf 32:
    // 42.2 dB / SSIM 0.979; H.264 crf 26 at the same SSIM was ~10% larger). -b:v 0 makes crf
    // constant-quality; row-mt lets the 360p frame use more than one thread. The source frame rate
    // (not the WebP's 10 fps) made it ~209 KB instead of ~119 KB on ~30 fps clips.
    public static List<string> BuildPreviewVideoArguments(string inputPath, double seconds, double durationSeconds, bool leftEyeOnly, string outputPath) =>
    [
        "-hide_banner", "-nostdin", "-y",
        "-ss", FormatSeconds(seconds), "-t", FormatSeconds(durationSeconds), "-i", inputPath,
        "-an", "-sn", "-dn",
        "-vf", SceneVideoFilter(leftEyeOnly, fps: null, bt709: true),
        "-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "32", "-deadline", "good", "-cpu-used", "2", "-row-mt", "1",
        "-pix_fmt", "yuv420p", "-f", "webm",
        outputPath,
    ];

    // Still settings chosen from the encoder comparison on (compression_level 6 is worth
    // it on a single still).
    private static string SceneVideoFilter(bool leftEyeOnly, int? fps, bool bt709 = false)
    {
        var filters = new List<string>();
        if (leftEyeOnly) filters.Add("crop=iw/2:ih:0:0");
        if (fps is { } rate) filters.Add($"fps={rate}");
        filters.Add($"scale=-2:360:{(bt709 ? VideoScaleOptions : WebpScaleOptions)}");
        if (bt709) filters.Add(Bt709Tags);
        return string.Join(',', filters);
    }

    private static string FormatSeconds(double seconds) =>
        Math.Max(seconds, 0).ToString("0.###", CultureInfo.InvariantCulture);

    private async Task<FfmpegRunResult> RunAsync(string operation, string inputPath, string outputPath, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        if (binaryResolver.FfmpegPath is not { } ffmpegPath)
        {
            return FfmpegRunResult.Failed("ffmpeg is not available.");
        }

        // Only stills and hover previews come through here: the short lane. No input duration is known, so they add no media seconds.
        return await RunInstrumentedAsync(operation, shortGate, TimeSpan.Zero, new RunIo([inputPath], outputPath), ct, scope => RunCoreAsync(scope, ffmpegPath, arguments, ct), r => r.Success);
    }

    private static async Task<FfmpegRunResult> RunCoreAsync(FfmpegRunScope scope, string ffmpegPath, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Nothing reads progress here, only the final error line: keep stderr to errors so it stays small.
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-nostats");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        scope.StartAtLowPriority(process);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            scope.Kill(process);
            throw;
        }

        var stderr = await stderrTask;
        scope.Observe(stderr);
        return process.ExitCode == 0
            ? FfmpegRunResult.Ok()
            : FfmpegRunResult.Failed($"ffmpeg exited with code {process.ExitCode}: {LastLine(stderr)}");
    }

    /// <summary>The files a run reads and writes, for the byte counters: <see cref="InputPaths"/>, plus the output file or the
    /// directory the run fills (trickplay writes N.webp sheets into a fresh staging directory).</summary>
    /// A probe reads only headers, so it declares no inputs and adds nothing to the input-bytes counter.
    private sealed record RunIo(IReadOnlyList<string> InputPaths, string? OutputFile = null, string? OutputDirectory = null)
    {
        public static RunIo None { get; } = new([]);
    }

    /// <summary>Runs <paramref name="body"/> (one process lifecycle) inside <paramref name="gate"/>'s slot, if any, and records
    /// its metrics: time queued on the gate, the run's duration from slot acquired to finished (queue time excluded),
    /// and its outcome (success per <paramref name="succeeded"/>, cancelled on cancellation, otherwise failed). A failed run
    /// also counts its failure reason, as bucketed by the <see cref="FfmpegRunScope"/> from the stderr the body observed.
    /// <paramref name="inputDuration"/> is the media the run covers; it is added to the input-media-seconds counter on
    /// success only, and not at all when zero (unknown), and divided by the run's wall time for the speed-ratio histogram.
    /// On success rather than incrementally from progress: progress is only parsed when a caller asks for it, and a failed
    /// or cancelled run would otherwise count media it never finished. Input and output bytes are also measured on success
    /// only (best effort: a missing or unreadable file adds nothing and never fails the run).</summary>
    private async Task<T> RunInstrumentedAsync<T>(string operation, FfmpegGate? gate, TimeSpan inputDuration, RunIo io, CancellationToken ct, Func<FfmpegRunScope, Task<T>> body, Func<T, bool> succeeded)
    {
        var gateName = gate?.Name ?? FfmpegGate.UngatedName;
        IDisposable? slot = null;
        var waited = Stopwatch.StartNew();
        if (gate is not null)
        {
            metrics?.FfmpegWaitStarted(gateName);
            try
            {
                slot = await gate.EnterAsync(ct);
            }
            finally
            {
                metrics?.FfmpegWaitEnded(gateName);
            }
        }

        using (slot)
        {
            // Inside the using so nothing between taking the slot and disposing it can leak it.
            if (gate is not null) metrics?.FfmpegGateWaitSeconds.Record(waited.Elapsed.TotalSeconds, new KeyValuePair<string, object?>("gate", gateName));
            var outcome = "failed";
            var scope = new FfmpegRunScope(operation, metrics);
            var ran = Stopwatch.StartNew();
            metrics?.FfmpegProcessStarted(gateName);
            try
            {
                var result = await body(scope);
                if (succeeded(result)) outcome = "success";
                return result;
            }
            catch (OperationCanceledException)
            {
                outcome = "cancelled";
                throw;
            }
            finally
            {
                var elapsedSeconds = ran.Elapsed.TotalSeconds;
                metrics?.FfmpegProcessEnded(gateName);
                if (metrics is not null)
                {
                    var operationTag = new KeyValuePair<string, object?>("operation", operation);
                    metrics.FfmpegRuns.Add(1, operationTag, new KeyValuePair<string, object?>("outcome", outcome));
                    metrics.FfmpegDurationSeconds.Record(elapsedSeconds, operationTag);
                    if (outcome == "failed")
                        metrics.FfmpegFailures.Add(1, operationTag, new KeyValuePair<string, object?>("reason", scope.FailureReason));
                    if (outcome == "success")
                    {
                        if (inputDuration > TimeSpan.Zero)
                        {
                            metrics.FfmpegInputMediaSeconds.Add(inputDuration.TotalSeconds, operationTag);
                            if (elapsedSeconds > 0) metrics.FfmpegSpeedRatio.Record(inputDuration.TotalSeconds / elapsedSeconds, operationTag);
                        }
                        RecordBytes(metrics.FfmpegInputBytes, operationTag, io.InputPaths.Sum(FileLengthOrZero));
                        RecordBytes(metrics.FfmpegOutputBytes, operationTag, io.OutputFile is null ? 0 : FileLengthOrZero(io.OutputFile));
                        RecordBytes(metrics.FfmpegOutputBytes, operationTag, io.OutputDirectory is null ? 0 : DirectoryBytesOrZero(io.OutputDirectory));
                    }
                }
            }
        }
    }

    private static void RecordBytes(System.Diagnostics.Metrics.Counter<long> counter, KeyValuePair<string, object?> operationTag, long bytes)
    {
        if (bytes > 0) counter.Add(bytes, operationTag);
    }

    private static long FileLengthOrZero(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return 0;
        }
    }

    private static long DirectoryBytesOrZero(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).EnumerateFiles().Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return 0;
        }
    }

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;

    /// <summary>ffmpeg arguments for the dts2pts repair remux. -map 0 keeps every
    /// stream: ffmpeg's default selection keeps only one video/audio/subtitle stream, which would
    /// silently drop extra audio tracks and subtitles from a "lossless" repair. -map -0:d? then
    /// drops data streams, the same as the chapter remux: MP4 files can carry RTP hint tracks,
    /// which the MP4 muxer can't write, failing the whole repair.</summary>
    public static List<string> BuildDts2PtsRemuxArguments(string inputPath, string outputPath)
    {
        var arguments = new List<string>
        {
            "-hide_banner", "-nostdin", "-y",
            "-fflags", "+genpts",
            "-i", inputPath,
            "-map", "0", "-map", "-0:d?",
            "-c", "copy",
            "-bsf:v", "dts2pts",
            "-avoid_negative_ts", "make_zero",
        };
        if (SceneChapterPlan.IsMp4Family(outputPath))
        {
            arguments.AddRange(["-movflags", "+faststart"]);
        }
        arguments.Add(outputPath);
        return arguments;
    }

    public async Task<FfmpegRunResult> RemuxDts2PtsAsync(string inputPath, string outputPath, TimeSpan totalDuration, IProgress<FfmpegProgress>? progress, CancellationToken ct = default)
    {
        if (binaryResolver.FfmpegPath is not { } ffmpegPath)
        {
            return FfmpegRunResult.Failed("ffmpeg is not available.");
        }

        // Deliberately not gated: a repair is user-initiated and shouldn't queue behind hours of background trickplay or scene detection.
        return await RunInstrumentedAsync("remux_dts2pts", null, totalDuration, new RunIo([inputPath], outputPath), ct, scope => RemuxDts2PtsCoreAsync(scope, ffmpegPath, inputPath, outputPath, totalDuration, progress, ct), r => r.Success);
    }

    private static async Task<FfmpegRunResult> RemuxDts2PtsCoreAsync(FfmpegRunScope scope, string ffmpegPath, string inputPath, string outputPath, TimeSpan totalDuration, IProgress<FfmpegProgress>? progress, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in BuildDts2PtsRemuxArguments(inputPath, outputPath))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        scope.StartAtLowPriority(process);

        var stderrTask = ReadStderrForProgressAsync(scope, process, totalDuration, progress, ct);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            scope.Kill(process);
            throw;
        }

        await stderrTask;

        return process.ExitCode == 0
            ? FfmpegRunResult.Ok()
            : FfmpegRunResult.Failed($"ffmpeg exited with code {process.ExitCode}.");
    }

    private static async Task ReadStderrForProgressAsync(FfmpegRunScope scope, Process process, TimeSpan totalDuration, IProgress<FfmpegProgress>? progress, CancellationToken ct,
        Func<string, TimeSpan, FfmpegProgress?>? parseLine = null)
    {
        parseLine ??= ParseProgressLine;
        string? line;
        while ((line = await process.StandardError.ReadLineAsync(ct)) is not null)
        {
            scope.Observe(line);
            if (progress is not null && totalDuration > TimeSpan.Zero)
            {
                var parsed = parseLine(line, totalDuration);
                if (parsed is not null) progress.Report(parsed);
            }
        }
    }

    /// <summary>Parses one line of ffmpeg's stderr progress output (e.g. "frame=123 ... time=00:01:02.34
    /// bitrate=...") into a percent-complete against the known total duration. Public and static for
    /// direct unit testing against real captured ffmpeg stderr lines. Returns null if the line has no
    /// time= field.</summary>
    public static FfmpegProgress? ParseProgressLine(string line, TimeSpan totalDuration)
    {
        var match = TimeProgressPattern().Match(line);
        if (!match.Success) return null;

        var elapsed = new TimeSpan(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), 0)
            + TimeSpan.FromSeconds(double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        var percent = totalDuration > TimeSpan.Zero
            ? Math.Clamp(elapsed.TotalSeconds / totalDuration.TotalSeconds * 100.0, 0, 100)
            : 0;
        return new FfmpegProgress(percent, elapsed);
    }

    private static double? ParseFrameRate(string? rFrameRate)
    {
        if (string.IsNullOrEmpty(rFrameRate)) return null;
        var parts = rFrameRate.Split('/');
        if (parts.Length != 2) return null;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) return null;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) || den == 0) return null;
        return num / den;
    }

    private class FfprobeOutput
    {
        public FfprobeFormat? Format { get; set; }
        public List<FfprobeStream>? Streams { get; set; }
        public List<FfprobeChapter>? Chapters { get; set; }
    }

    private class FfprobeChapter
    {
        [JsonPropertyName("start_time")]
        public string? StartTime { get; set; }

        [JsonPropertyName("end_time")]
        public string? EndTime { get; set; }

        public Dictionary<string, string>? Tags { get; set; }
    }

    private class FfprobeFormat
    {
        public string? Duration { get; set; }
    }

    private class FfprobeStream
    {
        [JsonPropertyName("codec_type")]
        public string? CodecType { get; set; }

        [JsonPropertyName("codec_name")]
        public string? CodecName { get; set; }

        public int? Width { get; set; }
        public int? Height { get; set; }

        [JsonPropertyName("r_frame_rate")]
        public string? RFrameRate { get; set; }

        [JsonPropertyName("channels")]
        public int? Channels { get; set; }
    }
}
