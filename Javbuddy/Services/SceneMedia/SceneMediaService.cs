using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Scenes;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.SceneMedia;

public interface ISceneMediaService
{
    /// <summary>Path of a scene's current screenshot ("thumb") or hover preview ("preview"), or
    /// null when there's none yet — in which case it's queued for generation.</summary>
    Task<string?> GetFilePathAsync(int sceneId, string variant, CancellationToken ct = default);

    /// <summary>Whether the current ImageCache:Mode serves this variant at all (Disabled: none;
    /// Thumbnails: screenshots only), regardless of cache rows left from an earlier mode
    ///.</summary>
    bool ServesVariant(string variant);

    /// <summary>Whether a cache row is one GetFilePathAsync would serve for the window: sampled for
    /// that range, unexpired and still on disk. Anything else would 404.</summary>
    bool IsCurrent(CachedImage row, SceneMediaWindow window);

    /// <summary>Queues every scene of the movie that lacks current media.</summary>
    Task EnsureQueuedAsync(int movieId, CancellationToken ct = default);

    /// <summary>Generates whatever media the scene is missing. Returns the movie id when anything
    /// was written, null otherwise (nothing to do, or the video file isn't reachable).</summary>
    Task<int?> GenerateAsync(int sceneId, CancellationToken ct = default);

    /// <summary>Removes the scene's media rows and files.</summary>
    Task DeleteForSceneAsync(int sceneId, CancellationToken ct = default);

    /// <summary>Records that the movie's scenes were just edited, so generation for it waits until
    /// the layout settles.</summary>
    void NoteScenesChanged(int movieId);
}

/// <summary>Scene screenshots and hover previews, stored as ordinary image-cache
/// entries — CachedImage rows with Role "scene" and Index = Scene.Id, WebP stills and WebM previews
/// under the cache directory — so they follow ImageCache:Mode (Disabled: none;
/// Thumbnails: screenshots only), TTL and size eviction, and PurgeAsync like every other cached image. An entry counts as current only
/// while it was sampled for the scene's current range (SceneStartMs and SceneEndMs);
/// anything missing, moved or expired is regenerated in the background (SceneMediaQueue) the next
/// time it's needed. This class holds the scene rules (effective ends from neighboring scenes); the
/// ffmpeg and cache work is ClipMediaGenerator's, shared with HighlightMediaService.</summary>
public class SceneMediaService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    IFfmpegClient ffmpegClient,
    ILocalImageCache imageCache,
    IImageCacheMaintenanceService cacheMaintenance,
    SceneMediaQueue queue) : ISceneMediaService
{
    public const string Role = "scene";
    public const string VariantThumb = LocalImageCacheService.VariantThumb;
    public const string VariantPreview = "preview";

    private const double StillOffsetSeconds = 3;
    private const double PreviewSeconds = 4;

    public static long StartMs(double startSeconds) => (long)Math.Round(startSeconds * 1000);

    /// <summary>The range media is sampled for: the scene's start and its effective end as resolved
    /// from the database (SceneRanges), so a boundary change anywhere in the movie that moves either
    /// one makes the media stale.</summary>
    public static SceneMediaWindow Window(double startSeconds, double? effectiveEndSeconds) =>
        new(StartMs(startSeconds), effectiveEndSeconds is { } end ? StartMs(end) : null);

    private readonly ClipMediaGenerator generator = new(dbFactory, localLibraryClient, ffmpegClient, imageCache, cacheMaintenance);

    private IReadOnlyList<string> EnabledVariants => generator.EnabledVariants;

    public bool IsCurrent(CachedImage row, SceneMediaWindow window) => generator.IsCurrent(row, window);

    public bool ServesVariant(string variant) => EnabledVariants.Contains(variant);

    public async Task<string?> GetFilePathAsync(int sceneId, string variant, CancellationToken ct = default)
    {
        if (!ServesVariant(variant)) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movieId = await db.Scenes.Where(s => s.Id == sceneId).Select(s => (int?)s.MovieId).FirstOrDefaultAsync(ct);
        if (movieId is null) return null;

        var window = (await ResolveWindowsAsync(db, movieId.Value, ct))[sceneId];
        var row = await db.CachedImages.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Role == Role && c.Index == sceneId && c.Variant == variant, ct);
        if (row is not null && IsCurrent(row, window))
        {
            return imageCache.GetStoragePath(row);
        }

        queue.Enqueue(sceneId, movieId.Value, window);
        return null;
    }

    public async Task EnsureQueuedAsync(int movieId, CancellationToken ct = default)
    {
        var variants = EnabledVariants;
        if (variants.Count == 0) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var windows = await ResolveWindowsAsync(db, movieId, ct);
        if (windows.Count == 0) return;

        var sceneIds = windows.Keys.ToList();
        var rows = await db.CachedImages.AsNoTracking()
            .Where(c => c.Role == Role && sceneIds.Contains(c.Index))
            .ToListAsync(ct);
        foreach (var (sceneId, window) in windows)
        {
            var current = rows.Where(r => r.Index == sceneId && IsCurrent(r, window)).Select(r => r.Variant).ToHashSet();
            if (!variants.All(current.Contains))
            {
                queue.Enqueue(sceneId, movieId, window);
            }
        }
    }

    public async Task<int?> GenerateAsync(int sceneId, CancellationToken ct = default)
    {
        var variants = EnabledVariants;
        if (variants.Count == 0) return null;

        Scene? scene;
        double? duration;
        List<CachedImage> rows;
        IReadOnlyList<ResolvedScene> resolved;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            scene = await db.Scenes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sceneId, ct);
            if (scene is null) return null;
            duration = await db.Movies.Where(m => m.Id == scene.MovieId).Select(m => m.MediaDurationSeconds).FirstOrDefaultAsync(ct);
            var siblings = await db.Scenes.AsNoTracking().Where(s => s.MovieId == scene.MovieId).ToListAsync(ct);
            resolved = SceneRanges.ResolveEffectiveRanges(siblings, duration);
            rows = await db.CachedImages.AsNoTracking().Where(c => c.Role == Role && c.Index == sceneId).ToListAsync(ct);
        }

        var resolvedEnd = resolved.FirstOrDefault(r => r.Scene.Id == sceneId)?.EffectiveEndSeconds;
        var window = Window(scene.StartSeconds, resolvedEnd);
        var outcome = await generator.GenerateAsync(Role, sceneId, scene.MovieId, scene.StartSeconds, resolvedEnd, window, rows, ct);
        if (outcome.FailedAny) queue.MarkFailed(sceneId, window);
        return outcome.WroteAny ? scene.MovieId : null;
    }

    /// <summary>Where to sample a scene: the preview runs for up to four seconds
    /// centered on the middle, inside the scene (all of a shorter one), and the still is its first
    /// frame so hovering swaps without a jump. With no known end there's no middle, so
    /// both start a few seconds in.</summary>
    public static (double StillAt, double PreviewAt, double PreviewSeconds) SampleWindow(double startSeconds, double? effectiveEndSeconds)
    {
        if (effectiveEndSeconds is not { } end || end <= startSeconds)
        {
            return (startSeconds + StillOffsetSeconds, startSeconds + StillOffsetSeconds, PreviewSeconds);
        }

        var middle = (startSeconds + end) / 2;
        var previewSeconds = Math.Min(PreviewSeconds, end - startSeconds);
        var previewAt = Math.Clamp(middle - previewSeconds / 2, startSeconds, end - previewSeconds);
        return (previewAt, previewAt, previewSeconds);
    }

    public void NoteScenesChanged(int movieId) => queue.NoteScenesChanged(movieId);

    public Task DeleteForSceneAsync(int sceneId, CancellationToken ct = default) => generator.DeleteAsync(Role, sceneId, ct);

    /// <summary>Each of the movie's scenes' current sample window, keyed by scene id.</summary>
    private static async Task<Dictionary<int, SceneMediaWindow>> ResolveWindowsAsync(AppDbContext db, int movieId, CancellationToken ct)
    {
        var duration = await db.Movies.Where(m => m.Id == movieId).Select(m => m.MediaDurationSeconds).FirstOrDefaultAsync(ct);
        var scenes = await db.Scenes.AsNoTracking().Where(s => s.MovieId == movieId).ToListAsync(ct);
        return SceneRanges.ResolveEffectiveRanges(scenes, duration)
            .ToDictionary(r => r.Scene.Id, r => Window(r.Scene.StartSeconds, r.EffectiveEndSeconds));
    }
}

/// <summary>The scene range, in milliseconds, that media was (or is to be) sampled for; EndMs is null
/// when the effective end is unknown.</summary>
public readonly record struct SceneMediaWindow(long StartMs, long? EndMs);
