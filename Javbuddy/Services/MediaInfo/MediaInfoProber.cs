using System.Globalization;
using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;
using MediaInfoLib = MediaInfo;

namespace Javbuddy.Services.MediaInfo;

/// <summary>Result of probing one video file's technical info via MediaInfoLib. Skipped means the
/// probe never ran because the MediaInfo integration is disabled in Settings — callers must leave
/// any previously stored Media* fields on the Movie untouched in that case, not null them out.</summary>
public class MediaProbeResult
{
    public bool Skipped { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    // General / container
    public string? ContainerFormat { get; init; }
    public string? ContainerFormatProfile { get; init; }
    public string? ContainerCodecId { get; init; }
    public string? ContainerCodecIdCompatible { get; init; }
    public string? WritingApplication { get; init; }
    public double? DurationSeconds { get; init; }
    public int? OverallBitRateKbps { get; init; }
    public double? FrameRate { get; init; }

    // Video
    public string? VideoStreamId { get; init; }
    public string? VideoCodec { get; init; }
    public string? VideoFormatInfo { get; init; }
    public string? VideoProfile { get; init; }
    public string? VideoFormatSettings { get; init; }
    public string? VideoCodecId { get; init; }
    public string? VideoCodecIdInfo { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? AspectRatio { get; init; }
    public string? FrameRateMode { get; init; }
    public double? FrameRateMin { get; init; }
    public double? FrameRateMax { get; init; }
    public int? VideoBitRateKbps { get; init; }
    public string? ColorSpace { get; init; }
    public string? ChromaSubsampling { get; init; }
    public int? BitDepth { get; init; }
    public string? ScanType { get; init; }
    public double? BitsPerPixelFrame { get; init; }
    public long? VideoStreamSizeBytes { get; init; }
    public string? WritingLibrary { get; init; }
    public string? EncodingSettings { get; init; }
    public string? ColorRange { get; init; }
    public string? ColorPrimaries { get; init; }
    public string? TransferCharacteristics { get; init; }
    public string? MatrixCoefficients { get; init; }
    public string? CodecConfigurationBox { get; init; }

    // Audio
    public string? AudioStreamId { get; init; }
    public string? AudioCodec { get; init; }
    public string? AudioFormatInfo { get; init; }
    public string? AudioFormatSettings { get; init; }
    public string? AudioCodecId { get; init; }
    public string? AudioBitRateMode { get; init; }
    public int? AudioBitRateKbps { get; init; }
    public int? AudioChannels { get; init; }
    public string? AudioChannelLayout { get; init; }
    public int? AudioSamplingRateHz { get; init; }
    public double? AudioFrameRate { get; init; }
    public string? AudioCompressionMode { get; init; }
    public long? AudioStreamSizeBytes { get; init; }
    public bool? AudioDefault { get; init; }
    public int? AudioAlternateGroup { get; init; }

    public int? SubtitleCount { get; init; }

    public static MediaProbeResult SkippedResult() => new() { Skipped = true };

    public static MediaProbeResult Failed(string errorMessage) => new() { Success = false, ErrorMessage = errorMessage };
}

public interface IMediaInfoProber
{
    /// <summary>Probes one video file with MediaInfoLib and returns structured technical fields.
    /// Returns a Skipped result without touching the file at all if the MediaInfo integration is
    /// disabled in Settings.</summary>
    Task<MediaProbeResult> ProbeAsync(string videoFilePath, CancellationToken ct = default);
}

public class MediaInfoProber(
    IDbContextFactory<AppDbContext> dbFactory,
    MediaProbeGate? probeGate = null,
    Func<string, MediaProbeResult>? nativeProbe = null,
    TimeSpan? probeTimeout = null) : IMediaInfoProber
{
    // A corrupt/malformed file can make MediaInfoLib's native parser hang instead of failing (a
    // healthy probe completes in well under 2 seconds, since only header/container metadata is read).
    // After the timeout, ProbeAsync gives up and reports a normal (retryable) failure instead of
    // blocking one of LibraryRescanTask's limited concurrent slots forever. The native call has no
    // cancellation checkpoint, so the abandoned call keeps running on its thread-pool thread; it can't
    // be killed from managed code. MediaProbeGate bounds how many such calls can be outstanding and
    // joins a retry of a still-running file instead of adding another.
    private static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromMinutes(1);

    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly MediaProbeGate probeGate = probeGate ?? new MediaProbeGate();
    private readonly Func<string, MediaProbeResult> nativeProbe = nativeProbe ?? ProbeCore;
    private readonly TimeSpan probeTimeout = probeTimeout ?? DefaultProbeTimeout;

    public async Task<MediaProbeResult> ProbeAsync(string videoFilePath, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.MediaInfoSettings.ReadSingleRowAsync(ct);
        if (settings is { Enabled: false })
        {
            return MediaProbeResult.SkippedResult();
        }

        // MediaInfoLib's calls are synchronous native I/O with no async surface of their own — the gate
        // runs them on the thread pool so a network-share probe
        // doesn't block a caller's async context, the same reason the .nfo XML parsing nearby uses an
        // async FileStream instead of XDocument.Load.
        return await probeGate.RunAsync(videoFilePath, () => nativeProbe(videoFilePath), probeTimeout, ct);
    }

    /// <summary>Races an already-started probe against a timeout, without ever cancelling the probe
    /// itself (MediaInfoLib's native call has no cancellation checkpoint to honor). Kept separate
    /// from ProbeAsync, and with an injectable timeout, so this race/cancellation-distinguishing
    /// logic can be unit tested directly against a controllable delay instead of a real (necessarily
    /// slow, to prove anything) native probe.</summary>
    public static async Task<MediaProbeResult> RaceWithTimeoutAsync(Task<MediaProbeResult> probeTask, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timeoutTask = Task.Delay(timeout, timeoutCts.Token);

        var completed = await Task.WhenAny(probeTask, timeoutTask);
        if (completed == probeTask)
        {
            timeoutCts.Cancel(); // Release the pending timer promptly instead of leaking it for the rest of the timeout.
            return await probeTask;
        }

        ct.ThrowIfCancellationRequested(); // Distinguishes the caller's own cancellation from a genuine timeout below — both make timeoutTask "complete".
        return MediaProbeResult.Failed($"MediaInfo probe timed out after {timeout.TotalSeconds:F0}s (the file may be corrupt, or the network share is unresponsive).");
    }

    /// <summary>Uses the low-level MediaInfo.Get(streamKind, streamNumber, parameter) API (from
    /// MediaInfo.Wrapper.Core, not our own P/Invoke) rather than the higher-level MediaInfoWrapper
    /// convenience class — the high-level class's parsed enums didn't match MediaInfoLib's own
    /// human-readable field values for several fields (e.g. its ColorSpace enum reports "Generic"
    /// where the classic report and this native field both say "YUV"), and it doesn't expose several
    /// fields needed for full report parity (Format settings, Codec ID, stream sizes, ...) at all.
    /// Every parameter name below was confirmed against a real file's actual Get() output — several
    /// differ subtly from MediaInfoLib's JSON output field names for the same data (e.g. the
    /// Get()-only "Channel(s)" vs JSON's "Channels", or Duration always being milliseconds via Get()
    /// regardless of the seconds-formatted decimal JSON uses).</summary>
    private static MediaProbeResult ProbeCore(string videoFilePath)
    {
        try
        {
            using var mi = new MediaInfoLib.MediaInfo();
            if (mi.Open(videoFilePath) == IntPtr.Zero)
            {
                return MediaProbeResult.Failed("MediaInfo could not open the video file.");
            }

            var videoCount = Int(mi, MediaInfoLib.StreamKind.General, "VideoCount") ?? 0;
            var audioCount = Int(mi, MediaInfoLib.StreamKind.General, "AudioCount") ?? 0;
            var subtitleCount = Int(mi, MediaInfoLib.StreamKind.General, "TextCount") ?? 0;
            if (videoCount == 0 && audioCount == 0 && subtitleCount == 0)
            {
                return MediaProbeResult.Failed("MediaInfo could not open or parse the video file.");
            }

            // MediaInfo splits a video profile like "High@L4" into separate Format_Profile ("High")
            // and Format_Level ("4") fields — recombine to match the classic text report.
            var videoProfile = Str(mi, MediaInfoLib.StreamKind.Video, "Format_Profile");
            var videoLevel = Str(mi, MediaInfoLib.StreamKind.Video, "Format_Level");
            var combinedVideoProfile = videoProfile is not null && videoLevel is not null
                ? $"{videoProfile}@L{videoLevel}"
                : videoProfile;

            return new MediaProbeResult
            {
                Success = true,

                ContainerFormat = Str(mi, MediaInfoLib.StreamKind.General, "Format"),
                ContainerFormatProfile = Str(mi, MediaInfoLib.StreamKind.General, "Format_Profile"),
                ContainerCodecId = Str(mi, MediaInfoLib.StreamKind.General, "CodecID"),
                ContainerCodecIdCompatible = Str(mi, MediaInfoLib.StreamKind.General, "CodecID_Compatible"),
                WritingApplication = Str(mi, MediaInfoLib.StreamKind.General, "Encoded_Application"),
                DurationSeconds = MsToSeconds(Dbl(mi, MediaInfoLib.StreamKind.General, "Duration")),
                OverallBitRateKbps = BpsToKbps(Dbl(mi, MediaInfoLib.StreamKind.General, "OverallBitRate")),
                FrameRate = Dbl(mi, MediaInfoLib.StreamKind.General, "FrameRate"),

                VideoStreamId = Str(mi, MediaInfoLib.StreamKind.Video, "ID"),
                VideoCodec = Str(mi, MediaInfoLib.StreamKind.Video, "Format"),
                VideoFormatInfo = Str(mi, MediaInfoLib.StreamKind.Video, "Format/Info"),
                VideoProfile = combinedVideoProfile,
                VideoFormatSettings = Str(mi, MediaInfoLib.StreamKind.Video, "Format_Settings"),
                VideoCodecId = Str(mi, MediaInfoLib.StreamKind.Video, "CodecID"),
                VideoCodecIdInfo = Str(mi, MediaInfoLib.StreamKind.Video, "CodecID/Info"),
                Width = Int(mi, MediaInfoLib.StreamKind.Video, "Width"),
                Height = Int(mi, MediaInfoLib.StreamKind.Video, "Height"),
                AspectRatio = Str(mi, MediaInfoLib.StreamKind.Video, "DisplayAspectRatio/String"),
                FrameRateMode = Str(mi, MediaInfoLib.StreamKind.Video, "FrameRate_Mode/String"),
                FrameRateMin = Dbl(mi, MediaInfoLib.StreamKind.Video, "FrameRate_Minimum"),
                FrameRateMax = Dbl(mi, MediaInfoLib.StreamKind.Video, "FrameRate_Maximum"),
                VideoBitRateKbps = BpsToKbps(Dbl(mi, MediaInfoLib.StreamKind.Video, "BitRate")),
                ColorSpace = Str(mi, MediaInfoLib.StreamKind.Video, "ColorSpace"),
                ChromaSubsampling = Str(mi, MediaInfoLib.StreamKind.Video, "ChromaSubsampling"),
                BitDepth = Int(mi, MediaInfoLib.StreamKind.Video, "BitDepth"),
                ScanType = Str(mi, MediaInfoLib.StreamKind.Video, "ScanType"),
                BitsPerPixelFrame = Dbl(mi, MediaInfoLib.StreamKind.Video, "Bits-(Pixel*Frame)"),
                VideoStreamSizeBytes = Long(mi, MediaInfoLib.StreamKind.Video, "StreamSize"),
                WritingLibrary = Str(mi, MediaInfoLib.StreamKind.Video, "Encoded_Library/String"),
                EncodingSettings = Str(mi, MediaInfoLib.StreamKind.Video, "Encoded_Library_Settings"),
                ColorRange = Str(mi, MediaInfoLib.StreamKind.Video, "colour_range"),
                ColorPrimaries = Str(mi, MediaInfoLib.StreamKind.Video, "colour_primaries"),
                TransferCharacteristics = Str(mi, MediaInfoLib.StreamKind.Video, "transfer_characteristics"),
                MatrixCoefficients = Str(mi, MediaInfoLib.StreamKind.Video, "matrix_coefficients"),
                CodecConfigurationBox = Str(mi, MediaInfoLib.StreamKind.Video, "CodecConfigurationBox"),

                AudioStreamId = Str(mi, MediaInfoLib.StreamKind.Audio, "ID"),
                AudioCodec = Str(mi, MediaInfoLib.StreamKind.Audio, "Format/String"),
                AudioFormatInfo = Str(mi, MediaInfoLib.StreamKind.Audio, "Format/Info"),
                AudioFormatSettings = Str(mi, MediaInfoLib.StreamKind.Audio, "Format_Settings"),
                AudioCodecId = Str(mi, MediaInfoLib.StreamKind.Audio, "CodecID"),
                AudioBitRateMode = Str(mi, MediaInfoLib.StreamKind.Audio, "BitRate_Mode/String"),
                AudioBitRateKbps = BpsToKbps(Dbl(mi, MediaInfoLib.StreamKind.Audio, "BitRate")),
                AudioChannels = Int(mi, MediaInfoLib.StreamKind.Audio, "Channel(s)"),
                AudioChannelLayout = Str(mi, MediaInfoLib.StreamKind.Audio, "ChannelLayout"),
                AudioSamplingRateHz = Int(mi, MediaInfoLib.StreamKind.Audio, "SamplingRate"),
                AudioFrameRate = Dbl(mi, MediaInfoLib.StreamKind.Audio, "FrameRate"),
                AudioCompressionMode = Str(mi, MediaInfoLib.StreamKind.Audio, "Compression_Mode"),
                AudioStreamSizeBytes = Long(mi, MediaInfoLib.StreamKind.Audio, "StreamSize"),
                AudioDefault = YesNo(mi, MediaInfoLib.StreamKind.Audio, "Default"),
                AudioAlternateGroup = Int(mi, MediaInfoLib.StreamKind.Audio, "AlternateGroup"),

                SubtitleCount = subtitleCount,
            };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return MediaProbeResult.Failed($"MediaInfo native library unavailable: {ex.Message}");
        }
    }

    /// <summary>MediaInfoLib's Duration field is milliseconds via Get() regardless of stream kind —
    /// confirmed empirically against a real file (10063104 for a ~10063.104s movie).</summary>
    public static double? MsToSeconds(double? milliseconds) => milliseconds is > 0 ? milliseconds / 1000.0 : null;

    public static int? BpsToKbps(double? bps) => bps is > 0 ? (int)Math.Round(bps.Value / 1000.0) : null;

    private static string? Str(MediaInfoLib.MediaInfo mi, MediaInfoLib.StreamKind kind, string parameter)
    {
        var value = mi.Get(kind, 0, parameter);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static double? Dbl(MediaInfoLib.MediaInfo mi, MediaInfoLib.StreamKind kind, string parameter) =>
        double.TryParse(Str(mi, kind, parameter), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static int? Int(MediaInfoLib.MediaInfo mi, MediaInfoLib.StreamKind kind, string parameter) =>
        int.TryParse(Str(mi, kind, parameter), NumberStyles.Any, CultureInfo.InvariantCulture, out var i) ? i : null;

    private static long? Long(MediaInfoLib.MediaInfo mi, MediaInfoLib.StreamKind kind, string parameter) =>
        long.TryParse(Str(mi, kind, parameter), NumberStyles.Any, CultureInfo.InvariantCulture, out var l) ? l : null;

    private static bool? YesNo(MediaInfoLib.MediaInfo mi, MediaInfoLib.StreamKind kind, string parameter) =>
        Str(mi, kind, parameter) switch { "Yes" => true, "No" => false, _ => null };
}
