using System.Text.RegularExpressions;
using Javbuddy.Data;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Javbuddy.Services.Movies;

/// <summary>Adds images to and deletes images from a movie's local "extrafanart" folder
///. The movie folder stays the source of truth: files are written straight to disk,
/// then the index-keyed extrafanart cache rows that may now describe a different file are dropped.</summary>
public interface IMovieExtraFanartService
{
    /// <summary>Validates every item as a JPEG, PNG or WebP image and saves the batch as
    /// fanart{N}.{ext}, numbered after the highest existing fanartN. All or nothing.</summary>
    Task<ExtraFanartResult> AddAsync(int movieId, IReadOnlyList<ExtraFanartUploadItem> items, IProgress<int>? progress = null, CancellationToken ct = default);

    /// <summary>Downloads one image and adds it like <see cref="AddAsync"/>.</summary>
    Task<ExtraFanartResult> AddFromUrlAsync(int movieId, string url, CancellationToken ct = default);

    /// <summary>Deletes one file from the extrafanart folder. Remaining files keep their names.</summary>
    Task<ExtraFanartResult> DeleteAsync(int movieId, string fileName, CancellationToken ct = default);
}

public sealed record ExtraFanartUploadItem(Func<Stream> OpenStream, long Size);

public sealed record ExtraFanartResult(bool Success, int AddedCount = 0, string? ErrorMessage = null)
{
    public static ExtraFanartResult Ok(int addedCount = 0) => new(true, addedCount);

    public static ExtraFanartResult Fail(string message) => new(false, ErrorMessage: message);
}

public partial class MovieExtraFanartService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    ILocalImageCache imageCache,
    IHttpClientFactory httpClientFactory,
    ImageUploadSettings? uploadSettings = null,
    ILogger<MovieExtraFanartService>? logger = null) : IMovieExtraFanartService
{
    private const string ExtraFanartFolderName = "extrafanart";
    private const string NoLocalFolderMessage = "This movie has no local folder.";
    private const string UnsupportedFormatMessage = "Unsupported or corrupted image. Only JPEG, PNG, or WebP images are supported.";

    private readonly ImageUploadSettings uploadSettings = uploadSettings ?? ImageUploadSettings.Default;

    [GeneratedRegex(@"^fanart(\d+)\.[^.]+$", RegexOptions.IgnoreCase)]
    private static partial Regex FanartNumberPattern();

    // Picking the next fanartN and moving files into place happens under one gate, so two batches
    // committing at once (two tabs, or an upload and a URL import) can't both take the same
    // number with different extensions (fanart4.jpg + fanart4.png).
    private static readonly SemaphoreSlim CommitGate = new(1, 1);

    private string TooLargeMessage => $"Each image must be no larger than {uploadSettings.MaxSizeMb} MB.";

    public async Task<ExtraFanartResult> AddAsync(int movieId, IReadOnlyList<ExtraFanartUploadItem> items, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (items.Count == 0) return ExtraFanartResult.Fail("Choose at least one image.");
        if (items.Count > uploadSettings.MaxBatchFiles) return ExtraFanartResult.Fail($"You can add at most {uploadSettings.MaxBatchFiles} images at a time.");

        var target = await ResolveTargetAsync(movieId, ct);
        if (target is null) return ExtraFanartResult.Fail(NoLocalFolderMessage);
        var (code, folder) = target.Value;

        // Each image is staged under a dot-prefixed .tmp name (never listed as extrafanart) so a
        // failure partway through the batch leaves no half-written or orphaned fanartN behind.
        var staged = new List<(string TmpPath, string Extension)>();
        var folderExisted = Directory.Exists(folder);
        try
        {
            foreach (var item in items)
            {
                if (item.Size > uploadSettings.MaxSizeBytes) return ExtraFanartResult.Fail(TooLargeMessage);

                byte[] bytes;
                try
                {
                    bytes = await ReadAllAsync(item, ct);
                }
                catch (IOException ex)
                {
                    // The browser's stream, not the movie folder: e.g. the file changed or vanished
                    // after it was selected.
                    return ExtraFanartResult.Fail($"Could not read a selected image: {ex.Message}");
                }
                if (bytes.LongLength == 0 || bytes.LongLength > uploadSettings.MaxSizeBytes) return ExtraFanartResult.Fail(TooLargeMessage);

                var extension = DetectExtension(bytes);
                if (extension is null) return ExtraFanartResult.Fail(UnsupportedFormatMessage);

                Directory.CreateDirectory(folder);
                var tmpPath = Path.Combine(folder, $".upload-{Guid.NewGuid():N}.tmp");
                staged.Add((tmpPath, extension));
                await File.WriteAllBytesAsync(tmpPath, bytes, ct);
                progress?.Report(staged.Count);
            }

            return await CommitAsync(movieId, code, folder, staged, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "Failed to add extrafanart images for {Code}", code);
            return ExtraFanartResult.Fail($"Could not write to the movie folder: {ex.Message}");
        }
        finally
        {
            // Whatever ended the batch (a rejected image, an IO error, the browser disconnecting
            // mid-stream), no staged file stays behind. Committed ones were moved, so this is a
            // no-op for them.
            foreach (var (tmpPath, _) in staged) TryDelete(tmpPath);

            // A batch that created the extrafanart folder and then failed leaves it empty; remove
            // it again. A folder that holds anything (this batch's committed files, or another
            // batch's) stays.
            if (!folderExisted) TryDeleteIfEmpty(folder);
        }
    }

    private static async Task<byte[]> ReadAllAsync(ExtraFanartUploadItem item, CancellationToken ct)
    {
        await using var stream = item.OpenStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        return memory.ToArray();
    }

    public async Task<ExtraFanartResult> AddFromUrlAsync(int movieId, string url, CancellationToken ct = default)
    {
        var urlFailure = RemoteImageDownloader.TryParseUrl(url, out var uri);
        var download = urlFailure != RemoteImageDownloadFailure.None
            ? new RemoteImageDownloadResult(urlFailure)
            : await RemoteImageDownloader.DownloadAsync(
                httpClientFactory.CreateClient(RemoteImageDownloader.ImportClientName),
                uri!,
                uploadSettings.MaxSizeBytes,
                ct,
                isAcceptableMediaType: mediaType => mediaType is null || mediaType.StartsWith("image/") || mediaType == "application/octet-stream");
        if (!download.Success) return ExtraFanartResult.Fail(RemoteImageDownloadMessages.Describe(download, uploadSettings.MaxSizeMb));

        var bytes = download.Bytes!;
        return await AddAsync(movieId, [new ExtraFanartUploadItem(() => new MemoryStream(bytes), bytes.LongLength)], ct: ct);
    }

    public async Task<ExtraFanartResult> DeleteAsync(int movieId, string fileName, CancellationToken ct = default)
    {
        var target = await ResolveTargetAsync(movieId, ct);
        if (target is null) return ExtraFanartResult.Fail(NoLocalFolderMessage);
        var code = target.Value.Code;

        // ResolveLiveExtraFanartFilePathAsync only ever returns a file inside the extrafanart
        // folder (it rejects path separators and ".."), so fileName can't reach anything else.
        var names = await localLibraryClient.ListExtraFanartFileNamesAsync(code, ct);
        var index = names.FindIndex(n => string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase));
        var path = await localLibraryClient.ResolveLiveExtraFanartFilePathAsync(code, fileName, ct);
        if (index < 0 || path is null) return ExtraFanartResult.Fail("That image no longer exists.");

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "Failed to delete extrafanart {FileName} for {Code}", fileName, code);
            return ExtraFanartResult.Fail($"Could not delete the image: {ex.Message}");
        }

        await AfterChangeAsync(movieId, code, index, ct);
        return ExtraFanartResult.Ok();
    }

    // Moves every staged file to the next free fanartN name. If a move fails, the files already
    // moved in this batch are deleted again (AddAsync's finally deletes the remaining .tmp files).
    private async Task<ExtraFanartResult> CommitAsync(int movieId, string code, string folder, List<(string TmpPath, string Extension)> staged, CancellationToken ct)
    {
        var countBefore = (await localLibraryClient.ListExtraFanartFileNamesAsync(code, ct)).Count;
        var committed = new List<string>();
        await CommitGate.WaitAsync(ct);
        try
        {
            var next = NextFanartNumber(folder);
            foreach (var (tmpPath, extension) in staged)
            {
                while (true)
                {
                    var finalPath = Path.Combine(folder, $"fanart{next++}{extension}");
                    if (File.Exists(finalPath)) continue;
                    try
                    {
                        File.Move(tmpPath, finalPath, overwrite: false);
                        committed.Add(finalPath);
                        break;
                    }
                    catch (IOException) when (File.Exists(finalPath))
                    {
                        // Raced with a concurrent add for the same number — take the next one.
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var path in committed) TryDelete(path);
            throw;
        }
        finally
        {
            CommitGate.Release();
        }

        var namesAfter = await localLibraryClient.ListExtraFanartFileNamesAsync(code, ct);
        var newNames = committed.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var firstNewIndex = namesAfter.FindIndex(newNames.Contains);
        await AfterChangeAsync(movieId, code, firstNewIndex < 0 ? countBefore : firstNewIndex, ct);
        return ExtraFanartResult.Ok(committed.Count);
    }

    private async Task<(string Code, string Folder)?> ResolveTargetAsync(int movieId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var code = await db.Movies.Where(m => m.Id == movieId).Select(m => m.Code).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(code)) return null;

        var movieFolder = await localLibraryClient.ResolveMovieFolderPathAsync(code, ct);
        return movieFolder is null ? null : (code, Path.Combine(movieFolder, ExtraFanartFolderName));
    }

    private static int NextFanartNumber(string folder)
    {
        var highest = 0;
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var match = FanartNumberPattern().Match(Path.GetFileName(file));
                if (match.Success && int.TryParse(match.Groups[1].Value, out var number) && number > highest) highest = number;
            }
        }
        return highest + 1;
    }

    private static string? DetectExtension(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var codec = SKCodec.Create(stream);
        return codec?.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => ".jpg",
            SKEncodedImageFormat.Png => ".png",
            SKEncodedImageFormat.Webp => ".webp",
            _ => null,
        };
    }

    // Extrafanart cache rows are keyed by natural-sort position (CachedImage.Index), so every row
    // at or after the first changed position may now describe a different file. Drop them;
    // /image-cache regenerates them on the next request.
    private async Task AfterChangeAsync(int movieId, string code, int firstAffectedIndex, CancellationToken ct)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var rows = await db.CachedImages.Where(c =>
                c.Code == code && c.Role == LocalImageCacheService.RoleExtraFanart && c.Index >= firstAffectedIndex).ToListAsync(ct);
            foreach (var row in rows) TryDelete(imageCache.GetStoragePath(row.StorageId));
            db.CachedImages.RemoveRange(rows);
            await db.SaveChangesAsync(ct);
        }

        await localLibraryClient.SyncImagesSignatureAsync(movieId, ct);
    }

    private void TryDeleteIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "Could not remove empty folder {Folder}", folder);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "Could not delete {Path}", path);
        }
    }
}
