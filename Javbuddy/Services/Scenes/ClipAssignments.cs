using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>Actor rules scenes, highlights and apexes share: the cast they pick from, the cast check, and
/// loading what they inherit.</summary>
internal static class ClipAssignments
{
    public static async Task<ApexActorOptions> LoadActorOptionsAsync(AppDbContext db, int movieId, CancellationToken ct) =>
        new(await LoadCastAsync(db, movieId, ct));

    /// <summary>Every scene's, highlight's and apex's effective actors for the movie: loads
    /// just the times, positions and own actors <see cref="ClipActors"/> needs.</summary>
    public static async Task<ClipActorsResult> LoadEffectiveActorsAsync(AppDbContext db, int movieId, CancellationToken ct)
    {
        var (cast, sceneItems, highlightItems, apexItems) = await LoadClipItemsAsync(db, movieId, ct);
        return ClipActors.Compute(cast, sceneItems, highlightItems, apexItems);
    }

    /// <summary>Every scene's, highlight's and apex's effective actor tags (<see cref="ClipActorTags"/>) for the movie.</summary>
    public static async Task<ClipActorTagsResult> LoadEffectiveActorTagsAsync(AppDbContext db, int movieId, CancellationToken ct) =>
        (await LoadEffectiveActorsAndTagsAsync(db, movieId, ct)).Tags;

    /// <summary>Both of the above from one load of the movie's clips, for ClipActorSync.</summary>
    public static async Task<(ClipActorsResult Actors, ClipActorTagsResult Tags)> LoadEffectiveActorsAndTagsAsync(AppDbContext db, int movieId, CancellationToken ct)
    {
        var (cast, scenes, highlights, apexes) = await LoadClipItemsAsync(db, movieId, ct);
        var sets = new ActorTagSets(
            Group((await db.MovieActorTags.AsNoTracking().Where(t => t.MovieId == movieId).Select(t => new { t.ActorId, t.TagId }).ToListAsync(ct)).Select(t => (0, t.ActorId, t.TagId))).GetValueOrDefault(0) ?? new Dictionary<int, IReadOnlySet<int>>(),
            Group((await db.SceneActorTags.AsNoTracking().Where(t => t.MovieId == movieId).Select(t => new { Owner = t.SceneId, t.ActorId, t.TagId }).ToListAsync(ct)).Select(t => (t.Owner, t.ActorId, t.TagId))),
            Group((await db.HighlightActorTags.AsNoTracking().Where(t => t.MovieId == movieId).Select(t => new { Owner = t.HighlightId, t.ActorId, t.TagId }).ToListAsync(ct)).Select(t => (t.Owner, t.ActorId, t.TagId))),
            Group((await db.ApexActorTags.AsNoTracking().Where(t => t.MovieId == movieId).Select(t => new { Owner = t.ApexId, t.ActorId, t.TagId }).ToListAsync(ct)).Select(t => (t.Owner, t.ActorId, t.TagId))));
        var actors = ClipActors.Compute(cast, scenes, highlights, apexes);
        return (actors, ClipActorTags.Compute(actors, scenes, highlights, apexes, sets));
    }

    private static async Task<(IReadOnlyList<SceneActorItem> Cast, List<SceneItem> Scenes, List<HighlightItem> Highlights, List<ApexItem> Apexes)> LoadClipItemsAsync(
        AppDbContext db, int movieId, CancellationToken ct)
    {
        var cast = await LoadCastAsync(db, movieId, ct);
        var duration = await db.Movies.Where(m => m.Id == movieId).Select(m => m.MediaDurationSeconds).FirstOrDefaultAsync(ct);
        var scenes = await db.Scenes.AsNoTracking()
            .Include(s => s.SceneActors).ThenInclude(sa => sa.MovieActor).ThenInclude(ma => ma.Actor)
            .Where(s => s.MovieId == movieId)
            .ToListAsync(ct);
        var highlights = await db.MovieHighlights.AsNoTracking()
            .Include(h => h.HighlightActors).ThenInclude(ha => ha.MovieActor).ThenInclude(ma => ma.Actor)
            .Where(h => h.MovieId == movieId)
            .ToListAsync(ct);
        var apexes = await db.MovieApexes.AsNoTracking()
            .Include(a => a.ApexActors).ThenInclude(aa => aa.MovieActor).ThenInclude(ma => ma.Actor)
            .Where(a => a.MovieId == movieId)
            .OrderBy(a => a.Seconds).ThenBy(a => a.Id)
            .ToListAsync(ct);

        var sceneItems = SceneRanges.ResolveEffectiveRanges(scenes, duration)
            .Select(r => new SceneItem(r.Scene.Id, r.Position, "", r.Scene.Title, r.Scene.StartSeconds, r.Scene.EndSeconds, r.EffectiveEndSeconds)
            {
                Actors = r.Scene.SceneActors.Select(sa => new SceneActorItem(sa.ActorId, sa.MovieActor.Actor.DisplayName)).ToList(),
            })
            .ToList();
        var highlightItems = HighlightRanges.Order(highlights)
            .Select((h, i) => new HighlightItem(h.Id, i + 1, "", h.Title, h.StartSeconds, h.EndSeconds, h.IsFavorite, 0)
            {
                Actors = h.HighlightActors.Select(ha => new SceneActorItem(ha.ActorId, ha.MovieActor.Actor.DisplayName)).ToList(),
            })
            .ToList();
        var apexItems = apexes
            .Select((a, i) => new ApexItem(a.Id, i + 1, a.Seconds, [])
            {
                Actors = a.ApexActors.Select(aa => new SceneActorItem(aa.ActorId, aa.MovieActor.Actor.DisplayName)).ToList(),
            })
            .ToList();
        return (cast, sceneItems, highlightItems, apexItems);
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

    private static async Task<IReadOnlyList<SceneActorItem>> LoadCastAsync(AppDbContext db, int movieId, CancellationToken ct)
    {
        var cast = await db.MovieActors.AsNoTracking()
            .Where(ma => ma.MovieId == movieId)
            .Select(ma => ma.Actor)
            .ToListAsync(ct);
        return cast.Select(a => new SceneActorItem(a.Id, a.DisplayName)).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
