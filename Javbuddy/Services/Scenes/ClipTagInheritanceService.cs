using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>The kind of clip a tag toggle or exclusion is on.</summary>
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
        if (!await db.Tags.AnyAsync(t => t.Id == tagId, ct))
        {
            return SceneOperationResult.Fail("Tag not found.");
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
