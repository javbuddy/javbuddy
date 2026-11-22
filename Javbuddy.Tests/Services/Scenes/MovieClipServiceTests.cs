using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Scenes;

public class MovieClipServiceTests
{
    private static string Show(SceneItem i) => $"{i.Id} {i.DisplayTitle} {string.Join(",", i.EffectiveActors.Actors.Select(a => a.Name))}";

    private static string Show(HighlightItem i) => $"{i.Id} {i.DisplayTitle} {string.Join(",", i.EffectiveActors.Actors.Select(a => a.Name))}";

    private static string Show(ApexItem i) => $"{i.Id} {i.DisplayLabel} {string.Join(",", i.EffectiveActors.Actors.Select(a => a.Name))}";

    [Fact]
    public async Task GetClipsAsync_ReturnsWhatTheThreeServicesReturn()
    {
        using var factory = new TestDbContextFactory();
        var scenes = new MovieSceneService(factory);
        var highlights = new MovieHighlightService(factory);
        var apexes = new MovieApexService(factory);
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var aika = new Actor { FirstName = "Aika" };
            var movie = new Movie { Code = "CLP-001", MediaDurationSeconds = 600 };
            db.AddRange(aika, movie);
            await db.SaveChangesAsync();
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = aika.Id });
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }
        await scenes.AddSceneAsync(movieId, 0, 100, "Intro");
        await highlights.AddHighlightAsync(movieId, 10, 20, null);
        await apexes.AddApexAsync(movieId, 15, []);
        var service = new MovieClipService(factory, scenes, highlights, apexes);

        var clips = await service.GetClipsAsync(movieId);

        // The items hold lists, so they are compared by what they show.
        Assert.Equal((await scenes.GetScenesAsync(movieId)).Select(Show), clips.Scenes.Select(Show));
        Assert.Equal((await highlights.GetHighlightsAsync(movieId)).Select(Show), clips.Highlights.Select(Show));
        Assert.Equal((await apexes.GetApexesAsync(movieId)).Select(Show), clips.Apexes.Select(Show));
        Assert.Equal("Aika", Assert.Single(clips.Highlights[0].EffectiveActors.Actors).Name);
        Assert.Single(clips.Scenes);
        Assert.Single(clips.Highlights);
        Assert.Single(clips.Apexes);
    }

    [Fact]
    public async Task GetClipsAsync_AMovieWithNoClips_IsEmptyNotMissing()
    {
        using var factory = new TestDbContextFactory();
        var service = new MovieClipService(factory, new MovieSceneService(factory), new MovieHighlightService(factory), new MovieApexService(factory));

        var clips = await service.GetClipsAsync(12345);

        Assert.Empty(clips.Scenes);
        Assert.Empty(clips.Highlights);
        Assert.Empty(clips.Apexes);
    }
}
