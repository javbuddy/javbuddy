using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;

namespace Javbuddy.Tests.Services.Movies;

public class MediaInfoTextFormatterTests
{
    private static Movie ScannedMovie() => new()
    {
        Code = "ABC-123",
        MediaScannedAt = DateTime.UtcNow,
        LocalFileSizeBytes = 1024L * 1024 * 1024 * 2,
        MediaContainerFormat = "MPEG-4",
        MediaContainerFormatProfile = "Base Media / Version 2",
        MediaContainerCodecId = "mp42",
        MediaDurationSeconds = 69 * 60 + 5,
        MediaOverallBitRateKbps = 8500,
        MediaFrameRate = 29.97,
        MediaVideoStreamId = "1",
        MediaVideoCodec = "AVC",
        MediaVideoFormatInfo = "Advanced Video Codec",
        MediaVideoProfile = "High@L4",
        MediaWidth = 1920,
        MediaHeight = 1080,
        MediaAspectRatio = "16:9",
        MediaVideoBitRateKbps = 8000,
        MediaColorSpace = "YUV",
        MediaChromaSubsampling = "4:2:0",
        MediaBitDepth = 8,
        MediaAudioStreamId = "2",
        MediaAudioCodec = "AAC",
        MediaAudioChannels = 2,
        MediaAudioBitRateKbps = 317,
        MediaAudioSamplingRateHz = 48000,
        MediaSubtitleCount = 1,
    };

    /// <summary>Mirrors a real probe's fields, checked against MediaInfoLib's own Inform() output —
    /// regression coverage for the fields the classic report needs that the original probe didn't capture (writing
    /// library/encoding settings, color metadata, codec configuration box, stream IDs, compression
    /// mode, combined Codec ID, and the file path/complete name).</summary>
    private static Movie RealSampleMovie() => new()
    {
        Code = "MIDV-907",
        MediaScannedAt = DateTime.UtcNow,
        MediaVideoFileName = "MIDV-907.mp4",
        LocalFileSizeBytes = 8_869_842_944, // 8.26 GiB
        MediaContainerFormat = "MPEG-4",
        MediaContainerFormatProfile = "Base Media",
        MediaContainerCodecId = "isom",
        MediaContainerCodecIdCompatible = "isom/iso2/avc1/mp41",
        MediaDurationSeconds = 3 * 3600 + 16 * 60,
        MediaOverallBitRateKbps = 6017,
        MediaFrameRate = 29.970,
        MediaWritingApplication = "Lavf59.27.100",
        MediaVideoStreamId = "1",
        MediaVideoCodec = "AVC",
        MediaVideoFormatInfo = "Advanced Video Codec",
        MediaVideoProfile = "Main@L4",
        MediaVideoFormatSettings = "CABAC / 3 Ref Frames",
        MediaVideoCodecId = "avc1",
        MediaVideoCodecIdInfo = "Advanced Video Coding",
        MediaVideoBitRateKbps = 5751,
        MediaWidth = 1920,
        MediaHeight = 1080,
        MediaAspectRatio = "16:9",
        MediaFrameRateMode = "Constant",
        MediaColorSpace = "YUV",
        MediaChromaSubsampling = "4:2:0",
        MediaBitDepth = 8,
        MediaScanType = "Progressive",
        MediaBitsPerPixelFrame = 0.093,
        MediaVideoStreamSizeBytes = 8_472_252_170, // 7.89 GiB, ~96% of the file
        MediaWritingLibrary = "x264 core 164 r3156 d46938d",
        MediaEncodingSettings = "cabac=1 / ref=3 / rc=abr / bitrate=5744",
        MediaColorRange = "Limited",
        MediaColorPrimaries = "BT.709",
        MediaTransferCharacteristics = "BT.709",
        MediaMatrixCoefficients = "BT.709",
        MediaCodecConfigurationBox = "avcC",
        MediaAudioStreamId = "2",
        MediaAudioCodec = "AAC LC",
        MediaAudioFormatInfo = "Advanced Audio Codec Low Complexity",
        MediaAudioCodecId = "mp4a-40-2",
        MediaAudioBitRateMode = "Constant",
        MediaAudioBitRateKbps = 256,
        MediaAudioChannels = 2,
        MediaAudioChannelLayout = "L R",
        MediaAudioSamplingRateHz = 48000,
        MediaAudioFrameRate = 46.875,
        MediaAudioCompressionMode = "Lossy",
        MediaAudioStreamSizeBytes = 377_487_360, // 360 MiB, ~4% of the file
        MediaAudioDefault = true,
        MediaAudioAlternateGroup = 1,
    };

    [Fact]
    public void FormatFull_NeverScanned_ReturnsNull()
    {
        var movie = new Movie { Code = "ABC-123" };
        Assert.Null(MediaInfoTextFormatter.FormatFull(movie));
    }

    [Fact]
    public void FormatFull_ProducesGeneralVideoAudioSections()
    {
        var report = MediaInfoTextFormatter.FormatFull(ScannedMovie());

        Assert.NotNull(report);
        Assert.Contains("General\n", report);
        Assert.Contains("Video\n", report);
        Assert.Contains("Audio\n", report);
        Assert.Contains("Text\n", report);
        Assert.Contains("Format                    : MPEG-4", report);
        Assert.Contains("Width                     : 1920 pixels", report);
        Assert.Contains("Channel(s)                : 2 channels", report);
        Assert.Contains("Count                     : 1", report);
    }

    [Fact]
    public void FormatFull_OmitsMissingFields()
    {
        var movie = new Movie
        {
            Code = "ABC-123",
            MediaScannedAt = DateTime.UtcNow,
            MediaVideoCodec = "AVC",
        };

        var report = MediaInfoTextFormatter.FormatFull(movie);

        Assert.NotNull(report);
        Assert.DoesNotContain("Chroma subsampling", report);
        Assert.DoesNotContain("Audio", report);
    }

    [Fact]
    public void FormatFull_MatchesRealMediaInfoClassicReportFieldByField()
    {
        var report = MediaInfoTextFormatter.FormatFull(RealSampleMovie());

        Assert.NotNull(report);
        Assert.Contains("Complete name             : MIDV-907.mp4", report);
        Assert.Contains("Codec ID                  : isom (isom/iso2/avc1/mp41)", report);
        Assert.Contains("File size                 : 8.26 GiB", report);
        Assert.Contains("Duration                  : 3 h 16 min", report);

        Assert.Contains("ID                        : 1", report);
        Assert.Contains("Writing library           : x264 core 164 r3156 d46938d", report);
        Assert.Contains("Color range               : Limited", report);
        Assert.Contains("Color primaries           : BT.709", report);
        Assert.Contains("Transfer characteristics  : BT.709", report);
        Assert.Contains("Matrix coefficients       : BT.709", report);
        Assert.Contains("Codec configuration box   : avcC", report);
        Assert.Contains("Stream size               : 7.89 GiB (96%)", report);

        Assert.Contains("ID                        : 2", report);
        Assert.Contains("Compression mode          : Lossy", report);
        Assert.Contains("Stream size               : 360 MiB (4%)", report);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0.0, null)]
    [InlineData(45.0, "45 s")]
    [InlineData(90.0, "1 min")]
    [InlineData(4145.0, "1 h 9 min")]
    public void FormatDuration_MatchesClassicReportStyle(double? seconds, string? expected)
    {
        Assert.Equal(expected, MediaInfoTextFormatter.FormatDuration(seconds));
    }

    [Fact]
    public void FormatFull_MovieFile_MatchesMovieOutput()
    {
        var movie = RealSampleMovie();
        var file = new MovieFile
        {
            FileName = movie.MediaVideoFileName!,
            FileSizeBytes = movie.LocalFileSizeBytes!.Value,
            VersionTag = "Original",
        };
        LocalLibraryMetadataMapper.CopyLegacyMovieToFile(movie, file);

        var movieReport = MediaInfoTextFormatter.FormatFull(movie);
        var fileReport = MediaInfoTextFormatter.FormatFull(file);

        Assert.NotNull(movieReport);
        Assert.Equal(movieReport, fileReport);
    }

}
