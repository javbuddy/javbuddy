using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Tags;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>Keeps MovieTag.FromClips in step with the explicit tags of a movie's scenes, highlights and
/// apexes and its actor tags (Tag.IsActorTag, at any level): the movie-level end of the tag roll-up, stored so every MovieTag reader (the
/// Movies grid, MetaGenres, .nfo sync and drift, Actor Detail) sees it unchanged. A row only the clips
/// held goes when they stop carrying the tag; an explicit row just loses the flag.</summary>
public static class ClipTagSync
{
    /// <summary>Recomputes and saves the flags for one movie. Returns true when any MovieTag row was
    /// added, removed or changed a flag.</summary>
    public static async Task<bool> RefreshAsync(AppDbContext db, int movieId, CancellationToken ct)
    {
        // One refresh at a time in the app: the stored-actor worker's and an editor's would otherwise both find a link
        // missing and both add it, and the unique (MovieId, TagId) key refuses the second (and EF logs that as an error).
        await Gate.WaitAsync(ct);
        try
        {
            return await RefreshOnceAsync(db, movieId, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            // Still possible from outside this process; start over from what it saved. Only the MovieTag rows are
            // dropped from the tracker; any other pending change of the caller's stays and is saved with the retry.
            foreach (var entry in db.ChangeTracker.Entries<MovieTag>().ToList()) entry.State = EntityState.Detached;
            return await RefreshOnceAsync(db, movieId, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static async Task<bool> RefreshOnceAsync(AppDbContext db, int movieId, CancellationToken ct)
    {
        var wanted = (await db.SceneTags.Where(st => st.Scene.MovieId == movieId).Select(st => st.TagId)
                .Concat(db.HighlightTags.Where(ht => ht.Highlight.MovieId == movieId).Select(ht => ht.TagId))
                .Concat(db.ApexTags.Where(at => at.Apex.MovieId == movieId).Select(at => at.TagId))
                .Concat(db.MovieActorTags.Where(t => t.MovieId == movieId).Select(t => t.TagId))
                .Concat(db.SceneActorTags.Where(t => t.MovieId == movieId).Select(t => t.TagId))
                .Concat(db.HighlightActorTags.Where(t => t.MovieId == movieId).Select(t => t.TagId))
                .Concat(db.ApexActorTags.Where(t => t.MovieId == movieId).Select(t => t.TagId))
                .ToListAsync(ct))
            .ToHashSet();
        var links = await db.MovieTags.Where(mt => mt.MovieId == movieId).ToListAsync(ct);

        var changed = false;
        foreach (var link in links)
        {
            var fromClips = wanted.Contains(link.TagId);
            if (link.FromClips == fromClips) continue;

            changed = true;
            if (!fromClips && !link.IsExplicit)
            {
                db.MovieTags.Remove(link);
            }
            else
            {
                link.FromClips = fromClips;
            }
        }
        foreach (var tagId in wanted.Where(id => links.All(l => l.TagId != id)))
        {
            db.MovieTags.Add(new MovieTag { MovieId = movieId, TagId = tagId, IsExplicit = false, FromClips = true });
            changed = true;
        }

        await db.SaveChangesAsync(ct);
        return changed;
    }
}

public interface IClipTagSyncService
{
    /// <summary>Refreshes the movie's clip tag flags and, when anything changed, its MetaGenres and .nfo
    /// drift check. Returns whether anything changed.</summary>
    Task<bool> RefreshAsync(int movieId, CancellationToken ct = default);
}

/// <summary>Runs <see cref="ClipTagSync"/> after a scene, highlight or apex edit, plus the follow-ups any
/// MovieTag change needs (MetaGenres, .nfo drift) — in one place, so the clip services take one dependency.</summary>
public class ClipTagSyncService(IDbContextFactory<AppDbContext> dbFactory, INfoSyncService nfoSyncService) : IClipTagSyncService
{
    public async Task<bool> RefreshAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await ClipTagSync.RefreshAsync(db, movieId, ct)) return false;

        await TagNormalization.SyncMetaGenresAsync(db, [movieId], ct);
        await db.SaveChangesAsync(ct);
        await nfoSyncService.CheckMovieNfoConflictAsync(movieId, ct);
        return true;
    }
}
