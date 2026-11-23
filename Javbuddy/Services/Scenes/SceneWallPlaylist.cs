using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>A clip as the wall's "Play all" player needs it. DisplayTitle is what the card shows; for an apex the
/// start and end are already the clip around it.</summary>
public sealed record WallPlaylistClip(
    int Id,
    int MovieId,
    string Code,
    string DisplayTitle,
    double StartSeconds,
    double? EndSeconds,
    string? JellyfinItemId,
    string? JellyfinServerId,
    double? DurationSeconds);

public sealed partial class SceneWallQueryService
{
    public async Task<IReadOnlyList<WallPlaylistClip>> GetScenePlaylistAsync(SceneWallFilter filter, SceneWallSort sort, int randomSeed, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await Order(Filter(db, filter).Where(s => s.Movie.LocalFileSizeBytes != null), sort, randomSeed)
            .Select(s => new { s.Id, s.MovieId, s.Movie.Code, s.Movie.JellyfinItemId, s.Movie.JellyfinServerId, s.Movie.MediaDurationSeconds })
            .ToListAsync(ct);

        // A scene's "Scene N" and effective end depend on its movie's other scenes, so those are read too, but
        // only their times and titles: no actors, tags or media.
        var movieIds = rows.Select(r => r.MovieId).Distinct().ToList();
        var durations = rows.GroupBy(r => r.MovieId).ToDictionary(g => g.Key, g => g.First().MediaDurationSeconds);
        var resolved = (await db.Scenes.AsNoTracking()
                .Where(s => movieIds.Contains(s.MovieId))
                .Select(s => new { s.Id, s.MovieId, s.Title, s.StartSeconds, s.EndSeconds })
                .ToListAsync(ct))
            .GroupBy(s => s.MovieId)
            .SelectMany(g => SceneRanges.ResolveEffectiveRanges(
                g.Select(s => new Scene { Id = s.Id, MovieId = s.MovieId, Title = s.Title, StartSeconds = s.StartSeconds, EndSeconds = s.EndSeconds }),
                durations[g.Key]))
            .ToDictionary(r => r.Scene.Id);

        return [.. rows
            .Where(r => resolved.ContainsKey(r.Id))
            .Select(r => new WallPlaylistClip(r.Id, r.MovieId, r.Code ?? string.Empty, resolved[r.Id].DisplayTitle,
                resolved[r.Id].Scene.StartSeconds, resolved[r.Id].EffectiveEndSeconds, r.JellyfinItemId, r.JellyfinServerId, r.MediaDurationSeconds))];
    }

    public async Task<IReadOnlyList<WallPlaylistClip>> GetHighlightPlaylistAsync(SceneWallFilter filter, SceneWallSort sort, int randomSeed, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await OrderHighlights(FilterHighlights(db, filter).Where(h => h.Movie.LocalFileSizeBytes != null), sort, randomSeed)
            .Select(h => new { h.Id, h.MovieId, h.Movie.Code, h.Title, h.StartSeconds, h.EndSeconds, h.Movie.JellyfinItemId, h.Movie.JellyfinServerId, h.Movie.MediaDurationSeconds })
            .ToListAsync(ct);

        // "Highlight N" counts the movie's highlights by start, so it needs the siblings' times (only).
        var movieIds = rows.Select(r => r.MovieId).Distinct().ToList();
        var positions = (await db.MovieHighlights.AsNoTracking()
                .Where(h => movieIds.Contains(h.MovieId))
                .Select(h => new { h.Id, h.MovieId, h.StartSeconds })
                .ToListAsync(ct))
            .GroupBy(h => h.MovieId)
            .SelectMany(g => g.OrderBy(h => h.StartSeconds).ThenBy(h => h.Id).Select((h, i) => (h.Id, Position: i + 1)))
            .ToDictionary(x => x.Id, x => x.Position);

        // Only an untitled highlight is named after its actors and tags.
        var untitled = rows.Where(r => string.IsNullOrWhiteSpace(r.Title)).Select(r => r.Id).ToList();
        var actors = await ActorNamesAsync(db, ClipRollupQueries.HighlightActors(db).Where(r => untitled.Contains(r.OwnerId)), ct);
        var tags = (await db.HighlightTags.AsNoTracking()
                .Where(ht => untitled.Contains(ht.HighlightId))
                .Select(ht => new { Owner = ht.HighlightId, ht.Tag.Name })
                .ToListAsync(ct))
            .ToLookup(t => t.Owner, t => t.Name);

        return [.. rows.Select(r => new WallPlaylistClip(r.Id, r.MovieId, r.Code ?? string.Empty,
            HighlightRanges.DisplayTitle(r.Title, positions.GetValueOrDefault(r.Id, 1), actors[r.Id],
                tags[r.Id].OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)),
            r.StartSeconds, r.EndSeconds, r.JellyfinItemId, r.JellyfinServerId, r.MediaDurationSeconds))];
    }

    public async Task<IReadOnlyList<WallPlaylistClip>> GetApexPlaylistAsync(SceneWallFilter filter, SceneWallSort sort, int randomSeed, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await OrderApexes(FilterApexes(db, filter).Where(a => a.Movie.LocalFileSizeBytes != null), sort, randomSeed)
            .Select(a => new { a.Id, a.MovieId, a.Movie.Code, a.Seconds, a.LeadInSeconds, a.TailSeconds, a.Movie.JellyfinItemId, a.Movie.JellyfinServerId, a.Movie.MediaDurationSeconds })
            .ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var actors = await ActorNamesAsync(db, ClipRollupQueries.ApexActors(db).Where(r => ids.Contains(r.OwnerId)), ct);
        // Own tags only, like ApexItem.DisplayLabel (the card's and Movie Detail's title): a label names what was put
        // on the apex; inherited tags show as chips and count in the filters.
        var tags = (await db.ApexTags.AsNoTracking()
                .Where(at => ids.Contains(at.ApexId))
                .Select(at => new { Owner = at.ApexId, at.Tag.Name })
                .ToListAsync(ct))
            .ToLookup(t => t.Owner, t => t.Name);
        var defaultWindow = await apexes.GetDefaultWindowAsync(ct);

        return [.. rows.Select(r =>
        {
            var (start, end) = ApexRanges.ClipFor(r.Seconds, ApexRanges.Resolve(r.LeadInSeconds, r.TailSeconds, defaultWindow), r.MediaDurationSeconds);
            // The wall names the actors even in a solo movie, like its cards.
            var title = ActorTagLabel.Format(actors[r.Id], tags[r.Id].OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)) ?? "Apex";
            return new WallPlaylistClip(r.Id, r.MovieId, r.Code ?? string.Empty, title, start, end, r.JellyfinItemId, r.JellyfinServerId, r.MediaDurationSeconds);
        })];
    }

    /// <summary>The names of each owner's effective actors, ordered by name.</summary>
    private static async Task<ILookup<int, string>> ActorNamesAsync(AppDbContext db, IQueryable<OwnerActor> owners, CancellationToken ct) =>
        (await (from r in owners
                join a in db.Actors on r.ActorId equals a.Id
                select new { r.OwnerId, a.FirstName, a.LastName, a.PlaceholderMovieId })
            .AsNoTracking()
            .ToListAsync(ct))
        .Select(x => (x.OwnerId, Name: ActorDisplayName.For(x.FirstName, x.LastName, x.PlaceholderMovieId)))
        .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        .ToLookup(x => x.OwnerId, x => x.Name);
}
