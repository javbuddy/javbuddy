using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.Services.Scenes;

/// <summary>The SQL roll-up of tags (mirroring ClipRollup) and the stored effective actors (computed by ClipActors
/// through ClipActorSync), checked against the same ClipCases the pure helpers are.</summary>
public sealed class ClipRollupQueriesTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();

    public void Dispose() => factory.Dispose();

    /// <summary>Case id → database id, per kind.</summary>
    private sealed record SeededCase(
        int MovieId,
        Dictionary<int, int> Actors,
        Dictionary<int, int> Tags,
        Dictionary<int, int> Scenes,
        Dictionary<int, int> Highlights,
        Dictionary<int, int> Apexes);

    private async Task<SeededCase> SeedAsync(ClipCase clipCase)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "CASE-" + clipCase.Name, MediaDurationSeconds = clipCase.Duration };
        db.Movies.Add(movie);
        var actorIds = clipCase.Cast;
        var actors = actorIds.ToDictionary(id => id, id => new Actor { FirstName = ClipCase.ActorName(id) });
        var tagIds = clipCase.Scenes.SelectMany(s => s.Tags)
            .Concat(clipCase.Highlights.SelectMany(h => h.Tags))
            .Concat(clipCase.Apexes.SelectMany(a => a.Tags))
            .Distinct();
        var tags = tagIds.ToDictionary(id => id, id => new Tag { Name = ClipCase.TagName(id) + "-" + clipCase.Name });
        db.Actors.AddRange(actors.Values);
        db.Tags.AddRange(tags.Values);
        await db.SaveChangesAsync();
        db.MovieActors.AddRange(actors.Values.Select(a => new MovieActor { MovieId = movie.Id, ActorId = a.Id }));
        await db.SaveChangesAsync();

        var scenes = clipCase.Scenes.ToDictionary(s => s.Id, s =>
        {
            var scene = new Scene { MovieId = movie.Id, StartSeconds = s.Start, EndSeconds = s.End };
            foreach (var t in s.Tags) scene.SceneTags.Add(new SceneTag { TagId = tags[t].Id });
            foreach (var a in s.Actors) scene.SceneActors.Add(new SceneActor { MovieId = movie.Id, ActorId = actors[a].Id });
            return scene;
        });
        var highlights = clipCase.Highlights.ToDictionary(h => h.Id, h =>
        {
            var highlight = new MovieHighlight { MovieId = movie.Id, StartSeconds = h.Start, EndSeconds = h.End };
            foreach (var t in h.Tags) highlight.HighlightTags.Add(new HighlightTag { TagId = tags[t].Id });
            foreach (var a in h.Actors) highlight.HighlightActors.Add(new HighlightActor { MovieId = movie.Id, ActorId = actors[a].Id });
            return highlight;
        });
        var apexes = clipCase.Apexes.ToDictionary(a => a.Id, a =>
        {
            var apex = new MovieApex { MovieId = movie.Id, Seconds = a.Seconds };
            foreach (var t in a.Tags) apex.ApexTags.Add(new ApexTag { TagId = tags[t].Id });
            foreach (var actor in a.Actors) apex.ApexActors.Add(new ApexActor { MovieId = movie.Id, ActorId = actors[actor].Id });
            return apex;
        });
        // Added in id order so the database ids follow the case's.
        foreach (var scene in scenes.OrderBy(kv => kv.Key)) { db.Scenes.Add(scene.Value); await db.SaveChangesAsync(); }
        foreach (var highlight in highlights.OrderBy(kv => kv.Key)) { db.MovieHighlights.Add(highlight.Value); await db.SaveChangesAsync(); }
        foreach (var apex in apexes.OrderBy(kv => kv.Key)) { db.MovieApexes.Add(apex.Value); await db.SaveChangesAsync(); }

        return new SeededCase(movie.Id,
            actors.ToDictionary(kv => kv.Key, kv => kv.Value.Id),
            tags.ToDictionary(kv => kv.Key, kv => kv.Value.Id),
            scenes.ToDictionary(kv => kv.Key, kv => kv.Value.Id),
            highlights.ToDictionary(kv => kv.Key, kv => kv.Value.Id),
            apexes.ToDictionary(kv => kv.Key, kv => kv.Value.Id));
    }

    private static int[] Sorted(IEnumerable<int> ids) => ids.Distinct().OrderBy(id => id).ToArray();

    private static async Task<ILookup<int, int>> LoadAsync(IQueryable<OwnerTag> query) =>
        (await query.ToListAsync()).ToLookup(r => r.OwnerId, r => r.TagId);

    private static async Task<ILookup<int, int>> LoadAsync(IQueryable<OwnerActor> query) =>
        (await query.ToListAsync()).ToLookup(r => r.OwnerId, r => r.ActorId);

    [Theory]
    [MemberData(nameof(ClipCases.All), MemberType = typeof(ClipCases))]
    public async Task Queries_RollTagsUpAndFlowActorsDown_LikeThePureHelpers(ClipCase clipCase)
    {
        var seeded = await SeedAsync(clipCase);
        await using (var refresh = await factory.CreateDbContextAsync())
        {
            await ClipActorSync.RefreshStaleAsync(refresh);
        }
        await using var db = await factory.CreateDbContextAsync();

        var sceneTags = await LoadAsync(ClipRollupQueries.SceneTags(db));
        var highlightTags = await LoadAsync(ClipRollupQueries.HighlightTags(db));
        var sceneActors = await LoadAsync(ClipRollupQueries.SceneActors(db));
        var highlightActors = await LoadAsync(ClipRollupQueries.HighlightActors(db));
        var apexActors = await LoadAsync(ClipRollupQueries.ApexActors(db));

        foreach (var scene in clipCase.Scenes)
        {
            var own = scene.Tags.Select(t => seeded.Tags[t]);
            var expectedTags = own.Concat(clipCase.ExpectedSceneImplicitTags.GetValueOrDefault(scene.Id, []).Select(t => seeded.Tags[t]));
            Assert.Equal(Sorted(expectedTags), Sorted(sceneTags[seeded.Scenes[scene.Id]]));
            Assert.Equal(Sorted(clipCase.ExpectedSceneActors.GetValueOrDefault(scene.Id, []).Select(a => seeded.Actors[a])), Sorted(sceneActors[seeded.Scenes[scene.Id]]));
        }
        foreach (var highlight in clipCase.Highlights)
        {
            var own = highlight.Tags.Select(t => seeded.Tags[t]);
            var expectedTags = own.Concat(clipCase.ExpectedHighlightImplicitTags.GetValueOrDefault(highlight.Id, []).Select(t => seeded.Tags[t]));
            Assert.Equal(Sorted(expectedTags), Sorted(highlightTags[seeded.Highlights[highlight.Id]]));
            Assert.Equal(Sorted(clipCase.ExpectedHighlightActors.GetValueOrDefault(highlight.Id, []).Select(a => seeded.Actors[a])), Sorted(highlightActors[seeded.Highlights[highlight.Id]]));
        }
        foreach (var apex in clipCase.Apexes)
        {
            Assert.Equal(Sorted(clipCase.ExpectedApexActors.GetValueOrDefault(apex.Id, []).Select(a => seeded.Actors[a])), Sorted(apexActors[seeded.Apexes[apex.Id]]));
        }
    }
}
