using System.Text.Json;
using System.Text.Json.Serialization;

namespace Javbuddy.Services.Javinizer;

// Mirrors javinizer-go's internal/api/contracts package (ScrapeRequest,
// ScrapeResponse, MovieView, ActressView, GenreView, ErrorResponse).

public class ScrapeRequestDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

public class ScrapeResponseDto
{
    [JsonPropertyName("cached")]
    public bool Cached { get; set; }

    [JsonPropertyName("movie")]
    public MovieViewDto? Movie { get; set; }

    [JsonPropertyName("sources_used")]
    public int SourcesUsed { get; set; }

    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }
}

public class MovieViewDto
{
    /// <summary>The human-facing release code (e.g. "MIAA-137") — confirmed live against both
    /// /api/v1/scrape and a real /api/v1/batch job result for the same title. This is what
    /// matches output.folder_format's "&lt;ID&gt;" token and the app's own Movie.Code, NOT the
    /// Code property below despite the name.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>javinizer-go's own internal identifier — an all-lowercase, dash-stripped slug
    /// (e.g. "miaa00137" for release code "MIAA-137"), confirmed live. Not the same thing as this
    /// app's Movie.Code — see Id above for the field that matches it.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("display_title")]
    public string? DisplayTitle { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("original_title")]
    public string? OriginalTitle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("release_date")]
    public DateTime? ReleaseDate { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("maker")]
    public string? Maker { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("series")]
    public string? Series { get; set; }

    [JsonPropertyName("rating_score")]
    public double? RatingScore { get; set; }

    [JsonPropertyName("rating_votes")]
    public int? RatingVotes { get; set; }

    [JsonPropertyName("poster_url")]
    public string? PosterUrl { get; set; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    /// <summary>Only populated by the batch-job API (GET /api/v1/batch/{id}?include_data=true) —
    /// the plain scrape API has no cropped variant.</summary>
    [JsonPropertyName("cropped_poster_url")]
    public string? CroppedPosterUrl { get; set; }

    [JsonPropertyName("should_crop_poster")]
    public bool? ShouldCropPoster { get; set; }

    [JsonPropertyName("original_should_crop_poster")]
    public bool? OriginalShouldCropPoster { get; set; }

    [JsonPropertyName("poster_crop_bounds")]
    public CropBoundsDto? PosterCropBounds { get; set; }

    [JsonPropertyName("poster_crop_source_full")]
    public bool? PosterCropSourceFull { get; set; }

    [JsonPropertyName("original_poster_url")]
    public string? OriginalPosterUrl { get; set; }

    [JsonPropertyName("original_cover_url")]
    public string? OriginalCoverUrl { get; set; }

    [JsonPropertyName("actresses")]
    public List<ActressViewDto>? Actresses { get; set; }

    /// <summary>Version of the stored cast; javinizer-go requires it back on a PATCH that changes
    /// the cast (with the result's revision), or it rejects the edit with 409.</summary>
    [JsonPropertyName("cast_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CastVersion { get; set; }

    [JsonPropertyName("genres")]
    public List<GenreViewDto>? Genres { get; set; }

    [JsonPropertyName("release_year")]
    public int? ReleaseYear { get; set; }

    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; }

    [JsonPropertyName("screenshot_urls")]
    public List<string>? ScreenshotUrls { get; set; }

    [JsonPropertyName("source_name")]
    public string? SourceName { get; set; }

    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("trailer_url")]
    public string? TrailerUrl { get; set; }
}

public class CropBoundsDto
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("source_aspect")]
    public double? SourceAspect { get; set; }
}

public class ActressViewDto
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("dmm_id")]
    public int? DmmId { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("japanese_name")]
    public string? JapaneseName { get; set; }

    [JsonPropertyName("thumb_url")]
    public string? ThumbUrl { get; set; }

    [JsonPropertyName("aliases")]
    public string? Aliases { get; set; }

    // Round-tripped untouched so an unchanged actress compares equal on javinizer-go's side.
    [JsonPropertyName("name_key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NameKey { get; set; }

    [JsonPropertyName("verified")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Verified { get; set; }

    [JsonPropertyName("origin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Origin { get; set; }

    [JsonPropertyName("thumb_edited")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ThumbEdited { get; set; }
}

public class UpdateMovieRequestDto
{
    [JsonPropertyName("movie")]
    public MovieViewDto Movie { get; set; } = new();

    /// <summary>Compare-and-swap baseline: the result's <see cref="BatchFileResultDto.Revision"/>.</summary>
    [JsonPropertyName("expected_result_revision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? ExpectedResultRevision { get; set; }
}

/// <summary>Request body for POST /api/v1/batch/{id}/results/{resultId}/poster-from-url — the
/// dedicated endpoint javinizer-go's own review UI uses to change a poster. Unlike the generic
/// whole-movie PATCH, this downloads the image server-side and regenerates CroppedPosterUrl, which
/// the generic PATCH leaves stale (confirmed against the real javinizer-go source/instance).</summary>
public class PosterFromUrlRequestDto
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

public class GenreViewDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class ErrorResponseDto
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

// javinizer-go v1.5.1's batch-job API (torrent sorting wizard).

/// <summary>The batch job's real lifecycle states. "completed" means scrape/match finished and
/// ready to review — it is NOT the same as "organized", the terminal state after a successful
/// /organize call. Kept as plain string constants (matching this file's other DTOs, which don't
/// use enums for API-shaped fields) rather than a C# enum, so callers compare against these
/// instead of hardcoding the raw JSON strings.</summary>
public static class JavinizerJobStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Organized = "organized";
    public const string Reverted = "reverted";
}

public class ScanRequestDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("recursive")]
    public bool Recursive { get; set; }

    [JsonPropertyName("filter")]
    public string? Filter { get; set; }
}

public class ScanResponseDto
{
    [JsonPropertyName("files")]
    public List<FileInfoDto> Files { get; set; } = new();

    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>Paths of files the scan skipped (e.g. non-video, hidden) — verified against a
    /// real javinizer-go response as a string array, not the count the field name suggests.</summary>
    [JsonPropertyName("skipped")]
    public List<string>? Skipped { get; set; }
}

public class FileInfoDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("is_dir")]
    public bool IsDir { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("mod_time")]
    public DateTime? ModTime { get; set; }

    [JsonPropertyName("movie_id")]
    public string? MovieId { get; set; }

    [JsonPropertyName("matched")]
    public bool Matched { get; set; }

    [JsonPropertyName("is_multi_part")]
    public bool IsMultiPart { get; set; }

    [JsonPropertyName("part_number")]
    public int? PartNumber { get; set; }

    [JsonPropertyName("part_suffix")]
    public string? PartSuffix { get; set; }
}

public class BatchScrapeRequestDto
{
    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = new();

    [JsonPropertyName("strict")]
    public bool Strict { get; set; }

    [JsonPropertyName("force")]
    public bool Force { get; set; }

    [JsonPropertyName("destination")]
    public string? Destination { get; set; }

    [JsonPropertyName("update")]
    public bool Update { get; set; }

    [JsonPropertyName("selected_scrapers")]
    public List<string>? SelectedScrapers { get; set; }

    [JsonPropertyName("preset")]
    public string? Preset { get; set; }

    [JsonPropertyName("scalar_strategy")]
    public string? ScalarStrategy { get; set; }

    [JsonPropertyName("array_strategy")]
    public string? ArrayStrategy { get; set; }

    [JsonPropertyName("operation_mode")]
    public string? OperationMode { get; set; }

    [JsonPropertyName("manual_inputs")]
    public Dictionary<string, string>? ManualInputs { get; set; }
}

public class BatchScrapeResponseDto
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;
}

public class BatchJobResponseDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("total_files")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("completed")]
    public int Completed { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("operation_count")]
    public int OperationCount { get; set; }

    [JsonPropertyName("reverted_count")]
    public int RevertedCount { get; set; }

    [JsonPropertyName("excluded")]
    public Dictionary<string, bool>? Excluded { get; set; }

    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    [JsonPropertyName("destination")]
    public string? Destination { get; set; }

    [JsonPropertyName("files")]
    public List<string>? Files { get; set; }

    [JsonPropertyName("results")]
    public Dictionary<string, BatchFileResultDto>? Results { get; set; }

    [JsonPropertyName("started_at")]
    public DateTime? StartedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("operation_mode_override")]
    public string? OperationModeOverride { get; set; }

    [JsonPropertyName("persist_error")]
    public string? PersistError { get; set; }
}

public class BatchFileResultDto
{
    [JsonPropertyName("result_id")]
    public string ResultId { get; set; } = string.Empty;

    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }

    [JsonPropertyName("movie_id")]
    public string? MovieId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("field_sources")]
    public JsonElement? FieldSources { get; set; }

    [JsonPropertyName("actress_sources")]
    public JsonElement? ActressSources { get; set; }

    [JsonPropertyName("movie")]
    public MovieViewDto? Movie { get; set; }

    [JsonPropertyName("is_multi_part")]
    public bool IsMultiPart { get; set; }

    [JsonPropertyName("part_number")]
    public int? PartNumber { get; set; }

    [JsonPropertyName("part_suffix")]
    public string? PartSuffix { get; set; }

    [JsonPropertyName("started_at")]
    public DateTime? StartedAt { get; set; }

    [JsonPropertyName("ended_at")]
    public DateTime? EndedAt { get; set; }

    /// <summary>Server-managed update counter, sent back as expected_result_revision on a PATCH
    /// (javinizer-go requires it, with the movie's cast_version, for a cast change).</summary>
    [JsonPropertyName("revision")]
    public ulong? Revision { get; set; }
}

public class ReviewApplyOverridesDto
{
    [JsonPropertyName("destination")]
    public string? Destination { get; set; }

    [JsonPropertyName("operation_mode")]
    public string? OperationMode { get; set; }

    [JsonPropertyName("preset")]
    public string? Preset { get; set; }

    [JsonPropertyName("scalar_strategy")]
    public string? ScalarStrategy { get; set; }

    [JsonPropertyName("array_strategy")]
    public string? ArrayStrategy { get; set; }

    [JsonPropertyName("force_overwrite")]
    public bool ForceOverwrite { get; set; }

    [JsonPropertyName("overwrite_existing_media")]
    public bool OverwriteExistingMedia { get; set; }

    [JsonPropertyName("preserve_nfo")]
    public bool PreserveNfo { get; set; }

    [JsonPropertyName("skip_nfo")]
    public bool SkipNfo { get; set; }

    [JsonPropertyName("skip_download")]
    public bool SkipDownload { get; set; }
}

/// <summary>Shared by both POST /batch/{id}/results/{resultId}/preview and POST
/// /batch/{id}/organize — the two request bodies are structurally identical.</summary>
public class OrganizePreviewRequestDto
{
    [JsonPropertyName("destination")]
    public string? Destination { get; set; }

    [JsonPropertyName("copy_only")]
    public bool CopyOnly { get; set; }

    [JsonPropertyName("link_mode")]
    public string? LinkMode { get; set; }

    [JsonPropertyName("operation_mode")]
    public string? OperationMode { get; set; }

    [JsonPropertyName("overrides")]
    public ReviewApplyOverridesDto? Overrides { get; set; }

    [JsonPropertyName("skip_nfo")]
    public bool SkipNfo { get; set; }

    [JsonPropertyName("skip_download")]
    public bool SkipDownload { get; set; }

    /// <summary>Unsaved-edits override, honored only by the per-result preview endpoint
    /// (javinizer-go OrganizePreviewRequest.Movie): the sort editor's live preview.
    /// Omitted when null, so organize requests are unchanged.</summary>
    [JsonPropertyName("movie")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MovieViewDto? Movie { get; set; }
}

public class OrganizePreviewResponseDto
{
    [JsonPropertyName("folder_name")]
    public string? FolderName { get; set; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    [JsonPropertyName("subfolder_path")]
    public string? SubfolderPath { get; set; }

    [JsonPropertyName("full_path")]
    public string? FullPath { get; set; }

    [JsonPropertyName("video_files")]
    public List<string>? VideoFiles { get; set; }

    [JsonPropertyName("nfo_path")]
    public string? NfoPath { get; set; }

    [JsonPropertyName("nfo_paths")]
    public List<string>? NfoPaths { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("fanart_path")]
    public string? FanartPath { get; set; }

    [JsonPropertyName("extrafanart_path")]
    public string? ExtraFanartPath { get; set; }

    [JsonPropertyName("screenshots")]
    public List<string>? Screenshots { get; set; }

    [JsonPropertyName("trailer_path")]
    public string? TrailerPath { get; set; }

    [JsonPropertyName("source_path")]
    public string? SourcePath { get; set; }

    [JsonPropertyName("operation_mode")]
    public string? OperationMode { get; set; }
}

public class BatchRescrapeRequestDto
{
    [JsonPropertyName("force")]
    public bool Force { get; set; }

    [JsonPropertyName("selected_scrapers")]
    public List<string>? SelectedScrapers { get; set; }

    [JsonPropertyName("manual_search_input")]
    public string? ManualSearchInput { get; set; }

    [JsonPropertyName("preset")]
    public string? Preset { get; set; }

    [JsonPropertyName("scalar_strategy")]
    public string? ScalarStrategy { get; set; }

    [JsonPropertyName("array_strategy")]
    public string? ArrayStrategy { get; set; }
}

public class JavinizerVersionResponseDto
{
    [JsonPropertyName("current")]
    public string? Current { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonIgnore]
    public string? EffectiveVersion => !string.IsNullOrWhiteSpace(Current) ? Current : Version;
}

/// <summary>Response of GET /api/v1/batch/{id}/results/{resultId}/sources: each scraper's raw
/// result for the movie (javinizer-go contracts.SourceResultsResponse), compared in the sort
/// editor's source viewer.</summary>
public class SourceResultsResponseDto
{
    [JsonPropertyName("results")]
    public List<ScraperSourceResultDto>? Results { get; set; }
}

/// <summary>The subset of javinizer-go's models.ScraperResult the sort editor compares.</summary>
public class ScraperSourceResultDto
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("original_title")]
    public string? OriginalTitle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("release_date")]
    public DateTime? ReleaseDate { get; set; }

    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("maker")]
    public string? Maker { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("series")]
    public string? Series { get; set; }

    [JsonPropertyName("actresses")]
    public List<ActressViewDto>? Actresses { get; set; }

    [JsonPropertyName("genres")]
    public List<string>? Genres { get; set; }
}

/// <summary>Request body for POST /api/v1/batch/{id}/results/{resultId}/field-override: take one
/// field's value from a named source (javinizer-go contracts.FieldOverrideRequest).</summary>
public class FieldOverrideRequestDto
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";
}

public class FieldOverrideResponseDto
{
    [JsonPropertyName("movie")]
    public MovieViewDto? Movie { get; set; }

    [JsonPropertyName("field_sources")]
    public Dictionary<string, string>? FieldSources { get; set; }

    [JsonPropertyName("actress_sources")]
    public Dictionary<string, string>? ActressSources { get; set; }
}

/// <summary>One entry of GET /api/v1/scrapers (javinizer-go contracts.ScraperInfo).</summary>
public class ScraperInfoDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("display_title")]
    public string? DisplayTitle { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

public class AvailableScrapersResponseDto
{
    [JsonPropertyName("scrapers")]
    public List<ScraperInfoDto>? Scrapers { get; set; }
}

/// <summary>Request body for POST /api/v1/batch/{id}/results/{resultId}/poster-crop: pixel bounds
/// on the full-size source javinizer-go measures (temp/posters/{job}/{movieId}-full.jpg). The
/// manual crop replaces the scraper auto-crop (javinizer-go contracts.PosterCropRequest).</summary>
public class PosterCropRequestDto
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}

public class PosterCropResponseDto
{
    [JsonPropertyName("cropped_poster_url")]
    public string? CroppedPosterUrl { get; set; }

    [JsonPropertyName("poster_crop_bounds")]
    public CropBoundsDto? PosterCropBounds { get; set; }

    [JsonPropertyName("should_crop_poster")]
    public bool ShouldCropPoster { get; set; }
}
