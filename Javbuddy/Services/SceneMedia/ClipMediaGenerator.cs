using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.SceneMedia;

/// <summary>What a generation run did: a range with any failed variant is remembered as
/// failed even when a sibling variant was written, and any write still reports the movie.</summary>
public readonly record struct ClipGenerateOutcome(bool WroteAny, bool FailedAny)
{
    public static readonly ClipGenerateOutcome NothingToDo = new(false, false);
    public static readonly ClipGenerateOutcome Failed = new(false, true);
}

/// <summary>The kind-agnostic half of scene/highlight media: find and probe the
/// movie's main file, sample a still (WebP) and a preview (WebM video) for a time range,
/// write them as image-cache entries stamped with that range, and keep the cache within its size limit. Callers decide the
/// cache role, the index and the range.</summary>
public sealed class ClipMediaGenerator(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    IFfmpegClient ffmpegClient,
    ILocalImageCache imageCache,
    IImageCacheMaintenanceService cacheMaintenance)
{
    public IReadOnlyList<string> EnabledVariants =>
        new[] { SceneMediaService.VariantThumb, SceneMediaService.VariantPreview }.Where(imageCache.Settings.CachesLocalVariant).ToList();

    public bool IsCurrent(CachedImage row, SceneMediaWindow window) =>
        row.SceneStartMs == window.StartMs
        && row.SceneEndMs == window.EndMs
        && !imageCache.Settings.IsExpired(row.UpdatedAt)
        && File.Exists(imageCache.GetStoragePath(row));

    /// <summary>Writes whichever enabled variants aren't current for <paramref name="window"/>.
    /// endSeconds null means unknown (the probed duration is used for the middle, as for scenes).
    /// <paramref name="variants"/> limits which of them are made (apexes only have a preview).</summary>
    public async Task<ClipGenerateOutcome> GenerateAsync(string role, int index, int movieId, double startSeconds, double? endSeconds,
        SceneMediaWindow window, IReadOnlyList<CachedImage> rows, CancellationToken ct, IReadOnlyCollection<string>? variants = null)
    {
        var missing = EnabledVariants.Where(v => variants is null || variants.Contains(v)).Where(v => rows.FirstOrDefault(r => r.Variant == v) is not { } row || !IsCurrent(row, window)).ToList();
        if (missing.Count == 0) return ClipGenerateOutcome.NothingToDo;

        var file = await MovieVideoFiles.FindMainAsync(dbFactory, localLibraryClient, movieId, ct);
        var probe = file is null ? null : await ffmpegClient.ProbeAsync(file.Path, ct);
        if (file is null || probe is null) return ClipGenerateOutcome.Failed;

        var effectiveEnd = endSeconds ?? (probe.DurationSeconds > 0 ? probe.DurationSeconds : null);
        var (stillAt, previewAt, previewSeconds) = SceneMediaService.SampleWindow(startSeconds, effectiveEnd);
        var leftEyeOnly = SideBySideVideo.IsSideBySide(probe.Width, probe.Height);

        long addedBytes = 0;
        var wroteAny = false;
        var failedAny = false;
        foreach (var variant in missing)
        {
            var storageId = rows.FirstOrDefault(r => r.Variant == variant)?.StorageId ?? Guid.NewGuid();
            var destination = imageCache.GetStoragePath(storageId, variant);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temp = Path.ChangeExtension(destination, ".tmp" + Path.GetExtension(destination));

            var result = variant == SceneMediaService.VariantThumb
                ? await ffmpegClient.ExtractStillWebpAsync(file.Path, stillAt, leftEyeOnly, temp, ct)
                : await ffmpegClient.ExtractPreviewVideoAsync(file.Path, previewAt, previewSeconds, leftEyeOnly, temp, ct);
            if (!result.Success || !File.Exists(temp) || new FileInfo(temp).Length == 0)
            {
                TryDelete(temp);
                failedAny = true;
                continue;
            }

            File.Move(temp, destination, overwrite: true);
            addedBytes += new FileInfo(destination).Length;
            await UpsertRowAsync(file.Code, role, index, variant, storageId, window, ct);
            wroteAny = true;
        }

        if (addedBytes > 0)
        {
            await cacheMaintenance.EnforceSizeLimitAsync(addedBytes, ct);
        }
        return new ClipGenerateOutcome(wroteAny, failedAny);
    }

    public async Task DeleteAsync(string role, int index, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.CachedImages.Where(c => c.Role == role && c.Index == index).ToListAsync(ct);
        if (rows.Count == 0) return;

        foreach (var row in rows)
        {
            TryDelete(imageCache.GetStoragePath(row));
        }
        db.CachedImages.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
    }

    private async Task UpsertRowAsync(string code, string role, int index, string variant, Guid storageId, SceneMediaWindow window, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.CachedImages.FirstOrDefaultAsync(c => c.Code == code && c.Role == role && c.Index == index && c.Variant == variant, ct);
        if (row is null)
        {
            row = new CachedImage { Code = code, Role = role, Index = index, Variant = variant };
            db.CachedImages.Add(row);
        }
        row.StorageId = storageId;
        row.SceneStartMs = window.StartMs;
        row.SceneEndMs = window.EndMs;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
