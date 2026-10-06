using Javbuddy.Data;
using Javbuddy.Services.Trickplay;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Tasks;

/// <summary>Queues trickplay generation for every movie whose main video file has no
/// trickplay yet, then deletes stored trickplay nothing uses any more. It only queues: generation
/// runs in TrickplayQueue, one movie at a time, and a full library can take days — the scheduler
/// runs tasks one after another, so waiting for it here would hold up every other task.
///
/// Cleanup deletes a set when no movie has its code any more, or when its identity matches none of
/// the movie's current files while the movie still has a local file (it was replaced). A movie
/// whose file is currently unreachable keeps its set: regenerating it is expensive. Sheets with no
/// saved set (an interrupted generation) are deleted once a day old. A highlight's own set
/// is deleted once no highlight of the movie matches it with any of the movie's
/// files: the highlight was moved or deleted, or the file replaced. It's cheap to regenerate, so an
/// unreachable file doesn't keep it. A highlight with none is queued for one, on the
/// SceneMediaQueue like the one its media queues.</summary>
public sealed class TrickplayBackfillTask(
    IDbContextFactory<AppDbContext> dbFactory,
    ITrickplayStore store,
    TrickplayQueue queue,
    IHighlightTrickplayService highlightTrickplay) : IScheduledTask
{
    private static readonly TimeSpan OrphanMaxAge = TimeSpan.FromDays(1);

    public string Name => "Trickplay Backfill";

    public string Description =>
        "Queues trickplay (scrub-bar preview thumbnails) generation for every movie whose video file has none yet, and for highlights without their own; " +
        "it then runs in the background one movie at a time, shown in the sidebar. Also deletes trickplay no movie's files use any more.";

    public TimeSpan GetInterval() => TimeSpan.FromHours(24);

    public async Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress)
    {
        List<TrickplayMainFile> mains;
        HashSet<int> withHighlights;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            mains = await TrickplayMainFiles.LoadAllWithLocalVideoAsync(db, ct);
            withHighlights = (await db.MovieHighlights.Select(h => h.MovieId).Distinct().ToListAsync(ct)).ToHashSet();
        }

        var saved = (await store.ListSetsAsync(ct)).ToHashSet();

        int queued = 0, current = 0, notProbed = 0, failedBefore = 0, highlightsQueued = 0, checkedCount = 0;
        foreach (var main in mains)
        {
            ct.ThrowIfCancellationRequested();
            if (main.Identity is not { } identity)
            {
                notProbed++;
            }
            else if (saved.Contains(new TrickplaySetLocation(TrickplayStore.CodeFolder(main.Code), identity)))
            {
                current++;
            }
            else if (queue.HasFailed(main.MovieId, identity))
            {
                failedBefore++;
            }
            else if (queue.Enqueue(main.MovieId, main.Code, identity, main.DurationSeconds))
            {
                queued++;
            }
            if (main.Identity is not null && withHighlights.Contains(main.MovieId))
            {
                highlightsQueued += await highlightTrickplay.EnsureQueuedAsync(main.MovieId, ct);
            }
            progress.Report(new TaskProgress(++checkedCount, mains.Count, "Checking movies"));
        }

        var deleted = await DeleteUnusedSetsAsync(ct);
        await store.DeleteOrphanedSheetsAsync(OrphanMaxAge, ct);

        var summary = $"queued {Movies(queued)}, {current} already had trickplay";
        if (notProbed > 0) summary += $", {notProbed} not probed yet";
        if (failedBefore > 0) summary += $", {failedBefore} skipped after failing earlier";
        if (highlightsQueued > 0) summary += $", queued {(highlightsQueued == 1 ? "1 highlight" : $"{highlightsQueued} highlights")}";
        if (deleted > 0) summary += $", deleted {deleted} unused trickplay {(deleted == 1 ? "set" : "sets")}";
        return summary;
    }

    private static string Movies(int count) => count == 1 ? "1 movie" : $"{count} movies";

    private async Task<int> DeleteUnusedSetsAsync(CancellationToken ct)
    {
        var sets = await store.ListSetsAsync(ct);
        if (sets.Count == 0) return 0;

        List<(string Code, bool HasLocalVideo, List<string?> Identities, HashSet<string> ClipIdentities)> movies;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var rows = await db.Movies.AsNoTracking()
                .Where(m => m.Code != null && m.Code != "")
                .Select(m => new
                {
                    m.Id,
                    m.Code,
                    HasLocalVideo = m.LocalFileSizeBytes != null,
                    Files = m.MovieFiles.Select(f => new { f.FileName, f.DurationSeconds, f.Width, f.Height }).ToList(),
                })
                .ToListAsync(ct);
            var highlights = (await db.MovieHighlights.AsNoTracking()
                    .Select(h => new { h.MovieId, h.StartSeconds, h.EndSeconds })
                    .ToListAsync(ct))
                .ToLookup(h => h.MovieId);
            movies = rows.Select(m =>
            {
                var identities = m.Files.Select(f => TrickplayIdentity.For(f.FileName, f.DurationSeconds, f.Width, f.Height)).ToList();
                var clipIdentities = identities.OfType<string>()
                    .SelectMany(file => highlights[m.Id].Select(h => HighlightTrickplay.Identity(file, h.StartSeconds, h.EndSeconds)))
                    .OfType<string>()
                    .ToHashSet(StringComparer.Ordinal);
                return (m.Code!, m.HasLocalVideo, identities, clipIdentities);
            }).ToList();
        }
        var byFolder = movies.ToLookup(m => TrickplayStore.CodeFolder(m.Code), StringComparer.Ordinal);

        var deleted = 0;
        foreach (var set in sets)
        {
            var owners = byFolder[set.CodeFolder].ToList();
            var unused = set.IsClip
                ? !owners.Any(m => m.ClipIdentities.Contains(set.Identity))
                : owners.Count == 0
                    || (!owners.Any(m => m.Identities.Contains(set.Identity)) && owners.Any(m => m.HasLocalVideo));
            if (!unused) continue;

            await store.DeleteSetAsync(set, ct);
            deleted++;
        }
        return deleted;
    }
}
