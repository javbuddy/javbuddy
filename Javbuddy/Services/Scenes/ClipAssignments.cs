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
        return ClipActors.Compute(cast, sceneItems, highlightItems, apexItems);
    }

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
