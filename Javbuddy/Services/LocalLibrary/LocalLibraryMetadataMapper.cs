using Javbuddy.Models;
using Javbuddy.Services.MediaInfo;
using Javbuddy.Services.Movies;

namespace Javbuddy.Services.LocalLibrary;

public static class LocalLibraryMetadataMapper
{
    public static void Apply(Movie movie, LocalMovieMetadata metadata)
    {
        ApplyDescriptiveMetadata(movie, metadata);

        movie.LocalFileSizeBytes = metadata.VideoFileSizeBytes;
        movie.MediaVideoFileName = metadata.VideoFileName;
        movie.MediaHasSubtitleFile = metadata.HasSubtitleFile;
        movie.MediaHasTrailerFile = metadata.HasTrailerFile;

        ApplyMediaProbe(movie, metadata.MediaProbe);
    }

    /// <summary>Just the javinizer-go-adjacent Meta* fields (title, plot, actresses, ...) — split
    /// out so RefreshLocalMetadataIfNfoChangedAsync can re-apply an edited .nfo's descriptive
    /// content without touching LocalFileSizeBytes/MediaHasSubtitleFile/the Media* probe block,
    /// which it doesn't have fresh values for (unlike the full local scan Apply above runs as part
    /// of, this is called on its own, independent of any video/subtitle probing that run).</summary>
    public static void ApplyDescriptiveMetadata(Movie movie, LocalMovieMetadata metadata)
    {
        movie.MetaTitle = metadata.Title;
        movie.MetaOriginalTitle = metadata.OriginalTitle;
        movie.MetaDescription = metadata.Plot;
        movie.MetaReleaseDate = metadata.ReleaseDate;
        movie.MetaDirector = metadata.Director;
        movie.MetaStudio = metadata.Studio;
        movie.MetaLabel = metadata.Label;
        movie.MetaSeries = metadata.SeriesName;
        movie.MetaRatingScore = metadata.RatingScore;
        movie.MetaRatingVotes = metadata.RatingVotes;
        movie.MetaRuntimeMinutes = metadata.RuntimeMinutes;
        movie.MetaActresses = metadata.Actresses.Count > 0 ? string.Join(", ", metadata.Actresses) : null;
        movie.MetaGenres = metadata.Genres.Count > 0 ? string.Join(", ", metadata.Genres) : null;

        // These hold the *remote* fallback URL only — pages always request images through the
        // /image-cache/{code}/{role}/{variant} endpoint (built from movie.Code), which prefers a
        // local library file and only redirects to these remote URLs when no local file exists.
        // A single stored string can't represent both a thumb and a full variant, so unlike the
        // old /local-media scheme these are never a local path.
        movie.MetaCoverUrl = metadata.PosterFallbackUrl;
        movie.MetaBackdropUrl = metadata.FanartFallbackUrl;

        movie.MetaSourceName = "Local";
        movie.MetaFetchedAt = DateTime.UtcNow;
    }

    /// <summary>Applies just the MediaInfo technical fields, independent of the rest of Apply's
    /// javinizer-go-adjacent Meta* fields above. Split out so LibraryRescanTask can rescan
    /// technical info for an existing library without also overwriting descriptive metadata that
    /// might currently be sourced from javinizer-go with whatever a local .nfo happens to contain
    /// — that's only ever appropriate as a side effect of the user's own explicit "Local" refresh,
    /// which still goes through Apply above.</summary>
    public static void ApplyMediaProbe(Movie movie, MediaProbeResult? probe)
    {
        if (probe is not { Skipped: false }) return;

        movie.MediaContainerFormat = probe.ContainerFormat;
        movie.MediaContainerFormatProfile = probe.ContainerFormatProfile;
        movie.MediaContainerCodecId = probe.ContainerCodecId;
        movie.MediaContainerCodecIdCompatible = probe.ContainerCodecIdCompatible;
        movie.MediaWritingApplication = probe.WritingApplication;
        movie.MediaDurationSeconds = probe.DurationSeconds;
        movie.MediaOverallBitRateKbps = probe.OverallBitRateKbps;
        movie.MediaVideoStreamId = probe.VideoStreamId;
        movie.MediaVideoCodec = probe.VideoCodec;
        movie.MediaVideoFormatInfo = probe.VideoFormatInfo;
        movie.MediaVideoProfile = probe.VideoProfile;
        movie.MediaVideoFormatSettings = probe.VideoFormatSettings;
        movie.MediaVideoCodecId = probe.VideoCodecId;
        movie.MediaVideoCodecIdInfo = probe.VideoCodecIdInfo;
        movie.MediaWidth = probe.Width;
        movie.MediaHeight = probe.Height;
        movie.MediaAspectRatio = probe.AspectRatio;
        movie.MediaFrameRate = probe.FrameRate;
        movie.MediaFrameRateMode = probe.FrameRateMode;
        movie.MediaFrameRateMin = probe.FrameRateMin;
        movie.MediaFrameRateMax = probe.FrameRateMax;
        movie.MediaVideoBitRateKbps = probe.VideoBitRateKbps;
        movie.MediaColorSpace = probe.ColorSpace;
        movie.MediaChromaSubsampling = probe.ChromaSubsampling;
        movie.MediaBitDepth = probe.BitDepth;
        movie.MediaScanType = probe.ScanType;
        movie.MediaBitsPerPixelFrame = probe.BitsPerPixelFrame;
        movie.MediaVideoStreamSizeBytes = probe.VideoStreamSizeBytes;
        movie.MediaWritingLibrary = probe.WritingLibrary;
        movie.MediaEncodingSettings = probe.EncodingSettings;
        movie.MediaColorRange = probe.ColorRange;
        movie.MediaColorPrimaries = probe.ColorPrimaries;
        movie.MediaTransferCharacteristics = probe.TransferCharacteristics;
        movie.MediaMatrixCoefficients = probe.MatrixCoefficients;
        movie.MediaCodecConfigurationBox = probe.CodecConfigurationBox;
        movie.MediaAudioStreamId = probe.AudioStreamId;
        movie.MediaAudioCodec = probe.AudioCodec;
        movie.MediaAudioFormatInfo = probe.AudioFormatInfo;
        movie.MediaAudioFormatSettings = probe.AudioFormatSettings;
        movie.MediaAudioCodecId = probe.AudioCodecId;
        movie.MediaAudioBitRateMode = probe.AudioBitRateMode;
        movie.MediaAudioBitRateKbps = probe.AudioBitRateKbps;
        movie.MediaAudioChannels = probe.AudioChannels;
        movie.MediaAudioChannelLayout = probe.AudioChannelLayout;
        movie.MediaAudioSamplingRateHz = probe.AudioSamplingRateHz;
        movie.MediaAudioFrameRate = probe.AudioFrameRate;
        movie.MediaAudioCompressionMode = probe.AudioCompressionMode;
        movie.MediaAudioStreamSizeBytes = probe.AudioStreamSizeBytes;
        movie.MediaAudioDefault = probe.AudioDefault;
        movie.MediaAudioAlternateGroup = probe.AudioAlternateGroup;
        movie.MediaSubtitleCount = probe.SubtitleCount;
        movie.MediaScannedAt = DateTime.UtcNow;
        movie.MediaScanError = probe.Success ? null : probe.ErrorMessage;
        // A real probe just ran against an actual video file — any earlier "no video file found"
        // warning no longer applies, regardless of whether this probe itself succeeded.
        movie.MediaScanWarning = null;
    }

    /// <summary>Resets every field this mapper ever writes from a local video file — LocalFileSizeBytes
    /// and the whole Media* block — back to "never scanned". Used by LibraryRescanTask when a
    /// previously-Got movie's local folder has disappeared: those fields describe a file that no
    /// longer exists, so keeping stale values around would be actively misleading. Deliberately
    /// leaves the javinizer-go-adjacent Meta* fields (title, plot, actresses, ...) untouched — that
    /// descriptive metadata isn't tied to the file's presence and stays useful for the movie once it
    /// reverts to Missing.</summary>
    public static void ClearLocalFileData(Movie movie)
    {
        movie.LocalFileSizeBytes = null;
        movie.MediaVideoFileName = null;
        movie.MediaHasSubtitleFile = false;
        movie.MediaHasTrailerFile = false;
        movie.MediaVideoFileLastWriteUtc = null;
        movie.MediaNfoLastWriteUtc = null;
        movie.MediaImagesSignature = null;
        movie.FileAddedAt = null;

        movie.MediaContainerFormat = null;
        movie.MediaContainerFormatProfile = null;
        movie.MediaContainerCodecId = null;
        movie.MediaContainerCodecIdCompatible = null;
        movie.MediaWritingApplication = null;
        movie.MediaDurationSeconds = null;
        movie.MediaOverallBitRateKbps = null;
        movie.MediaVideoStreamId = null;
        movie.MediaVideoCodec = null;
        movie.MediaVideoFormatInfo = null;
        movie.MediaVideoProfile = null;
        movie.MediaVideoFormatSettings = null;
        movie.MediaVideoCodecId = null;
        movie.MediaVideoCodecIdInfo = null;
        movie.MediaWidth = null;
        movie.MediaHeight = null;
        movie.MediaAspectRatio = null;
        movie.MediaFrameRate = null;
        movie.MediaFrameRateMode = null;
        movie.MediaFrameRateMin = null;
        movie.MediaFrameRateMax = null;
        movie.MediaVideoBitRateKbps = null;
        movie.MediaColorSpace = null;
        movie.MediaChromaSubsampling = null;
        movie.MediaBitDepth = null;
        movie.MediaScanType = null;
        movie.MediaBitsPerPixelFrame = null;
        movie.MediaVideoStreamSizeBytes = null;
        movie.MediaWritingLibrary = null;
        movie.MediaEncodingSettings = null;
        movie.MediaColorRange = null;
        movie.MediaColorPrimaries = null;
        movie.MediaTransferCharacteristics = null;
        movie.MediaMatrixCoefficients = null;
        movie.MediaCodecConfigurationBox = null;
        movie.MediaAudioStreamId = null;
        movie.MediaAudioCodec = null;
        movie.MediaAudioFormatInfo = null;
        movie.MediaAudioFormatSettings = null;
        movie.MediaAudioCodecId = null;
        movie.MediaAudioBitRateMode = null;
        movie.MediaAudioBitRateKbps = null;
        movie.MediaAudioChannels = null;
        movie.MediaAudioChannelLayout = null;
        movie.MediaAudioSamplingRateHz = null;
        movie.MediaAudioFrameRate = null;
        movie.MediaAudioCompressionMode = null;
        movie.MediaAudioStreamSizeBytes = null;
        movie.MediaAudioDefault = null;
        movie.MediaAudioAlternateGroup = null;
        movie.MediaSubtitleCount = null;

        movie.MediaScannedAt = null;
        movie.MediaScanError = null;
        movie.MediaScanWarning = null;
        movie.FileCount = 0;
        movie.VrType = null;
    }

    /// <summary>Applies MediaInfo probe results directly to a MovieFile entity.</summary>
    public static void ApplyMediaProbe(MovieFile file, MediaProbeResult? probe)
    {
        if (probe is not { Skipped: false }) return;

        file.ContainerFormat = probe.ContainerFormat;
        file.ContainerFormatProfile = probe.ContainerFormatProfile;
        file.ContainerCodecId = probe.ContainerCodecId;
        file.ContainerCodecIdCompatible = probe.ContainerCodecIdCompatible;
        file.WritingApplication = probe.WritingApplication;
        file.DurationSeconds = probe.DurationSeconds;
        file.OverallBitRateKbps = probe.OverallBitRateKbps;
        file.VideoStreamId = probe.VideoStreamId;
        file.VideoCodec = probe.VideoCodec;
        file.VideoFormatInfo = probe.VideoFormatInfo;
        file.VideoProfile = probe.VideoProfile;
        file.VideoFormatSettings = probe.VideoFormatSettings;
        file.VideoCodecId = probe.VideoCodecId;
        file.VideoCodecIdInfo = probe.VideoCodecIdInfo;
        file.Width = probe.Width;
        file.Height = probe.Height;
        file.AspectRatio = probe.AspectRatio;
        file.FrameRate = probe.FrameRate;
        file.FrameRateMode = probe.FrameRateMode;
        file.FrameRateMin = probe.FrameRateMin;
        file.FrameRateMax = probe.FrameRateMax;
        file.VideoBitRateKbps = probe.VideoBitRateKbps;
        file.ColorSpace = probe.ColorSpace;
        file.ChromaSubsampling = probe.ChromaSubsampling;
        file.BitDepth = probe.BitDepth;
        file.ScanType = probe.ScanType;
        file.BitsPerPixelFrame = probe.BitsPerPixelFrame;
        file.VideoStreamSizeBytes = probe.VideoStreamSizeBytes;
        file.WritingLibrary = probe.WritingLibrary;
        file.EncodingSettings = probe.EncodingSettings;
        file.ColorRange = probe.ColorRange;
        file.ColorPrimaries = probe.ColorPrimaries;
        file.TransferCharacteristics = probe.TransferCharacteristics;
        file.MatrixCoefficients = probe.MatrixCoefficients;
        file.CodecConfigurationBox = probe.CodecConfigurationBox;
        file.AudioStreamId = probe.AudioStreamId;
        file.AudioCodec = probe.AudioCodec;
        file.AudioFormatInfo = probe.AudioFormatInfo;
        file.AudioFormatSettings = probe.AudioFormatSettings;
        file.AudioCodecId = probe.AudioCodecId;
        file.AudioBitRateMode = probe.AudioBitRateMode;
        file.AudioBitRateKbps = probe.AudioBitRateKbps;
        file.AudioChannels = probe.AudioChannels;
        file.AudioChannelLayout = probe.AudioChannelLayout;
        file.AudioSamplingRateHz = probe.AudioSamplingRateHz;
        file.AudioFrameRate = probe.AudioFrameRate;
        file.AudioCompressionMode = probe.AudioCompressionMode;
        file.AudioStreamSizeBytes = probe.AudioStreamSizeBytes;
        file.AudioDefault = probe.AudioDefault;
        file.AudioAlternateGroup = probe.AudioAlternateGroup;
        file.SubtitleCount = probe.SubtitleCount;
        file.MediaScannedAt = DateTime.UtcNow;
        file.MediaScanError = probe.Success ? null : probe.ErrorMessage;
    }

    /// <summary>Synchronizes primary version specs and aggregated counts/sizes onto Movie.</summary>
    /// <summary>Re-derives every unpinned version's VR / 3D format and version tag from its file
    /// name, frame shape and the movie's genres. No I/O, so a refresh that skips the
    /// probe still runs it. Returns whether anything changed.</summary>
    public static bool ApplyVrFormats(Movie movie)
    {
        var changed = false;
        foreach (var file in movie.MovieFiles)
        {
            var tag = MovieVersionParser.ExtractVersionTag(file.FileName, movie.Code);
            if (file.VersionTag != tag)
            {
                file.VersionTag = tag;
                changed = true;
            }
            if (file.VrTypePinned) continue;

            var vrType = VrFormat.Detect(file.FileName, movie.Code, file.Width, file.Height, movie.MetaGenres);
            if (file.VrType != vrType)
            {
                file.VrType = vrType;
                changed = true;
            }
        }

        var primaryVrType = movie.MovieFiles.FirstOrDefault(f => f.IsPrimary)?.VrType;
        if (movie.VrType != primaryVrType)
        {
            movie.VrType = primaryVrType;
            changed = true;
        }
        return changed;
    }

    public static void SyncPrimaryFileToMovie(Movie movie, MovieFile primaryFile, int fileCount, long totalFileSizeBytes)
    {
        movie.FileCount = fileCount;
        movie.LocalFileSizeBytes = totalFileSizeBytes;
        movie.MediaVideoFileName = primaryFile.FileName;
        movie.VrType = primaryFile.VrType;
        movie.MediaVideoFileLastWriteUtc = primaryFile.LastWriteUtc;
        movie.FileAddedAt ??= primaryFile.FileAddedAt;

        movie.MediaContainerFormat = primaryFile.ContainerFormat;
        movie.MediaContainerFormatProfile = primaryFile.ContainerFormatProfile;
        movie.MediaContainerCodecId = primaryFile.ContainerCodecId;
        movie.MediaContainerCodecIdCompatible = primaryFile.ContainerCodecIdCompatible;
        movie.MediaWritingApplication = primaryFile.WritingApplication;
        movie.MediaDurationSeconds = primaryFile.DurationSeconds;
        movie.MediaOverallBitRateKbps = primaryFile.OverallBitRateKbps;
        movie.MediaVideoStreamId = primaryFile.VideoStreamId;
        movie.MediaVideoCodec = primaryFile.VideoCodec;
        movie.MediaVideoFormatInfo = primaryFile.VideoFormatInfo;
        movie.MediaVideoProfile = primaryFile.VideoProfile;
        movie.MediaVideoFormatSettings = primaryFile.VideoFormatSettings;
        movie.MediaVideoCodecId = primaryFile.VideoCodecId;
        movie.MediaVideoCodecIdInfo = primaryFile.VideoCodecIdInfo;
        movie.MediaWidth = primaryFile.Width;
        movie.MediaHeight = primaryFile.Height;
        movie.MediaAspectRatio = primaryFile.AspectRatio;
        movie.MediaFrameRate = primaryFile.FrameRate;
        movie.MediaFrameRateMode = primaryFile.FrameRateMode;
        movie.MediaFrameRateMin = primaryFile.FrameRateMin;
        movie.MediaFrameRateMax = primaryFile.FrameRateMax;
        movie.MediaVideoBitRateKbps = primaryFile.VideoBitRateKbps;
        movie.MediaColorSpace = primaryFile.ColorSpace;
        movie.MediaChromaSubsampling = primaryFile.ChromaSubsampling;
        movie.MediaBitDepth = primaryFile.BitDepth;
        movie.MediaScanType = primaryFile.ScanType;
        movie.MediaBitsPerPixelFrame = primaryFile.BitsPerPixelFrame;
        movie.MediaVideoStreamSizeBytes = primaryFile.VideoStreamSizeBytes;
        movie.MediaWritingLibrary = primaryFile.WritingLibrary;
        movie.MediaEncodingSettings = primaryFile.EncodingSettings;
        movie.MediaColorRange = primaryFile.ColorRange;
        movie.MediaColorPrimaries = primaryFile.ColorPrimaries;
        movie.MediaTransferCharacteristics = primaryFile.TransferCharacteristics;
        movie.MediaMatrixCoefficients = primaryFile.MatrixCoefficients;
        movie.MediaCodecConfigurationBox = primaryFile.CodecConfigurationBox;
        movie.MediaAudioStreamId = primaryFile.AudioStreamId;
        movie.MediaAudioCodec = primaryFile.AudioCodec;
        movie.MediaAudioFormatInfo = primaryFile.AudioFormatInfo;
        movie.MediaAudioFormatSettings = primaryFile.AudioFormatSettings;
        movie.MediaAudioCodecId = primaryFile.AudioCodecId;
        movie.MediaAudioBitRateMode = primaryFile.AudioBitRateMode;
        movie.MediaAudioBitRateKbps = primaryFile.AudioBitRateKbps;
        movie.MediaAudioChannels = primaryFile.AudioChannels;
        movie.MediaAudioChannelLayout = primaryFile.AudioChannelLayout;
        movie.MediaAudioSamplingRateHz = primaryFile.AudioSamplingRateHz;
        movie.MediaAudioFrameRate = primaryFile.AudioFrameRate;
        movie.MediaAudioCompressionMode = primaryFile.AudioCompressionMode;
        movie.MediaAudioStreamSizeBytes = primaryFile.AudioStreamSizeBytes;
        movie.MediaAudioDefault = primaryFile.AudioDefault;
        movie.MediaAudioAlternateGroup = primaryFile.AudioAlternateGroup;
        movie.MediaSubtitleCount = primaryFile.SubtitleCount;
        movie.MediaScannedAt = DateTime.UtcNow;
        movie.MediaScanError = primaryFile.MediaScanError;
        movie.MediaScanWarning = null;
    }

    /// <summary>Copies technical media metadata from Movie to a MovieFile (used when backfilling legacy single-file movies).</summary>
    public static void CopyLegacyMovieToFile(Movie movie, MovieFile file)
    {
        file.ContainerFormat = movie.MediaContainerFormat;
        file.ContainerFormatProfile = movie.MediaContainerFormatProfile;
        file.ContainerCodecId = movie.MediaContainerCodecId;
        file.ContainerCodecIdCompatible = movie.MediaContainerCodecIdCompatible;
        file.WritingApplication = movie.MediaWritingApplication;
        file.DurationSeconds = movie.MediaDurationSeconds;
        file.OverallBitRateKbps = movie.MediaOverallBitRateKbps;
        file.VideoStreamId = movie.MediaVideoStreamId;
        file.VideoCodec = movie.MediaVideoCodec;
        file.VideoFormatInfo = movie.MediaVideoFormatInfo;
        file.VideoProfile = movie.MediaVideoProfile;
        file.VideoFormatSettings = movie.MediaVideoFormatSettings;
        file.VideoCodecId = movie.MediaVideoCodecId;
        file.VideoCodecIdInfo = movie.MediaVideoCodecIdInfo;
        file.Width = movie.MediaWidth;
        file.Height = movie.MediaHeight;
        file.AspectRatio = movie.MediaAspectRatio;
        file.FrameRate = movie.MediaFrameRate;
        file.FrameRateMode = movie.MediaFrameRateMode;
        file.FrameRateMin = movie.MediaFrameRateMin;
        file.FrameRateMax = movie.MediaFrameRateMax;
        file.VideoBitRateKbps = movie.MediaVideoBitRateKbps;
        file.ColorSpace = movie.MediaColorSpace;
        file.ChromaSubsampling = movie.MediaChromaSubsampling;
        file.BitDepth = movie.MediaBitDepth;
        file.ScanType = movie.MediaScanType;
        file.BitsPerPixelFrame = movie.MediaBitsPerPixelFrame;
        file.VideoStreamSizeBytes = movie.MediaVideoStreamSizeBytes;
        file.WritingLibrary = movie.MediaWritingLibrary;
        file.EncodingSettings = movie.MediaEncodingSettings;
        file.ColorRange = movie.MediaColorRange;
        file.ColorPrimaries = movie.MediaColorPrimaries;
        file.TransferCharacteristics = movie.MediaTransferCharacteristics;
        file.MatrixCoefficients = movie.MediaMatrixCoefficients;
        file.CodecConfigurationBox = movie.MediaCodecConfigurationBox;
        file.AudioStreamId = movie.MediaAudioStreamId;
        file.AudioCodec = movie.MediaAudioCodec;
        file.AudioFormatInfo = movie.MediaAudioFormatInfo;
        file.AudioFormatSettings = movie.MediaAudioFormatSettings;
        file.AudioCodecId = movie.MediaAudioCodecId;
        file.AudioBitRateMode = movie.MediaAudioBitRateMode;
        file.AudioBitRateKbps = movie.MediaAudioBitRateKbps;
        file.AudioChannels = movie.MediaAudioChannels;
        file.AudioChannelLayout = movie.MediaAudioChannelLayout;
        file.AudioSamplingRateHz = movie.MediaAudioSamplingRateHz;
        file.AudioFrameRate = movie.MediaAudioFrameRate;
        file.AudioCompressionMode = movie.MediaAudioCompressionMode;
        file.AudioStreamSizeBytes = movie.MediaAudioStreamSizeBytes;
        file.AudioDefault = movie.MediaAudioDefault;
        file.AudioAlternateGroup = movie.MediaAudioAlternateGroup;
        file.SubtitleCount = movie.MediaSubtitleCount;
        file.MediaScannedAt = movie.MediaScannedAt;
        file.MediaScanError = movie.MediaScanError;
    }
}
