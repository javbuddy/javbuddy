using Javbuddy.Data;

namespace Javbuddy.Services.Scenes;

/// <summary>A scene's, highlight's or apex's tag, own or rolled up.</summary>
public sealed class OwnerTag
{
    public int OwnerId { get; init; }
    public int TagId { get; init; }
}

/// <summary>A scene's, highlight's or apex's effective actor, own or inherited.</summary>
public sealed class OwnerActor
{
    public int OwnerId { get; init; }
    public int ActorId { get; init; }
}

/// <summary>A clip (by id) and something it is in or holds: a scene, a highlight or an apex.</summary>
public sealed class ClipPair
{
    public int OwnerId { get; init; }
    public int OtherId { get; init; }
}

/// <summary>A point in a movie owned by a clip: an apex's time or a highlight's start.</summary>
public sealed class ClipPoint
{
    public int OwnerId { get; init; }
    public int MovieId { get; init; }
    public double Seconds { get; init; }
}

/// <summary>EF-translatable mirror of <see cref="ClipRollup"/> (tags up) for the Scenes wall's filters, options and
/// cards, plus the stored effective actors <see cref="ClipActors"/> computes (kept by
/// ClipActorSync). Each containment rule is one pair query below, reused by every roll-up and by the wall's apex
/// filters, and ClipRollupQueriesTests checks tags and actors against the same cases as the pure helpers. The results
/// are composed into correlated subqueries (<c>…Any(r =&gt; r.OwnerId == s.Id)</c>), so nothing here runs on its own.</summary>
public static class ClipRollupQueries
{
    // --- Containment ---

    public static IQueryable<ClipPoint> ApexPoints(AppDbContext db) =>
        db.MovieApexes.Select(a => new ClipPoint { OwnerId = a.Id, MovieId = a.MovieId, Seconds = a.Seconds });

    public static IQueryable<ClipPoint> HighlightStarts(AppDbContext db) =>
        db.MovieHighlights.Select(h => new ClipPoint { OwnerId = h.Id, MovieId = h.MovieId, Seconds = h.StartSeconds });

    /// <summary>(point owner, scene) for the scene whose effective range [start, end) holds the point: the
    /// latest-starting scene at or before it (ties to the higher id, as SceneRanges orders them — scenes don't
    /// overlap, so an open end runs to exactly the next one's start), unless the point is past that scene's
    /// explicit end or the movie's duration. Same rule as ApexRanges.IsWithin on effective ranges; an indexed
    /// scalar lookup per point rather than a pairwise NOT EXISTS, which is what keeps the wall fast.</summary>
    public static IQueryable<ClipPair> SceneHolding(AppDbContext db, IQueryable<ClipPoint> points) =>
        from p in points
        let sceneId = db.Scenes
            .Where(s => s.MovieId == p.MovieId && s.StartSeconds <= p.Seconds)
            .OrderByDescending(s => s.StartSeconds).ThenByDescending(s => s.Id)
            .Select(s => (int?)s.Id)
            .FirstOrDefault()
        from s in db.Scenes
        where s.Id == sceneId
            && (s.EndSeconds == null || p.Seconds < s.EndSeconds.Value)
            && (s.Movie.MediaDurationSeconds == null || p.Seconds < s.Movie.MediaDurationSeconds)
        select new ClipPair { OwnerId = p.OwnerId, OtherId = s.Id };

    /// <summary>(apex, scene) — the scene holding the apex.</summary>
    public static IQueryable<ClipPair> ApexScenes(AppDbContext db) => SceneHolding(db, ApexPoints(db));

    /// <summary>(highlight, scene) — the scene the highlight starts in.</summary>
    public static IQueryable<ClipPair> HighlightStartScenes(AppDbContext db) => SceneHolding(db, HighlightStarts(db));

    /// <summary>(highlight, scene) for every scene whose effective range the highlight overlaps; touching
    /// edges don't count (ClipRollup.HighlightOverlapsScene). As scenes don't overlap, those are the scene its
    /// start is in plus every scene starting strictly inside it.</summary>
    public static IQueryable<ClipPair> HighlightOverlappedScenes(AppDbContext db) =>
        HighlightStartScenes(db)
            .Concat(from h in db.MovieHighlights
                    from s in db.Scenes
                    where s.MovieId == h.MovieId && s.StartSeconds > h.StartSeconds && s.StartSeconds < h.EndSeconds
                    select new ClipPair { OwnerId = h.Id, OtherId = s.Id });

    /// <summary>(apex, highlight) for every highlight whose [start, end) holds the apex.</summary>
    public static IQueryable<ClipPair> ApexHighlights(AppDbContext db) =>
        from a in db.MovieApexes
        from h in db.MovieHighlights
        where h.MovieId == a.MovieId && a.Seconds >= h.StartSeconds && a.Seconds < h.EndSeconds
        select new ClipPair { OwnerId = a.Id, OtherId = h.Id };

    // --- Tags up ---

    /// <summary>Each scene's own tags, plus those of the highlights overlapping it and the apexes in it.</summary>
    public static IQueryable<OwnerTag> SceneTags(AppDbContext db) =>
        db.SceneTags.Select(st => new OwnerTag { OwnerId = st.SceneId, TagId = st.TagId })
            .Concat(from p in HighlightOverlappedScenes(db)
                    from ht in db.HighlightTags
                    where ht.HighlightId == p.OwnerId
                    select new OwnerTag { OwnerId = p.OtherId, TagId = ht.TagId })
            .Concat(from p in ApexScenes(db)
                    from at in db.ApexTags
                    where at.ApexId == p.OwnerId
                    select new OwnerTag { OwnerId = p.OtherId, TagId = at.TagId });

    /// <summary>Each highlight's own tags, plus those of the apexes in it.</summary>
    public static IQueryable<OwnerTag> HighlightTags(AppDbContext db) =>
        db.HighlightTags.Select(ht => new OwnerTag { OwnerId = ht.HighlightId, TagId = ht.TagId })
            .Concat(from p in ApexHighlights(db)
                    from at in db.ApexTags
                    where at.ApexId == p.OwnerId
                    select new OwnerTag { OwnerId = p.OtherId, TagId = at.TagId });

    // --- Actors down: stored ---

    /// <summary>Each scene's effective actors — its own, else the cast — as ClipActorSync stores them.</summary>
    public static IQueryable<OwnerActor> SceneActors(AppDbContext db) =>
        db.SceneEffectiveActors.Select(r => new OwnerActor { OwnerId = r.SceneId, ActorId = r.ActorId });

    /// <summary>Each highlight's effective actors — its own, else those of the scene it starts in, else the cast.</summary>
    public static IQueryable<OwnerActor> HighlightActors(AppDbContext db) =>
        db.HighlightEffectiveActors.Select(r => new OwnerActor { OwnerId = r.HighlightId, ActorId = r.ActorId });

    /// <summary>Each apex's effective actors — its own, else its parent highlight's, else its scene's, else the cast.</summary>
    public static IQueryable<OwnerActor> ApexActors(AppDbContext db) =>
        db.ApexEffectiveActors.Select(r => new OwnerActor { OwnerId = r.ApexId, ActorId = r.ActorId });
}
