using Javbuddy.Data;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Javbuddy.Services.Movies;

public enum ExtraFanartStillsMode
{
    /// <summary>Add the stills after the existing extrafanart images.</summary>
    Append,

    /// <summary>Delete the existing extrafanart images, then add the stills.</summary>
    ReplaceAll,

    /// <summary>Only generate when the movie has no extrafanart yet.</summary>
    OnlyIfNone,
}

/// <summary>One candidate still: its position in the video and a small preview image.</summary>
public sealed record ExtraFanartStill(int Id, double Seconds, byte[] PreviewWebp);

/// <summary>Candidate stills extracted for a movie. Owns a scratch folder holding the full-size frames
/// until it is disposed, so the user can pick which ones to save.</summary>
public sealed class ExtraFanartStillsPreview(string scratchDirectory, IReadOnlyList<ExtraFanartStill> stills) : IDisposable
{
    public IReadOnlyList<ExtraFanartStill> Stills { get; } = stills;

    public string FramePath(int id) => Path.Combine(scratchDirectory, $"{id}.webp");

    public void Dispose()
    {
        try { Directory.Delete(scratchDirectory, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

public sealed record ExtraFanartStillsPreviewResult(ExtraFanartStillsPreview? Preview, string? ErrorMessage = null)
{
    public bool Success => Preview is not null;
}

public interface IMovieExtraFanartStillsService
{
    /// <summary>Samples frames evenly across the movie's video (skipping its first and last 10%), scores them for
    /// sharpness and exposure and keeps the better of two frames per slot, in video order. leftEyeOnly crops side-by-side VR frames to the left eye.</summary>
    Task<ExtraFanartStillsPreviewResult> PreviewAsync(int movieId, int count, ExtraFanartStillsMode mode, bool leftEyeOnly = false, Action<ExtraFanartStill>? onStill = null, CancellationToken ct = default);

    /// <summary>Saves the chosen stills as WebP into the movie's extrafanart folder, following <paramref name="mode"/>.</summary>
    Task<ExtraFanartResult> SaveAsync(int movieId, ExtraFanartStillsPreview preview, IReadOnlyCollection<int> selectedIds, int quality, ExtraFanartStillsMode mode, CancellationToken ct = default);
}

/// <summary>Generates extrafanart from a movie's own video. WebP end to end: the bundled ffmpeg only has
/// the libwebp encoder (no JPEG), and the extrafanart folder accepts WebP.</summary>
public class MovieExtraFanartStillsService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    IFfmpegClient ffmpegClient,
    IMovieExtraFanartService extraFanartService,
    ILogger<MovieExtraFanartStillsService> logger,
    ImageUploadSettings? uploadSettings = null) : IMovieExtraFanartStillsService
{
    private readonly ImageUploadSettings uploadSettings = uploadSettings ?? ImageUploadSettings.Default;

    public const int MinCount = 1;
    public const int MaxCount = 20;
    public const int DefaultCount = 10;
    public const int MinQuality = 50;
    public const int MaxQuality = 100;
    public const int DefaultQuality = 92;

    private const double EdgeSkipFraction = 0.10;
    private const int PreviewWidth = 480;
    private const int PreviewQuality = 70;

    public async Task<ExtraFanartStillsPreviewResult> PreviewAsync(int movieId, int count, ExtraFanartStillsMode mode, bool leftEyeOnly = false, Action<ExtraFanartStill>? onStill = null, CancellationToken ct = default)
    {
        count = Math.Clamp(count, MinCount, MaxCount);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null || string.IsNullOrWhiteSpace(movie.Code)) return Fail("Movie not found.");

        var folder = await localLibraryClient.ResolveMovieFolderPathAsync(movie.Code, ct);
        if (folder is null) return Fail("No local folder found for this movie.");

        if (mode == ExtraFanartStillsMode.OnlyIfNone && (await localLibraryClient.ListExtraFanartFileNamesAsync(movie.Code, ct)).Count > 0)
        {
            return Fail("The movie already has extrafanart images.");
        }

        var main = await MovieVideoFiles.FindMainAsync(dbFactory, localLibraryClient, movieId, ct);
        var video = main?.Path;
        if (video is null || movie.MediaDurationSeconds is not > 0)
        {
            return Fail("The movie has no scanned local video to take frames from. Rescan it first.");
        }

        var scratch = Path.Combine(Path.GetTempPath(), "javbuddy-stills-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var handedOver = false;
        try
        {
            // One still per slot of the video: two frames are taken inside the slot and the sharper, better-exposed
            // one is kept, so each still is final (and reported through onStill) as soon as its slot is done.
            var stills = new List<ExtraFanartStill>();
            string? firstError = null;
            foreach (var (slot, candidateTimes) in SlotCandidateTimes(movie.MediaDurationSeconds.Value, count).Index())
            {
                var id = slot + 1;
                (double Seconds, double Score, string Path)? best = null;
                foreach (var (attempt, at) in candidateTimes.Index())
                {
                    var framePath = Path.Combine(scratch, $"{id}-{attempt}.webp");
                    var extracted = await ffmpegClient.ExtractFrameWebpAsync(video, at, leftEyeOnly, framePath, ct);
                    if (!extracted.Success || !File.Exists(framePath) || new FileInfo(framePath).Length == 0)
                    {
                        firstError ??= extracted.ErrorMessage ?? "ffmpeg wrote no frame at that point of the video";
                        continue;
                    }

                    using var bitmap = SKBitmap.Decode(framePath);
                    if (bitmap is null)
                    {
                        firstError ??= "the extracted frame couldn't be decoded";
                        continue;
                    }

                    var score = FrameScorer.Score(bitmap);
                    if (best is null || score > best.Value.Score) best = (at, score, framePath);
                }
                if (best is null) continue;

                var chosenPath = Path.Combine(scratch, $"{id}.webp");
                File.Move(best.Value.Path, chosenPath, overwrite: true);
                foreach (var other in Directory.EnumerateFiles(scratch, $"{id}-*.webp")) File.Delete(other);
                var preview = EncodeWebp(chosenPath, PreviewWidth, PreviewQuality);
                if (preview is null) continue;

                var still = new ExtraFanartStill(id, best.Value.Seconds, preview);
                stills.Add(still);
                onStill?.Invoke(still);
            }

            if (stills.Count == 0 && firstError is not null)
            {
                logger.LogWarning("No still could be extracted from {Video} for {Code}: {Error}", video, movie.Code, firstError);
                return Fail($"Couldn't extract any frames from the video ({firstError}).");
            }
            if (stills.Count == 0) return Fail("Couldn't build previews from the extracted frames.");

            handedOver = true;
            return new ExtraFanartStillsPreviewResult(new ExtraFanartStillsPreview(scratch, stills));
        }
        finally
        {
            if (!handedOver)
            {
                try { Directory.Delete(scratch, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    public async Task<ExtraFanartResult> SaveAsync(int movieId, ExtraFanartStillsPreview preview, IReadOnlyCollection<int> selectedIds, int quality, ExtraFanartStillsMode mode, CancellationToken ct = default)
    {
        quality = Math.Clamp(quality, MinQuality, MaxQuality);
        var chosen = preview.Stills.Where(s => selectedIds.Contains(s.Id)).ToList();
        if (chosen.Count == 0) return ExtraFanartResult.Fail("Select at least one still.");

        // Everything that can fail on a bad frame happens before anything is deleted or written.
        var encoded = new List<byte[]>();
        foreach (var still in chosen)
        {
            var bytes = EncodeWebp(preview.FramePath(still.Id), maxWidth: null, quality);
            if (bytes is null) return ExtraFanartResult.Fail("Couldn't convert an extracted frame to WebP.");
            // The limits AddAsync enforces, checked before Replace all deletes anything.
            if (bytes.LongLength > uploadSettings.MaxSizeBytes) return ExtraFanartResult.Fail($"A still is larger than {uploadSettings.MaxSizeMb} MB; lower the quality.");
            encoded.Add(bytes);
        }
        if (encoded.Count > uploadSettings.MaxBatchFiles) return ExtraFanartResult.Fail($"You can add at most {uploadSettings.MaxBatchFiles} images at a time.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var code = await db.Movies.Where(m => m.Id == movieId).Select(m => m.Code).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(code)) return ExtraFanartResult.Fail("Movie not found.");

        var existing = await localLibraryClient.ListExtraFanartFileNamesAsync(code, ct);
        if (mode == ExtraFanartStillsMode.OnlyIfNone && existing.Count > 0)
        {
            return ExtraFanartResult.Fail("The movie already has extrafanart images.");
        }

        if (mode == ExtraFanartStillsMode.ReplaceAll)
        {
            foreach (var name in existing)
            {
                var deleted = await extraFanartService.DeleteAsync(movieId, name, ct);
                if (!deleted.Success) return deleted with { ErrorMessage = $"{deleted.ErrorMessage} Some existing images may already have been removed." };
            }
        }

        var items = encoded.Select(b => new ExtraFanartUploadItem(() => new MemoryStream(b), b.LongLength)).ToList();
        return await extraFanartService.AddAsync(movieId, items, ct: ct);
    }

    /// <summary>For each of <paramref name="count"/> equal slots of the video (minus its first and last 10%, where logos and
    /// credits are), the two times inside the slot at a quarter and three quarters of its length.</summary>
    public static List<double[]> SlotCandidateTimes(double durationSeconds, int count)
    {
        var start = durationSeconds * EdgeSkipFraction;
        var slot = durationSeconds * (1 - 2 * EdgeSkipFraction) / count;
        return Enumerable.Range(0, count).Select(i => new[] { start + slot * (i + 0.25), start + slot * (i + 0.75) }).ToList();
    }

    private static byte[]? EncodeWebp(string sourcePath, int? maxWidth, int quality)
    {
        using var frame = SKBitmap.Decode(sourcePath);
        if (frame is null) return null;

        var bitmap = frame;
        SKBitmap? resized = null;
        try
        {
            if (maxWidth is { } w && frame.Width > w)
            {
                resized = frame.Resize(new SKImageInfo(w, Math.Max(1, frame.Height * w / frame.Width)), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                if (resized is not null) bitmap = resized;
            }

            using var data = bitmap.Encode(SKEncodedImageFormat.Webp, quality);
            return data?.ToArray();
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private static ExtraFanartStillsPreviewResult Fail(string message) => new(null, message);
}
