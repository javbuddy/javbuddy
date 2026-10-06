namespace Javbuddy.Services.Ffmpeg;

/// <summary>Technical shape of one probed video file, enough to flag a stream mismatch between VR
/// parts before a stream-copy concat (which silently produces a broken timeline if codecs/timebases
/// differ across parts — see VrMergeService).</summary>
public record FfprobeMediaInfo
{
    public double DurationSeconds { get; init; }
    public string? VideoCodec { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? FrameRate { get; init; }
    public string? AudioCodec { get; init; }
    public int? AudioChannels { get; init; }

    /// <summary>Number of chapters ffprobe reports (via -show_chapters) — used by VrMergeService to
    /// verify a just-merged file really got one chapter per source part before trusting it.</summary>
    public int ChapterCount { get; init; }

    /// <summary>The file's embedded chapters in file order — imported as scenes by
    /// SceneChapterImportService.</summary>
    public IReadOnlyList<FfprobeChapterInfo> Chapters { get; init; } = [];
}

/// <summary>One embedded chapter as ffprobe reports it; Title is null when the chapter has none.</summary>
public record FfprobeChapterInfo(double StartSeconds, double EndSeconds, string? Title);

public record FfmpegProgress(double PercentComplete, TimeSpan Elapsed);

public record FfmpegRunResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static FfmpegRunResult Ok() => new() { Success = true };
    public static FfmpegRunResult Failed(string message) => new() { Success = false, ErrorMessage = message };
}

/// <summary>One source part for ConcatWithChaptersAsync: its absolute path, the chapter title it
/// will receive, and its ffprobe'd duration (already converted to microseconds — see
/// VrPartDetector.BuildFfMetadata for why microseconds and how it's rounded).</summary>
public record FfmpegConcatPart(string AbsolutePath, string ChapterTitle, long DurationMicroseconds);

public record FfmpegConcatRequest(IReadOnlyList<FfmpegConcatPart> Parts, string OutputPath);

/// <summary>A stretch of (near-)black frames found by ffmpeg's blackdetect filter.</summary>
public record FfmpegBlackStretch(double StartSeconds, double EndSeconds);

/// <summary>A hard cut found by ffmpeg's scdet filter, with its scene-change score (0–100).</summary>
public record FfmpegSceneCut(double Seconds, double Score);

/// <summary>Raw signal detection output for one file.</summary>
public record FfmpegSceneSignals(IReadOnlyList<FfmpegBlackStretch> BlackStretches, IReadOnlyList<FfmpegSceneCut> Cuts);

/// <summary>Follows a running scene-signal scan: each report as ffmpeg writes it to
/// stderr, in file order, awaited before the next line is read.</summary>
public interface IFfmpegSceneSignalSink
{
    Task OnBlackStretchAsync(FfmpegBlackStretch black, CancellationToken ct);

    Task OnCutAsync(FfmpegSceneCut cut, CancellationToken ct);

    /// <summary>The decode has got this far into the file (ffmpeg's time= stats); every signal
    /// before it has been reported.</summary>
    Task OnDecodedAsync(double seconds, CancellationToken ct);
}

/// <summary>How to render a video's trickplay tile sheets: one thumbnail of
/// ThumbnailWidth x ThumbnailHeight every IntervalSeconds, TileWidth x TileHeight per sheet.
/// LeftEyeOnly crops a side-by-side VR frame to its left half first; KeyframeOnly decodes only
/// keyframes (much faster, but a thumbnail may come from up to one keyframe gap away). StartSeconds
/// and LengthSeconds limit it to a clip (a highlight's own set); null covers the whole
/// video.</summary>
public record FfmpegTrickplayRequest(
    int IntervalSeconds,
    int ThumbnailWidth,
    int ThumbnailHeight,
    int TileWidth,
    int TileHeight,
    bool LeftEyeOnly,
    bool KeyframeOnly,
    double? StartSeconds = null,
    double? LengthSeconds = null);
