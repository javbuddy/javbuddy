using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.SceneMedia;

public interface IApexMediaService
{
    /// <summary>Path of an apex's current hover preview, or null when there's none yet — in which case
    /// it's queued for generation. Apexes have no screenshot, so any other variant is null.</summary>
    Task<string?> GetFilePathAsync(int apexId, string variant, CancellationToken ct = default);

    /// <summary>Whether the image cache's mode keeps previews at all.</summary>
    bool ServesPreview { get; }

    bool IsCurrent(CachedImage row, SceneMediaWindow window);

    /// <summary>Queues every apex of the movie that lacks a current preview.</summary>
    Task EnsureQueuedAsync(int movieId, CancellationToken ct = default);

    /// <summary>Returns the movie id when anything was written, null otherwise.</summary>
    Task<int?> GenerateAsync(int apexId, CancellationToken ct = default);

    Task DeleteForApexAsync(int apexId, CancellationToken ct = default);
}

/// <summary>Hover previews for apex markers: a WebM of the few seconds around the
/// apex, as CachedImage rows with Role "apex", Index = MovieApex.Id and Variant "preview" only.
/// A row is stamped with the apex's time, so only moving the apex makes it stale. Generation shares
/// ClipMediaGenerator and the single SceneMediaQueue worker.</summary>
public class ApexMediaService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    IFfmpegClient ffmpegClient,
    ILocalImageCache imageCache,
    IImageCacheMaintenanceService cacheMaintenance,
    SceneMediaQueue queue) : IApexMediaService
{
    public const string Role = "apex";

    /// <summary>The clip runs this long either side of the apex, inside the movie.</summary>
    public const double HalfSpanSeconds = 2;

    private static readonly string[] Variants = [SceneMediaService.VariantPreview];

    private readonly ClipMediaGenerator generator = new(dbFactory, localLibraryClient, ffmpegClient, imageCache, cacheMaintenance);

    public static SceneMediaWindow Window(double seconds) => new(SceneMediaService.StartMs(seconds), SceneMediaService.StartMs(seconds));

    public bool ServesPreview => generator.EnabledVariants.Contains(SceneMediaService.VariantPreview);

    public bool IsCurrent(CachedImage row, SceneMediaWindow window) => generator.IsCurrent(row, window);

    public async Task<string?> GetFilePathAsync(int apexId, string variant, CancellationToken ct = default)
    {
        if (variant != SceneMediaService.VariantPreview || !ServesPreview) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var apex = await db.MovieApexes.AsNoTracking().FirstOrDefaultAsync(a => a.Id == apexId, ct);
        if (apex is null) return null;

        var window = Window(apex.Seconds);
        var row = await db.CachedImages.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Role == Role && c.Index == apexId && c.Variant == variant, ct);
        if (row is not null && generator.IsCurrent(row, window))
        {
            return imageCache.GetStoragePath(row);
        }

        queue.EnqueueApex(apexId, apex.MovieId, window);
        return null;
    }

    public async Task EnsureQueuedAsync(int movieId, CancellationToken ct = default)
    {
        if (!ServesPreview) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var apexes = await db.MovieApexes.AsNoTracking().Where(a => a.MovieId == movieId).ToListAsync(ct);
        if (apexes.Count == 0) return;
        var ids = apexes.Select(a => a.Id).ToList();
        var rows = await db.CachedImages.AsNoTracking().Where(c => c.Role == Role && ids.Contains(c.Index)).ToListAsync(ct);
        foreach (var apex in apexes)
        {
            var window = Window(apex.Seconds);
            if (!rows.Any(r => r.Index == apex.Id && r.Variant == SceneMediaService.VariantPreview && generator.IsCurrent(r, window)))
            {
                queue.EnqueueApex(apex.Id, movieId, window);
            }
        }
    }

    public async Task<int?> GenerateAsync(int apexId, CancellationToken ct = default)
    {
        MovieApex? apex;
        double? duration;
        List<CachedImage> rows;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            apex = await db.MovieApexes.AsNoTracking().FirstOrDefaultAsync(a => a.Id == apexId, ct);
            if (apex is null) return null;
            duration = await db.Movies.Where(m => m.Id == apex.MovieId).Select(m => m.MediaDurationSeconds).FirstOrDefaultAsync(ct);
            rows = await db.CachedImages.AsNoTracking().Where(c => c.Role == Role && c.Index == apexId).ToListAsync(ct);
        }

        var start = Math.Max(apex.Seconds - HalfSpanSeconds, 0);
        var end = apex.Seconds + HalfSpanSeconds;
        if (duration is > 0 and var d) end = Math.Min(end, d);
        var window = Window(apex.Seconds);
        var outcome = await generator.GenerateAsync(Role, apexId, apex.MovieId, start, end, window, rows, ct, Variants);
        if (outcome.FailedAny) queue.MarkApexFailed(apexId, window);
        return outcome.WroteAny ? apex.MovieId : null;
    }

    public Task DeleteForApexAsync(int apexId, CancellationToken ct = default) => generator.DeleteAsync(Role, apexId, ct);
}
