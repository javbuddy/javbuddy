using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Trickplay;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.SceneMedia;

public interface IHighlightMediaService
{
    /// <summary>Path of a highlight's current screenshot ("thumb") or hover preview ("preview"), or
    /// null when there's none yet — in which case it's queued for generation.</summary>
    Task<string?> GetFilePathAsync(int highlightId, string variant, CancellationToken ct = default);

    bool ServesVariant(string variant);

    /// <summary>Like ISceneMediaService.IsCurrent, for a highlight's cache row.</summary>
    bool IsCurrent(CachedImage row, SceneMediaWindow window);

    /// <summary>Queues every highlight of the movie that lacks current media, or its own trickplay set.</summary>
    Task EnsureQueuedAsync(int movieId, CancellationToken ct = default);

    /// <summary>Returns the movie id when anything was written, null otherwise.</summary>
    Task<int?> GenerateAsync(int highlightId, CancellationToken ct = default);

    Task DeleteForHighlightAsync(int highlightId, CancellationToken ct = default);

    /// <summary>Records that the movie's highlights were just edited, so media generation for the
    /// movie waits until edits settle (the same per-movie delay as scene edits).</summary>
    void NoteHighlightsChanged(int movieId);
}

/// <summary>Screenshots and hover previews for highlights, the highlight twin of
/// SceneMediaService: CachedImage rows with Role "highlight" and Index = MovieHighlight.Id, sampled
/// for the highlight's own start and end — so only editing the highlight's times makes them stale.
/// Generation shares ClipMediaGenerator and the single SceneMediaQueue worker. highlightTrickplay,
/// when present, has the highlights' own trickplay sets queued along with their media,
/// so playing a highlight never has to.</summary>
public class HighlightMediaService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    IFfmpegClient ffmpegClient,
    ILocalImageCache imageCache,
    IImageCacheMaintenanceService cacheMaintenance,
    SceneMediaQueue queue,
    IHighlightTrickplayService? highlightTrickplay = null) : IHighlightMediaService
{
    public const string Role = "highlight";

    private readonly ClipMediaGenerator generator = new(dbFactory, localLibraryClient, ffmpegClient, imageCache, cacheMaintenance);

    public static SceneMediaWindow Window(double startSeconds, double endSeconds) =>
        new(SceneMediaService.StartMs(startSeconds), SceneMediaService.StartMs(endSeconds));

    public bool ServesVariant(string variant) => generator.EnabledVariants.Contains(variant);

    public bool IsCurrent(CachedImage row, SceneMediaWindow window) => generator.IsCurrent(row, window);

    public async Task<string?> GetFilePathAsync(int highlightId, string variant, CancellationToken ct = default)
    {
        if (!ServesVariant(variant)) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var highlight = await db.MovieHighlights.AsNoTracking().FirstOrDefaultAsync(h => h.Id == highlightId, ct);
        if (highlight is null) return null;

        var window = Window(highlight.StartSeconds, highlight.EndSeconds);
        var row = await db.CachedImages.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Role == Role && c.Index == highlightId && c.Variant == variant, ct);
        if (row is not null && generator.IsCurrent(row, window))
        {
            return imageCache.GetStoragePath(row);
        }

        queue.EnqueueHighlight(highlightId, highlight.MovieId, window);
        if (highlightTrickplay is not null) await highlightTrickplay.EnsureQueuedAsync(highlight.MovieId, ct);
        return null;
    }

    public async Task EnsureQueuedAsync(int movieId, CancellationToken ct = default)
    {
        // Not image cache, so not limited by its mode.
        if (highlightTrickplay is not null) await highlightTrickplay.EnsureQueuedAsync(movieId, ct);

        var variants = generator.EnabledVariants;
        if (variants.Count == 0) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var highlights = await db.MovieHighlights.AsNoTracking().Where(h => h.MovieId == movieId).ToListAsync(ct);
        if (highlights.Count == 0) return;
        var ids = highlights.Select(h => h.Id).ToList();
        var rows = await db.CachedImages.AsNoTracking().Where(c => c.Role == Role && ids.Contains(c.Index)).ToListAsync(ct);
        foreach (var highlight in highlights)
        {
            var window = Window(highlight.StartSeconds, highlight.EndSeconds);
            var current = rows.Where(r => r.Index == highlight.Id && generator.IsCurrent(r, window)).Select(r => r.Variant).ToHashSet();
            if (!variants.All(current.Contains))
            {
                queue.EnqueueHighlight(highlight.Id, movieId, window);
            }
        }
    }

    public async Task<int?> GenerateAsync(int highlightId, CancellationToken ct = default)
    {
        MovieHighlight? highlight;
        List<CachedImage> rows;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            highlight = await db.MovieHighlights.AsNoTracking().FirstOrDefaultAsync(h => h.Id == highlightId, ct);
            if (highlight is null) return null;
            rows = await db.CachedImages.AsNoTracking().Where(c => c.Role == Role && c.Index == highlightId).ToListAsync(ct);
        }

        var window = Window(highlight.StartSeconds, highlight.EndSeconds);
        var outcome = await generator.GenerateAsync(Role, highlightId, highlight.MovieId, highlight.StartSeconds, highlight.EndSeconds, window, rows, ct);
        if (outcome.FailedAny) queue.MarkHighlightFailed(highlightId, window);
        return outcome.WroteAny ? highlight.MovieId : null;
    }

    public Task DeleteForHighlightAsync(int highlightId, CancellationToken ct = default) => generator.DeleteAsync(Role, highlightId, ct);

    public void NoteHighlightsChanged(int movieId) => queue.NoteScenesChanged(movieId);
}
