using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Javbuddy.Services.Common;
using Javbuddy.Services.MediaInfo;

namespace Javbuddy.Models;

public class Movie : IMediaTechnicalInfo
{
    public int Id { get; set; }

    public ICollection<MovieActor> MovieActors { get; } = new List<MovieActor>();
    public ICollection<MovieFile> MovieFiles { get; } = new List<MovieFile>();
    public ICollection<MovieTag> MovieTags { get; } = new List<MovieTag>();
    public ICollection<Scene> Scenes { get; } = new List<Scene>();
    public ICollection<MovieHighlight> Highlights { get; } = new List<MovieHighlight>();

    public ICollection<MovieApex> Apexes { get; } = new List<MovieApex>();

    /// <summary>Number of local video files/versions tracked for this movie.</summary>
    public int FileCount { get; set; }

    /// <summary>The primary version's VR / 3D format (see VrFormat), or null for flat
    /// video and for movies without a local video file.</summary>
    [StringLength(20)]
    public string? VrType { get; set; }

    [NotMapped]
    public bool IsVr => !string.IsNullOrEmpty(VrType);

    /// <summary>Manual override display name. Rarely set — MetaTitle from javinizer-go takes precedence.</summary>
    [StringLength(300)]
    public string? Title { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    /// <summary>Release ID (e.g. "IPX-535") used to look up metadata via javinizer-go — the primary identifier for a movie.</summary>
    [Required]
    [StringLength(50)]
    public string? Code { get; set; }

    public MovieStatus Status { get; set; } = MovieStatus.Missing;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Metadata fetched from javinizer-go. Null until a successful fetch.
    public string? MetaTitle { get; set; }
    public string? MetaOriginalTitle { get; set; }
    public string? MetaDescription { get; set; }
    public DateTime? MetaReleaseDate { get; set; }
    public string? MetaDirector { get; set; }
    public string? MetaStudio { get; set; }
    public string? MetaLabel { get; set; }
    public string? MetaSeries { get; set; }
    public double? MetaRatingScore { get; set; }
    public int? MetaRatingVotes { get; set; }
    public string? MetaCoverUrl { get; set; }
    public string? MetaBackdropUrl { get; set; }
    public int? MetaRuntimeMinutes { get; set; }
    public string? MetaActresses { get; set; }
    public string? MetaGenres { get; set; }
    public string? MetaSourceName { get; set; }
    public string? MetaSourceUrl { get; set; }
    public DateTime? MetaFetchedAt { get; set; }

    // Jellyfin library link — ownership/"do I already have this" only. Descriptive and
    // technical metadata used to also be gathered from Jellyfin (Stage 6) but that was
    // replaced with the local file-based source below (Stage 7); this is just the link.
    public string? JellyfinItemId { get; set; }
    public string? JellyfinServerId { get; set; }
    public string? JellyfinLibraryId { get; set; }
    public string? JellyfinLibraryName { get; set; }
    public DateTime? JellyfinCheckedAt { get; set; }

    // Local file-based metadata source (Stage 7): file size of the video found in the
    // movie's local folder, if one was resolved. No codec/resolution probing — just size.
    public long? LocalFileSizeBytes { get; set; }

    // Cheap change-detection fingerprints LibraryRescanTask compares against the current
    // filesystem state on every run, so it can notice a file was replaced/edited without redoing
    // the expensive work (native MediaInfo probe, full .nfo re-parse) unless something actually
    // changed. Null means "never recorded" — the first observation after this field was
    // introduced establishes the baseline without treating it as a change (see
    // LocalLibraryClient.RefreshMediaInfoOnlyAsync / RefreshLocalMetadataIfNfoChangedAsync /
    // SyncImagesSignatureAsync), so rolling this out to an existing library can't mass-fire "this
    // changed" for every movie that was merely never tracked before.
    public DateTime? MediaVideoFileLastWriteUtc { get; set; }
    public DateTime? MediaNfoLastWriteUtc { get; set; }

    /// <summary>Whether (and in which direction) Javbuddy's metadata and the local .nfo file have
    /// drifted apart — see NfoDriftDetector. None when they agree or there's no .nfo.
    /// Detected during library rescan and after every Javbuddy edit/write that can cause drift.</summary>
    public NfoDriftKind NfoDriftKind { get; set; }

    /// <summary>Human-readable per-field drift detail, each line prefixed by its direction (e.g.
    /// "[Javbuddy changed] Title: .nfo has 'Old', canonical is 'New'"). Null when NfoDriftKind is None.</summary>
    [StringLength(1000)]
    public string? NfoConflictDetails { get; set; }

    /// <summary>Serialized NfoBaselineState: per field, both sides' values at the last check where
    /// they agreed (which gives a drift its direction), plus the .nfo's values as last parsed.</summary>
    public string? NfoBaselineJson { get; set; }

    /// <summary>Fingerprint of the .nfo file NfoBaselineJson's cached disk values were parsed
    /// from: while the file's last-write time and size still match, the drift check reuses those
    /// values instead of re-reading the file.</summary>
    public DateTime? NfoBaselineLastWriteUtc { get; set; }
    public long? NfoBaselineSize { get; set; }

    /// <summary>True when at least one name in MetaActresses doesn't match any tracked Actor
    /// (see MovieActorAssociation, which computes this alongside the MovieActor links it
    /// maintains). False when MetaActresses is empty — that's the separate "no cast at all"
    /// case, not an unmatched-name case.</summary>
    public bool HasUnmatchedActors { get; set; }

    /// <summary>Comma-joined names from MetaActresses that didn't match any tracked Actor, for
    /// the Movies page's unmatched-actor tooltip. Null when HasUnmatchedActors is false.</summary>
    [StringLength(1000)]
    public string? UnmatchedActorNames { get; set; }

    /// <summary>The video file's on-disk <c>CreationTimeUtc</c> the first time it was observed —
    /// unlike MediaVideoFileLastWriteUtc above, this is set once and never updated again, so a later
    /// video-file upgrade/replacement doesn't reset when the movie was originally added. Backs the
    /// Movies page's "Added" sort (falling back to CreatedAt for a movie with no resolved local file
    /// yet). Null means "never observed" — same baseline convention as the fields above.</summary>
    public DateTime? FileAddedAt { get; set; }

    /// <summary>True once the user has blacklisted this movie from the Cleanup review flow
    /// — "never show this again." Set via MovieCleanupService.BlacklistAsync,
    /// cleared via UnblacklistAsync (surfaced as a reversible indicator on Movie Detail).</summary>
    public bool CleanupBlacklisted { get; set; }

    /// <summary>Set by MovieCleanupService.SnoozeAsync to UtcNow.AddDays(90) when the user snoozes
    /// this movie in the Cleanup review flow — excluded from the queue until this time passes.
    /// Null means never snoozed, or a past snooze has already lapsed back to eligible.</summary>
    public DateTime? CleanupSnoozedUntil { get; set; }

    /// <summary>Set by MovieCleanupService.StampReviewedAsync to UtcNow when the user clicks Next in
    /// Review mode — "I've checked this, moving on." Null means the movie has never
    /// been reviewed. Re-scan and metadata refresh never clear this; only an explicit
    /// MarkUnreviewedAsync call would (not currently surfaced).</summary>
    public DateTime? LastReviewedAt { get; set; }

    /// <summary>True when the movie has been marked (in the Cleanup review flow) as having broken B-frame timestamps
    /// causing stuttering or looping browser playback. Cleared when
    /// the video is repaired via lossless stream copy.</summary>
    public bool HasBrokenBFrames { get; set; }

    /// <summary>True when the user has marked this movie as a favorite — toggled
    /// via MovieService.ToggleFavoriteAsync from the Movies poster card or Movie Detail's toolbar.
    /// Mirrors Actor.IsFavorite/FavoritedAt.</summary>
    public bool IsFavorite { get; set; }

    public DateTime? FavoritedAt { get; set; }

    /// <summary>The movie's stored effective actors (SceneEffectiveActor and co.) need recomputing.
    /// Set in the same save as any change that can alter them (ClipActorStaleInterceptor, ClipActorStale),
    /// cleared by ClipActorSync. New movies start stale; the migration made every existing one stale, which
    /// is the first fill.</summary>
    public bool ClipActorsStale { get; set; } = true;

    /// <summary>Cheap composite signature of poster/fanart/extrafanart file names, sizes, and
    /// (for poster/fanart) last-write times, plus extrafanart count/total size — not a
    /// cryptographic hash, just enough to notice "something in the image set changed" without
    /// statting every extrafanart file individually on every scan. See
    /// LocalLibraryClient.ComputeImagesSignature.</summary>
    public string? MediaImagesSignature { get; set; }

    // Technical media info gathered via MediaInfo (see Services/MediaInfo) from the same local
    // video file as LocalFileSizeBytes above. Null until a probe has run; MediaScanError is set
    // (with the other Media* fields null) when a probe was attempted but failed, so that's
    // distinguishable from "never scanned". Fields mirror MediaInfo's own classic text report field
    // by field, for the movie detail page's "Media Info" toolbar button/modal.

    /// <summary>Just the video file's name (no directory) — MediaInfo's own classic report calls
    /// this "Complete name" and shows the full path there, but this app deliberately never surfaces
    /// the local filesystem layout (it commonly runs in Docker with real user media mounted in).
    /// Set alongside LocalFileSizeBytes wherever that is (LocalLibraryClient), not by the probe itself.</summary>
    public string? MediaVideoFileName { get; set; }

    public string? MediaContainerFormat { get; set; }
    public string? MediaContainerFormatProfile { get; set; }
    public string? MediaContainerCodecId { get; set; }
    public string? MediaContainerCodecIdCompatible { get; set; }
    public string? MediaWritingApplication { get; set; }
    public double? MediaDurationSeconds { get; set; }
    public int? MediaOverallBitRateKbps { get; set; }
    public string? MediaVideoStreamId { get; set; }
    public string? MediaVideoCodec { get; set; }
    public string? MediaVideoFormatInfo { get; set; }
    public string? MediaVideoProfile { get; set; }
    public string? MediaVideoFormatSettings { get; set; }
    public string? MediaVideoCodecId { get; set; }
    public string? MediaVideoCodecIdInfo { get; set; }
    public int? MediaWidth { get; set; }
    public int? MediaHeight { get; set; }
    public string? MediaAspectRatio { get; set; }
    public double? MediaFrameRate { get; set; }
    public string? MediaFrameRateMode { get; set; }
    public double? MediaFrameRateMin { get; set; }
    public double? MediaFrameRateMax { get; set; }
    public int? MediaVideoBitRateKbps { get; set; }
    public string? MediaColorSpace { get; set; }
    public string? MediaChromaSubsampling { get; set; }
    public int? MediaBitDepth { get; set; }
    public string? MediaScanType { get; set; }
    public double? MediaBitsPerPixelFrame { get; set; }
    public long? MediaVideoStreamSizeBytes { get; set; }
    public string? MediaWritingLibrary { get; set; }
    public string? MediaEncodingSettings { get; set; }
    public string? MediaColorRange { get; set; }
    public string? MediaColorPrimaries { get; set; }
    public string? MediaTransferCharacteristics { get; set; }
    public string? MediaMatrixCoefficients { get; set; }
    public string? MediaCodecConfigurationBox { get; set; }
    public string? MediaAudioStreamId { get; set; }
    public string? MediaAudioCodec { get; set; }
    public string? MediaAudioFormatInfo { get; set; }
    public string? MediaAudioFormatSettings { get; set; }
    public string? MediaAudioCodecId { get; set; }
    public string? MediaAudioBitRateMode { get; set; }
    public int? MediaAudioBitRateKbps { get; set; }
    public int? MediaAudioChannels { get; set; }
    public string? MediaAudioChannelLayout { get; set; }
    public int? MediaAudioSamplingRateHz { get; set; }
    public double? MediaAudioFrameRate { get; set; }
    public string? MediaAudioCompressionMode { get; set; }
    public long? MediaAudioStreamSizeBytes { get; set; }
    public bool? MediaAudioDefault { get; set; }
    public int? MediaAudioAlternateGroup { get; set; }
    public int? MediaSubtitleCount { get; set; }

    /// <summary>Whether the movie's local folder has a sidecar subtitle file (.srt/.ass/.ssa/.vtt) —
    /// the common case for this library, unlike MediaSubtitleCount above (embedded tracks inside
    /// the video container itself, which most local files don't have). "Has subtitles" as a concept
    /// should check both.</summary>
    public bool MediaHasSubtitleFile { get; set; }

    /// <summary>Whether the movie's local folder has a "{code}-trailer.{ext}" video file alongside
    /// its own video file.</summary>
    public bool MediaHasTrailerFile { get; set; }

    public DateTime? MediaScannedAt { get; set; }
    public string? MediaScanError { get; set; }

    /// <summary>Set instead of MediaScanError when the movie's local folder was found but had no
    /// video file in it at all — expected for a "Got" movie whose file hasn't actually been placed
    /// yet, not a probe failure worth retrying every scheduled rescan. MediaScannedAt is still set
    /// alongside this, so LibraryRescanTask treats the movie as settled instead of retrying it
    /// forever; it's cleared the next time a video file is actually found and probed (success or
    /// failure), so a stale warning never lingers once there's something real to report on.</summary>
    public string? MediaScanWarning { get; set; }

    /// <summary>The name to show in the UI: fetched metadata title, falling back to the code.</summary>
    [NotMapped]
    public string DisplayName => !string.IsNullOrWhiteSpace(MetaTitle)
        ? MetaTitle
        : !string.IsNullOrWhiteSpace(Title)
            ? Title
            : Code ?? $"Movie #{Id}";

    /// <summary>Whether a local video file was found for the movie — what the player needs, since it
    /// streams the local file. LocalLibraryClient clears LocalFileSizeBytes when the
    /// file goes missing.</summary>
    /// <summary>Not stored: the Movies grid's projection fills it with the poster's
    /// <see cref="Javbuddy.Services.Images.PosterVersion"/> for its thumbnail URL.</summary>
    [NotMapped]
    public string? PosterVersion { get; set; }

    [NotMapped]
    public bool HasLocalVideo => LocalFileSizeBytes is not null;

    [NotMapped]
    public string? LocalFileSizeDisplay => LocalFileSizeBytes is long bytes ? ByteSizeFormatter.Format(bytes) : null;
}
