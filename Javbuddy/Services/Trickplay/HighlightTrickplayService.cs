using Javbuddy.Data;
using Javbuddy.Services.SceneMedia;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Trickplay;

/// <summary>A highlight's own denser trickplay set, for the clip player: generated for
/// just the highlight's range, at HighlightTrickplay's interval, and stored next to the movie's set.
/// Its identity follows the highlight's range and the movie's main file, so editing the highlight
/// or replacing the file makes it stale; the backfill task deletes the old one. A new one is queued
/// with the highlight's screenshot and preview, and by the backfill task — never by playing the
/// highlight: ffmpeg decoding the file while the player streams it stalled 8K
/// playback. Generation runs on the SceneMediaQueue, with the highlights' screenshots and previews:
/// a clip is a short job, which mustn't wait behind a library-wide trickplay backfill.</summary>
public interface IHighlightTrickplayService
{
    /// <summary>The highlight's current set, or null when there's none (yet), or the highlight is long
    /// enough for the movie's own set. Queues nothing.</summary>
    Task<TrickplayLayout?> GetAsync(int highlightId, CancellationToken ct = default);

    /// <summary>Queues a set for each of the movie's highlights that needs one and has no current
    /// one; returns how many were newly queued.</summary>
    Task<int> EnsureQueuedAsync(int movieId, CancellationToken ct = default);

    /// <summary>Returns the movie id when a set was written, null otherwise.</summary>
    Task<int?> GenerateAsync(int highlightId, CancellationToken ct = default);
}

public sealed class HighlightTrickplayService(
    IDbContextFactory<AppDbContext> dbFactory,
    ITrickplayStore store,
    ITrickplayGenerator generator,
    SceneMediaQueue queue) : IHighlightTrickplayService
{
    public async Task<TrickplayLayout?> GetAsync(int highlightId, CancellationToken ct = default)
    {
        var target = await LoadAsync(highlightId, ct);
        if (target is null) return null;
        var (highlight, main) = target.Value;
        if (main.Identity is not { } fileIdentity
            || HighlightTrickplay.Identity(fileIdentity, highlight.StartSeconds, highlight.EndSeconds) is not { } identity)
        {
            return null;
        }

        return await store.GetSetAsync(main.Code, identity, ct) is { } set
            ? new TrickplayLayout(
                set.Width, set.Height, set.TileWidth, set.TileHeight, set.ThumbnailCount,
                set.IntervalMs, set.DurationSeconds, TrickplayService.TileUrlTemplate(main.MovieId, identity), set.StartSeconds)
            : null;
    }

    public async Task<int> EnsureQueuedAsync(int movieId, CancellationToken ct = default)
    {
        List<Models.MovieHighlight> highlights;
        TrickplayMainFile? main;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            highlights = await db.MovieHighlights.AsNoTracking().Where(h => h.MovieId == movieId).ToListAsync(ct);
            if (highlights.Count == 0) return 0;
            main = await TrickplayMainFiles.LoadAsync(db, movieId, ct);
        }
        if (main?.Identity is not { } fileIdentity) return 0;

        var queued = 0;
        foreach (var highlight in highlights)
        {
            if (HighlightTrickplay.Identity(fileIdentity, highlight.StartSeconds, highlight.EndSeconds) is not { } identity
                || await store.GetSetAsync(main.Code, identity, ct) is not null)
            {
                continue;
            }

            if (queue.EnqueueHighlightTrickplay(highlight.Id, movieId, HighlightMediaService.Window(highlight.StartSeconds, highlight.EndSeconds)))
            {
                queued++;
            }
        }
        return queued;
    }

    public async Task<int?> GenerateAsync(int highlightId, CancellationToken ct = default)
    {
        var target = await LoadAsync(highlightId, ct);
        if (target is null) return null;
        var (highlight, main) = target.Value;

        var result = await generator.GenerateClipAsync(main.MovieId, highlight.StartSeconds, highlight.EndSeconds, ct);
        if (result is TrickplayGenerateResult.Failed or TrickplayGenerateResult.NoFile or TrickplayGenerateResult.NotProbed)
        {
            queue.MarkHighlightTrickplayFailed(highlightId, HighlightMediaService.Window(highlight.StartSeconds, highlight.EndSeconds));
        }
        return result == TrickplayGenerateResult.Generated ? main.MovieId : null;
    }

    private async Task<(Models.MovieHighlight Highlight, TrickplayMainFile Main)?> LoadAsync(int highlightId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var highlight = await db.MovieHighlights.AsNoTracking().FirstOrDefaultAsync(h => h.Id == highlightId, ct);
        if (highlight is null) return null;
        var main = await TrickplayMainFiles.LoadAsync(db, highlight.MovieId, ct);
        return main is null ? null : (highlight, main);
    }
}
