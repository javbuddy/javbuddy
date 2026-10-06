using Javbuddy.Services.Ffmpeg;

namespace Javbuddy.Services.VideoRepair;

public record VideoRepairResult
{
    public bool Success { get; init; }
    public string? OutputPath { get; init; }
    public string? ErrorMessage { get; init; }

    public static VideoRepairResult Ok(string path) => new() { Success = true, OutputPath = path };
    public static VideoRepairResult Failed(string message) => new() { Success = false, ErrorMessage = message };
}

public record VideoRepairCandidate
{
    public int MovieId { get; init; }
    public string MovieCode { get; init; } = string.Empty;
    public int? MovieFileId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string VersionTag { get; init; } = "Original";
    public long FileSizeBytes { get; init; }
    public double DurationSeconds { get; init; }
    public string? ResolutionDisplay { get; init; }
    public bool IsPrimary { get; init; }
    public bool HasBrokenBFrames { get; init; }
}

/// <summary>What a tracked job does to the movie's video file. Both kinds share one remux slot.</summary>
public enum VideoFileJobKind
{
    /// <summary>Lossless B-frame repair.</summary>
    Repair,

    /// <summary>Writing scenes into the file as chapters.</summary>
    Chapters,

    /// <summary>Scanning the file for scene boundaries — reads, never writes, but is a
    /// full decode, so it takes the same one-at-a-time slot.</summary>
    Detection,
}

public class VideoRepairJob
{
    public VideoFileJobKind Kind { get; init; } = VideoFileJobKind.Repair;
    public int MovieId { get; init; }
    public int? MovieFileId { get; init; }
    public string MovieCode { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public Task<VideoRepairResult>? Task { get; set; }
    public CancellationTokenSource Cts { get; } = new();
    public FfmpegProgress? LatestProgress { get; set; }
    public DateTime StartedAt { get; } = DateTime.UtcNow;
}

public static class VideoFileJobs
{
    public static string Describe(VideoFileJobKind kind) => kind switch
    {
        VideoFileJobKind.Chapters => "chapter writing",
        VideoFileJobKind.Detection => "scene detection",
        _ => "video repair",
    };
}
