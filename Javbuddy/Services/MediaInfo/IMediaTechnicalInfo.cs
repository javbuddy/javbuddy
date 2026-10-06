namespace Javbuddy.Services.MediaInfo;

/// <summary>Common interface for entities holding technical media probe details (implemented by Movie and MovieFile),
/// allowing MediaInfoTextFormatter to format either entity uniformly.</summary>
public interface IMediaTechnicalInfo
{
    string? MediaVideoFileName { get; }
    long? LocalFileSizeBytes { get; }
    DateTime? MediaScannedAt { get; }
    string? MediaScanError { get; }
    string? MediaContainerFormat { get; }
    string? MediaContainerFormatProfile { get; }
    string? MediaContainerCodecId { get; }
    string? MediaContainerCodecIdCompatible { get; }
    string? MediaWritingApplication { get; }
    double? MediaDurationSeconds { get; }
    int? MediaOverallBitRateKbps { get; }
    string? MediaVideoStreamId { get; }
    string? MediaVideoCodec { get; }
    string? MediaVideoFormatInfo { get; }
    string? MediaVideoProfile { get; }
    string? MediaVideoFormatSettings { get; }
    string? MediaVideoCodecId { get; }
    string? MediaVideoCodecIdInfo { get; }
    int? MediaWidth { get; }
    int? MediaHeight { get; }
    string? MediaAspectRatio { get; }
    double? MediaFrameRate { get; }
    string? MediaFrameRateMode { get; }
    double? MediaFrameRateMin { get; }
    double? MediaFrameRateMax { get; }
    int? MediaVideoBitRateKbps { get; }
    string? MediaColorSpace { get; }
    string? MediaChromaSubsampling { get; }
    int? MediaBitDepth { get; }
    string? MediaScanType { get; }
    double? MediaBitsPerPixelFrame { get; }
    long? MediaVideoStreamSizeBytes { get; }
    string? MediaWritingLibrary { get; }
    string? MediaEncodingSettings { get; }
    string? MediaColorRange { get; }
    string? MediaColorPrimaries { get; }
    string? MediaTransferCharacteristics { get; }
    string? MediaMatrixCoefficients { get; }
    string? MediaCodecConfigurationBox { get; }
    string? MediaAudioStreamId { get; }
    string? MediaAudioCodec { get; }
    string? MediaAudioFormatInfo { get; }
    string? MediaAudioFormatSettings { get; }
    string? MediaAudioCodecId { get; }
    string? MediaAudioBitRateMode { get; }
    int? MediaAudioBitRateKbps { get; }
    int? MediaAudioChannels { get; }
    string? MediaAudioChannelLayout { get; }
    int? MediaAudioSamplingRateHz { get; }
    double? MediaAudioFrameRate { get; }
    string? MediaAudioCompressionMode { get; }
    long? MediaAudioStreamSizeBytes { get; }
    bool? MediaAudioDefault { get; }
    int? MediaAudioAlternateGroup { get; }
    int? MediaSubtitleCount { get; }
}
