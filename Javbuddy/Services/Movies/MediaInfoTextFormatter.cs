using System.Globalization;
using System.Text;
using Javbuddy.Services.MediaInfo;

namespace Javbuddy.Services.Movies;

/// <summary>Formats the Media* fields already stored on a Movie or MovieFile (populated by
/// LibraryRescanTask/MediaInfoProber — see Services/MediaInfo) into the two views the movie detail
/// page shows: a one-line summary, and a General/Video/Audio "Label : value" report matching the
/// shape of MediaInfoLib's own classic text output. Pure functions over already-scanned data — no re-probing happens here.</summary>
public static class MediaInfoTextFormatter
{
    private const int LabelWidth = 26;

    /// <summary>Full General/Video/Audio report, formatted like MediaInfoLib's own classic text
    /// output (including its GiB/MiB binary byte units and "(NN%) of file size" stream-size
    /// annotations). Null if the movie has never been scanned.</summary>
    public static string? FormatFull(IMediaTechnicalInfo movie)
    {
        if (movie.MediaScannedAt is null || movie.MediaScanError is not null)
        {
            return null;
        }

        var sb = new StringBuilder();

        AppendSection(sb, "General", section =>
        {
            Line(section, "Complete name", movie.MediaVideoFileName);
            Line(section, "Format", movie.MediaContainerFormat);
            Line(section, "Format profile", movie.MediaContainerFormatProfile);
            Line(section, "Codec ID", CombineCodecId(movie.MediaContainerCodecId, movie.MediaContainerCodecIdCompatible));
            Line(section, "File size", movie.LocalFileSizeBytes is > 0 ? FormatBinarySize(movie.LocalFileSizeBytes.Value) : null);
            Line(section, "Duration", FormatDuration(movie.MediaDurationSeconds));
            Line(section, "Overall bit rate", FormatKbps(movie.MediaOverallBitRateKbps));
            Line(section, "Frame rate", FormatFps(movie.MediaFrameRate));
            Line(section, "Writing application", movie.MediaWritingApplication);
        });

        AppendSection(sb, "Video", section =>
        {
            Line(section, "ID", movie.MediaVideoStreamId);
            Line(section, "Format", movie.MediaVideoCodec);
            Line(section, "Format/Info", movie.MediaVideoFormatInfo);
            Line(section, "Format profile", movie.MediaVideoProfile);
            Line(section, "Format settings", movie.MediaVideoFormatSettings);
            Line(section, "Codec ID", movie.MediaVideoCodecId);
            Line(section, "Codec ID/Info", movie.MediaVideoCodecIdInfo);
            Line(section, "Duration", FormatDuration(movie.MediaDurationSeconds));
            Line(section, "Bit rate", FormatKbps(movie.MediaVideoBitRateKbps));
            Line(section, "Width", movie.MediaWidth is > 0 ? $"{movie.MediaWidth} pixels" : null);
            Line(section, "Height", movie.MediaHeight is > 0 ? $"{movie.MediaHeight} pixels" : null);
            Line(section, "Display aspect ratio", movie.MediaAspectRatio);
            Line(section, "Frame rate mode", movie.MediaFrameRateMode);
            Line(section, "Frame rate", FormatFps(movie.MediaFrameRate));
            Line(section, "Minimum frame rate", FormatFps(movie.MediaFrameRateMin));
            Line(section, "Maximum frame rate", FormatFps(movie.MediaFrameRateMax));
            Line(section, "Color space", movie.MediaColorSpace);
            Line(section, "Chroma subsampling", movie.MediaChromaSubsampling);
            Line(section, "Bit depth", movie.MediaBitDepth is > 0 ? $"{movie.MediaBitDepth} bits" : null);
            Line(section, "Scan type", movie.MediaScanType);
            Line(section, "Bits/(Pixel*Frame)", movie.MediaBitsPerPixelFrame?.ToString("0.000", CultureInfo.InvariantCulture));
            Line(section, "Stream size", FormatStreamSize(movie.MediaVideoStreamSizeBytes, movie.LocalFileSizeBytes));
            Line(section, "Writing library", movie.MediaWritingLibrary);
            Line(section, "Encoding settings", movie.MediaEncodingSettings);
            Line(section, "Color range", movie.MediaColorRange);
            Line(section, "Color primaries", movie.MediaColorPrimaries);
            Line(section, "Transfer characteristics", movie.MediaTransferCharacteristics);
            Line(section, "Matrix coefficients", movie.MediaMatrixCoefficients);
            Line(section, "Codec configuration box", movie.MediaCodecConfigurationBox);
        });

        AppendSection(sb, "Audio", section =>
        {
            Line(section, "ID", movie.MediaAudioStreamId);
            Line(section, "Format", movie.MediaAudioCodec);
            Line(section, "Format/Info", movie.MediaAudioFormatInfo);
            Line(section, "Format settings", movie.MediaAudioFormatSettings);
            Line(section, "Codec ID", movie.MediaAudioCodecId);
            Line(section, "Duration", FormatDuration(movie.MediaDurationSeconds));
            Line(section, "Bit rate mode", movie.MediaAudioBitRateMode);
            Line(section, "Bit rate", FormatKbps(movie.MediaAudioBitRateKbps));
            Line(section, "Channel(s)", movie.MediaAudioChannels is > 0 ? $"{movie.MediaAudioChannels} channels" : null);
            Line(section, "Channel layout", movie.MediaAudioChannelLayout);
            Line(section, "Sampling rate", movie.MediaAudioSamplingRateHz is > 0 ? $"{movie.MediaAudioSamplingRateHz / 1000.0:0.#} kHz" : null);
            Line(section, "Frame rate", FormatFps(movie.MediaAudioFrameRate));
            Line(section, "Compression mode", movie.MediaAudioCompressionMode);
            Line(section, "Stream size", FormatStreamSize(movie.MediaAudioStreamSizeBytes, movie.LocalFileSizeBytes));
            Line(section, "Default", movie.MediaAudioDefault is { } isDefault ? (isDefault ? "Yes" : "No") : null);
            Line(section, "Alternate group", movie.MediaAudioAlternateGroup?.ToString(CultureInfo.InvariantCulture));
        });

        if (movie.MediaSubtitleCount is > 0)
        {
            AppendSection(sb, "Text", section => Line(section, "Count", movie.MediaSubtitleCount?.ToString(CultureInfo.InvariantCulture)));
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static void AppendSection(StringBuilder sb, string title, Action<StringBuilder> body)
    {
        var section = new StringBuilder();
        body(section);
        if (section.Length == 0)
        {
            return;
        }

        if (sb.Length > 0)
        {
            sb.Append('\n');
        }

        sb.Append(title).Append('\n');
        sb.Append(section);
    }

    private static void Line(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        sb.Append(label.PadRight(LabelWidth)).Append(": ").Append(value).Append('\n');
    }

    /// <summary>Matches the classic report's single combined "Codec ID" line (e.g.
    /// "isom (isom/iso2/avc1/mp41)") — MediaInfoLib exposes CodecID and CodecID_Compatible as two
    /// separate Get() values that Inform() only ever renders together.</summary>
    private static string? CombineCodecId(string? codecId, string? compatible)
    {
        if (string.IsNullOrWhiteSpace(codecId))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(compatible) ? codecId : $"{codecId} ({compatible})";
    }

    private static string? FormatKbps(int? kbps) => kbps is > 0 ? $"{kbps} kb/s" : null;

    private static string? FormatFps(double? fps) => fps is > 0 ? $"{fps.Value.ToString("0.000", CultureInfo.InvariantCulture)} FPS" : null;

    /// <summary>MediaInfoLib's own classic report uses binary byte units (GiB/MiB/KiB, base 1024)
    /// rather than this app's usual decimal-labeled ByteSizeFormatter — kept local to this
    /// MediaInfo-specific view rather than changing that shared formatter's output everywhere else.</summary>
    private static string FormatBinarySize(long bytes)
    {
        double size = bytes;
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var i = 0;
        while (size >= 1024 && i < units.Length - 1)
        {
            size /= 1024;
            i++;
        }

        return $"{size.ToString("0.##", CultureInfo.InvariantCulture)} {units[i]}";
    }

    /// <summary>A stream's "Stream size" line also carries what percentage of the whole file that
    /// stream accounts for (e.g. "7.89 GiB (96%)"), same as the classic report.</summary>
    private static string? FormatStreamSize(long? streamBytes, long? totalFileBytes)
    {
        if (streamBytes is not > 0)
        {
            return null;
        }

        var formatted = FormatBinarySize(streamBytes.Value);
        if (totalFileBytes is not > 0)
        {
            return formatted;
        }

        var percent = (int)Math.Round(streamBytes.Value * 100.0 / totalFileBytes.Value);
        return $"{formatted} ({percent}%)";
    }

    /// <summary>Matches MediaInfoLib's own classic-report duration style ("1 h 9 min", "45 min",
    /// "32 s") rather than a raw seconds count.</summary>
    public static string? FormatDuration(double? seconds)
    {
        if (seconds is not > 0)
        {
            return null;
        }

        var span = TimeSpan.FromSeconds(seconds.Value);
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours} h {span.Minutes} min";
        }

        if (span.TotalMinutes >= 1)
        {
            return $"{(int)span.TotalMinutes} min";
        }

        return $"{(int)span.TotalSeconds} s";
    }
}
