using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>A kind of clip: what a tag toggle or exclusion is on, or what a clip export cuts.</summary>
public enum ClipKind { Scene, Highlight, Apex }

/// <summary>Edits what tags pass down to clips (<see cref="ClipTagInheritance"/>): a scene's own tag's "pass down"
/// toggle and a clip's exclusions. The movie's own toggle is on ITagService, a highlight's saves with its form.
/// Every write is tracked, so ClipActorStaleInterceptor marks the movie and the worker refreshes the stored rows.</summary>
public interface IClipTagInheritanceService
{
    /// <summary>The movie's own tags that pass down to its clips.</summary>
    Task<IReadOnlyList<SceneTagItem>> GetPropagatingMovieTagsAsync(int movieId, CancellationToken ct = default);

    /// <summary>Turns on or off whether the scene's own tag passes down to its highlights and apexes.</summary>
    Task<SceneOperationResult> SetSceneTagPropagatesDownAsync(int sceneId, int tagId, bool propagatesDown, CancellationToken ct = default);

    /// <summary>Opts the clip out of (or back into) inheriting the tag.</summary>
    Task<SceneOperationResult> SetExcludedAsync(ClipKind kind, int clipId, int tagId, bool excluded, CancellationToken ct = default);
}

public class ClipTagInheritanceService(IDbContextFactory<AppDbContext> dbFactory) : IClipTagInheritanceService
{
    public async Task<IReadOnlyList<SceneTagItem>> GetPropagatingMovieTagsAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await ClipAssignments.LoadPropagatingMovieTagsAsync(db, movieId, ct);
    }

    public async Task<SceneOperationResult> SetSceneTagPropagatesDownAsync(int sceneId, int tagId, bool propagatesDown, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var link = await db.SceneTags.FirstOrDefaultAsync(st => st.SceneId == sceneId && st.TagId == tagId, ct);
        if (link is null)
        {
            return SceneOperationResult.Fail("Tag is not on this scene.");
        }
        if (link.PropagatesDown != propagatesDown)
        {
            link.PropagatesDown = propagatesDown;
            await db.SaveChangesAsync(ct);
        }
        return SceneOperationResult.Ok(sceneId);
    }

    public async Task<SceneOperationResult> SetExcludedAsync(ClipKind kind, int clipId, int tagId, bool excluded, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movieId = kind switch
        {
            ClipKind.Scene => await db.Scenes.Where(s => s.Id == clipId).Select(s => (int?)s.MovieId).FirstOrDefaultAsync(ct),
            ClipKind.Highlight => await db.MovieHighlights.Where(h => h.Id == clipId).Select(h => (int?)h.MovieId).FirstOrDefaultAsync(ct),
            _ => await db.MovieApexes.Where(a => a.Id == clipId).Select(a => (int?)a.MovieId).FirstOrDefaultAsync(ct),
        };
        if (movieId is null)
        {
            return SceneOperationResult.Fail("Clip not found.");
        }
        var isActorTag = await db.Tags.Where(t => t.Id == tagId).Select(t => (bool?)t.IsActorTag).FirstOrDefaultAsync(ct);
        if (isActorTag is null)
        {
            return SceneOperationResult.Fail("Tag not found.");
        }
        if (isActorTag.Value)
        {
            return SceneOperationResult.Fail("Actor tags aren't passed down, so there's nothing to opt out of.");
        }
        // Only opt out of a tag the clip inherits now: a stray row would lie dormant and silently block the tag later,
        // once something starts passing it down. Opting back in is always allowed, so a stray row can still be cleared.
        if (excluded)
        {
            var inherited = await ClipAssignments.LoadInheritedTagsAsync(db, movieId.Value, ct);
            var pools = kind switch
            {
                ClipKind.Scene => inherited.Scenes,
                ClipKind.Highlight => inherited.Highlights,
                _ => inherited.Apexes,
            };
            if (!pools.TryGetValue(clipId, out var pool) || !pool.Any(t => t.Tag.TagId == tagId))
            {
                return SceneOperationResult.Fail("Nothing passes this tag down to the clip.");
            }
        }

        switch (kind)
        {
            case ClipKind.Scene:
                Toggle(db.SceneTagExclusions, await db.SceneTagExclusions.FindAsync([clipId, tagId], ct), excluded,
                    () => new SceneTagExclusion { SceneId = clipId, MovieId = movieId.Value, TagId = tagId });
                break;
            case ClipKind.Highlight:
                Toggle(db.HighlightTagExclusions, await db.HighlightTagExclusions.FindAsync([clipId, tagId], ct), excluded,
                    () => new HighlightTagExclusion { HighlightId = clipId, MovieId = movieId.Value, TagId = tagId });
                break;
            default:
                Toggle(db.ApexTagExclusions, await db.ApexTagExclusions.FindAsync([clipId, tagId], ct), excluded,
                    () => new ApexTagExclusion { ApexId = clipId, MovieId = movieId.Value, TagId = tagId });
                break;
        }
        await db.SaveChangesAsync(ct);
        return SceneOperationResult.Ok(clipId);
    }

    private static void Toggle<TRow>(DbSet<TRow> rows, TRow? existing, bool excluded, Func<TRow> create) where TRow : class
    {
        if (excluded && existing is null) rows.Add(create());
        else if (!excluded && existing is not null) rows.Remove(existing);
    }
}
