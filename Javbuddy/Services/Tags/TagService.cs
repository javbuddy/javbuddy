using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Nfo;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Tags;

public enum TagSortOrder
{
    NameAsc,
    UsageDesc,
    UsageAsc,
    DateAddedDesc
}

public record TagOperationResult(bool Success, string? ErrorMessage = null, Tag? Tag = null)
{
    public static TagOperationResult Ok(Tag tag) => new(true, null, tag);
    public static TagOperationResult Fail(string error) => new(false, error, null);
}

public record OperationResult(bool Success, string? ErrorMessage = null)
{
    public static OperationResult Ok() => new(true);
    public static OperationResult Fail(string error) => new(false, error);
}

public record TagListItem(
    int Id,
    string Name,
    int MovieCount,
    bool NeedsReview,
    DateTime CreatedAt,
    int? ParentTagId = null,
    string? ParentTagName = null,
    int SubtagCount = 0)
{
    /// <summary>Scenes carrying this tag directly. Shown next to MovieCount but kept
    /// out of it, so usage sorting stays movie-based.</summary>
    public int SceneCount { get; init; }
}

public record TagTreeNode(
    int Id,
    string Name,
    int DirectMovieCount,
    int TotalMovieCount,
    bool NeedsReview,
    DateTime CreatedAt,
    IReadOnlyList<TagListItem> Subtags);

public record TagMergeCandidate(int Id, string Name, int MovieCount, bool IsSuggested, int? ParentTagId = null, string? ParentTagName = null);

public record TagReplacementRuleItem(int Id, string SourceValue, TagMatchMode MatchMode, int TargetTagId, string TargetTagName, DateTime CreatedAt);

public record IgnoredTagItem(int Id, string Value, TagMatchMode MatchMode, DateTime CreatedAt);

public interface ITagService
{
    Task<IReadOnlyList<TagListItem>> GetTagsAsync(string? search = null, TagSortOrder sort = TagSortOrder.NameAsc, CancellationToken ct = default);
    Task<IReadOnlyList<TagTreeNode>> GetTagTreeAsync(string? search = null, CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetTagSubtreeIdsAsync(int tagId, CancellationToken ct = default);
    Task<Tag?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<TagOperationResult> CreateTagAsync(string name, CancellationToken ct = default);
    Task<TagOperationResult> CreateTagAsync(string name, int? parentTagId, CancellationToken ct = default);
    Task<TagOperationResult> SetParentAsync(int tagId, int? parentTagId, CancellationToken ct = default);
    Task<TagOperationResult> RenameAsync(int tagId, string newName, CancellationToken ct = default);
    Task<IReadOnlyList<TagMergeCandidate>> GetMergeCandidatesAsync(int sourceTagId, string? search = null, CancellationToken ct = default);
    Task<TagOperationResult> MergeAsync(int sourceTagId, int targetTagId, bool createReplacementRule = false, CancellationToken ct = default);
    Task<TagOperationResult> DeleteAsync(int tagId, CancellationToken ct = default);
    Task<TagOperationResult> IgnoreAsync(int tagId, CancellationToken ct = default);
    Task<TagOperationResult> ApproveAsync(int tagId, CancellationToken ct = default);
    Task<int> ApproveManyAsync(IReadOnlyList<int> tagIds, CancellationToken ct = default);
    Task<int> IgnoreManyAsync(IReadOnlyList<int> tagIds, CancellationToken ct = default);
    Task<TagOperationResult> MergeManyAsync(IReadOnlyList<int> sourceTagIds, int targetTagId, bool createReplacementRule = false, CancellationToken ct = default);
    Task<TagOperationResult> AddTagToMovieAsync(int movieId, int tagId, CancellationToken ct = default);
    Task<OperationResult> RemoveTagFromMovieAsync(int movieId, int tagId, CancellationToken ct = default);
}

public class TagService(IDbContextFactory<AppDbContext> dbFactory, INfoSyncService nfoSyncService, INfoDriftCheckQueue? driftChecks = null) : ITagService
{
    private const string ActorTagsCannotMerge = "Actor tags can't be merged.";

    public async Task<IReadOnlyList<TagListItem>> GetTagsAsync(string? search = null, TagSortOrder sort = TagSortOrder.NameAsc, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = db.Tags.AsNoTracking().Where(t => !t.IsActorTag).Include(t => t.ParentTag).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var upper = search.Trim().ToUpper();
            query = query.Where(t => t.Name.ToUpper().Contains(upper) || (t.ParentTag != null && t.ParentTag.Name.ToUpper().Contains(upper)));
        }

        var projected = await query
            .Select(t => new TagListItem(
                t.Id,
                t.Name,
                t.MovieTags.Count,
                t.NeedsReview,
                t.CreatedAt,
                t.ParentTagId,
                t.ParentTag != null ? t.ParentTag.Name : null,
                t.Subtags.Count))
            .ToListAsync(ct);

        var sceneCounts = await db.SceneTags
            .GroupBy(st => st.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TagId, x => x.Count, ct);
        if (sceneCounts.Count > 0)
        {
            projected = projected.Select(t => t with { SceneCount = sceneCounts.GetValueOrDefault(t.Id) }).ToList();
        }

        return sort switch
        {
            TagSortOrder.UsageDesc => projected.OrderByDescending(t => t.MovieCount).ThenBy(t => t.Name).ToList(),
            TagSortOrder.UsageAsc => projected.OrderBy(t => t.MovieCount).ThenBy(t => t.Name).ToList(),
            TagSortOrder.DateAddedDesc => projected.OrderByDescending(t => t.CreatedAt).ToList(),
            _ => projected.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    public async Task<IReadOnlyList<TagTreeNode>> GetTagTreeAsync(string? search = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var allTags = await db.Tags.AsNoTracking()
            .Where(t => !t.IsActorTag)
            .Select(t => new { t.Id, t.Name, t.ParentTagId, t.NeedsReview, t.CreatedAt, MovieCount = t.MovieTags.Count })
            .ToListAsync(ct);

        // A root's distinct movies across itself and its subtags, counted by the database rather than
        // from every MovieTag row. Tags nest at most two levels, so a link's root is its tag's parent or the tag.
        var uniqueMoviesByRoot = await db.MovieTags
            .GroupBy(mt => mt.Tag.ParentTagId ?? mt.TagId)
            .Select(g => new { RootId = g.Key, Count = g.Select(mt => mt.MovieId).Distinct().Count() })
            .ToDictionaryAsync(x => x.RootId, x => x.Count, ct);

        var rootTags = allTags.Where(t => t.ParentTagId == null).ToList();
        var subtagsByParent = allTags.Where(t => t.ParentTagId != null)
            .GroupBy(t => t.ParentTagId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var nodes = new List<TagTreeNode>();

        foreach (var root in rootTags)
        {
            var childItems = subtagsByParent.GetValueOrDefault(root.Id, [])
                .Select(c => new TagListItem(c.Id, c.Name, c.MovieCount, c.NeedsReview, c.CreatedAt, c.ParentTagId, root.Name, 0))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            nodes.Add(new TagTreeNode(
                root.Id,
                root.Name,
                root.MovieCount,
                uniqueMoviesByRoot.GetValueOrDefault(root.Id),
                root.NeedsReview,
                root.CreatedAt,
                childItems));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var upper = search.Trim().ToUpper();
            nodes = nodes
                .Where(n => n.Name.ToUpper().Contains(upper) || n.Subtags.Any(s => s.Name.ToUpper().Contains(upper)))
                .Select(n =>
                {
                    if (n.Name.ToUpper().Contains(upper)) return n;
                    return n with { Subtags = n.Subtags.Where(s => s.Name.ToUpper().Contains(upper)).ToList() };
                })
                .ToList();
        }

        return nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<int>> GetTagSubtreeIdsAsync(int tagId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var subtagIds = await db.Tags.AsNoTracking()
            .Where(t => t.ParentTagId == tagId)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var result = new List<int> { tagId };
        result.AddRange(subtagIds);
        return result;
    }

    public async Task<Tag?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Tags.AsNoTracking().Include(t => t.ParentTag).FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public Task<TagOperationResult> CreateTagAsync(string name, CancellationToken ct = default) =>
        CreateTagAsync(name, parentTagId: null, ct);

    public async Task<TagOperationResult> CreateTagAsync(string name, int? parentTagId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return TagOperationResult.Fail("Tag name is required.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length > 200)
        {
            return TagOperationResult.Fail("Tag name cannot exceed 200 characters.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var duplicate = await db.Tags.AnyAsync(t => t.Name.ToUpper() == trimmed.ToUpper(), ct);
        if (duplicate)
        {
            return TagOperationResult.Fail($"A tag named \"{trimmed}\" already exists.");
        }

        if (parentTagId.HasValue)
        {
            var parent = await db.Tags.FirstOrDefaultAsync(t => t.Id == parentTagId.Value, ct);
            if (parent is null)
            {
                return TagOperationResult.Fail("Parent tag not found.");
            }
            if (parent.NeedsReview)
            {
                return TagOperationResult.Fail("Cannot assign an unapproved tag as a parent category. Approve the tag first.");
            }
            if (parent.IsActorTag)
            {
                return TagOperationResult.Fail("Create it as an actor tag to nest it under one.");
            }
            if (parent.ParentTagId != null)
            {
                return TagOperationResult.Fail("Cannot nest under a subtag (maximum 2 levels supported).");
            }
        }

        var tag = new Tag
        {
            Name = trimmed,
            ParentTagId = parentTagId,
            NeedsReview = false,
            CreatedAt = DateTime.UtcNow
        };

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

    public async Task<TagOperationResult> SetParentAsync(int tagId, int? parentTagId, CancellationToken ct = default)
    {
        if (parentTagId == tagId)
        {
            return TagOperationResult.Fail("A tag cannot be its own parent.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var tag = await db.Tags.Include(t => t.Subtags).FirstOrDefaultAsync(t => t.Id == tagId, ct);
        if (tag is null)
        {
            return TagOperationResult.Fail("Tag not found.");
        }

        if (parentTagId.HasValue)
        {
            if (tag.Subtags.Count > 0)
            {
                return TagOperationResult.Fail("A category that has subtags cannot become a subtag (maximum 2 levels supported).");
            }

            var parent = await db.Tags.FirstOrDefaultAsync(t => t.Id == parentTagId.Value, ct);
            if (parent is null)
            {
                return TagOperationResult.Fail("Parent tag not found.");
            }

            if (parent.NeedsReview)
            {
                return TagOperationResult.Fail("Cannot assign an unapproved tag as a parent category. Approve the tag first.");
            }

            if (parent.ParentTagId != null)
            {
                return TagOperationResult.Fail("Cannot nest under a subtag (maximum 2 levels supported).");
            }

            if (tag.IsActorTag != parent.IsActorTag)
            {
                return TagOperationResult.Fail("Actor tags only nest under actor tags, and plain tags under plain tags.");
            }

            tag.ParentTagId = parent.Id;
        }
        else
        {
            tag.ParentTagId = null;
        }

        await db.SaveChangesAsync(ct);

        var movieIds = await db.MovieTags.Where(mt => mt.TagId == tagId).Select(mt => mt.MovieId).ToListAsync(ct);
        await TagNormalization.SyncMetaGenresAsync(db, movieIds, ct);
        await db.SaveChangesAsync(ct);

        driftChecks?.Enqueue(movieIds);

        return TagOperationResult.Ok(tag);
    }

    public async Task<TagOperationResult> RenameAsync(int tagId, string newName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return TagOperationResult.Fail("Tag name is required.");
        }

        var trimmed = newName.Trim();
        if (trimmed.Length > 200)
        {
            return TagOperationResult.Fail("Tag name cannot exceed 200 characters.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var duplicate = await db.Tags.AnyAsync(t => t.Id != tagId && t.Name.ToUpper() == trimmed.ToUpper(), ct);
        if (duplicate)
        {
            return TagOperationResult.Fail($"A tag named \"{trimmed}\" already exists.");
        }

        var tag = await db.Tags.Include(t => t.Subtags).FirstOrDefaultAsync(t => t.Id == tagId, ct);
        if (tag is null)
        {
            return TagOperationResult.Fail("Tag not found.");
        }

        tag.Name = trimmed;
        tag.NeedsReview = false;
        await db.SaveChangesAsync(ct);

        var allAffectedTagIds = new List<int> { tagId };
        allAffectedTagIds.AddRange(tag.Subtags.Select(s => s.Id));

        var movieIds = await db.MovieTags.Where(mt => allAffectedTagIds.Contains(mt.TagId)).Select(mt => mt.MovieId).Distinct().ToListAsync(ct);
        await TagNormalization.SyncMetaGenresAsync(db, movieIds, ct);
        await db.SaveChangesAsync(ct);

        driftChecks?.Enqueue(movieIds);

        return TagOperationResult.Ok(tag);
    }

    public async Task<IReadOnlyList<TagMergeCandidate>> GetMergeCandidatesAsync(int sourceTagId, string? search = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var source = await db.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == sourceTagId, ct);
        if (source is null) return [];

        if (source.IsActorTag) return [];

        var query = db.Tags.AsNoTracking().Where(t => t.Id != sourceTagId && !t.IsActorTag);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var upper = search.Trim().ToUpper();
            query = query.Where(t => t.Name.ToUpper().Contains(upper));
        }

        var candidates = await query
            .Select(t => new { t.Id, t.Name, MovieCount = t.MovieTags.Count, t.ParentTagId, ParentTagName = t.ParentTag != null ? t.ParentTag.Name : null })
            .OrderByDescending(t => t.MovieCount)
            .ThenBy(t => t.Name)
            .Take(50)
            .ToListAsync(ct);

        return candidates
            .Select(t => new TagMergeCandidate(
                t.Id,
                t.Name,
                t.MovieCount,
                IsSuggested: t.Name.Contains(source.Name, StringComparison.OrdinalIgnoreCase)
                    || source.Name.Contains(t.Name, StringComparison.OrdinalIgnoreCase),
                t.ParentTagId,
                t.ParentTagName))
            .OrderByDescending(c => c.IsSuggested)
            .ThenByDescending(c => c.MovieCount)
            .ThenBy(c => c.Name)
            .ToList();
    }


    public async Task<TagOperationResult> MergeAsync(int sourceTagId, int targetTagId, bool createReplacementRule = false, CancellationToken ct = default)
    {
        if (sourceTagId == targetTagId)
        {
            return TagOperationResult.Fail("Cannot merge a tag into itself.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var source = await db.Tags.Include(t => t.Subtags).FirstOrDefaultAsync(t => t.Id == sourceTagId, ct);
        var target = await db.Tags.FirstOrDefaultAsync(t => t.Id == targetTagId, ct);
        if (source is null) return TagOperationResult.Fail("Source tag not found.");
        if (target is null) return TagOperationResult.Fail("Target tag not found.");
        if (source.IsActorTag || target.IsActorTag) return TagOperationResult.Fail(ActorTagsCannotMerge);

        if (source.Subtags.Count > 0)
        {
            foreach (var subtag in source.Subtags)
            {
                // When the target is itself a sub-tag, reparenting to it would create a
                // 3-level hierarchy (beyond the 2-level limit). Promote orphaned subtags to
                // root level instead so the merge is never blocked by the target's depth.
                subtag.ParentTagId = target.ParentTagId == null ? target.Id : null;
            }
        }

        if (createReplacementRule)
        {
            var ruleExists = await db.TagReplacementRules.AnyAsync(r => r.SourceValue.ToUpper() == source.Name.ToUpper(), ct);
            if (!ruleExists)
            {
                db.TagReplacementRules.Add(new TagReplacementRule
                {
                    SourceValue = source.Name,
                    MatchMode = TagMatchMode.CaseInsensitive,
                    TargetTagId = target.Id
                });
            }
        }

        var targetLinks = await db.MovieTags.Where(mt => mt.TagId == target.Id).ToDictionaryAsync(mt => mt.MovieId, ct);
        var sourceLinks = await db.MovieTags.Where(mt => mt.TagId == source.Id).ToListAsync(ct);
        var affectedMovieIds = new HashSet<int>(targetLinks.Keys);

        foreach (var link in sourceLinks)
        {
            affectedMovieIds.Add(link.MovieId);
            db.MovieTags.Remove(link);
            MergeMovieTagInto(db, targetLinks, link, target.Id);
        }

        await RepointClipTagsAsync(db, [source.Id], target.Id, ct);

        // TagReplacementRule.TargetTagId cascade-deletes when its target Tag is removed — without
        // this, any rule that pointed at the now-merged-away source would silently vanish instead
        // of following it to the new canonical target.
        var rulesTargetingSource = await db.TagReplacementRules.Where(r => r.TargetTagId == source.Id).ToListAsync(ct);
        foreach (var rule in rulesTargetingSource)
        {
            var alreadyCoveredByTarget = await db.TagReplacementRules.AnyAsync(r =>
                r.Id != rule.Id && r.TargetTagId == target.Id && r.MatchMode == rule.MatchMode && r.SourceValue.ToUpper() == rule.SourceValue.ToUpper(), ct);
            if (alreadyCoveredByTarget)
            {
                db.TagReplacementRules.Remove(rule);
            }
            else
            {
                rule.TargetTagId = target.Id;
            }
        }

        target.NeedsReview = false;
        db.Tags.Remove(source);
        await db.SaveChangesAsync(ct);

        await TagNormalization.SyncMetaGenresAsync(db, affectedMovieIds, ct);
        await db.SaveChangesAsync(ct);

        driftChecks?.Enqueue(affectedMovieIds);

        return TagOperationResult.Ok(target);
    }

    public async Task<TagOperationResult> DeleteAsync(int tagId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tag = await db.Tags.Include(t => t.Subtags).FirstOrDefaultAsync(t => t.Id == tagId, ct);
        if (tag is null) return TagOperationResult.Fail("Tag not found.");

        if (tag.Subtags.Count > 0)
        {
            return TagOperationResult.Fail("Cannot delete a tag that has subtags. Reparent or delete the subtags first.");
        }

        var movieIds = await db.MovieTags.Where(mt => mt.TagId == tagId).Select(mt => mt.MovieId).ToListAsync(ct);
        db.Tags.Remove(tag);
        await db.SaveChangesAsync(ct);

        await TagNormalization.SyncMetaGenresAsync(db, movieIds, ct);
        await db.SaveChangesAsync(ct);

        // After the save, in the background: a big tag can be on hundreds of movies.
        driftChecks?.Enqueue(movieIds);

        return TagOperationResult.Ok(tag);
    }

    public async Task<TagOperationResult> IgnoreAsync(int tagId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tag = await db.Tags.Include(t => t.Subtags).FirstOrDefaultAsync(t => t.Id == tagId, ct);
        if (tag is null) return TagOperationResult.Fail("Tag not found.");

        if (tag.Subtags.Count > 0)
        {
            return TagOperationResult.Fail("Cannot ignore a category that has subtags. Reparent or delete the subtags first.");
        }

        var alreadyIgnored = await db.IgnoredTags.AnyAsync(i => i.Value.ToUpper() == tag.Name.ToUpper(), ct);
        if (!alreadyIgnored)
        {
            db.IgnoredTags.Add(new IgnoredTag { Value = tag.Name, MatchMode = TagMatchMode.CaseInsensitive });
        }

        var movieIds = await db.MovieTags.Where(mt => mt.TagId == tagId).Select(mt => mt.MovieId).ToListAsync(ct);
        db.Tags.Remove(tag);
        await db.SaveChangesAsync(ct);

        await TagNormalization.SyncMetaGenresAsync(db, movieIds, ct);
        await db.SaveChangesAsync(ct);

        driftChecks?.Enqueue(movieIds);

        return TagOperationResult.Ok(tag);
    }

    public async Task<TagOperationResult> ApproveAsync(int tagId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == tagId, ct);
        if (tag is null) return TagOperationResult.Fail("Tag not found.");

        tag.NeedsReview = false;
        await db.SaveChangesAsync(ct);
        return TagOperationResult.Ok(tag);
    }

    public async Task<int> ApproveManyAsync(IReadOnlyList<int> tagIds, CancellationToken ct = default)
    {
        if (tagIds.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // SQLite counts every matched row, so already-approved tags still count, as they did when this loaded them.
        return await db.Tags
            .Where(t => tagIds.Contains(t.Id))
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.NeedsReview, false), ct);
    }

    public async Task<int> IgnoreManyAsync(IReadOnlyList<int> tagIds, CancellationToken ct = default)
    {
        if (tagIds.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tags = await db.Tags.Include(t => t.Subtags).Where(t => tagIds.Contains(t.Id)).ToListAsync(ct);
        // Do not delete categories that currently have subtags
        var tagsToIgnore = tags.Where(t => t.Subtags.Count == 0).ToList();
        if (tagsToIgnore.Count == 0) return 0;

        var existingIgnored = (await db.IgnoredTags.Select(i => i.Value).ToListAsync(ct))
            .Select(v => v.ToUpper())
            .ToHashSet();
        var ignoreIds = tagsToIgnore.Select(t => t.Id).ToList();
        var affectedMovieIds = await db.MovieTags
            .Where(mt => ignoreIds.Contains(mt.TagId))
            .Select(mt => mt.MovieId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var tag in tagsToIgnore)
        {
            if (existingIgnored.Add(tag.Name.ToUpper()))
            {
                db.IgnoredTags.Add(new IgnoredTag { Value = tag.Name, MatchMode = TagMatchMode.CaseInsensitive });
            }

            db.Tags.Remove(tag);
        }

        await db.SaveChangesAsync(ct);

        await TagNormalization.SyncMetaGenresAsync(db, affectedMovieIds, ct);
        await db.SaveChangesAsync(ct);

        driftChecks?.Enqueue(affectedMovieIds);

        return tagsToIgnore.Count;
    }

    public async Task<TagOperationResult> MergeManyAsync(IReadOnlyList<int> sourceTagIds, int targetTagId, bool createReplacementRule = false, CancellationToken ct = default)
    {
        var sourceIds = sourceTagIds.Where(id => id != targetTagId).Distinct().ToList();
        if (sourceIds.Count == 0)
        {
            return TagOperationResult.Fail("Select at least one tag to merge, other than the target.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var target = await db.Tags.FirstOrDefaultAsync(t => t.Id == targetTagId, ct);
        if (target is null) return TagOperationResult.Fail("Target tag not found.");

        var sources = await db.Tags.Include(t => t.Subtags).Where(t => sourceIds.Contains(t.Id)).ToListAsync(ct);
        if (sources.Count == 0) return TagOperationResult.Fail("Source tags not found.");
        if (target.IsActorTag || sources.Any(s => s.IsActorTag)) return TagOperationResult.Fail(ActorTagsCannotMerge);

        var loadedSourceIds = sources.Select(s => s.Id).ToList();
        var targetLinks = await db.MovieTags.Where(mt => mt.TagId == target.Id).ToDictionaryAsync(mt => mt.MovieId, ct);
        var affectedMovieIds = new HashSet<int>(targetLinks.Keys);
        var sourceLinksByTag = (await db.MovieTags.Where(mt => loadedSourceIds.Contains(mt.TagId)).ToListAsync(ct))
            .ToLookup(mt => mt.TagId);
        await RepointClipTagsAsync(db, loadedSourceIds, target.Id, ct);

        // Every rule this merge can read or re-point, loaded once: rules targeting a source or the
        // target (for re-pointing and dedup), plus any rule whose value matches a source name (for
        // the createReplacementRule existence check). Mutations stay tracked on these entities.
        var sourceNamesUpper = sources.Select(s => s.Name.ToUpper()).ToList();
        var relevantRules = await db.TagReplacementRules
            .Where(r => r.TargetTagId == target.Id
                || loadedSourceIds.Contains(r.TargetTagId)
                || (createReplacementRule && sourceNamesUpper.Contains(r.SourceValue.ToUpper())))
            .ToListAsync(ct);
        var rulesBySourceTag = relevantRules.Where(r => r.TargetTagId != target.Id).ToLookup(r => r.TargetTagId);

        // In-memory dedup state standing in for per-source SaveChanges: the (SourceValue, MatchMode)
        // unique index is SQLite's default case-sensitive collation, so two sources can
        // independently carry rules that are case-variant duplicates (e.g. "Uncensored" vs
        // "UNCENSORED"), and case-variant source names can each want a replacement rule. Both sets
        // are keyed case-insensitively and updated after each source is processed, so later sources
        // see earlier sources' remappings and new rules without a flush in between.
        var existingRuleValues = relevantRules.Select(r => r.SourceValue.ToUpper()).ToHashSet();
        var rulesCoveredByTarget = relevantRules
            .Where(r => r.TargetTagId == target.Id)
            .Select(r => (r.MatchMode, Value: r.SourceValue.ToUpper()))
            .ToHashSet();

        foreach (var source in sources)
        {
            if (source.Subtags.Count > 0)
            {
                foreach (var subtag in source.Subtags)
                {
                    // When the target is itself a sub-tag, reparenting to it would create a
                    // 3-level hierarchy (beyond the 2-level limit). Promote orphaned subtags to
                    // root level instead so the merge is never blocked by the target's depth.
                    subtag.ParentTagId = target.ParentTagId == null ? target.Id : null;
                }
            }

            // Rules this source adds to the target only become visible to later sources' dedup,
            // matching what the former per-source SaveChanges exposed.
            var newlyCoveredByTarget = new List<(TagMatchMode MatchMode, string Value)>();

            if (createReplacementRule && existingRuleValues.Add(source.Name.ToUpper()))
            {
                db.TagReplacementRules.Add(new TagReplacementRule
                {
                    SourceValue = source.Name,
                    MatchMode = TagMatchMode.CaseInsensitive,
                    TargetTagId = target.Id
                });
                newlyCoveredByTarget.Add((TagMatchMode.CaseInsensitive, source.Name.ToUpper()));
            }

            foreach (var link in sourceLinksByTag[source.Id])
            {
                affectedMovieIds.Add(link.MovieId);
                db.MovieTags.Remove(link);
                MergeMovieTagInto(db, targetLinks, link, target.Id);
            }

            foreach (var rule in rulesBySourceTag[source.Id])
            {
                var key = (rule.MatchMode, rule.SourceValue.ToUpper());
                if (rulesCoveredByTarget.Contains(key))
                {
                    db.TagReplacementRules.Remove(rule);
                }
                else
                {
                    rule.TargetTagId = target.Id;
                    newlyCoveredByTarget.Add(key);
                }
            }

            rulesCoveredByTarget.UnionWith(newlyCoveredByTarget);

            target.NeedsReview = false;
            db.Tags.Remove(source);
        }

        await db.SaveChangesAsync(ct);

        await TagNormalization.SyncMetaGenresAsync(db, affectedMovieIds, ct);
        await db.SaveChangesAsync(ct);

        driftChecks?.Enqueue(affectedMovieIds);

        return TagOperationResult.Ok(target);
    }

    public async Task<TagOperationResult> AddTagToMovieAsync(int movieId, int tagId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var movieExists = await db.Movies.AnyAsync(m => m.Id == movieId, ct);
        if (!movieExists)
        {
            return TagOperationResult.Fail("Movie not found.");
        }

        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == tagId, ct);
        if (tag is null)
        {
            return TagOperationResult.Fail("Tag not found.");
        }
        if (tag.IsActorTag)
        {
            return TagOperationResult.Fail($"\"{tag.Name}\" is an actor tag: add it to an actor in the movie, not to the movie.");
        }

        var existing = await db.MovieTags.FirstOrDefaultAsync(mt => mt.MovieId == movieId && mt.TagId == tagId, ct);
        if (existing is { IsExplicit: true })
        {
            return TagOperationResult.Fail($"\"{tag.Name}\" is already on this movie.");
        }

        // A tag the movie only has through its clips becomes its own as well.
        if (existing is not null)
        {
            existing.IsExplicit = true;
        }
        else
        {
            db.MovieTags.Add(new MovieTag { MovieId = movieId, TagId = tagId });
        }
        await db.SaveChangesAsync(ct);

        await TagNormalization.SyncMetaGenresAsync(db, [movieId], ct);
        await db.SaveChangesAsync(ct);

        // Re-checks .nfo drift immediately rather than waiting for the next scheduled Library
        // Rescan (the genre comparison would otherwise only ever catch up on this movie's
        // tag edit hours/days later).
        await nfoSyncService.CheckMovieNfoConflictAsync(movieId, ct);

        return TagOperationResult.Ok(tag);
    }

    public async Task<OperationResult> RemoveTagFromMovieAsync(int movieId, int tagId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var link = await db.MovieTags.FirstOrDefaultAsync(mt => mt.MovieId == movieId && mt.TagId == tagId, ct);
        if (link is null)
        {
            return OperationResult.Fail("Tag is not on this movie.");
        }

        // While a scene, highlight or apex still carries it, the movie keeps it as a clip-only tag.
        if (link.FromClips)
        {
            link.IsExplicit = false;
        }
        else
        {
            db.MovieTags.Remove(link);
        }
        await db.SaveChangesAsync(ct);

        await TagNormalization.SyncMetaGenresAsync(db, [movieId], ct);
        await db.SaveChangesAsync(ct);

        // See AddTagToMovieAsync's matching comment: re-check .nfo drift immediately instead of
        // only on the next scheduled Library Rescan.
        await nfoSyncService.CheckMovieNfoConflictAsync(movieId, ct);

        return OperationResult.Ok();
    }

    /// <summary>Moves a merged-away source tag's movie link onto the target: ORs its flags into the
    /// movie's target link when there is one, else adds a target link carrying the same flags.</summary>
    private static void MergeMovieTagInto(AppDbContext db, Dictionary<int, MovieTag> targetLinks, MovieTag sourceLink, int targetTagId)
    {
        if (targetLinks.TryGetValue(sourceLink.MovieId, out var existing))
        {
            existing.IsExplicit |= sourceLink.IsExplicit;
            existing.FromClips |= sourceLink.FromClips;
            return;
        }

        var added = new MovieTag { MovieId = sourceLink.MovieId, TagId = targetTagId, IsExplicit = sourceLink.IsExplicit, FromClips = sourceLink.FromClips };
        db.MovieTags.Add(added);
        targetLinks[sourceLink.MovieId] = added;
    }

    /// <summary>Moves scene, highlight and apex tags from the merged-away
    /// source tags onto the target, the way merges move MovieTags; without this they'd cascade away with
    /// the source, leaving the movie's FromClips flags pointing at tags no clip carries. A clip that
    /// already has the target just loses the source.</summary>
    private static async Task RepointClipTagsAsync(AppDbContext db, IReadOnlyCollection<int> sourceTagIds, int targetTagId, CancellationToken ct)
    {
        await RepointSceneTagsAsync(db, sourceTagIds, targetTagId, ct);

        var targetHighlightIds = (await db.HighlightTags.Where(ht => ht.TagId == targetTagId).Select(ht => ht.HighlightId).ToListAsync(ct)).ToHashSet();
        foreach (var link in await db.HighlightTags.Where(ht => sourceTagIds.Contains(ht.TagId)).ToListAsync(ct))
        {
            db.HighlightTags.Remove(link);
            if (targetHighlightIds.Add(link.HighlightId))
            {
                db.HighlightTags.Add(new HighlightTag { HighlightId = link.HighlightId, TagId = targetTagId });
            }
        }

        var targetApexIds = (await db.ApexTags.Where(at => at.TagId == targetTagId).Select(at => at.ApexId).ToListAsync(ct)).ToHashSet();
        foreach (var link in await db.ApexTags.Where(at => sourceTagIds.Contains(at.TagId)).ToListAsync(ct))
        {
            db.ApexTags.Remove(link);
            if (targetApexIds.Add(link.ApexId))
            {
                db.ApexTags.Add(new ApexTag { ApexId = link.ApexId, TagId = targetTagId });
            }
        }
    }

    private static async Task RepointSceneTagsAsync(AppDbContext db, IReadOnlyCollection<int> sourceTagIds, int targetTagId, CancellationToken ct)
    {
        var targetSceneIds = (await db.SceneTags.Where(st => st.TagId == targetTagId).Select(st => st.SceneId).ToListAsync(ct)).ToHashSet();
        var sourceLinks = await db.SceneTags.Where(st => sourceTagIds.Contains(st.TagId)).ToListAsync(ct);
        foreach (var link in sourceLinks)
        {
            db.SceneTags.Remove(link);
            if (targetSceneIds.Add(link.SceneId))
            {
                db.SceneTags.Add(new SceneTag { SceneId = link.SceneId, TagId = targetTagId });
            }
        }
    }
}
