using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Common;
using Javbuddy.Services.SceneMedia;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>A movie's scene as shown in a scene list, with its null end already resolved.</summary>
public sealed record SceneItem(
    int Id,
    int Position,
    string DisplayTitle,
    string? Title,
    double StartSeconds,
    double? EndSeconds,
    double? EffectiveEndSeconds)
{
    /// <summary>Actors in this scene, ordered by name.</summary>
    public IReadOnlyList<SceneActorItem> Actors { get; init; } = [];

    /// <summary>Its actors as <see cref="ClipActors"/> sees them: its own, else the cast.</summary>
    public EffectiveActors EffectiveActors { get; init; } = EffectiveActors.None;

    /// <summary>Tags on this scene, ordered by name.</summary>
    public IReadOnlyList<SceneTagItem> Tags { get; init; } = [];

    /// <summary>Version (cache-buster) of the scene's generated screenshot, null when there is no
    /// current one. Served at <c>/scene-image/{Id}/thumb?v=…</c>.</summary>
    public long? ThumbVersion { get; init; }

    /// <summary>Like ThumbVersion, for the animated hover preview (<c>/scene-image/{Id}/preview</c>).</summary>
    public long? PreviewVersion { get; init; }

    /// <summary>Favorite scene.</summary>
    public bool IsFavorite { get; init; }

    /// <summary>Left out of the Scenes overview.</summary>
    public bool IsHiddenFromOverview { get; init; }
}

public sealed record SceneActorItem(int ActorId, string Name);

/// <summary>ParentName is set for a subtag.</summary>
public sealed record SceneTagItem(int TagId, string Name, string? ParentName);

public sealed record SceneOperationResult(bool Success, string? ErrorMessage = null, int? SceneId = null)
{
    public static SceneOperationResult Ok(int sceneId) => new(true, null, sceneId);
    public static SceneOperationResult Fail(string error) => new(false, error);
}

public interface IMovieSceneService
{
    /// <summary>The movie's scenes ordered by start time, each with its effective end.</summary>
    Task<IReadOnlyList<SceneItem>> GetScenesAsync(int movieId, CancellationToken ct = default);

    Task<SceneOperationResult> AddSceneAsync(int movieId, double startSeconds, double? endSeconds, string? title, CancellationToken ct = default);

    Task<SceneOperationResult> UpdateSceneAsync(int sceneId, double startSeconds, double? endSeconds, string? title, CancellationToken ct = default);

    Task<SceneOperationResult> DeleteSceneAsync(int sceneId, CancellationToken ct = default);

    /// <summary>The movie's cast — the only actors a scene can be given.</summary>
    Task<IReadOnlyList<SceneActorItem>> GetCastOptionsAsync(int movieId, CancellationToken ct = default);

    Task<SceneOperationResult> AddSceneActorAsync(int sceneId, int actorId, CancellationToken ct = default);

    Task<SceneOperationResult> RemoveSceneActorAsync(int sceneId, int actorId, CancellationToken ct = default);

    /// <summary>Replaces the scene's own actors; none makes it inherit the cast again.</summary>
    Task<SceneOperationResult> SetSceneActorsAsync(int sceneId, IReadOnlyCollection<int> actorIds, CancellationToken ct = default);

    Task<SceneOperationResult> AddSceneTagAsync(int sceneId, int tagId, CancellationToken ct = default);

    Task<SceneOperationResult> RemoveSceneTagAsync(int sceneId, int tagId, CancellationToken ct = default);

    /// <summary>Flips the scene's favorite flag; returns the new value (false when not found).</summary>
    Task<bool> ToggleFavoriteAsync(int sceneId, CancellationToken ct = default);

    /// <summary>Hides the scene from, or shows it again on, the Scenes overview.</summary>
    Task<SceneOperationResult> SetHiddenFromOverviewAsync(int sceneId, bool hidden, CancellationToken ct = default);
}

/// <summary>CRUD for per-movie scene ranges; validation rules live in SceneRanges.
/// sceneMedia, when present, removes a deleted scene's screenshot/preview and is told
/// about range edits so media generation waits for the layout to settle, and decides
/// which media variants the scene list advertises.
/// clipTags, when present, refreshes the movie's clip tags after a scene tag change or delete.
/// clipActors, when present, wakes the stored-actor refresh after a delete the tracker can't see.</summary>
public class MovieSceneService(IDbContextFactory<AppDbContext> dbFactory, ISceneMediaService? sceneMedia = null, IClipTagSyncService? clipTags = null, ClipActorRefreshSignal? clipActors = null) : IMovieSceneService
{
    private const int TitleMaxLength = 200;

    public async Task<IReadOnlyList<SceneItem>> GetScenesAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var duration = await db.Movies.Where(m => m.Id == movieId).Select(m => m.MediaDurationSeconds).FirstOrDefaultAsync(ct);
        var scenes = await db.Scenes.AsNoTracking()
            .Where(s => s.MovieId == movieId)
            .Include(s => s.SceneActors).ThenInclude(sa => sa.MovieActor).ThenInclude(ma => ma.Actor)
            .Include(s => s.SceneTags).ThenInclude(st => st.Tag).ThenInclude(t => t.ParentTag)
            .ToListAsync(ct);
        var sceneIds = scenes.Select(s => s.Id).ToList();
        var media = await db.CachedImages.AsNoTracking()
            .Where(c => c.Role == SceneMediaService.Role && sceneIds.Contains(c.Index))
            .ToListAsync(ct);

        // Only media /scene-image would serve counts: sampled for the scene's current range
        //, a variant the current cache mode serves, unexpired and still
        // on disk. Anything else would show as a broken image. Without a media service
        // (tests), whatever is cached for the range counts.
        long? MediaVersion(ResolvedScene resolved, string variant)
        {
            if (sceneMedia is not null && !sceneMedia.ServesVariant(variant)) return null;
            var window = SceneMediaService.Window(resolved.Scene.StartSeconds, resolved.EffectiveEndSeconds);
            var row = media.FirstOrDefault(m => m.Index == resolved.Scene.Id && m.Variant == variant);
            var current = row is not null && (sceneMedia?.IsCurrent(row, window) ?? (row.SceneStartMs == window.StartMs && row.SceneEndMs == window.EndMs));
            return current ? row!.UpdatedAt.Ticks : null;
        }

        var effective = await ClipAssignments.LoadEffectiveActorsAsync(db, movieId, ct);
        return SceneRanges.ResolveEffectiveRanges(scenes, duration)
            .Select(r => new SceneItem(r.Scene.Id, r.Position, r.DisplayTitle, r.Scene.Title, r.Scene.StartSeconds, r.Scene.EndSeconds, r.EffectiveEndSeconds)
            {
                Actors = r.Scene.SceneActors
                    .Select(sa => new SceneActorItem(sa.ActorId, sa.MovieActor.Actor.DisplayName))
                    .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                EffectiveActors = effective.Scenes.GetValueOrDefault(r.Scene.Id, EffectiveActors.None),
                Tags = r.Scene.SceneTags
                    .Select(st => new SceneTagItem(st.TagId, st.Tag.Name, st.Tag.ParentTag?.Name))
                    .OrderBy(t => t.ParentName ?? t.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                IsFavorite = r.Scene.IsFavorite,
                IsHiddenFromOverview = r.Scene.IsHiddenFromOverview,
                ThumbVersion = MediaVersion(r, SceneMediaService.VariantThumb),
                PreviewVersion = MediaVersion(r, SceneMediaService.VariantPreview)
            })
            .ToList();
    }

    public async Task<SceneOperationResult> AddSceneAsync(int movieId, double startSeconds, double? endSeconds, string? title, CancellationToken ct = default)
    {
        var normalizedTitle = title.TrimToNull();
        if (normalizedTitle?.Length > TitleMaxLength)
        {
            return SceneOperationResult.Fail($"Title cannot exceed {TitleMaxLength} characters.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.Where(m => m.Id == movieId).Select(m => new { m.MediaDurationSeconds }).FirstOrDefaultAsync(ct);
        if (movie is null)
        {
            return SceneOperationResult.Fail("Movie not found.");
        }

        var scene = new Scene
        {
            MovieId = movieId,
            StartSeconds = startSeconds,
            EndSeconds = endSeconds,
            Title = normalizedTitle,
            CreatedAt = DateTime.UtcNow
        };
        var others = await db.Scenes.AsNoTracking().Where(s => s.MovieId == movieId).ToListAsync(ct);
        var error = SceneRanges.Validate(scene, others, movie.MediaDurationSeconds);
        if (error is not null)
        {
            return SceneOperationResult.Fail(error);
        }

        db.Scenes.Add(scene);
        await db.SaveChangesAsync(ct);
        sceneMedia?.NoteScenesChanged(movieId);
        return SceneOperationResult.Ok(scene.Id);
    }

    public async Task<SceneOperationResult> UpdateSceneAsync(int sceneId, double startSeconds, double? endSeconds, string? title, CancellationToken ct = default)
    {
        var normalizedTitle = title.TrimToNull();
        if (normalizedTitle?.Length > TitleMaxLength)
        {
            return SceneOperationResult.Fail($"Title cannot exceed {TitleMaxLength} characters.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var scene = await db.Scenes.Include(s => s.Movie).FirstOrDefaultAsync(s => s.Id == sceneId, ct);
        if (scene is null)
        {
            return SceneOperationResult.Fail("Scene not found.");
        }

        var candidate = new Scene { Id = scene.Id, MovieId = scene.MovieId, StartSeconds = startSeconds, EndSeconds = endSeconds, Title = normalizedTitle };
        var others = await db.Scenes.AsNoTracking().Where(s => s.MovieId == scene.MovieId && s.Id != sceneId).ToListAsync(ct);
        var error = SceneRanges.Validate(candidate, others, scene.Movie.MediaDurationSeconds);
        if (error is not null)
        {
            return SceneOperationResult.Fail(error);
        }

        // A title-only edit doesn't move any range, so it doesn't hold back media generation.
        var rangeChanged = scene.StartSeconds != startSeconds || scene.EndSeconds != endSeconds;
        scene.StartSeconds = startSeconds;
        scene.EndSeconds = endSeconds;
        scene.Title = normalizedTitle;
        await db.SaveChangesAsync(ct);
        if (rangeChanged) sceneMedia?.NoteScenesChanged(scene.MovieId);
        return SceneOperationResult.Ok(scene.Id);
    }

    public async Task<SceneOperationResult> DeleteSceneAsync(int sceneId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movieId = await db.Scenes.Where(s => s.Id == sceneId).Select(s => (int?)s.MovieId).FirstOrDefaultAsync(ct);
        var deleted = await db.Scenes.Where(s => s.Id == sceneId).ExecuteDeleteAsync(ct);
        if (deleted == 0 || movieId is null)
        {
            return SceneOperationResult.Fail("Scene not found.");
        }
        // ExecuteDelete bypasses ClipActorStaleInterceptor: what its highlights and apexes inherit changes.
        await ClipActorStale.MarkAsync(db, movieId.Value, ct);
        clipActors?.Signal();
        if (sceneMedia is not null)
        {
            await sceneMedia.DeleteForSceneAsync(sceneId, ct);
            sceneMedia.NoteScenesChanged(movieId.Value);
        }
        // Its tag links cascaded away with it.
        if (clipTags is not null) await clipTags.RefreshAsync(movieId.Value, ct);
        return SceneOperationResult.Ok(sceneId);
    }

    public async Task<IReadOnlyList<SceneActorItem>> GetCastOptionsAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var actors = await db.MovieActors.AsNoTracking()
            .Where(ma => ma.MovieId == movieId)
            .Select(ma => ma.Actor)
            .ToListAsync(ct);
        return actors
            .Select(a => new SceneActorItem(a.Id, a.DisplayName))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SceneOperationResult> AddSceneActorAsync(int sceneId, int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movieId = await db.Scenes.Where(s => s.Id == sceneId).Select(s => (int?)s.MovieId).FirstOrDefaultAsync(ct);
        if (movieId is null)
        {
            return SceneOperationResult.Fail("Scene not found.");
        }
        if (!await db.MovieActors.AnyAsync(ma => ma.MovieId == movieId && ma.ActorId == actorId, ct))
        {
            return SceneOperationResult.Fail("Only actors in the movie's cast can be added to a scene.");
        }
        if (!await db.SceneActors.AnyAsync(sa => sa.SceneId == sceneId && sa.ActorId == actorId, ct))
        {
            db.SceneActors.Add(new SceneActor { SceneId = sceneId, MovieId = movieId.Value, ActorId = actorId });
            await db.SaveChangesAsync(ct);
        }
        return SceneOperationResult.Ok(sceneId);
    }

    public async Task<SceneOperationResult> RemoveSceneActorAsync(int sceneId, int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var link = await db.SceneActors.FindAsync([sceneId, actorId], ct);
        if (link is not null)
        {
            // A tracked delete, so ClipActorStaleInterceptor marks the movie and wakes the refresh.
            db.SceneActors.Remove(link);
            await db.SaveChangesAsync(ct);
        }
        return SceneOperationResult.Ok(sceneId);
    }

    public async Task<SceneOperationResult> SetSceneActorsAsync(int sceneId, IReadOnlyCollection<int> actorIds, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var scene = await db.Scenes.Include(s => s.SceneActors).FirstOrDefaultAsync(s => s.Id == sceneId, ct);
        if (scene is null)
        {
            return SceneOperationResult.Fail("Scene not found.");
        }
        var wanted = actorIds.ToHashSet();
        var castIds = await db.MovieActors.Where(ma => ma.MovieId == scene.MovieId && wanted.Contains(ma.ActorId)).Select(ma => ma.ActorId).ToListAsync(ct);
        if (castIds.Count != wanted.Count)
        {
            return SceneOperationResult.Fail("Only actors in the movie's cast can be added to a scene.");
        }
        foreach (var link in scene.SceneActors.Where(sa => !wanted.Contains(sa.ActorId)).ToList())
        {
            scene.SceneActors.Remove(link);
        }
        foreach (var actorId in wanted.Where(id => scene.SceneActors.All(sa => sa.ActorId != id)))
        {
            scene.SceneActors.Add(new SceneActor { SceneId = sceneId, MovieId = scene.MovieId, ActorId = actorId });
        }
        await db.SaveChangesAsync(ct);
        return SceneOperationResult.Ok(sceneId);
    }

    public async Task<SceneOperationResult> AddSceneTagAsync(int sceneId, int tagId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movieId = await db.Scenes.Where(s => s.Id == sceneId).Select(s => (int?)s.MovieId).FirstOrDefaultAsync(ct);
        if (movieId is null)
        {
            return SceneOperationResult.Fail("Scene not found.");
        }
        if (!await db.Tags.AnyAsync(t => t.Id == tagId && !t.IsActorTag, ct))
        {
            return SceneOperationResult.Fail("Tag not found.");
        }
        if (!await db.SceneTags.AnyAsync(st => st.SceneId == sceneId && st.TagId == tagId, ct))
        {
            db.SceneTags.Add(new SceneTag { SceneId = sceneId, TagId = tagId });
            await db.SaveChangesAsync(ct);
            // Rolls up to the movie, exact match: a subtag even when the movie has its parent.
            if (clipTags is not null) await clipTags.RefreshAsync(movieId.Value, ct);
        }
        return SceneOperationResult.Ok(sceneId);
    }

    public async Task<SceneOperationResult> RemoveSceneTagAsync(int sceneId, int tagId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movieId = await db.Scenes.Where(s => s.Id == sceneId).Select(s => (int?)s.MovieId).FirstOrDefaultAsync(ct);
        var removed = await db.SceneTags.Where(st => st.SceneId == sceneId && st.TagId == tagId).ExecuteDeleteAsync(ct);
        if (removed > 0 && movieId is not null && clipTags is not null) await clipTags.RefreshAsync(movieId.Value, ct);
        return SceneOperationResult.Ok(sceneId);
    }

    public async Task<bool> ToggleFavoriteAsync(int sceneId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var scene = await db.Scenes.FirstOrDefaultAsync(s => s.Id == sceneId, ct);
        if (scene is null) return false;

        scene.IsFavorite = !scene.IsFavorite;
        scene.FavoritedAt = scene.IsFavorite ? DateTime.UtcNow : null;
        await db.SaveChangesAsync(ct);
        return scene.IsFavorite;
    }

    public async Task<SceneOperationResult> SetHiddenFromOverviewAsync(int sceneId, bool hidden, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var updated = await db.Scenes.Where(s => s.Id == sceneId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsHiddenFromOverview, hidden), ct);
        return updated == 0 ? SceneOperationResult.Fail("Scene not found.") : SceneOperationResult.Ok(sceneId);
    }
}
