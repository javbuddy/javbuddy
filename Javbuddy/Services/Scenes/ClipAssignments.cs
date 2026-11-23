using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.SceneMedia;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>Actor rules scenes, highlights and apexes share: the cast they pick from, the cast check, and
/// loading what they inherit.</summary>
public static class ClipAssignments
{
    /// <summary>The cached thumb/preview rows of the given scenes, highlights or apexes (<paramref name="role"/>),
    /// from one query, ready to answer which media version each card should show.</summary>
    /// <param name="servesVariant">Whether the media endpoint would serve a variant under the current cache mode;
    /// null (tests without a media service) serves every variant.</param>
    /// <param name="isCurrent">Whether a row is current for a window (unexpired, still on disk);
    /// null (tests) falls back to the row matching the window.</param>
    public static async Task<ClipMediaVersions> LoadClipMediaVersionsAsync(AppDbContext db, string role, IReadOnlyCollection<int> clipIds,
        Func<string, bool>? servesVariant, Func<CachedImage, SceneMediaWindow, bool>? isCurrent, CancellationToken ct)
    {
        var rows = clipIds.Count == 0
            ? []
            : await db.CachedImages.AsNoTracking().Where(c => c.Role == role && clipIds.Contains(c.Index)).ToListAsync(ct);
        return new ClipMediaVersions(rows.ToLookup(c => (c.Index, c.Variant)), servesVariant, isCurrent);
    }

    public static async Task<ApexActorOptions> LoadActorOptionsAsync(AppDbContext db, int movieId, CancellationToken ct) =>
        new(await LoadCastAsync(db, movieId, ct));

    /// <summary>Every scene's, highlight's and apex's effective actors for the movie: loads
    /// just the times, positions and own actors <see cref="ClipActors"/> needs.</summary>
    public static async Task<ClipActorsResult> LoadEffectiveActorsAsync(AppDbContext db, int movieId, CancellationToken ct) =>
        (await LoadEffectiveActorsAsync(db, [movieId], ct))[movieId];

    /// <summary>The same for many movies at once, in a fixed handful of queries however many movies there
    /// are (the wall's page covers dozens). Every requested id is in the result, empty for one with no clips.</summary>
    public static async Task<IReadOnlyDictionary<int, ClipActorsResult>> LoadEffectiveActorsAsync(AppDbContext db, IReadOnlyCollection<int> movieIds, CancellationToken ct)
    {
        var items = await LoadClipItemsAsync(db, movieIds, ct);
        return items.ToDictionary(kv => kv.Key, kv => ClipActors.Compute(kv.Value.Cast, kv.Value.Scenes, kv.Value.Highlights, kv.Value.Apexes));
    }

    /// <summary>Every scene's, highlight's and apex's effective actor tags (<see cref="ClipActorTags"/>) for the movie.</summary>
    public static async Task<ClipActorTagsResult> LoadEffectiveActorTagsAsync(AppDbContext db, int movieId, CancellationToken ct) =>
        (await LoadEffectiveActorsAndTagsAsync(db, movieId, ct)).Tags;

    /// <summary>Both of the above from one load of the movie's clips, for ClipActorSync.</summary>
    public static async Task<(ClipActorsResult Actors, ClipActorTagsResult Tags)> LoadEffectiveActorsAndTagsAsync(AppDbContext db, int movieId, CancellationToken ct)
    {
        var (cast, scenes, highlights, apexes) = (await LoadClipItemsAsync(db, [movieId], ct))[movieId];
        var sets = new ActorTagSets(
            Group((await db.MovieActorTags.AsNoTracking().Where(t => t.MovieId == movieId).Select(t => new { t.ActorId, t.TagId }).ToListAsync(ct)).Select(t => (0, t.ActorId, t.TagId))).GetValueOrDefault(0) ?? new Dictionary<int, IReadOnlySet<int>>(),
            Group((await db.SceneActorTags.AsNoTracking().Where(t => t.MovieId == movieId).Select(t => new { Owner = t.SceneId, t.ActorId, t.TagId }).ToListAsync(ct)).Select(t => (t.Owner, t.ActorId, t.TagId))),
            Group((await db.HighlightActorTags.AsNoTracking().Where(t => t.MovieId == movieId).Select(t => new { Owner = t.HighlightId, t.ActorId, t.TagId }).ToListAsync(ct)).Select(t => (t.Owner, t.ActorId, t.TagId))),
            Group((await db.ApexActorTags.AsNoTracking().Where(t => t.MovieId == movieId).Select(t => new { Owner = t.ApexId, t.ActorId, t.TagId }).ToListAsync(ct)).Select(t => (t.Owner, t.ActorId, t.TagId))));
        var actors = ClipActors.Compute(cast, scenes, highlights, apexes);
        return (actors, ClipActorTags.Compute(actors, scenes, highlights, apexes, sets));
    }

    /// <summary>The movie's explicit tags it passes down to its clips (MovieTag.PropagatesDown), ordered by name.</summary>
    public static async Task<IReadOnlyList<SceneTagItem>> LoadPropagatingMovieTagsAsync(AppDbContext db, int movieId, CancellationToken ct) =>
        (await db.MovieTags.AsNoTracking()
            .Where(mt => mt.MovieId == movieId && mt.IsExplicit && mt.PropagatesDown && !mt.Tag.IsActorTag)
            .Select(mt => new { mt.TagId, mt.Tag.Name, Parent = mt.Tag.ParentTag != null ? mt.Tag.ParentTag.Name : null })
            .ToListAsync(ct))
        .Select(t => new SceneTagItem(t.TagId, t.Name, t.Parent) { PropagatesDown = true })
        .OrderBy(t => t.ParentName ?? t.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>The movie's clips' inherited tags (<see cref="ClipTagInheritance"/>), for ClipActorSync: loads just
    /// the times, the tag links that pass down and the exclusions. Tag names are left empty.</summary>
    public static async Task<ClipTagInheritanceResult> LoadInheritedTagsAsync(AppDbContext db, int movieId, CancellationToken ct)
    {
        var duration = await db.Movies.Where(m => m.Id == movieId).Select(m => m.MediaDurationSeconds).FirstOrDefaultAsync(ct);
        var movieTags = await db.MovieTags.AsNoTracking()
            .Where(mt => mt.MovieId == movieId && mt.IsExplicit && mt.PropagatesDown && !mt.Tag.IsActorTag)
            .Select(mt => mt.TagId)
            .ToListAsync(ct);
        var scenes = await db.Scenes.AsNoTracking().Where(s => s.MovieId == movieId).ToListAsync(ct);
        var sceneTags = (await db.SceneTags.AsNoTracking().Where(t => t.Scene.MovieId == movieId && t.PropagatesDown).Select(t => new { t.SceneId, t.TagId }).ToListAsync(ct))
            .ToLookup(t => t.SceneId, t => Passed(t.TagId));
        var highlights = await db.MovieHighlights.AsNoTracking().Where(h => h.MovieId == movieId).ToListAsync(ct);
        var highlightTags = (await db.HighlightTags.AsNoTracking().Where(t => t.Highlight.MovieId == movieId && t.PropagatesDown).Select(t => new { t.HighlightId, t.TagId }).ToListAsync(ct))
            .ToLookup(t => t.HighlightId, t => Passed(t.TagId));
        var apexes = await db.MovieApexes.AsNoTracking().Where(a => a.MovieId == movieId).OrderBy(a => a.Seconds).ThenBy(a => a.Id).ToListAsync(ct);
        var sceneExclusions = (await db.SceneTagExclusions.AsNoTracking().Where(e => e.MovieId == movieId).ToListAsync(ct)).ToLookup(e => e.SceneId, e => e.TagId);
        var highlightExclusions = (await db.HighlightTagExclusions.AsNoTracking().Where(e => e.MovieId == movieId).ToListAsync(ct)).ToLookup(e => e.HighlightId, e => e.TagId);
        var apexExclusions = (await db.ApexTagExclusions.AsNoTracking().Where(e => e.MovieId == movieId).ToListAsync(ct)).ToLookup(e => e.ApexId, e => e.TagId);

        var sceneItems = SceneRanges.ResolveEffectiveRanges(scenes, duration)
            .Select(r => new SceneItem(r.Scene.Id, r.Position, "", r.Scene.Title, r.Scene.StartSeconds, r.Scene.EndSeconds, r.EffectiveEndSeconds)
            {
                Tags = sceneTags[r.Scene.Id].ToList(),
                ExcludedTagIds = sceneExclusions[r.Scene.Id].ToHashSet(),
            })
            .ToList();
        var highlightItems = HighlightRanges.Order(highlights)
            .Select((h, i) => new HighlightItem(h.Id, i + 1, "", h.Title, h.StartSeconds, h.EndSeconds, h.IsFavorite, 0)
            {
                Tags = highlightTags[h.Id].ToList(),
                ExcludedTagIds = highlightExclusions[h.Id].ToHashSet(),
            })
            .ToList();
        var apexItems = apexes
            .Select((a, i) => new ApexItem(a.Id, i + 1, a.Seconds, []) { ExcludedTagIds = apexExclusions[a.Id].ToHashSet() })
            .ToList();
        return ClipTagInheritance.Compute(movieTags.Select(Passed).ToList(), sceneItems, highlightItems, apexItems);

        static SceneTagItem Passed(int tagId) => new(tagId, "", null) { PropagatesDown = true };
    }

    private static async Task<Dictionary<int, (IReadOnlyList<SceneActorItem> Cast, List<SceneItem> Scenes, List<HighlightItem> Highlights, List<ApexItem> Apexes)>> LoadClipItemsAsync(
        AppDbContext db, IReadOnlyCollection<int> movieIds, CancellationToken ct)
    {
        var ids = movieIds.Distinct().ToList();
        var casts = await LoadCastsAsync(db, ids, ct);
        var durations = (await db.Movies.Where(m => ids.Contains(m.Id)).Select(m => new { m.Id, m.MediaDurationSeconds }).ToListAsync(ct))
            .ToDictionary(m => m.Id, m => m.MediaDurationSeconds);
        var scenes = (await db.Scenes.AsNoTracking()
                .Include(s => s.SceneActors).ThenInclude(sa => sa.MovieActor).ThenInclude(ma => ma.Actor)
                .Where(s => ids.Contains(s.MovieId))
                .ToListAsync(ct))
            .ToLookup(s => s.MovieId);
        var highlights = (await db.MovieHighlights.AsNoTracking()
                .Include(h => h.HighlightActors).ThenInclude(ha => ha.MovieActor).ThenInclude(ma => ma.Actor)
                .Where(h => ids.Contains(h.MovieId))
                .ToListAsync(ct))
            .ToLookup(h => h.MovieId);
        var apexes = (await db.MovieApexes.AsNoTracking()
                .Include(a => a.ApexActors).ThenInclude(aa => aa.MovieActor).ThenInclude(ma => ma.Actor)
                .Where(a => ids.Contains(a.MovieId))
                .OrderBy(a => a.Seconds).ThenBy(a => a.Id)
                .ToListAsync(ct))
            .ToLookup(a => a.MovieId);

        var result = new Dictionary<int, (IReadOnlyList<SceneActorItem>, List<SceneItem>, List<HighlightItem>, List<ApexItem>)>();
        foreach (var movieId in ids)
        {
            var sceneItems = SceneRanges.ResolveEffectiveRanges(scenes[movieId].ToList(), durations.GetValueOrDefault(movieId))
                .Select(r => new SceneItem(r.Scene.Id, r.Position, "", r.Scene.Title, r.Scene.StartSeconds, r.Scene.EndSeconds, r.EffectiveEndSeconds)
                {
                    Actors = r.Scene.SceneActors.Select(sa => new SceneActorItem(sa.ActorId, sa.MovieActor.Actor.DisplayName)).ToList(),
                })
                .ToList();
            var highlightItems = HighlightRanges.Order(highlights[movieId].ToList())
                .Select((h, i) => new HighlightItem(h.Id, i + 1, "", h.Title, h.StartSeconds, h.EndSeconds, h.IsFavorite, 0)
                {
                    Actors = h.HighlightActors.Select(ha => new SceneActorItem(ha.ActorId, ha.MovieActor.Actor.DisplayName)).ToList(),
                })
                .ToList();
            var apexItems = apexes[movieId]
                .Select((a, i) => new ApexItem(a.Id, i + 1, a.Seconds, [])
                {
                    Actors = a.ApexActors.Select(aa => new SceneActorItem(aa.ActorId, aa.MovieActor.Actor.DisplayName)).ToList(),
                })
                .ToList();
            result[movieId] = (casts[movieId], sceneItems, highlightItems, apexItems);
        }
        return result;
    }

    private static Dictionary<int, IReadOnlyDictionary<int, IReadOnlySet<int>>> Group(IEnumerable<(int Owner, int ActorId, int TagId)> rows) =>
        rows.GroupBy(r => r.Owner).ToDictionary(
            owner => owner.Key,
            owner => (IReadOnlyDictionary<int, IReadOnlySet<int>>)owner.GroupBy(r => r.ActorId)
                .ToDictionary(actor => actor.Key, actor => (IReadOnlySet<int>)actor.Select(r => r.TagId).ToHashSet()));

    /// <summary>Returns an error message when an actor isn't in the movie's cast, else null.</summary>
    public static async Task<string?> ValidateActorsAsync(AppDbContext db, int movieId, List<int> actorIds, CancellationToken ct) =>
        await db.MovieActors.CountAsync(ma => ma.MovieId == movieId && actorIds.Contains(ma.ActorId), ct) == actorIds.Count
            ? null
            : "Actor is not in the movie's cast.";

    private static async Task<IReadOnlyList<SceneActorItem>> LoadCastAsync(AppDbContext db, int movieId, CancellationToken ct) =>
        (await LoadCastsAsync(db, [movieId], ct))[movieId];

    /// <summary>Each movie's cast ordered by name; every requested id has an entry.</summary>
    private static async Task<Dictionary<int, IReadOnlyList<SceneActorItem>>> LoadCastsAsync(AppDbContext db, IReadOnlyCollection<int> movieIds, CancellationToken ct)
    {
        var rows = await db.MovieActors.AsNoTracking()
            .Where(ma => movieIds.Contains(ma.MovieId))
            .Select(ma => new { ma.MovieId, ma.Actor })
            .ToListAsync(ct);
        var byMovie = rows.ToLookup(r => r.MovieId, r => new SceneActorItem(r.Actor.Id, r.Actor.DisplayName));
        return movieIds.Distinct().ToDictionary(
            id => id,
            id => (IReadOnlyList<SceneActorItem>)byMovie[id].OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }
}

/// <summary>What <see cref="ClipAssignments.LoadClipMediaVersionsAsync"/> loaded.</summary>
public sealed class ClipMediaVersions(
    ILookup<(int Index, string Variant), CachedImage> rows,
    Func<string, bool>? servesVariant,
    Func<CachedImage, SceneMediaWindow, bool>? isCurrent)
{
    /// <summary>The cache row's version (ticks) when the media endpoint would serve that variant for the clip's current
    /// window, else null: anything else would show as a broken image.</summary>
    public long? Of(int clipId, string variant, SceneMediaWindow window)
    {
        if (servesVariant is not null && !servesVariant(variant)) return null;
        var row = rows[(clipId, variant)].FirstOrDefault();
        var current = row is not null && (isCurrent?.Invoke(row, window) ?? (row.SceneStartMs == window.StartMs && row.SceneEndMs == window.EndMs));
        return current ? row!.UpdatedAt.Ticks : null;
    }
}
