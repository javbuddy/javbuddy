namespace Javbuddy.Services.ClipExport;

/// <summary>The file format a clip is stream-copied into: ffmpeg's muxer name, the file extension and the download's content type.</summary>
public sealed record ClipContainer(string Muxer, string Extension, string ContentType)
{
    public static readonly ClipContainer WebM = new("webm", ".webm", "video/webm");
    public static readonly ClipContainer Mp4 = new("mp4", ".mp4", "video/mp4");

    /// <summary>Takes any codec, so it's the fallback when the source's streams fit neither WebM nor MP4.</summary>
    public static readonly ClipContainer Matroska = new("matroska", ".mkv", "video/x-matroska");

    private static readonly HashSet<string> WebMVideo = new(StringComparer.OrdinalIgnoreCase) { "vp8", "vp9", "av1" };
    private static readonly HashSet<string> WebMAudio = new(StringComparer.OrdinalIgnoreCase) { "opus", "vorbis" };
    private static readonly HashSet<string> Mp4Video = new(StringComparer.OrdinalIgnoreCase) { "h264", "hevc", "av1", "vp9", "mpeg4" };
    // FLAC and PCM are left out: ffmpeg's MP4 muxer takes FLAC only as experimental, and PCM not at all.
    private static readonly HashSet<string> Mp4Audio = new(StringComparer.OrdinalIgnoreCase) { "aac", "mp3", "ac3", "eac3", "opus", "alac" };

    /// <summary>The format for a source whose video and audio are <paramref name="videoCodec"/> and <paramref name="audioCodec"/>
    /// (ffprobe codec names; null audio means none): WebM when both fit it (VP8/VP9/AV1 with Opus/Vorbis), else MP4 when both fit
    /// it (H.264/HEVC/AV1/VP9/MPEG-4 Part 2 with AAC/MP3/AC-3/E-AC-3/Opus/ALAC), else Matroska. An unknown video codec gets
    /// Matroska.</summary>
    public static ClipContainer For(string? videoCodec, string? audioCodec)
    {
        if (string.IsNullOrWhiteSpace(videoCodec)) return Matroska;
        if (WebMVideo.Contains(videoCodec) && (audioCodec is null || WebMAudio.Contains(audioCodec))) return WebM;
        if (Mp4Video.Contains(videoCodec) && (audioCodec is null || Mp4Audio.Contains(audioCodec))) return Mp4;
        return Matroska;
    }
}
