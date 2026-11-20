namespace Javbuddy.Services.Metrics;

/// <summary>Turns the media info stored per file (MediaInfo's own naming: "AVC", "MPEG-4", ...) into the small fixed label
/// sets of the <c>javbuddy_library_video_*_total</c> gauges, so odd or new values can never grow the label cardinality.</summary>
public static class LibraryMediaLabels
{
    public const string Unknown = "unknown";
    public const string Other = "other";

    public static string Codec(string? videoCodec)
    {
        if (string.IsNullOrWhiteSpace(videoCodec)) return Unknown;
        return videoCodec.Trim().ToUpperInvariant() switch
        {
            "AVC" or "H264" or "H.264" => "h264",
            "HEVC" or "H265" or "H.265" => "hevc",
            "AV1" => "av1",
            "VP9" => "vp9",
            "VP8" => "vp8",
            "MPEG-4 VISUAL" or "MPEG4" or "MPEG-4" or "XVID" or "DIVX" => "mpeg4",
            "MPEG VIDEO" or "MPEG-2" or "MPEG2" => "mpeg2",
            "VC-1" or "VC1" => "vc1",
            var wmv when wmv.StartsWith("WMV", StringComparison.Ordinal) || wmv.StartsWith("WINDOWS MEDIA", StringComparison.Ordinal) => "wmv",
            _ => Other,
        };
    }

    public static string Container(string? containerFormat)
    {
        if (string.IsNullOrWhiteSpace(containerFormat)) return Unknown;
        return containerFormat.Trim().ToUpperInvariant() switch
        {
            "MPEG-4" or "MP4" => "mp4",
            "MATROSKA" => "mkv",
            "WEBM" => "webm",
            "AVI" => "avi",
            "MPEG-TS" or "BDAV" => "ts",
            "MPEG-PS" => "mpeg",
            "WINDOWS MEDIA" or "ASF" => "wmv",
            "FLASH VIDEO" => "flv",
            _ => Other,
        };
    }

    /// <summary>By frame width, with margin below each standard width so cropped encodes (1916 px wide) land in their
    /// usual bucket. Width, not height, so a letterboxed-and-cropped 1920x800 file is still 1080p. Side-by-side VR
    /// files are bucketed by their full frame like any other.</summary>
    public static string Resolution(int? width) => width switch
    {
        null or <= 0 => Unknown,
        < 1200 => "sd",
        < 1800 => "720p",
        < 2400 => "1080p",
        < 3600 => "1440p",
        < 7000 => "4k",
        _ => "8k_plus",
    };
}
