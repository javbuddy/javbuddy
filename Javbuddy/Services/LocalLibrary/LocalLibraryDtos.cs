using Javbuddy.Services.MediaInfo;

namespace Javbuddy.Services.LocalLibrary;

/// <summary>Metadata parsed from a movie's local folder (Kodi/Jellyfin-style .nfo file plus
/// poster.jpg/fanart.jpg/video file presence).</summary>
public class LocalMovieMetadata
{
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? Plot { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? Director { get; set; }
    public string? Studio { get; set; }
    public string? Label { get; set; }
    public string? SeriesName { get; set; }
    public double? RatingScore { get; set; }
    public int? RatingVotes { get; set; }
    public int? RuntimeMinutes { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<string> Actresses { get; set; } = new();
    public List<LocalActorMetadata> Actors { get; set; } = new();

    /// <summary>The .nfo's own remote poster URL — only used if no local poster file exists.</summary>
    public string? PosterFallbackUrl { get; set; }

    /// <summary>The .nfo's own remote fanart/backdrop URL — only used if no local fanart.jpg exists.</summary>
    public string? FanartFallbackUrl { get; set; }

    /// <summary>"poster.jpg" or "folder.jpg" — whichever local poster file exists in the movie's
    /// folder (poster.jpg preferred if both do), or null if neither does.</summary>
    public string? LocalPosterFileName { get; set; }

    public bool HasLocalFanart { get; set; }
    public long? VideoFileSizeBytes { get; set; }

    /// <summary>Just the resolved video file's name (no directory) — MediaInfo's own classic
    /// report calls this "Complete name", but this app never surfaces the local filesystem layout.</summary>
    public string? VideoFileName { get; set; }

    /// <summary>A sidecar subtitle file (.srt/.ass/.ssa/.vtt) found in the movie's folder —
    /// most local subtitles aren't embedded in the video container, they're a separate file
    /// (e.g. "ABC-123.whisper large v3.en.srt" alongside "ABC-123.mkv").</summary>
    public bool HasSubtitleFile { get; set; }

    /// <summary>A "{code}-trailer.{ext}" video file found alongside the movie's own video file
    /// (e.g. "ABC-123-trailer.mp4" next to "ABC-123.webm") — the same suffix convention
    /// video-file selection excludes when picking the movie's own file.</summary>
    public bool HasTrailerFile { get; set; }

    public MediaProbeResult? MediaProbe { get; set; }

    /// <summary>All video files/versions discovered in the movie's local folder.</summary>
    public List<LocalVideoFileMetadata> VideoFiles { get; set; } = new();
}

public class LocalVideoFileMetadata
{
    public string FileName { get; set; } = string.Empty;
    public string VersionTag { get; set; } = "Original";
    public long FileSizeBytes { get; set; }
    public DateTime? FileAddedAt { get; set; }
    public DateTime? LastWriteUtc { get; set; }
    public bool IsPrimary { get; set; }
    public MediaProbeResult? MediaProbe { get; set; }
}

public record LocalLookupResult(bool Found, string? FolderPath, LocalMovieMetadata? Metadata, string? ErrorMessage);

public class LocalActorMetadata
{
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
    public string? Type { get; set; }
    public string? Thumb { get; set; }
    public string? AltName { get; set; }
    public List<string> Aliases { get; set; } = new();
}
