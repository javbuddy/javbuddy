using Javbuddy.Models;
using Javbuddy.Services.R18Dev;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.R18Dev;

public class ActorFilmographyServiceTests
{
    [Fact]
    public async Task GetFilmographyAsync_DeletedMovieCanonicalVariant_IsPreviouslyDeleted()
    {
        using var factory = new TestDbContextFactory();
        var actorId = SeedActor(factory);
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "MIDE-001" };
            await Javbuddy.Services.Movies.DeletedMovieHistory.RecordAsync(db, movie);
            await db.SaveChangesAsync();
        }
        var service = new ActorFilmographyService(factory, StoreReturning(true, Entry("MIDE-00001"), Entry("MIDE-002")));
        var result = await service.GetFilmographyAsync(actorId, "Test Actress", null);
        Assert.True(result.Rows[0].PreviouslyDeleted);
        Assert.False(result.Rows[0].InLibrary);
        Assert.Equal("missing", result.Rows[1].StatusClass);
    }

    private static R18DevFilmographyEntry Entry(string dvdId) => new(dvdId, "Title", null, null, null);

    private static int SeedActor(TestDbContextFactory factory, params Movie[] linkedMovies)
    {
        using var db = factory.CreateDbContext();
        var actor = new Actor { FirstName = "Actress", LastName = "Test" };
        db.Actors.Add(actor);
        foreach (var movie in linkedMovies)
        {
            db.MovieActors.Add(new MovieActor { Movie = movie, Actor = actor });
        }
        db.SaveChanges();
        return actor.Id;
    }

    private static void SeedMovies(TestDbContextFactory factory, params Movie[] movies)
    {
        using var db = factory.CreateDbContext();
        db.Movies.AddRange(movies);
        db.SaveChanges();
    }

    private static IR18DevDumpStore StoreReturning(bool dumpAvailable, params R18DevFilmographyEntry[] entries)
    {
        var store = Substitute.For<IR18DevDumpStore>();
        store.GetFilmographyForActorAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new R18DevFilmographyResult(dumpAvailable, entries));
        return store;
    }

    [Fact]
    public async Task GetFilmographyAsync_PassesLinkedCodesAndNamesToTheDumpAndCountsLinkedMovies()
    {
        using var factory = new TestDbContextFactory();
        var actorId = SeedActor(factory,
            new Movie { Code = "MIDE-001", Status = MovieStatus.Got },
            new Movie { Code = "MIDE-002", Status = MovieStatus.Missing });
        SeedMovies(factory, new Movie { Code = "OTHER-001", Status = MovieStatus.Got });
        var store = StoreReturning(true);
        var service = new ActorFilmographyService(factory, store);

        var result = await service.GetFilmographyAsync(actorId, "Test Actress", "Actress Test");

        Assert.Equal(2, result.LinkedMovieCount);
        await store.Received(1).GetFilmographyForActorAsync(
            Arg.Is<IReadOnlyCollection<string>>(codes => codes.Count == 2 && codes.Contains("MIDE-001") && codes.Contains("MIDE-002")),
            "Test Actress",
            "Actress Test",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetFilmographyAsync_ClassifiesStatusesAgainstTheWholeLibrary()
    {
        using var factory = new TestDbContextFactory();
        var actorId = SeedActor(factory);
        SeedMovies(factory,
            new Movie { Code = "MIDE-001", Status = MovieStatus.Got },
            new Movie { Code = "MIDE-002", Status = MovieStatus.Missing });
        var service = new ActorFilmographyService(factory, StoreReturning(true, Entry("MIDE-001"), Entry("MIDE-002"), Entry("MIDE-003")));

        var result = await service.GetFilmographyAsync(actorId, "Test Actress", null);

        Assert.True(result.DumpAvailable);
        Assert.Equal(["got", "wanted", "missing"], result.Rows.Select(r => r.StatusClass));
        Assert.Equal("/movies/MIDE-001", result.Rows[0].LinkUrl);
    }

    [Fact]
    public async Task GetFilmographyAsync_ExactCodeMatchWinsOverCanonicalMatch()
    {
        using var factory = new TestDbContextFactory();
        var actorId = SeedActor(factory);
        SeedMovies(factory,
            new Movie { Code = "MIDE-001", Status = MovieStatus.Got },
            new Movie { Code = "MIDE-00001", Status = MovieStatus.Missing });
        var service = new ActorFilmographyService(factory, StoreReturning(true, Entry("MIDE-00001")));

        var result = await service.GetFilmographyAsync(actorId, "Test Actress", null);

        var row = Assert.Single(result.Rows);
        Assert.Equal("wanted", row.StatusClass);
        Assert.Equal("/movies/MIDE-00001", row.LinkUrl);
    }

    [Fact]
    public async Task GetFilmographyAsync_CanonicalMatchUsedWhenNoExactCode()
    {
        using var factory = new TestDbContextFactory();
        var actorId = SeedActor(factory);
        SeedMovies(factory, new Movie { Code = "MIDE-001", Status = MovieStatus.Got });
        var service = new ActorFilmographyService(factory, StoreReturning(true, Entry("MIDE-00001")));

        var result = await service.GetFilmographyAsync(actorId, "Test Actress", null);

        var row = Assert.Single(result.Rows);
        Assert.Equal("got", row.StatusClass);
        Assert.Equal("/movies/MIDE-001", row.LinkUrl);
    }

    [Fact]
    public async Task GetFilmographyAsync_DumpUnavailable_ReturnsNoRowsButKeepsLinkedCount()
    {
        using var factory = new TestDbContextFactory();
        var actorId = SeedActor(factory, new Movie { Code = "MIDE-001", Status = MovieStatus.Got });
        var service = new ActorFilmographyService(factory, StoreReturning(false));

        var result = await service.GetFilmographyAsync(actorId, "Test Actress", null);

        Assert.False(result.DumpAvailable);
        Assert.Empty(result.Rows);
        Assert.Equal(1, result.LinkedMovieCount);
    }

    [Fact]
    public async Task SaveR18DevNameAsync_TrimsAndPersists()
    {
        using var factory = new TestDbContextFactory();
        var actorId = SeedActor(factory);
        var service = new ActorFilmographyService(factory, Substitute.For<IR18DevDumpStore>());

        var stored = await service.SaveR18DevNameAsync(actorId, "  Matsumoto Ichika  ");

        Assert.Equal("Matsumoto Ichika", stored);
        using var db = factory.CreateDbContext();
        Assert.Equal("Matsumoto Ichika", db.Actors.Single().R18DevName);
    }

    [Fact]
    public async Task SaveR18DevNameAsync_BlankClearsTheOverride()
    {
        using var factory = new TestDbContextFactory();
        var actorId = SeedActor(factory);
        var service = new ActorFilmographyService(factory, Substitute.For<IR18DevDumpStore>());
        await service.SaveR18DevNameAsync(actorId, "Matsumoto Ichika");

        var stored = await service.SaveR18DevNameAsync(actorId, "   ");

        Assert.Null(stored);
        using var db = factory.CreateDbContext();
        Assert.Null(db.Actors.Single().R18DevName);
    }
}
