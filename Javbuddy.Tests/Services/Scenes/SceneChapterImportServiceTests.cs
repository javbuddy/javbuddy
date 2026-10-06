using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Scenes;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Scenes;

public sealed class SceneChapterImportServiceTests : IDisposable
{
    private readonly TestDbContextFactory factory = new();
    private readonly ILocalLibraryClient localLibrary = Substitute.For<ILocalLibraryClient>();
    private readonly IFfmpegClient ffmpeg = Substitute.For<IFfmpegClient>();
    private readonly string folder = Path.Combine(Path.GetTempPath(), "javbuddy-tests-" + Guid.NewGuid().ToString("N"));

    public SceneChapterImportServiceTests()
    {
        Directory.CreateDirectory(folder);
        localLibrary.ResolveMovieFolderPathAsync("DEVR-041", Arg.Any<CancellationToken>()).Returns(folder);
    }

    public void Dispose()
    {
        factory.Dispose();
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private SceneChapterImportService CreateService() => new(factory, localLibrary, ffmpeg);

    private async Task<int> SeedMovieAsync(double? duration = 10, params (string Name, bool Primary)[] files)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = new Movie { Code = "DEVR-041", MediaDurationSeconds = duration };
        foreach (var (name, primary) in files)
        {
            movie.MovieFiles.Add(new MovieFile { FileName = name, IsPrimary = primary });
        }
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, "");
        return path;
    }

    private void ProbeReturns(string path, params FfprobeChapterInfo[] chapters) =>
        ffmpeg.ProbeAsync(path, Arg.Any<CancellationToken>())
            .Returns(new FfprobeMediaInfo { DurationSeconds = 10, ChapterCount = chapters.Length, Chapters = chapters });

    private async Task<List<Scene>> StoredScenesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Scenes.OrderBy(s => s.StartSeconds).ToListAsync();
    }

    [Fact]
    public void ToSceneRanges_ContiguousChaptersAndLastAtEnd_GetNullEnds()
    {
        var ranges = SceneChapterImportService.ToSceneRanges(
            [new(4, 7.5, "B"), new(0, 4.02, "A"), new(7.5, 9.6, null)],
            durationSeconds: 10);

        Assert.Equal(
            [new ImportedSceneRange(0, null, "A"), new ImportedSceneRange(4, null, "B"), new ImportedSceneRange(7.5, null, null)],
            ranges);
    }

    [Fact]
    public void ToSceneRanges_KeepsGapsAndEarlyEnds_ClampsToDuration_DropsEmptyChapters()
    {
        var ranges = SceneChapterImportService.ToSceneRanges(
            [new(0, 3, "Intro"), new(5, 5, "Empty"), new(5, 7, "Middle"), new(8, 30, "Late")],
            durationSeconds: 20);

        Assert.Equal(
            [new ImportedSceneRange(0, 3, "Intro"), new ImportedSceneRange(5, 7, "Middle"), new ImportedSceneRange(8, null, "Late")],
            ranges);
    }

    [Fact]
    public void ToSceneRanges_UnknownDuration_KeepsLastEnd_AndTruncatesLongTitles()
    {
        var ranges = SceneChapterImportService.ToSceneRanges([new(0, 60, new string('x', 250))], durationSeconds: null);

        var range = Assert.Single(ranges);
        Assert.Equal(60d, range.EndSeconds);
        Assert.Equal(200, range.Title!.Length);
    }

    [Fact]
    public async Task GetFileChapters_ProbesThePrimaryVersion()
    {
        var movieId = await SeedMovieAsync(10, ("DEVR-041-RIFE.mp4", false), ("DEVR-041.mp4", true));
        CreateFile("DEVR-041-RIFE.mp4");
        ProbeReturns(CreateFile("DEVR-041.mp4"), new FfprobeChapterInfo(0, 10, "DEVR-041-A"));

        var found = await CreateService().GetFileChaptersAsync(movieId);

        Assert.Equal("DEVR-041.mp4", found!.FileName);
        Assert.Equal("DEVR-041-A", Assert.Single(found.Chapters).Title);
    }

    [Fact]
    public async Task GetFileChapters_FallsBackToTheFirstVideoInTheFolder()
    {
        var movieId = await SeedMovieAsync();
        ProbeReturns(CreateFile("devr-041.mkv"));

        var found = await CreateService().GetFileChaptersAsync(movieId);

        Assert.Equal("devr-041.mkv", found!.FileName);
        Assert.Empty(found.Chapters);
    }

    [Fact]
    public async Task GetFileChapters_MissingFolder_ReturnsNull()
    {
        var movieId = await SeedMovieAsync();
        localLibrary.ResolveMovieFolderPathAsync("DEVR-041", Arg.Any<CancellationToken>()).Returns((string?)null);

        Assert.Null(await CreateService().GetFileChaptersAsync(movieId));
    }

    [Fact]
    public async Task Import_CreatesOneScenePerChapter()
    {
        var movieId = await SeedMovieAsync(10, ("DEVR-041.mp4", true));
        ProbeReturns(CreateFile("DEVR-041.mp4"),
            new(0, 4, "DEVR-041-A"), new(4, 7.5, "DEVR-041-B"), new(7.5, 10, "DEVR-041-C"));

        var result = await CreateService().ImportFileChaptersAsync(movieId);

        Assert.True(result.Success);
        Assert.Equal(3, result.ImportedCount);
        var scenes = await StoredScenesAsync();
        Assert.Equal(["DEVR-041-A", "DEVR-041-B", "DEVR-041-C"], scenes.Select(s => s.Title));
        Assert.Equal([0d, 4d, 7.5d], scenes.Select(s => s.StartSeconds));
        Assert.All(scenes, s => Assert.Null(s.EndSeconds));
    }

    [Fact]
    public async Task Import_LeavesEverySceneInheritingTheCast()
    {
        var movieId = await SeedMovieAsync(10, ("DEVR-041.mp4", true));
        await using (var db = await factory.CreateDbContextAsync())
        {
            var yua = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var aika = new Actor { FirstName = "Aika" };
            db.Actors.AddRange(yua, aika);
            await db.SaveChangesAsync();
            db.MovieActors.AddRange(new MovieActor { MovieId = movieId, ActorId = yua.Id }, new MovieActor { MovieId = movieId, ActorId = aika.Id });
            await db.SaveChangesAsync();
        }
        ProbeReturns(CreateFile("DEVR-041.mp4"), new(0, 4, "DEVR-041-A"), new(4, 10, "DEVR-041-B"));

        Assert.True((await CreateService().ImportFileChaptersAsync(movieId)).Success);

        await using var check = await factory.CreateDbContextAsync();
        // No actors of their own: they inherit the cast.
        var actorCounts = await check.Scenes.OrderBy(s => s.StartSeconds).Select(s => s.SceneActors.Count).ToListAsync();
        Assert.Equal([0, 0], actorCounts);
        var scenes = await new MovieSceneService(factory).GetScenesAsync(movieId);
        Assert.All(scenes, s => Assert.Equal(["Aika", "Mikami Yua"], s.EffectiveActors.Actors.Select(a => a.Name)));
    }

    [Fact]
    public async Task Import_RefusedWhenTheMovieAlreadyHasScenes()
    {
        var movieId = await SeedMovieAsync(10, ("DEVR-041.mp4", true));
        ProbeReturns(CreateFile("DEVR-041.mp4"), new FfprobeChapterInfo(0, 10, "A"));
        await new MovieSceneService(factory).AddSceneAsync(movieId, 2, null, "Mine");

        var result = await CreateService().ImportFileChaptersAsync(movieId);

        Assert.Equal("This movie already has scenes.", result.ErrorMessage);
        Assert.Equal("Mine", Assert.Single(await StoredScenesAsync()).Title);
    }

    [Fact]
    public async Task Import_OverlappingChapters_ImportsNothing()
    {
        var movieId = await SeedMovieAsync(10, ("DEVR-041.mp4", true));
        ProbeReturns(CreateFile("DEVR-041.mp4"), new(0, 6, "A"), new(4, 10, "B"));

        var result = await CreateService().ImportFileChaptersAsync(movieId);

        Assert.False(result.Success);
        Assert.Equal("Chapter 2 can't be imported: Overlaps \"A\".", result.ErrorMessage);
        Assert.Empty(await StoredScenesAsync());
    }

    [Fact]
    public async Task Import_NoChaptersOrNoFile_Fails()
    {
        var movieId = await SeedMovieAsync(10, ("DEVR-041.mp4", true));
        var service = CreateService();

        Assert.Equal("The movie's video file couldn't be found or read.", (await service.ImportFileChaptersAsync(movieId)).ErrorMessage);

        ProbeReturns(CreateFile("DEVR-041.mp4"));
        Assert.Equal("The video file has no chapters.", (await service.ImportFileChaptersAsync(movieId)).ErrorMessage);
    }
}
