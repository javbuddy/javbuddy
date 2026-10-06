using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.MediaInfo;

namespace Javbuddy.Tests.Services.LocalLibrary;

public class LocalLibraryMetadataMapperTests
{
    [Fact]
    public void Apply_MapsScalarFieldsAndFileSize()
    {
        var movie = new Movie { Code = "SIVR-505" };
        var metadata = new LocalMovieMetadata
        {
            Title = "Local Title",
            OriginalTitle = "Local Original",
            Plot = "Local plot.",
            ReleaseDate = new DateTime(2026, 8, 13),
            Director = "ZAMPA",
            Studio = "S1",
            Label = "S1 VR",
            SeriesName = "S1 VR",
            RatingScore = 10,
            RatingVotes = 1,
            RuntimeMinutes = 69,
            VideoFileSizeBytes = 5_000_000_000,
        };

        LocalLibraryMetadataMapper.Apply(movie, metadata);

        Assert.Equal("Local Title", movie.MetaTitle);
        Assert.Equal("Local Original", movie.MetaOriginalTitle);
        Assert.Equal("Local plot.", movie.MetaDescription);
        Assert.Equal(new DateTime(2026, 8, 13), movie.MetaReleaseDate);
        Assert.Equal("ZAMPA", movie.MetaDirector);
        Assert.Equal("S1", movie.MetaStudio);
        Assert.Equal("S1 VR", movie.MetaLabel);
        Assert.Equal(69, movie.MetaRuntimeMinutes);
        Assert.Equal(5_000_000_000, movie.LocalFileSizeBytes);
        Assert.Equal("Local", movie.MetaSourceName);
        Assert.NotNull(movie.MetaFetchedAt);
    }

    [Fact]
    public void Apply_MapsSubtitleAndTrailerFlags()
    {
        var movie = new Movie { Code = "SIVR-505" };
        var metadata = new LocalMovieMetadata { HasSubtitleFile = true, HasTrailerFile = true };

        LocalLibraryMetadataMapper.Apply(movie, metadata);

        Assert.True(movie.MediaHasSubtitleFile);
        Assert.True(movie.MediaHasTrailerFile);
    }

    [Fact]
    public void Apply_ActressesAndGenresJoined()
    {
        var movie = new Movie { Code = "SIVR-505" };
        var metadata = new LocalMovieMetadata
        {
            Actresses = ["Araki Noa"],
            Genres = ["VR", "Solowork"],
        };

        LocalLibraryMetadataMapper.Apply(movie, metadata);

        Assert.Equal("Araki Noa", movie.MetaActresses);
        Assert.Equal("VR, Solowork", movie.MetaGenres);
    }

    [Fact]
    public void Apply_EmptyActressesAndGenres_MapToNull()
    {
        var movie = new Movie { Code = "SIVR-505" };
        var metadata = new LocalMovieMetadata();

        LocalLibraryMetadataMapper.Apply(movie, metadata);

        Assert.Null(movie.MetaActresses);
        Assert.Null(movie.MetaGenres);
    }

    [Fact]
    public void Apply_FallbackUrlsMapToCoverAndBackdrop()
    {
        var movie = new Movie { Code = "SIVR-505" };
        var metadata = new LocalMovieMetadata
        {
            PosterFallbackUrl = "http://example.test/poster.jpg",
            FanartFallbackUrl = "http://example.test/fanart.jpg",
        };

        LocalLibraryMetadataMapper.Apply(movie, metadata);

        Assert.Equal("http://example.test/poster.jpg", movie.MetaCoverUrl);
        Assert.Equal("http://example.test/fanart.jpg", movie.MetaBackdropUrl);
    }

    [Fact]
    public void Apply_SuccessfulMediaProbe_MapsFieldsAndClearsError()
    {
        var movie = new Movie { Code = "SIVR-505", MediaScanError = "stale error from a previous failed probe" };
        var metadata = new LocalMovieMetadata
        {
            MediaProbe = new MediaProbeResult
            {
                Success = true,
                ContainerFormat = "MPEG-4",
                ContainerFormatProfile = "Base Media",
                ContainerCodecId = "isom",
                ContainerCodecIdCompatible = "isom/iso2/avc1/mp41",
                WritingApplication = "Lavf62.8.100",
                DurationSeconds = 10063.104,
                OverallBitRateKbps = 4972,
                VideoCodec = "AVC",
                VideoFormatInfo = "Advanced Video Codec",
                VideoProfile = "High@L4",
                VideoFormatSettings = "CABAC / 4 Ref Frames",
                VideoCodecId = "avc1",
                VideoCodecIdInfo = "Advanced Video Coding",
                Width = 1920,
                Height = 1080,
                AspectRatio = "16:9",
                FrameRate = 29.970,
                FrameRateMode = "Variable",
                FrameRateMin = 14.985,
                FrameRateMax = 29.970,
                VideoBitRateKbps = 4896,
                ColorSpace = "YUV",
                ChromaSubsampling = "4:2:0",
                BitDepth = 8,
                ScanType = "Progressive",
                BitsPerPixelFrame = 0.079,
                VideoStreamSizeBytes = 6_158_473_472,
                AudioCodec = "AAC LC",
                AudioFormatInfo = "Advanced Audio Codec Low Complexity",
                AudioFormatSettings = "PNS",
                AudioCodecId = "mp4a-40-2",
                AudioBitRateMode = "Constant",
                AudioBitRateKbps = 64,
                AudioChannels = 2,
                AudioChannelLayout = "L R",
                AudioSamplingRateHz = 48000,
                AudioFrameRate = 46.875,
                AudioStreamSizeBytes = 80_504_833,
                AudioDefault = true,
                AudioAlternateGroup = 1,
                SubtitleCount = 0,
            },
        };

        LocalLibraryMetadataMapper.Apply(movie, metadata);

        Assert.Equal("MPEG-4", movie.MediaContainerFormat);
        Assert.Equal("Base Media", movie.MediaContainerFormatProfile);
        Assert.Equal("isom", movie.MediaContainerCodecId);
        Assert.Equal("isom/iso2/avc1/mp41", movie.MediaContainerCodecIdCompatible);
        Assert.Equal("Lavf62.8.100", movie.MediaWritingApplication);
        Assert.Equal(10063.104, movie.MediaDurationSeconds);
        Assert.Equal(1920, movie.MediaWidth);
        Assert.Equal(1080, movie.MediaHeight);
        Assert.Equal("AVC", movie.MediaVideoCodec);
        Assert.Equal("Advanced Video Codec", movie.MediaVideoFormatInfo);
        Assert.Equal("High@L4", movie.MediaVideoProfile);
        Assert.Equal("CABAC / 4 Ref Frames", movie.MediaVideoFormatSettings);
        Assert.Equal("avc1", movie.MediaVideoCodecId);
        Assert.Equal("16:9", movie.MediaAspectRatio);
        Assert.Equal("Variable", movie.MediaFrameRateMode);
        Assert.Equal(14.985, movie.MediaFrameRateMin);
        Assert.Equal(29.970, movie.MediaFrameRateMax);
        Assert.Equal("YUV", movie.MediaColorSpace);
        Assert.Equal("4:2:0", movie.MediaChromaSubsampling);
        Assert.Equal(8, movie.MediaBitDepth);
        Assert.Equal("Progressive", movie.MediaScanType);
        Assert.Equal(0.079, movie.MediaBitsPerPixelFrame);
        Assert.Equal(6_158_473_472, movie.MediaVideoStreamSizeBytes);
        Assert.Equal("AAC LC", movie.MediaAudioCodec);
        Assert.Equal("mp4a-40-2", movie.MediaAudioCodecId);
        Assert.Equal("Constant", movie.MediaAudioBitRateMode);
        Assert.Equal("L R", movie.MediaAudioChannelLayout);
        Assert.Equal(48000, movie.MediaAudioSamplingRateHz);
        Assert.Equal(46.875, movie.MediaAudioFrameRate);
        Assert.Equal(80_504_833, movie.MediaAudioStreamSizeBytes);
        Assert.True(movie.MediaAudioDefault);
        Assert.Equal(1, movie.MediaAudioAlternateGroup);
        Assert.Null(movie.MediaScanError);
        Assert.NotNull(movie.MediaScannedAt);
    }

    [Fact]
    public void Apply_FailedMediaProbe_ClearsFieldsAndRecordsError()
    {
        var movie = new Movie { Code = "SIVR-505", MediaWidth = 1920, MediaHeight = 1080 };
        var metadata = new LocalMovieMetadata
        {
            MediaProbe = MediaProbeResult.Failed("MediaInfo could not open the video file."),
        };

        LocalLibraryMetadataMapper.Apply(movie, metadata);

        Assert.Null(movie.MediaWidth);
        Assert.Null(movie.MediaHeight);
        Assert.Equal("MediaInfo could not open the video file.", movie.MediaScanError);
        Assert.NotNull(movie.MediaScannedAt);
    }

    [Fact]
    public void Apply_SkippedMediaProbe_LeavesExistingFieldsUntouched()
    {
        var movie = new Movie
        {
            Code = "SIVR-505",
            MediaWidth = 1920,
            MediaHeight = 1080,
            MediaScannedAt = new DateTime(2026, 1, 1),
        };
        var metadata = new LocalMovieMetadata { MediaProbe = MediaProbeResult.SkippedResult() };

        LocalLibraryMetadataMapper.Apply(movie, metadata);

        Assert.Equal(1920, movie.MediaWidth);
        Assert.Equal(1080, movie.MediaHeight);
        Assert.Equal(new DateTime(2026, 1, 1), movie.MediaScannedAt);
    }

    [Fact]
    public void Apply_NoMediaProbe_LeavesExistingFieldsUntouched()
    {
        var movie = new Movie { Code = "SIVR-505", MediaWidth = 1920 };
        var metadata = new LocalMovieMetadata();

        LocalLibraryMetadataMapper.Apply(movie, metadata);

        Assert.Equal(1920, movie.MediaWidth);
    }

    [Fact]
    public void ClearLocalFileData_ClearsFileSizeAndAllMediaFields_ButLeavesDescriptiveMetadata()
    {
        var movie = new Movie
        {
            Code = "SIVR-505",
            MetaTitle = "Kept",
            MetaGenres = "VR, Solowork",
            LocalFileSizeBytes = 5_000_000_000,
            MediaHasSubtitleFile = true,
            MediaHasTrailerFile = true,
            MediaContainerFormat = "MPEG-4",
            MediaWidth = 1920,
            MediaHeight = 1080,
            MediaAudioChannels = 2,
            MediaSubtitleCount = 1,
            MediaScannedAt = DateTime.UtcNow,
            MediaScanError = "some old error",
            MediaScanWarning = "some old warning",
            FileAddedAt = DateTime.UtcNow.AddYears(-1),
        };

        LocalLibraryMetadataMapper.ClearLocalFileData(movie);

        Assert.Null(movie.LocalFileSizeBytes);
        Assert.False(movie.MediaHasSubtitleFile);
        Assert.False(movie.MediaHasTrailerFile);
        Assert.Null(movie.MediaContainerFormat);
        Assert.Null(movie.MediaWidth);
        Assert.Null(movie.MediaHeight);
        Assert.Null(movie.MediaAudioChannels);
        Assert.Null(movie.MediaSubtitleCount);
        Assert.Null(movie.MediaScannedAt);
        Assert.Null(movie.MediaScanError);
        Assert.Null(movie.MediaScanWarning);
        Assert.Null(movie.FileAddedAt);

        Assert.Equal("Kept", movie.MetaTitle);
        Assert.Equal("VR, Solowork", movie.MetaGenres);
    }
}
