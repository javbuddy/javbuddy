using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.MediaInfo;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Tags;
using Javbuddy.Services.Trickplay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Javbuddy.Services.LocalLibrary;

public interface ILocalLibraryClient
{
    Task<List<string>> GetRootPathsAsync(CancellationToken ct = default);

    /// <summary>True if at least one configured local-library root directory currently exists and
    /// is reachable. Used to guard automatic actions that treat an already-tracked movie's folder
    /// as gone (see LibraryRescanTask) against a false read caused by a temporarily unmounted
    /// network share rather than an actual on-disk deletion — if every configured root is
    /// unreachable, ListMovieCodesAsync would otherwise return an empty list indistinguishable from
    /// "the whole library was deleted".</summary>
    Task<bool> AnyRootReachableAsync(CancellationToken ct = default);

    /// <summary>Finds the movie's local folder (by exact then case-insensitive name match against
    /// each configured root) and, if found, parses its .nfo and checks for poster/fanart/video files.</summary>
    Task<LocalLookupResult> TryGetMetadataAsync(string code, CancellationToken ct = default, bool includeMediaInfo = true);

    /// <summary>Resolves the absolute path to a movie's local poster/folder/fanart image from the
    /// real library folder. Null if not found. fileName must be one of poster/folder/fanart
    /// combined with a supported extension (.jpg, .jpeg, .png, .webp) — anything else returns
    /// null without touching the filesystem.</summary>
    Task<string?> ResolveLiveLocalFilePathAsync(string code, string fileName, CancellationToken ct = default);

    /// <summary>ResolveLiveLocalFilePathAsync for a whole preference-ordered candidate list,
    /// resolving the movie's folder once for the lot instead of once per name. Callers that try
    /// several file names for the same image (poster.jpg, then poster.png, then folder.jpg, …)
    /// must use this: the per-name form repeats the folder lookup — including, when nothing
    /// matches by exact name, a directory enumeration of every configured root — for every
    /// candidate, which over a network share is the difference between one round-trip and eight
    /// per image request.</summary>
    Task<string?> ResolveFirstExistingLocalFilePathAsync(string code, IReadOnlyList<string> fileNames, CancellationToken ct = default);

    /// <summary>Lists every top-level folder name (i.e. candidate release code) across all
    /// configured root directories, deduplicated case-insensitively. Used by the Library Import
    /// page's local library scan — the only place this app enumerates directories in bulk.</summary>
    Task<List<string>> ListMovieCodesAsync(CancellationToken ct = default);

    /// <summary>Returns the raw text of the movie's .nfo file, or null if no local folder/.nfo
    /// was found.</summary>
    Task<string?> GetRawNfoAsync(string code, CancellationToken ct = default);

    /// <summary>Overwrites the movie's local .nfo file with the given text, via NfoFileWriter (atomic
    /// swap). Only ever called explicitly by the user (never automatically) — this does not
    /// re-parse/refresh the movie's stored metadata; that stays a separate, deliberate "Local"
    /// refresh. Returns the content it replaced (for NfoHistoryService to keep), or null if no
    /// local folder/.nfo file was found to overwrite, or it's read-only/unwritable.</summary>
    Task<string?> SaveRawNfoAsync(string code, string content, CancellationToken ct = default);

    Task<ConditionalNfoSaveResult> SaveRawNfoIfUnchangedAsync(
        string code,
        string expectedContent,
        string content,
        CancellationToken ct = default);

    /// <summary>Lists image file names in the movie's local "extrafanart" subfolder, naturally
    /// sorted (fanart1, fanart2, ... fanart10 — not the ASCII "fanart1, fanart10, fanart2" order
    /// the filesystem returns). Empty if no local folder/extrafanart subfolder was found.</summary>
    Task<List<string>> ListExtraFanartFileNamesAsync(string code, CancellationToken ct = default);

    /// <summary>Resolves the absolute path to one image inside the movie's local "extrafanart"
    /// subfolder from the real library folder. Null if not found. fileName is matched against the
    /// live subfolder's actual contents (case-insensitively) — it never touches the filesystem
    /// with an unvalidated path.</summary>
    Task<string?> ResolveLiveExtraFanartFilePathAsync(string code, string fileName, CancellationToken ct = default);

    /// <summary>Resolves the absolute path to an actor's image inside the movie's local ".actors"
    /// subfolder (e.g. ".actors\Nagase Yui.jpg"), matched case-insensitively against actorName by
    /// file stem, any of the supported image extensions. Null if no local folder/".actors"
    /// subfolder/matching file was found.</summary>
    Task<string?> ResolveActorImagePathAsync(string code, string actorName, CancellationToken ct = default);

    /// <summary>Resolves the absolute path to an actor's image inside the movie's local ".actors"
    /// subfolder, matching case-insensitively against any of the candidate names by file stem (or underscore variant).</summary>
    Task<string?> ResolveActorImagePathAsync(string code, IReadOnlyList<string> candidateNames, CancellationToken ct = default);

    /// <summary>Finds the movie's local .nfo file and extracts rich actor metadata without probing
    /// video streams or scanning artwork. Returns an empty list if no local folder/.nfo was found.</summary>
    Task<List<LocalActorMetadata>> GetMovieActorsAsync(string code, CancellationToken ct = default);

    /// <summary>Resolves which configured root path (see GetRootPathsAsync) a code's local folder
    /// lives under, or would be created under — the same lookup FindMovieFolderAsync already does
    /// internally, exposed here so callers can show the user which Javbuddy-side root a code
    /// resolves to (e.g. as a hint next to a destination field whose value must be typed in a
    /// different filesystem's terms — see the torrent sorting wizard). Null if no configured root
    /// contains a matching folder for code.</summary>
    Task<string?> ResolveRootForCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Resolves the absolute path to the movie's local folder (the same lookup
    /// ResolveRootForCodeAsync already does internally, but returning the folder itself rather
    /// than just which root it lives under) — for a caller that needs to act on the whole folder,
    /// e.g. Movie Cleanup deleting it entirely. Null if no configured root contains
    /// a matching folder for code.</summary>
    Task<string?> ResolveMovieFolderPathAsync(string code, CancellationToken ct = default);

    /// <summary>Runs the same "look up local .nfo/artwork, apply onto Movie.Meta*, save" flow
    /// MovieDetail.razor's local-refresh button uses, keyed by movie id — shared with
    /// TorrentSortService so a successful organize can immediately refresh local metadata without
    /// duplicating the mapper call.</summary>
    Task<LocalRefreshResult> RefreshLocalMetadataAsync(int movieId, CancellationToken ct = default);

    /// <summary>Runs only the MediaInfo technical probe against the movie's local video file and
    /// saves the result — unlike RefreshLocalMetadataAsync, this never touches descriptive
    /// metadata (title, plot, actresses, ...) or crops the poster. Used by LibraryRescanTask so
    /// rescanning technical info for an existing library can't silently overwrite metadata already
    /// sourced from javinizer-go with whatever a local .nfo file happens to contain.</summary>
    Task<LocalRefreshResult> RefreshMediaInfoOnlyAsync(int movieId, CancellationToken ct = default);

    /// <summary>Recomputes just the sidecar-subtitle-file flag (see HasSubtitleFile) for a movie's
    /// local folder — nothing else. Deliberately separate from RefreshMediaInfoOnlyAsync: a
    /// subtitle file can be dropped into an already-fully-probed movie's folder at any time, so
    /// LibraryRescanTask needs to recheck it on every run for every Got movie, not just the ones
    /// still eligible for a MediaInfo retry (MediaScannedAt null / MediaScanError set) — this is a
    /// cheap directory listing, not the MediaInfoLib native call RefreshMediaInfoOnlyAsync makes, so
    /// redoing it unconditionally is fine.</summary>
    Task<LocalRefreshResult> RefreshSubtitleFlagOnlyAsync(int movieId, CancellationToken ct = default);

    /// <summary>Recomputes just the trailer-file flag (see HasTrailerFile) for a movie's local
    /// folder — nothing else. Same reasoning as RefreshSubtitleFlagOnlyAsync: a trailer file can be
    /// added or removed from an already-fully-probed movie's folder at any time, so LibraryRescanTask
    /// needs to recheck it on every run for every Got movie, independent of MediaInfo's retry gate.
    /// Cheap directory listing, not the MediaInfoLib native call.</summary>
    Task<LocalRefreshResult> RefreshTrailerFlagOnlyAsync(int movieId, CancellationToken ct = default);

    /// <summary>Re-parses and re-applies just the descriptive .nfo-sourced fields (see
    /// LocalLibraryMetadataMapper.ApplyDescriptiveMetadata) when the .nfo file's last-write time
    /// has moved on from what's stored on Movie.MediaNfoLastWriteUtc — and logs a debug message
    /// when it does. The very first observation for a movie (stored value still null) only
    /// establishes the baseline and never applies/logs: without that guard, rolling this out to
    /// an existing library would re-apply local .nfo content over every movie's current metadata
    /// in one pass, even where that metadata is better sourced from javinizer-go.</summary>
    Task<LocalRefreshResult> RefreshLocalMetadataIfNfoChangedAsync(int movieId, CancellationToken ct = default);

    /// <summary>Recomputes the movie's poster/fanart/extrafanart signature (see
    /// ComputeImagesSignature) and logs a debug message when it differs from
    /// Movie.MediaImagesSignature — same null-baseline-only-on-first-observation guard as
    /// RefreshLocalMetadataIfNfoChangedAsync. Detection only: actually recaching a changed image is
    /// ImageCacheTask's job, decoupled and already driven by its own per-image
    /// size/last-write-time freshness check.</summary>
    Task<LocalRefreshResult> SyncImagesSignatureAsync(int movieId, CancellationToken ct = default);

    /// <summary>Resolves the on-disk file path to the movie's .nfo file if one exists, or null if not found.</summary>
    Task<string?> ResolveNfoFilePathAsync(string code, CancellationToken ct = default);
}

public record LocalRefreshResult(bool Success, string? ErrorMessage);

public enum ConditionalNfoSaveResult
{
    Saved,
    FileChanged,
    NotFoundOrUnwritable,
}

/// <summary>Reads local library root paths from environment variables / appsettings.
/// Supports standard .NET array configuration:
/// - Environment variables: LocalLibrary__RootPaths__0, LocalLibrary__RootPaths__1
/// - appsettings.json: "LocalLibrary": { "RootPaths": ["path1", "path2"] }
/// - Single string (JSON array, newline-separated, or comma-separated): LocalLibrary__RootPaths
/// Takes precedence over the Connections page when configured.</summary>
public static class LocalLibraryEnvConfig
{
    public static List<string> GetRootPaths(IConfiguration configuration)
    {
        var section = configuration.GetSection("LocalLibrary:RootPaths");
        var children = section.GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToList();

        if (children.Count > 0)
        {
            return children;
        }

        var scalar = configuration["LocalLibrary:RootPaths"];
        if (string.IsNullOrWhiteSpace(scalar))
        {
            return [];
        }

        return LocalLibraryClient.ParseRootPaths(scalar);
    }

    public static bool IsSet(IConfiguration configuration) => GetRootPaths(configuration).Count > 0;
}

public class LocalLibraryClient(
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration configuration,
    IMemoryCache cache,
    IMediaInfoProber mediaInfoProber,
    ILogger<LocalLibraryClient> logger,
    ITrickplayTrigger? trickplayTrigger = null) : ILocalLibraryClient
{
    private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".m4v", ".wmv", ".mov", ".ts", ".webm" };

    // Sidecar subtitles are usually .srt (often named after whatever transcription tool produced
    // them, not just "{code}.srt"); the rest is defensive breadth for other common formats.
    private static readonly string[] SubtitleExtensions = { ".srt", ".ass", ".ssa", ".vtt", ".sub" };
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".jfif" };

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "extrafanart", "extrathumbs", "sample", "samples", "proof", "subs", "subtitles", "featurettes", ".actors", "actors", ".actor", "actor"
    };

    // Preference order: poster.* before folder.*, and within each, the ImageExtensions order
    // (.jpg first — the common case — down to .webp).
    private static readonly string[] PosterFileNames =
        new[] { "poster", "folder" }.SelectMany(stem => ImageExtensions.Select(ext => stem + ext)).ToArray();
    private static readonly string[] FanartFileNames =
        ImageExtensions.Select(ext => "fanart" + ext).ToArray();
    private static readonly string[] AllowedServeFileNames = PosterFileNames.Concat(FanartFileNames).ToArray();

    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly IConfiguration configuration = configuration;

    public async Task<List<string>> GetRootPathsAsync(CancellationToken ct = default)
    {
        var envPaths = LocalLibraryEnvConfig.GetRootPaths(configuration);
        if (envPaths.Count > 0)
        {
            return envPaths;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.LocalLibrarySettings.ReadSingleRowAsync(ct);
        return ParseRootPaths(settings?.RootPaths);
    }

    public static List<string> ParseRootPaths(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(trimmed);
                if (parsed is { Count: > 0 })
                {
                    return parsed.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Fall back to delimiter splitting
            }
        }

        return trimmed.Split([',', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    public async Task<LocalLookupResult> TryGetMetadataAsync(string code, CancellationToken ct = default, bool includeMediaInfo = true)
    {
        var folder = await FindMovieFolderAsync(code, ct);
        if (folder is null)
        {
            return new LocalLookupResult(false, null, null, "No local folder found for this code.");
        }

        var nfoPath = FindNfoFile(folder, code);
        var metadata = nfoPath is not null ? await LocalNfoParser.ParseAsync(nfoPath, ct) : null;
        metadata ??= new LocalMovieMetadata();

        metadata.LocalPosterFileName = PosterFileNames.FirstOrDefault(name => File.Exists(Path.Combine(folder, name)));
        metadata.HasLocalFanart = FanartFileNames.Any(name => File.Exists(Path.Combine(folder, name)));
        metadata.HasSubtitleFile = HasSubtitleFile(folder);
        metadata.HasTrailerFile = HasTrailerFile(folder);

        var videoPaths = MovieVersionParser.FindVideoFiles(folder);
        if (videoPaths.Count > 0)
        {
            var primaryPath = videoPaths.FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), code, StringComparison.OrdinalIgnoreCase))
                ?? videoPaths[0];

            foreach (var vPath in videoPaths)
            {
                var fInfo = new FileInfo(vPath);
                var fName = Path.GetFileName(vPath);
                var isPrimary = vPath == primaryPath;
                var vTag = MovieVersionParser.ExtractVersionTag(fName, code);
                metadata.VideoFiles.Add(new LocalVideoFileMetadata
                {
                    FileName = fName,
                    VersionTag = vTag,
                    FileSizeBytes = fInfo.Length,
                    FileAddedAt = fInfo.CreationTimeUtc,
                    LastWriteUtc = fInfo.LastWriteTimeUtc,
                    IsPrimary = isPrimary,
                    MediaProbe = includeMediaInfo ? await mediaInfoProber.ProbeAsync(vPath, ct) : null,
                });
            }

            var primaryMeta = metadata.VideoFiles.FirstOrDefault(f => f.IsPrimary) ?? metadata.VideoFiles[0];
            metadata.VideoFileSizeBytes = metadata.VideoFiles.Sum(f => f.FileSizeBytes);
            metadata.VideoFileName = primaryMeta.FileName;
            metadata.MediaProbe = primaryMeta.MediaProbe;
        }

        return new LocalLookupResult(true, folder, metadata, null);
    }

    public Task<string?> ResolveLiveLocalFilePathAsync(string code, string fileName, CancellationToken ct = default) =>
        ResolveFirstExistingLocalFilePathAsync(code, new[] { fileName }, ct);

    public async Task<string?> ResolveFirstExistingLocalFilePathAsync(string code, IReadOnlyList<string> fileNames, CancellationToken ct = default)
    {
        if (!fileNames.Any(AllowedServeFileNames.Contains)) return null;

        var folder = await FindMovieFolderAsync(code, ct);
        if (folder is null) return null;

        foreach (var fileName in fileNames)
        {
            if (!AllowedServeFileNames.Contains(fileName)) continue;

            var path = Path.Combine(folder, fileName);
            if (File.Exists(path)) return path;
        }

        return null;
    }

    public async Task<bool> AnyRootReachableAsync(CancellationToken ct = default)
    {
        var roots = await GetRootPathsAsync(ct);
        return roots.Any(root => !string.IsNullOrWhiteSpace(root) && Directory.Exists(root));
    }

    public async Task<List<string>> ListMovieCodesAsync(CancellationToken ct = default)
    {
        var roots = await GetRootPathsAsync(ct);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var codes = new List<string>();

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            foreach (var (code, _) in GetRootFolderIndex(root))
            {
                if (seen.Add(code))
                {
                    codes.Add(code);
                }
            }
        }

        return codes;
    }

    public async Task<string?> GetRawNfoAsync(string code, CancellationToken ct = default)
    {
        var folder = await FindMovieFolderAsync(code, ct);
        if (folder is null) return null;

        var nfoPath = FindNfoFile(folder, code);
        if (nfoPath is null) return null;

        try
        {
            return await File.ReadAllTextAsync(nfoPath, ct);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task<string?> SaveRawNfoAsync(string code, string content, CancellationToken ct = default)
    {
        var folder = await FindMovieFolderAsync(code, ct);
        if (folder is null)
        {
            logger.LogWarning("Cannot save .nfo for {Code}: movie folder not found", code);
            return null;
        }

        var nfoPath = FindNfoFile(folder, code);
        if (nfoPath is null)
        {
            logger.LogWarning("Cannot save .nfo for {Code}: no .nfo file in {Folder}", code, folder);
            return null;
        }

        try
        {
            var result = await NfoFileWriter.WriteTextAsync(nfoPath, content, expectedContent: null, ct);
            if (result.Status != NfoWriteStatus.Saved)
            {
                logger.LogWarning("Did not save .nfo {Path}: {Status}", nfoPath, result.Status);
            }
            return result.Status == NfoWriteStatus.Saved ? result.PreviousContent : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to write .nfo {Path}", nfoPath);
            return null;
        }
    }

    public async Task<ConditionalNfoSaveResult> SaveRawNfoIfUnchangedAsync(
        string code,
        string expectedContent,
        string content,
        CancellationToken ct = default)
    {
        var folder = await FindMovieFolderAsync(code, ct);
        if (folder is null)
        {
            logger.LogWarning("Cannot save .nfo for {Code}: movie folder not found", code);
            return ConditionalNfoSaveResult.NotFoundOrUnwritable;
        }

        var nfoPath = FindNfoFile(folder, code);
        if (nfoPath is null)
        {
            logger.LogWarning("Cannot save .nfo for {Code}: no .nfo file in {Folder}", code, folder);
            return ConditionalNfoSaveResult.NotFoundOrUnwritable;
        }

        try
        {
            var result = await NfoFileWriter.WriteTextAsync(nfoPath, content, expectedContent, ct);
            if (result.Status is NfoWriteStatus.ReadOnly or NfoWriteStatus.NotFound)
            {
                logger.LogWarning("Did not save .nfo {Path}: {Status}", nfoPath, result.Status);
            }
            return result.Status switch
            {
                NfoWriteStatus.Saved => ConditionalNfoSaveResult.Saved,
                NfoWriteStatus.FileChanged => ConditionalNfoSaveResult.FileChanged,
                _ => ConditionalNfoSaveResult.NotFoundOrUnwritable,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to write .nfo {Path}", nfoPath);
            return ConditionalNfoSaveResult.NotFoundOrUnwritable;
        }
    }

    public async Task<string?> ResolveNfoFilePathAsync(string code, CancellationToken ct = default)
    {
        var folder = await FindMovieFolderAsync(code, ct);
        if (folder is null) return null;
        return FindNfoFile(folder, code);
    }

    public async Task<List<string>> ListExtraFanartFileNamesAsync(string code, CancellationToken ct = default)
    {
        var folder = await FindMovieFolderAsync(code, ct);
        if (folder is null) return new List<string>();

        var extraFanartFolder = Path.Combine(folder, "extrafanart");
        if (!Directory.Exists(extraFanartFolder)) return new List<string>();

        return EnumerateExtraFanartFiles(extraFanartFolder)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderBy(name => name, Comparer<string>.Create(NaturalCompare))
            .ToList();
    }

    public async Task<string?> ResolveLiveExtraFanartFilePathAsync(string code, string fileName, CancellationToken ct = default)
    {
        if (!IsSafePathSegment(fileName)) return null;

        var folder = await FindMovieFolderAsync(code, ct);
        if (folder is null) return null;

        var extraFanartFolder = Path.Combine(folder, "extrafanart");
        if (!Directory.Exists(extraFanartFolder)) return null;

        return EnumerateExtraFanartFiles(extraFanartFolder)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
    }

    public Task<string?> ResolveActorImagePathAsync(string code, string actorName, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(actorName)
            ? Task.FromResult<string?>(null)
            : ResolveActorImagePathAsync(code, new[] { actorName }, ct);

    public async Task<string?> ResolveActorImagePathAsync(string code, IReadOnlyList<string> candidateNames, CancellationToken ct = default)
    {
        if (candidateNames is null || candidateNames.Count == 0) return null;

        var folder = await FindMovieFolderAsync(code, ct);
        if (folder is null)
        {
            logger.LogDebug("Cannot resolve actor image for [{CandidateNames}] in movie {Code}: movie folder not found",
                string.Join(", ", candidateNames), code);
            return null;
        }

        var actorsFolder = FindActorsFolder(folder);

        // Check explicit relative candidate paths first (e.g. from <thumb> if it's a relative path)
        foreach (var candidate in candidateNames)
        {
            if (string.IsNullOrWhiteSpace(candidate) || candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedCandidate = candidate.Replace('\\', '/').TrimStart('/');
            if (normalizedCandidate.Contains('/'))
            {
                var directPath = Path.GetFullPath(Path.Combine(folder, normalizedCandidate));
                if (directPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && File.Exists(directPath))
                {
                    logger.LogDebug("Resolved actor image via direct candidate path for {Code}: {Path}", code, directPath);
                    return directPath;
                }
            }
            else if (actorsFolder is not null)
            {
                var inActors = Path.Combine(actorsFolder, normalizedCandidate);
                if (File.Exists(inActors))
                {
                    logger.LogDebug("Resolved actor image via direct actor folder file for {Code}: {Path}", code, inActors);
                    return inActors;
                }
            }
        }

        if (actorsFolder is null)
        {
            logger.LogDebug("No actors folder found in movie folder {Folder} for {Code}", folder, code);
            return null;
        }

        var filePaths = EnumerateActorFolderFiles(actorsFolder);
        if (filePaths.Count == 0)
        {
            logger.LogDebug("Actors folder {ActorsFolder} for {Code} contains no supported image files", actorsFolder, code);
            return null;
        }

        var match = ActorImageMatching.FindBestMatch(filePaths, candidateNames);
        if (match is not null)
        {
            logger.LogDebug("Resolved actor image for [{CandidateNames}] in movie {Code} to {Match}",
                string.Join(", ", candidateNames), code, match);
            return match;
        }

        // Secondary attempt: inspect the movie's NFO for rich actor metadata (alternate names, Japanese names, aliases)
        var movieActors = await GetMovieActorsAsync(code, ct);
        if (movieActors.Count > 0)
        {
            var matchingNfoActor = movieActors.FirstOrDefault(nfoActor =>
                ActorImageMatching.MatchesAny(nfoActor.Name, candidateNames)
                || (!string.IsNullOrWhiteSpace(nfoActor.AltName) && ActorImageMatching.MatchesAny(nfoActor.AltName, candidateNames))
                || nfoActor.Aliases.Any(alias => ActorImageMatching.MatchesAny(alias, candidateNames)));

            if (matchingNfoActor is not null)
            {
                var enrichedNames = new List<string>(candidateNames);
                if (!string.IsNullOrWhiteSpace(matchingNfoActor.Name)) enrichedNames.Add(matchingNfoActor.Name);
                if (!string.IsNullOrWhiteSpace(matchingNfoActor.AltName)) enrichedNames.Add(matchingNfoActor.AltName);
                foreach (var alias in matchingNfoActor.Aliases)
                {
                    if (!string.IsNullOrWhiteSpace(alias)) enrichedNames.Add(alias);
                }

                if (!string.IsNullOrWhiteSpace(matchingNfoActor.Thumb)
                    && !matchingNfoActor.Thumb.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !matchingNfoActor.Thumb.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var thumbRel = matchingNfoActor.Thumb.Replace('\\', '/').TrimStart('/');
                    var thumbDirect = Path.GetFullPath(Path.Combine(folder, thumbRel));
                    if (thumbDirect.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && File.Exists(thumbDirect))
                    {
                        logger.LogDebug("Resolved actor image via NFO thumb path for {Code}: {Path}", code, thumbDirect);
                        return thumbDirect;
                    }
                }

                match = ActorImageMatching.FindBestMatch(filePaths, enrichedNames);
                if (match is not null)
                {
                    logger.LogDebug("Resolved actor image for [{CandidateNames}] in movie {Code} to {Match} via NFO metadata enrichment",
                        string.Join(", ", candidateNames), code, match);
                    return match;
                }
            }
        }

        var availableNames = filePaths.Select(Path.GetFileName).ToList();
        logger.LogDebug("No actor image matched for [{CandidateNames}] in movie {Code} ({ActorsFolder}). Available files: [{AvailableFiles}]",
            string.Join(", ", candidateNames), code, actorsFolder, string.Join(", ", availableNames));
        return null;
    }

    private static string? FindActorsFolder(string movieFolder)
    {
        if (string.IsNullOrWhiteSpace(movieFolder) || !Directory.Exists(movieFolder)) return null;

        var dotActors = Path.Combine(movieFolder, ".actors");
        if (Directory.Exists(dotActors)) return dotActors;

        try
        {
            var dirs = Directory.GetDirectories(movieFolder);
            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                if (string.Equals(name, ".actors", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "actors", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, ".actor", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "actor", StringComparison.OrdinalIgnoreCase))
                {
                    return dir;
                }
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static List<string> EnumerateActorFolderFiles(string actorsFolder)
    {
        var results = new List<string>();
        try
        {
            var topFiles = Directory.EnumerateFiles(actorsFolder)
                .Where(f => ActorImageMatching.IsSupportedImageExtension(Path.GetExtension(f)));
            results.AddRange(topFiles);

            foreach (var subDir in Directory.GetDirectories(actorsFolder))
            {
                var subFiles = Directory.EnumerateFiles(subDir)
                    .Where(f => ActorImageMatching.IsSupportedImageExtension(Path.GetExtension(f)));
                results.AddRange(subFiles);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return results;
    }

    public async Task<List<LocalActorMetadata>> GetMovieActorsAsync(string code, CancellationToken ct = default)
    {
        var folder = await FindMovieFolderAsync(code, ct);
        if (folder is null) return [];

        var nfoPath = FindNfoFile(folder, code);
        if (nfoPath is null) return [];

        var metadata = await LocalNfoParser.ParseAsync(nfoPath, ct);
        return metadata?.Actors ?? [];
    }

    private static bool IsSafePathSegment(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.IndexOfAny(new[] { '/', '\\' }) < 0
        && value is not ("." or "..");

    private static IEnumerable<string> EnumerateExtraFanartFiles(string extraFanartFolder)
    {
        try
        {
            return Directory.EnumerateFiles(extraFanartFolder)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        catch (IOException)
        {
            return Enumerable.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>Sorts digit runs by numeric value instead of ASCII order, so "fanart2" sorts
    /// before "fanart10" (the filesystem/ASCII order is fanart1, fanart10, fanart11, ..., fanart2).</summary>
    private static int NaturalCompare(string a, string b)
    {
        int ai = 0, bi = 0;
        while (ai < a.Length && bi < b.Length)
        {
            if (char.IsDigit(a[ai]) && char.IsDigit(b[bi]))
            {
                var aStart = ai;
                while (ai < a.Length && char.IsDigit(a[ai])) ai++;
                var bStart = bi;
                while (bi < b.Length && char.IsDigit(b[bi])) bi++;

                var aNum = a[aStart..ai].TrimStart('0');
                var bNum = b[bStart..bi].TrimStart('0');

                if (aNum.Length != bNum.Length) return aNum.Length - bNum.Length;
                var numCmp = string.CompareOrdinal(aNum, bNum);
                if (numCmp != 0) return numCmp;
            }
            else
            {
                var cmp = char.ToLowerInvariant(a[ai]).CompareTo(char.ToLowerInvariant(b[bi]));
                if (cmp != 0) return cmp;
                ai++;
                bi++;
            }
        }
        return (a.Length - ai) - (b.Length - bi);
    }

    private async Task<string?> FindMovieFolderAsync(string code, CancellationToken ct)
    {
        var (_, folder) = await FindMovieFolderWithRootAsync(code, ct);
        return folder;
    }

    public async Task<string?> ResolveRootForCodeAsync(string code, CancellationToken ct = default)
    {
        var (root, _) = await FindMovieFolderWithRootAsync(code, ct);
        return root;
    }

    public async Task<string?> ResolveMovieFolderPathAsync(string code, CancellationToken ct = default)
    {
        var (_, folder) = await FindMovieFolderWithRootAsync(code, ct);
        return folder;
    }

    private async Task<(string? Root, string? Folder)> FindMovieFolderWithRootAsync(string code, CancellationToken ct)
    {
        // code ends up in Path.Combine() below — reject anything that isn't a single, safe
        // path segment before it ever touches the filesystem (no traversal via "..", no
        // absolute paths, no separators).
        if (string.IsNullOrWhiteSpace(code)
            || code.IndexOfAny(new[] { '/', '\\' }) >= 0
            || code is "." or "..")
        {
            return (null, null);
        }

        var roots = await GetRootPathsAsync(ct);

        // Fast path: exact-case match, no directory enumeration.
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var exact = Path.Combine(root, code);
            if (Directory.Exists(exact)) return (root, exact);
        }

        // Fallback: case-insensitive lookup in each root's (cached) top-level folder listing.
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            if (GetRootFolderIndex(root).TryGetValue(code, out var match)) return (root, match);
        }

        // Last resort: the folder's name and the movie's code can be the same release with
        // different zero-padding (e.g. movie.Code "SAVR-1195" vs a folder named "SAVR-01195") —
        // compare zero-padding-tolerant keys instead of the literal name. Deliberately narrower
        // than CodeNormalization.GetCanonicalKey (used for r18.dev dedup): that method's
        // distributor prefix/suffix stripping is too permissive here and can collapse two
        // unrelated folder names to the same key.
        var paddingKey = CodeNormalization.GetZeroPaddingToleranceKey(code);
        if (paddingKey is null) return (null, null);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            foreach (var (folderName, folderPath) in GetRootFolderIndex(root))
            {
                if (CodeNormalization.GetZeroPaddingToleranceKey(folderName) == paddingKey) return (root, folderPath);
            }
        }

        return (null, null);
    }

    // The fallback above used to enumerate a whole root per lookup, and a miss enumerated *every*
    // root — which is the normal outcome for a Missing movie, since it has no local folder at all.
    // The Movies grid asks for one poster per visible card, so over a network share that turned a
    // single screenful of thumbnails into hundreds of directory listings. Caching the listing for
    // a few seconds collapses a burst of thumbnail requests into one enumeration per root. The
    // exact-name Directory.Exists fast path above is deliberately left uncached, so a folder that
    // was just created (by the torrent sorter, which names them exactly after the code) is still
    // picked up immediately rather than after the TTL.
    private static readonly TimeSpan RootFolderIndexTtl = TimeSpan.FromSeconds(30);

    private IReadOnlyDictionary<string, string> GetRootFolderIndex(string root) =>
        cache.GetOrCreate((nameof(LocalLibraryClient), root), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RootFolderIndexTtl;
            return BuildRootFolderIndex(root);
        })!;

    private static Dictionary<string, string> BuildRootFolderIndex(string root)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root)) return index;

        var queue = new Queue<(string DirPath, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (currentDir, depth) = queue.Dequeue();

            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(currentDir);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name.StartsWith('.') || IgnoredDirectoryNames.Contains(name)) continue;

                var isMovieFolder = LooksLikeMovieFolder(dir, name);
                if (isMovieFolder)
                {
                    index.TryAdd(name, dir);
                    var extracted = CodeNormalization.ExtractMovieCode(name);
                    if (extracted is not null)
                    {
                        index.TryAdd(extracted, dir);
                    }
                    continue;
                }

                if (depth < 2)
                {
                    queue.Enqueue((dir, depth + 1));
                }
                else
                {
                    index.TryAdd(name, dir);
                }
            }
        }

        return index;
    }

    private static bool LooksLikeMovieFolder(string folder, string name)
    {
        if (CodeNormalization.ExtractMovieCode(name) is { Length: > 0 } extracted
            && string.Equals(extracted, name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var files = Directory.EnumerateFiles(folder);
            foreach (var f in files)
            {
                var ext = Path.GetExtension(f);
                if (string.Equals(ext, ".nfo", StringComparison.OrdinalIgnoreCase)
                    || VideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    public async Task<LocalRefreshResult> RefreshLocalMetadataAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.FindAsync(new object[] { movieId }, ct);
        if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
        {
            return new LocalRefreshResult(false, "Movie not found.");
        }

        // This method refreshes technical information immediately below. Avoid probing the same
        // files once while discovering local metadata and again while synchronizing MovieFiles.
        var result = await TryGetMetadataAsync(movie.Code, ct, includeMediaInfo: false);
        if (!result.Found || result.Metadata is null)
        {
            return new LocalRefreshResult(false, result.ErrorMessage ?? "No local folder found for this code.");
        }

        if (result.FolderPath is not null)
        {
            var posterPath = Path.Combine(result.FolderPath, "poster.jpg");
            if (File.Exists(posterPath))
            {
                ImageConverter.EnsureLocalPosterCropped(posterPath);
            }
        }

        LocalLibraryMetadataMapper.Apply(movie, result.Metadata);
        await MovieActorAssociation.SynchronizeAsync(db, movie, ct);
        await TagNormalization.ApplyToMovieAsync(db, movie, result.Metadata.Genres, ct);
        await db.SaveChangesAsync(ct);
        await RefreshMediaInfoOnlyAsync(movieId, ct);
        return new LocalRefreshResult(true, null);
    }

    public async Task<LocalRefreshResult> RefreshMediaInfoOnlyAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies
            .Include(m => m.MovieFiles)
            .FirstOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
        {
            return new LocalRefreshResult(false, "Movie not found.");
        }

        var folder = await FindMovieFolderAsync(movie.Code, ct);
        if (folder is null)
        {
            return new LocalRefreshResult(false, "No local folder found for this code.");
        }

        // Independent of whether a video file resolves below — sidecar subtitle/trailer file
        // presence is a property of the folder's contents, not coupled to the video probe's own outcome.
        movie.MediaHasSubtitleFile = HasSubtitleFile(folder);
        movie.MediaHasTrailerFile = HasTrailerFile(folder);

        var videoPaths = MovieVersionParser.FindVideoFiles(folder);
        if (videoPaths.Count == 0)
        {
            // The movie's folder exists but has no video file yet — expected for a "Got" movie
            // whose file hasn't actually been placed, not a transient probe failure. Recording it
            // as a warning (not MediaScanError) settles LibraryRescanTask's retry filter on this
            // movie instead of retrying it every single scheduled run forever.
            const string warning = "No video file found in the local folder.";
            movie.MediaScannedAt = DateTime.UtcNow;
            movie.MediaScanError = null;
            movie.MediaScanWarning = warning;
            movie.FileCount = 0;
            movie.LocalFileSizeBytes = null;
            movie.MediaVideoFileName = null;
            movie.VrType = null;
            if (movie.MovieFiles.Count > 0)
            {
                db.MovieFiles.RemoveRange(movie.MovieFiles);
            }
            await db.SaveChangesAsync(ct);
            return new LocalRefreshResult(false, warning);
        }

        var diskFileInfos = new List<(string Path, string FileName, FileInfo Info)>();
        foreach (var path in videoPaths)
        {
            try
            {
                var info = new FileInfo(path);
                diskFileInfos.Add((path, Path.GetFileName(path), info));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new LocalRefreshResult(false, "Could not read the video file.");
            }
        }

        // Set once, on first observation, and never touched again below — see Movie.FileAddedAt's
        // doc comment for why this must survive later probes of the same movie (a video-file
        // upgrade/replacement shouldn't reset when the movie was originally added).
        if (movie.FileAddedAt is null && diskFileInfos.Count > 0)
        {
            movie.FileAddedAt = diskFileInfos.Min(d => d.Info.CreationTimeUtc);
            await db.SaveChangesAsync(ct);
        }

        var diskFileMap = diskFileInfos.ToDictionary(d => d.FileName, StringComparer.OrdinalIgnoreCase);
        var existingFileMap = movie.MovieFiles.ToDictionary(f => f.FileName, StringComparer.OrdinalIgnoreCase);

        // Cheap pre-check before the expensive native probe below: skip re-probing a movie whose
        // files match existing files, none were added or removed, and each file's size and last-write
        // time haven't moved since a successful probe.
        var countsMatch = movie.MovieFiles.Count == diskFileInfos.Count;
        var movieAlreadySucceeded = movie.MediaScannedAt is not null && movie.MediaScanError is null;
        var allUnchanged = countsMatch && movieAlreadySucceeded && diskFileInfos.All(d =>
            existingFileMap.TryGetValue(d.FileName, out var existing)
            && existing.MediaScannedAt is not null
            && existing.MediaScanError is null
            && existing.FileSizeBytes == d.Info.Length
            && existing.LastWriteUtc == d.Info.LastWriteTimeUtc);

        if (allUnchanged)
        {
            // Still re-derived without a probe: it depends on the file names (and the genres, which
            // a metadata refresh can change), and rows scanned before don't have it yet.
            if (LocalLibraryMetadataMapper.ApplyVrFormats(movie))
            {
                await db.SaveChangesAsync(ct);
            }
            return new LocalRefreshResult(true, null);
        }

        // Backfill legacy single-file records if MovieFiles has not yet been populated
        if (movie.MovieFiles.Count == 0 && diskFileInfos.Count == 1 && movieAlreadySucceeded)
        {
            var single = diskFileInfos[0];
            if (movie.LocalFileSizeBytes == single.Info.Length && movie.MediaVideoFileLastWriteUtc == single.Info.LastWriteTimeUtc)
            {
                var mf = new MovieFile
                {
                    MovieId = movie.Id,
                    FileName = single.FileName,
                    VersionTag = MovieVersionParser.ExtractVersionTag(single.FileName, movie.Code),
                    FileSizeBytes = single.Info.Length,
                    FileAddedAt = movie.FileAddedAt ?? single.Info.CreationTimeUtc,
                    LastWriteUtc = single.Info.LastWriteTimeUtc,
                    IsPrimary = true,
                };
                LocalLibraryMetadataMapper.CopyLegacyMovieToFile(movie, mf);
                movie.MovieFiles.Add(mf);
                db.MovieFiles.Add(mf);
                movie.FileCount = 1;
                movie.MediaVideoFileName = single.FileName;
                LocalLibraryMetadataMapper.ApplyVrFormats(movie);
                await db.SaveChangesAsync(ct);
                return new LocalRefreshResult(true, null);
            }
        }

        // Remove deleted files
        var filesToRemove = movie.MovieFiles.Where(mf => !diskFileMap.ContainsKey(mf.FileName)).ToList();
        foreach (var f in filesToRemove)
        {
            movie.MovieFiles.Remove(f);
            db.MovieFiles.Remove(f);
        }

        // Process disk files: add new or re-probe changed
        foreach (var (vPath, fName, fInfo) in diskFileInfos)
        {
            if (!existingFileMap.TryGetValue(fName, out var mf))
            {
                mf = new MovieFile
                {
                    MovieId = movie.Id,
                    FileName = fName,
                    VersionTag = MovieVersionParser.ExtractVersionTag(fName, movie.Code),
                    FileSizeBytes = fInfo.Length,
                    FileAddedAt = fInfo.CreationTimeUtc,
                    LastWriteUtc = fInfo.LastWriteTimeUtc,
                };
                movie.MovieFiles.Add(mf);
                db.MovieFiles.Add(mf);

                var probe = await mediaInfoProber.ProbeAsync(vPath, ct);
                LocalLibraryMetadataMapper.ApplyMediaProbe(mf, probe);
            }
            else
            {
                mf.VersionTag = MovieVersionParser.ExtractVersionTag(fName, movie.Code);
                mf.FileAddedAt ??= fInfo.CreationTimeUtc;

                var fileUnchanged = mf.MediaScannedAt is not null
                    && mf.MediaScanError is null
                    && mf.FileSizeBytes == fInfo.Length
                    && mf.LastWriteUtc == fInfo.LastWriteTimeUtc;

                if (!fileUnchanged)
                {
                    mf.FileSizeBytes = fInfo.Length;
                    mf.LastWriteUtc = fInfo.LastWriteTimeUtc;
                    var probe = await mediaInfoProber.ProbeAsync(vPath, ct);
                    LocalLibraryMetadataMapper.ApplyMediaProbe(mf, probe);
                }
            }
        }

        var primaryFile = MovieVersionParser.DeterminePrimaryFile(movie.MovieFiles, movie.Code)
            ?? movie.MovieFiles.FirstOrDefault();

        if (primaryFile is null)
        {
            return new LocalRefreshResult(false, "No valid video file found.");
        }

        foreach (var mf in movie.MovieFiles)
        {
            mf.IsPrimary = (mf == primaryFile);
        }
        LocalLibraryMetadataMapper.ApplyVrFormats(movie);

        var oldWidth = movie.MediaWidth;
        var oldHeight = movie.MediaHeight;
        var totalBytes = movie.MovieFiles.Sum(f => f.FileSizeBytes);

        LocalLibraryMetadataMapper.SyncPrimaryFileToMovie(movie, primaryFile, movie.MovieFiles.Count, totalBytes);

        // Only a genuine resolution change counts as "upgraded" — both old and new must be known,
        // so the very first successful probe for a movie (oldWidth/oldHeight still null) never
        // logs a false "upgrade" against nothing.
        if (primaryFile.MediaScanError is null && oldWidth is not null && oldHeight is not null
            && (primaryFile.Width != oldWidth || primaryFile.Height != oldHeight))
        {
            logger.LogDebug(
                "Video file changed for {Code}: {OldResolution} -> {NewResolution}",
                movie.Code, FormatResolution(oldWidth, oldHeight), FormatResolution(primaryFile.Width, primaryFile.Height));
        }

        await db.SaveChangesAsync(ct);

        // Past the unchanged-files shortcut above, so a video file was added, replaced or rewritten.
        if (primaryFile.MediaScanError is null && trickplayTrigger is not null)
        {
            await trickplayTrigger.OnVideoFilesChangedAsync(movieId, ct);
        }
        return new LocalRefreshResult(primaryFile.MediaScanError is null, primaryFile.MediaScanError);
    }

    private static string FormatResolution(int? width, int? height) =>
        width is not null && height is not null ? $"{width}x{height}" : "unknown resolution";

    public async Task<LocalRefreshResult> RefreshSubtitleFlagOnlyAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.FindAsync(new object[] { movieId }, ct);
        if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
        {
            return new LocalRefreshResult(false, "Movie not found.");
        }

        var folder = await FindMovieFolderAsync(movie.Code, ct);
        if (folder is null)
        {
            return new LocalRefreshResult(false, "No local folder found for this code.");
        }

        var newValue = HasSubtitleFile(folder);
        if (movie.MediaHasSubtitleFile != newValue)
        {
            logger.LogDebug(
                "Subtitle file {Change} for {Code}",
                newValue ? "detected" : "no longer found", movie.Code);
        }

        movie.MediaHasSubtitleFile = newValue;
        await db.SaveChangesAsync(ct);
        return new LocalRefreshResult(true, null);
    }

    public async Task<LocalRefreshResult> RefreshTrailerFlagOnlyAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.FindAsync(new object[] { movieId }, ct);
        if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
        {
            return new LocalRefreshResult(false, "Movie not found.");
        }

        var folder = await FindMovieFolderAsync(movie.Code, ct);
        if (folder is null)
        {
            return new LocalRefreshResult(false, "No local folder found for this code.");
        }

        var newValue = HasTrailerFile(folder);
        if (movie.MediaHasTrailerFile != newValue)
        {
            logger.LogDebug(
                "Trailer file {Change} for {Code}",
                newValue ? "detected" : "no longer found", movie.Code);
        }

        movie.MediaHasTrailerFile = newValue;
        await db.SaveChangesAsync(ct);
        return new LocalRefreshResult(true, null);
    }

    public async Task<LocalRefreshResult> RefreshLocalMetadataIfNfoChangedAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.FindAsync(new object[] { movieId }, ct);
        if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
        {
            return new LocalRefreshResult(false, "Movie not found.");
        }

        var folder = await FindMovieFolderAsync(movie.Code, ct);
        if (folder is null)
        {
            return new LocalRefreshResult(false, "No local folder found for this code.");
        }

        var nfoPath = FindNfoFile(folder, movie.Code);
        if (nfoPath is null)
        {
            if (movie.MediaNfoLastWriteUtc is not null)
            {
                movie.MediaNfoLastWriteUtc = null;
                await db.SaveChangesAsync(ct);
            }
            return new LocalRefreshResult(true, null);
        }

        DateTime currentWriteUtc;
        try
        {
            currentWriteUtc = File.GetLastWriteTimeUtc(nfoPath);
        }
        catch (IOException)
        {
            return new LocalRefreshResult(false, "Could not read the .nfo file.");
        }
        catch (UnauthorizedAccessException)
        {
            return new LocalRefreshResult(false, "Could not read the .nfo file.");
        }

        if (movie.MediaNfoLastWriteUtc is null)
        {
            // First observation for this movie — establish the baseline only. Applying now would
            // mean a whole-library rollout re-applies every movie's local .nfo content over
            // whatever's currently stored, even where that's better-sourced javinizer-go metadata.
            movie.MediaNfoLastWriteUtc = currentWriteUtc;
            await db.SaveChangesAsync(ct);
            return new LocalRefreshResult(true, null);
        }

        if (movie.MediaNfoLastWriteUtc.Value == currentWriteUtc)
        {
            return new LocalRefreshResult(true, null);
        }

        // Info in Javbuddy has absolute authority over metadata sources.
        // Rescan must not clobber existing Javbuddy metadata with .nfo contents.
        // If the movie has no descriptive metadata yet in Javbuddy, populate it from .nfo.
        // If the movie already has metadata in Javbuddy, preserve it — any divergence between
        // the .nfo file and Javbuddy will be detected non-destructively by DetectConflictsAsync.
        bool hasExistingMetadata = !string.IsNullOrWhiteSpace(movie.MetaTitle)
            || !string.IsNullOrWhiteSpace(movie.MetaSourceName)
            || !string.IsNullOrWhiteSpace(movie.MetaActresses);

        if (!hasExistingMetadata)
        {
            var metadata = await LocalNfoParser.ParseAsync(nfoPath, ct);
            if (metadata is not null)
            {
                LocalLibraryMetadataMapper.ApplyDescriptiveMetadata(movie, metadata);
                await MovieActorAssociation.SynchronizeAsync(db, movie, ct);
                await TagNormalization.ApplyToMovieAsync(db, movie, metadata.Genres, ct);
                logger.LogDebug(".nfo file observed for previously unpopulated {Code} — descriptive metadata populated", movie.Code);
            }
        }
        else
        {
            logger.LogDebug(".nfo file changed for {Code} — preserving authoritative Javbuddy metadata", movie.Code);
        }

        movie.MediaNfoLastWriteUtc = currentWriteUtc;
        await db.SaveChangesAsync(ct);
        return new LocalRefreshResult(true, null);
    }

    public async Task<LocalRefreshResult> SyncImagesSignatureAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.FindAsync(new object[] { movieId }, ct);
        if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
        {
            return new LocalRefreshResult(false, "Movie not found.");
        }

        var folder = await FindMovieFolderAsync(movie.Code, ct);
        if (folder is null)
        {
            return new LocalRefreshResult(false, "No local folder found for this code.");
        }

        var signature = ComputeImagesSignature(folder);

        if (movie.MediaImagesSignature is null)
        {
            // First observation — establish the baseline only, same reasoning as
            // RefreshLocalMetadataIfNfoChangedAsync: without this, a whole-library rollout would
            // report every movie's images as "changed" in one pass.
            movie.MediaImagesSignature = signature;
            await db.SaveChangesAsync(ct);
            return new LocalRefreshResult(true, null);
        }

        if (movie.MediaImagesSignature != signature)
        {
            logger.LogDebug("Poster, fanart, or extrafanart images changed for {Code}", movie.Code);
            movie.MediaImagesSignature = signature;
            await db.SaveChangesAsync(ct);
        }

        return new LocalRefreshResult(true, null);
    }

    /// <summary>Cheap composite signature of a movie folder's image set — not a cryptographic hash,
    /// just enough (file name/size/last-write-time for poster and fanart, count + total size for
    /// extrafanart) to notice something changed without statting every extrafanart file on every
    /// scan across the whole library.</summary>
    private static string ComputeImagesSignature(string folder)
    {
        var posterPath = PosterFileNames.Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
        var fanartPath = FanartFileNames.Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);

        var extraCount = 0;
        long extraTotalSize = 0;
        var extraFanartFolder = Path.Combine(folder, "extrafanart");
        if (Directory.Exists(extraFanartFolder))
        {
            try
            {
                foreach (var file in EnumerateExtraFanartFiles(extraFanartFolder))
                {
                    extraCount++;
                    extraTotalSize += new FileInfo(file).Length;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return string.Join('|',
            FileSignaturePart("poster", posterPath),
            FileSignaturePart("fanart", fanartPath),
            $"extra:{extraCount}:{extraTotalSize}");
    }

    private static string FileSignaturePart(string label, string? path)
    {
        if (path is null) return $"{label}:none";

        try
        {
            var info = new FileInfo(path);
            return $"{label}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }
        catch (IOException)
        {
            return $"{label}:unknown";
        }
        catch (UnauthorizedAccessException)
        {
            return $"{label}:unknown";
        }
    }

    private static string? FindNfoFile(string folder, string code)
    {
        var exact = Path.Combine(folder, code + ".nfo");
        if (File.Exists(exact)) return exact;

        try
        {
            return Directory.EnumerateFiles(folder, "*.nfo").FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // A folder can exist but be unreadable (e.g. a permission-denied network share entry, as hit
    // against a real corrupted-file directory in production) — Directory.EnumerateFiles throws
    // UnauthorizedAccessException for that, distinct from IOException, and both must be treated the
    // same "can't tell, assume no" way as everywhere else in this file that enumerates a folder.
    private static bool HasSubtitleFile(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder)
                .Any(f => SubtitleExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Same "-trailer" suffix convention video-file selection already excludes when picking the movie's own
    // video file (e.g. "ABC-123-trailer.mp4" alongside "ABC-123.webm") — here that's exactly what
    // we're looking for instead of excluding.
    private static bool HasTrailerFile(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder)
                .Any(f => VideoExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
                    && Path.GetFileNameWithoutExtension(f).EndsWith("-trailer", StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
