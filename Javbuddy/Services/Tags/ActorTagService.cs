using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Tags;

/// <summary>Where an actor tag sits: on the actor within the movie, or within one scene, highlight or apex.</summary>
public enum ActorTagLevel { Movie, Scene, Highlight, Apex }

/// <summary>MovieTagsChanged: the change altered the movie's plain tags through its clips, so a host showing them
/// should re-read them.</summary>
public sealed record ActorTagResult(bool Success, string? ErrorMessage = null)
{
    public bool MovieTagsChanged { get; init; }

    public static ActorTagResult Ok(bool movieTagsChanged) => new(true) { MovieTagsChanged = movieTagsChanged };
    public static ActorTagResult Fail(string error) => new(false, error);
}

/// <summary>An actor tag in the library: Tag.IsActorTag set.</summary>
public sealed record ActorTagListItem(int Id, string Name, int UseCount, int? ParentTagId = null, string? ParentTagName = null, int SubtagCount = 0)
{
    public DateTime CreatedAt { get; init; }

    /// <summary>"Hair › Long" for a subtag, else the name.</summary>
    public string Label => ParentTagName is null ? Name : $"{ParentTagName}{ActorTagOptions.LabelSeparator}{Name}";

    /// <summary>As the tag library's item, for the shared tag components (TagSearchAdd, the create and parent modals).</summary>
    public TagListItem ToTagListItem() => new(Id, Name, UseCount, false, CreatedAt, ParentTagId, ParentTagName, SubtagCount);
}

public interface IActorTagService
{
    /// <summary>The actor tags in the library, ordered by parent then name; search matches part of the name or its parent's.</summary>
    Task<IReadOnlyList<ActorTagListItem>> GetActorTagsAsync(string? search = null, CancellationToken ct = default);

    /// <summary>Creates a new actor tag, optionally under a parent actor tag (one level of nesting, as plain tags have),
    /// or fails when a tag of that name exists.</summary>
    Task<TagOperationResult> CreateActorTagAsync(string name, int? parentTagId = null, CancellationToken ct = default);

    /// <summary>Makes a plain tag an actor tag, or an actor tag a plain one again; either way only outside any hierarchy.
    /// A plain tag becomes one while no scene, highlight or apex carries it, and leaves its movies' genres (metadata
    /// skips it from then on): a genre doesn't say which actor it describes. An actor tag goes back only while unused.</summary>
    Task<TagOperationResult> SetIsActorTagAsync(int tagId, bool isActorTag, CancellationToken ct = default);

    /// <summary>The movie's scenes', highlights' and apexes' actor tags: own, inherited and rolled up (<see cref="ClipActorTags"/>).</summary>
    Task<ClipActorTagsResult> GetEffectiveAsync(int movieId, CancellationToken ct = default);

    /// <summary>The actors' own tags at movie level, as <see cref="EffectiveActorTag"/>s with no source (nothing is inherited above the movie), plus the ones only its clips carry as rolled up.</summary>
    Task<IReadOnlyList<EffectiveActorTag>> GetMovieTagsAsync(int movieId, CancellationToken ct = default);

    /// <summary>Replaces the actor's own tags at one level (the owner is the movie id for <see cref="ActorTagLevel.Movie"/>,
    /// else the scene's, highlight's or apex's). An empty set means none of their own: they inherit. The actor must be
    /// in the movie's cast and every tag an actor tag.</summary>
    Task<ActorTagResult> SetAsync(ActorTagLevel level, int ownerId, int actorId, IReadOnlyCollection<int> tagIds, CancellationToken ct = default);
}

/// <summary>Actor-scoped tags (Tag.IsActorTag): "blonde" for one actor in a movie or clip. They flow down and roll up
/// per actor (ClipActorTags), and reach the movie as plain MovieTags through ClipTagSync.</summary>
public class ActorTagService(IDbContextFactory<AppDbContext> dbFactory, IClipTagSyncService? clipTags = null) : IActorTagService
{
    public async Task<IReadOnlyList<ActorTagListItem>> GetActorTagsAsync(string? search = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Tags.AsNoTracking().Where(t => t.IsActorTag);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var upper = search.Trim().ToUpper();
            query = query.Where(t => t.Name.ToUpper().Contains(upper) || (t.ParentTag != null && t.ParentTag.Name.ToUpper().Contains(upper)));
        }

        var tags = await query.Select(t => new
        {
            t.Id,
            t.Name,
            MovieCount = t.MovieTags.Count,
            t.ParentTagId,
            t.CreatedAt,
            ParentName = t.ParentTag != null ? t.ParentTag.Name : null,
            SubtagCount = t.Subtags.Count,
        }).ToListAsync(ct);
        return tags
            .Select(t => new ActorTagListItem(t.Id, t.Name, t.MovieCount, t.ParentTagId, t.ParentName, t.SubtagCount) { CreatedAt = t.CreatedAt })
            .OrderBy(t => t.ParentTagName ?? t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.ParentTagName is null ? 0 : 1)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<TagOperationResult> CreateActorTagAsync(string name, int? parentTagId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return TagOperationResult.Fail("Tag name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > 200) return TagOperationResult.Fail("Tag name cannot exceed 200 characters.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Tags.AnyAsync(t => t.Name.ToUpper() == trimmed.ToUpper(), ct))
        {
            return TagOperationResult.Fail($"A tag named \"{trimmed}\" already exists.");
        }

        if (parentTagId is { } parentId)
        {
            var parent = await db.Tags.FirstOrDefaultAsync(t => t.Id == parentId, ct);
            if (parent is null) return TagOperationResult.Fail("Parent tag not found.");
            if (!parent.IsActorTag) return TagOperationResult.Fail("An actor tag nests only under another actor tag.");
            if (parent.ParentTagId is not null) return TagOperationResult.Fail("Cannot nest under a subtag (maximum 2 levels supported).");
        }

        var tag = new Tag { Name = trimmed, ParentTagId = parentTagId, IsActorTag = true, NeedsReview = false, CreatedAt = DateTime.UtcNow };
        try
        {
            db.Tags.Add(tag);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return TagOperationResult.Fail($"A tag named \"{trimmed}\" already exists.");
        }
        return TagOperationResult.Ok(tag);
    }

    public async Task<TagOperationResult> SetIsActorTagAsync(int tagId, bool isActorTag, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tag = await db.Tags.Include(t => t.Subtags).FirstOrDefaultAsync(t => t.Id == tagId, ct);
        if (tag is null) return TagOperationResult.Fail("Tag not found.");
        if (tag.IsActorTag == isActorTag) return TagOperationResult.Ok(tag);

        if (tag.ParentTagId is not null || tag.Subtags.Count > 0)
        {
            return TagOperationResult.Fail($"\"{tag.Name}\" is part of a tag hierarchy, so it can't change kind. Remove its parent or subtags first.");
        }
        if (isActorTag && await IsOnClipsAsync(db, tagId, ct))
        {
            return TagOperationResult.Fail($"\"{tag.Name}\" is on scenes, highlights or apexes, so it can't become an actor tag. Remove it from them first.");
        }
        if (!isActorTag && await IsUsedAsync(db, tagId, ct))
        {
            return TagOperationResult.Fail($"\"{tag.Name}\" is in use, so it can't change kind. Remove it from everything first.");
        }

        List<int> movieIds = [];
        if (isActorTag)
        {
            var links = await db.MovieTags.Where(mt => mt.TagId == tagId).ToListAsync(ct);
            movieIds = [.. links.Select(mt => mt.MovieId)];
            db.MovieTags.RemoveRange(links);
        }
        tag.IsActorTag = isActorTag;
        if (isActorTag) tag.NeedsReview = false;
        await db.SaveChangesAsync(ct);
        if (movieIds.Count > 0)
        {
            await TagNormalization.SyncMetaGenresAsync(db, movieIds, ct);
            await db.SaveChangesAsync(ct);
        }
        return TagOperationResult.Ok(tag);
    }

    public async Task<ClipActorTagsResult> GetEffectiveAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await ClipAssignments.LoadEffectiveActorTagsAsync(db, movieId, ct);
    }

    public async Task<IReadOnlyList<EffectiveActorTag>> GetMovieTagsAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var own = (await db.MovieActorTags.AsNoTracking().Where(t => t.MovieId == movieId).Select(t => new { t.ActorId, t.TagId }).ToListAsync(ct))
            .Select(t => (t.ActorId, t.TagId)).ToHashSet();
        var fromClips = (await db.SceneActorTags.AsNoTracking().Where(t => t.MovieId == movieId).Select(t => new { t.ActorId, t.TagId }).ToListAsync(ct))
            .Concat(await db.HighlightActorTags.AsNoTracking().Where(t => t.MovieId == movieId).Select(t => new { t.ActorId, t.TagId }).ToListAsync(ct))
            .Concat(await db.ApexActorTags.AsNoTracking().Where(t => t.MovieId == movieId).Select(t => new { t.ActorId, t.TagId }).ToListAsync(ct))
            .Select(t => (t.ActorId, t.TagId))
            .ToHashSet();
        return own.OrderBy(t => t.ActorId).ThenBy(t => t.TagId).Select(t => new EffectiveActorTag(t.ActorId, t.TagId, false, null))
            .Concat(fromClips.Where(t => !own.Contains(t)).OrderBy(t => t.ActorId).ThenBy(t => t.TagId).Select(t => new EffectiveActorTag(t.ActorId, t.TagId, true, "its scenes, highlights and apexes")))
            .ToList();
    }

    public async Task<ActorTagResult> SetAsync(ActorTagLevel level, int ownerId, int actorId, IReadOnlyCollection<int> tagIds, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movieId = level switch
        {
            ActorTagLevel.Movie => await db.Movies.Where(m => m.Id == ownerId).Select(m => (int?)m.Id).FirstOrDefaultAsync(ct),
            ActorTagLevel.Scene => await db.Scenes.Where(s => s.Id == ownerId).Select(s => (int?)s.MovieId).FirstOrDefaultAsync(ct),
            ActorTagLevel.Highlight => await db.MovieHighlights.Where(h => h.Id == ownerId).Select(h => (int?)h.MovieId).FirstOrDefaultAsync(ct),
            _ => await db.MovieApexes.Where(a => a.Id == ownerId).Select(a => (int?)a.MovieId).FirstOrDefaultAsync(ct),
        };
        if (movieId is null) return ActorTagResult.Fail($"{level} not found.");
        if (await ClipAssignments.ValidateActorsAsync(db, movieId.Value, [actorId], ct) is { } actorError) return ActorTagResult.Fail(actorError);

        var wanted = tagIds.Distinct().ToList();
        if (await db.Tags.CountAsync(t => wanted.Contains(t.Id) && t.IsActorTag, ct) != wanted.Count)
        {
            return ActorTagResult.Fail("Tag not found.");
        }

        switch (level)
        {
            case ActorTagLevel.Movie:
                await Replace(db.MovieActorTags, db.MovieActorTags.Where(t => t.MovieId == ownerId && t.ActorId == actorId), wanted, tagId =>
                    new MovieActorTag { MovieId = ownerId, ActorId = actorId, TagId = tagId }, t => t.TagId, ct);
                break;
            case ActorTagLevel.Scene:
                await Replace(db.SceneActorTags, db.SceneActorTags.Where(t => t.SceneId == ownerId && t.ActorId == actorId), wanted, tagId =>
                    new SceneActorTag { SceneId = ownerId, MovieId = movieId.Value, ActorId = actorId, TagId = tagId }, t => t.TagId, ct);
                break;
            case ActorTagLevel.Highlight:
                await Replace(db.HighlightActorTags, db.HighlightActorTags.Where(t => t.HighlightId == ownerId && t.ActorId == actorId), wanted, tagId =>
                    new HighlightActorTag { HighlightId = ownerId, MovieId = movieId.Value, ActorId = actorId, TagId = tagId }, t => t.TagId, ct);
                break;
            default:
                await Replace(db.ApexActorTags, db.ApexActorTags.Where(t => t.ApexId == ownerId && t.ActorId == actorId), wanted, tagId =>
                    new ApexActorTag { ApexId = ownerId, MovieId = movieId.Value, ActorId = actorId, TagId = tagId }, t => t.TagId, ct);
                break;
        }
        await db.SaveChangesAsync(ct);

        // The tags (and so the stale mark) are saved; the movie's plain tags follow from them.
        var tagsChanged = clipTags is not null && await clipTags.RefreshAsync(movieId.Value, ct);
        return ActorTagResult.Ok(tagsChanged);
    }

    // Adds the missing rows and removes the ones no longer wanted, leaving the rest untouched.
    private static async Task Replace<TRow>(DbSet<TRow> set, IQueryable<TRow> current, List<int> wanted, Func<int, TRow> create, Func<TRow, int> tagId, CancellationToken ct)
        where TRow : class
    {
        var stored = await current.ToListAsync(ct);
        set.RemoveRange(stored.Where(row => !wanted.Contains(tagId(row))));
        var kept = stored.Select(tagId).ToHashSet();
        set.AddRange(wanted.Where(id => !kept.Contains(id)).Select(create));
    }

    private static async Task<bool> IsOnClipsAsync(AppDbContext db, int tagId, CancellationToken ct) =>
        await db.SceneTags.AnyAsync(t => t.TagId == tagId, ct)
        || await db.HighlightTags.AnyAsync(t => t.TagId == tagId, ct)
        || await db.ApexTags.AnyAsync(t => t.TagId == tagId, ct);

    private static async Task<bool> IsUsedAsync(AppDbContext db, int tagId, CancellationToken ct) =>
        await db.MovieTags.AnyAsync(t => t.TagId == tagId, ct)
        || await IsOnClipsAsync(db, tagId, ct)
        || await db.MovieActorTags.AnyAsync(t => t.TagId == tagId, ct)
        || await db.SceneActorTags.AnyAsync(t => t.TagId == tagId, ct)
        || await db.HighlightActorTags.AnyAsync(t => t.TagId == tagId, ct)
        || await db.ApexActorTags.AnyAsync(t => t.TagId == tagId, ct);
}
