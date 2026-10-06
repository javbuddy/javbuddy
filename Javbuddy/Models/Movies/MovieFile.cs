using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Javbuddy.Services.Common;
using Javbuddy.Services.MediaInfo;
using Javbuddy.Services.Movies;

namespace Javbuddy.Models;

/// <summary>Represents a single video file/version associated with a Movie (e.g. standard release,
/// RIFE 60fps, 4K remux, or AI upscale).</summary>
public class MovieFile : IMediaTechnicalInfo
{
    public int Id { get; set; }

    public int MovieId { get; set; }
    public Movie Movie { get; set; } = null!;

    /// <summary>The video file's name (no directory path), e.g. "MIDE-400.mkv" or "MIDE-400-RIFE-3.1.webm".</summary>
    [Required]
    [StringLength(500)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>Extracted version tag (e.g. "Original", "RIFE-3.1", "4K", "Upscale").</summary>
    [Required]
    [StringLength(100)]
    public string VersionTag { get; set; } = "Original";

    public long FileSizeBytes { get; set; }

    /// <summary>The video file's on-disk CreationTimeUtc the first time it was observed.</summary>
    public DateTime? FileAddedAt { get; set; }

    /// <summary>Change-detection fingerprint: on-disk LastWriteTimeUtc.</summary>
    public DateTime? LastWriteUtc { get; set; }

    /// <summary>True if this is the primary/preferred version of the movie.</summary>
    public bool IsPrimary { get; set; }

    /// <summary>True when the user picked this version as primary on Movie Detail, so a
    /// library refresh keeps it primary instead of re-deriving one (MovieVersionParser.DeterminePrimaryFile).</summary>
    public bool IsPrimaryPinned { get; set; }

    /// <summary>This version's VR / 3D format (see VrFormat), or null for flat video.
    /// Detected on every library refresh unless VrTypePinned.</summary>
    [StringLength(20)]
    public string? VrType { get; set; }

    /// <summary>True when the user set VrType by hand (including to none) on Movie Detail, so a
    /// library refresh keeps it instead of detecting it again.</summary>
    public bool VrTypePinned { get; set; }

    public string? ContainerFormat { get; set; }
    public string? ContainerFormatProfile { get; set; }
    public string? ContainerCodecId { get; set; }
    public string? ContainerCodecIdCompatible { get; set; }
    public string? WritingApplication { get; set; }
    public double? DurationSeconds { get; set; }
    public int? OverallBitRateKbps { get; set; }
    public string? VideoStreamId { get; set; }
    public string? VideoCodec { get; set; }
    public string? VideoFormatInfo { get; set; }
    public string? VideoProfile { get; set; }
    public string? VideoFormatSettings { get; set; }
    public string? VideoCodecId { get; set; }
    public string? VideoCodecIdInfo { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? AspectRatio { get; set; }
    public double? FrameRate { get; set; }
    public string? FrameRateMode { get; set; }
    public double? FrameRateMin { get; set; }
    public double? FrameRateMax { get; set; }
    public int? VideoBitRateKbps { get; set; }
    public string? ColorSpace { get; set; }
    public string? ChromaSubsampling { get; set; }
    public int? BitDepth { get; set; }
    public string? ScanType { get; set; }
    public double? BitsPerPixelFrame { get; set; }
    public long? VideoStreamSizeBytes { get; set; }
    public string? WritingLibrary { get; set; }
    public string? EncodingSettings { get; set; }
    public string? ColorRange { get; set; }
    public string? ColorPrimaries { get; set; }
    public string? TransferCharacteristics { get; set; }
    public string? MatrixCoefficients { get; set; }
    public string? CodecConfigurationBox { get; set; }
    public string? AudioStreamId { get; set; }
    public string? AudioCodec { get; set; }
    public string? AudioFormatInfo { get; set; }
    public string? AudioFormatSettings { get; set; }
    public string? AudioCodecId { get; set; }
    public string? AudioBitRateMode { get; set; }
    public int? AudioBitRateKbps { get; set; }
    public int? AudioChannels { get; set; }
    public string? AudioChannelLayout { get; set; }
    public int? AudioSamplingRateHz { get; set; }
    public double? AudioFrameRate { get; set; }
    public string? AudioCompressionMode { get; set; }
    public long? AudioStreamSizeBytes { get; set; }
    public bool? AudioDefault { get; set; }
    public int? AudioAlternateGroup { get; set; }
    public int? SubtitleCount { get; set; }

    public DateTime? MediaScannedAt { get; set; }
    public string? MediaScanError { get; set; }

    [NotMapped]
    public string FileSizeDisplay => ByteSizeFormatter.Format(FileSizeBytes);

    [NotMapped]
    public string? ResolutionDisplay => MediaQualityLabelFormatter.FormatDetail(Width, Height, ScanType);

    // IMediaTechnicalInfo explicit/implicit mapping
    string? IMediaTechnicalInfo.MediaVideoFileName => FileName;
    long? IMediaTechnicalInfo.LocalFileSizeBytes => FileSizeBytes;
    DateTime? IMediaTechnicalInfo.MediaScannedAt => MediaScannedAt;
    string? IMediaTechnicalInfo.MediaScanError => MediaScanError;
    string? IMediaTechnicalInfo.MediaContainerFormat => ContainerFormat;
    string? IMediaTechnicalInfo.MediaContainerFormatProfile => ContainerFormatProfile;
    string? IMediaTechnicalInfo.MediaContainerCodecId => ContainerCodecId;
    string? IMediaTechnicalInfo.MediaContainerCodecIdCompatible => ContainerCodecIdCompatible;
    string? IMediaTechnicalInfo.MediaWritingApplication => WritingApplication;
    double? IMediaTechnicalInfo.MediaDurationSeconds => DurationSeconds;
    int? IMediaTechnicalInfo.MediaOverallBitRateKbps => OverallBitRateKbps;
    string? IMediaTechnicalInfo.MediaVideoStreamId => VideoStreamId;
    string? IMediaTechnicalInfo.MediaVideoCodec => VideoCodec;
    string? IMediaTechnicalInfo.MediaVideoFormatInfo => VideoFormatInfo;
    string? IMediaTechnicalInfo.MediaVideoProfile => VideoProfile;
    string? IMediaTechnicalInfo.MediaVideoFormatSettings => VideoFormatSettings;
    string? IMediaTechnicalInfo.MediaVideoCodecId => VideoCodecId;
    string? IMediaTechnicalInfo.MediaVideoCodecIdInfo => VideoCodecIdInfo;
    int? IMediaTechnicalInfo.MediaWidth => Width;
    int? IMediaTechnicalInfo.MediaHeight => Height;
    string? IMediaTechnicalInfo.MediaAspectRatio => AspectRatio;
    double? IMediaTechnicalInfo.MediaFrameRate => FrameRate;
    string? IMediaTechnicalInfo.MediaFrameRateMode => FrameRateMode;
    double? IMediaTechnicalInfo.MediaFrameRateMin => FrameRateMin;
    double? IMediaTechnicalInfo.MediaFrameRateMax => FrameRateMax;
    int? IMediaTechnicalInfo.MediaVideoBitRateKbps => VideoBitRateKbps;
    string? IMediaTechnicalInfo.MediaColorSpace => ColorSpace;
    string? IMediaTechnicalInfo.MediaChromaSubsampling => ChromaSubsampling;
    int? IMediaTechnicalInfo.MediaBitDepth => BitDepth;
    string? IMediaTechnicalInfo.MediaScanType => ScanType;
    double? IMediaTechnicalInfo.MediaBitsPerPixelFrame => BitsPerPixelFrame;
    long? IMediaTechnicalInfo.MediaVideoStreamSizeBytes => VideoStreamSizeBytes;
    string? IMediaTechnicalInfo.MediaWritingLibrary => WritingLibrary;
    string? IMediaTechnicalInfo.MediaEncodingSettings => EncodingSettings;
    string? IMediaTechnicalInfo.MediaColorRange => ColorRange;
    string? IMediaTechnicalInfo.MediaColorPrimaries => ColorPrimaries;
    string? IMediaTechnicalInfo.MediaTransferCharacteristics => TransferCharacteristics;
    string? IMediaTechnicalInfo.MediaMatrixCoefficients => MatrixCoefficients;
    string? IMediaTechnicalInfo.MediaCodecConfigurationBox => CodecConfigurationBox;
    string? IMediaTechnicalInfo.MediaAudioStreamId => AudioStreamId;
    string? IMediaTechnicalInfo.MediaAudioCodec => AudioCodec;
    string? IMediaTechnicalInfo.MediaAudioFormatInfo => AudioFormatInfo;
    string? IMediaTechnicalInfo.MediaAudioFormatSettings => AudioFormatSettings;
    string? IMediaTechnicalInfo.MediaAudioCodecId => AudioCodecId;
    string? IMediaTechnicalInfo.MediaAudioBitRateMode => AudioBitRateMode;
    int? IMediaTechnicalInfo.MediaAudioBitRateKbps => AudioBitRateKbps;
    int? IMediaTechnicalInfo.MediaAudioChannels => AudioChannels;
    string? IMediaTechnicalInfo.MediaAudioChannelLayout => AudioChannelLayout;
    int? IMediaTechnicalInfo.MediaAudioSamplingRateHz => AudioSamplingRateHz;
    double? IMediaTechnicalInfo.MediaAudioFrameRate => AudioFrameRate;
    string? IMediaTechnicalInfo.MediaAudioCompressionMode => AudioCompressionMode;
    long? IMediaTechnicalInfo.MediaAudioStreamSizeBytes => AudioStreamSizeBytes;
    bool? IMediaTechnicalInfo.MediaAudioDefault => AudioDefault;
    int? IMediaTechnicalInfo.MediaAudioAlternateGroup => AudioAlternateGroup;
    int? IMediaTechnicalInfo.MediaSubtitleCount => SubtitleCount;
}
