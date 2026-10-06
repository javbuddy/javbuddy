using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Nfo;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.Nfo;

public class NfoHistoryServiceTests
{
    /// <summary>A local library whose single .nfo lives in memory: SaveRawNfoAsync swaps it and
    /// returns the replaced content, like the real client does through NfoFileWriter.</summary>
    private sealed class FakeNfo
    {
        public string? Content { get; set; } = "<movie>v0</movie>";

        public ILocalLibraryClient Client { get; } = Substitute.For<ILocalLibraryClient>();

        public FakeNfo()
        {
            Client.SaveRawNfoAsync("ABC-123", Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var previous = Content;
                    if (previous is not null) Content = call.ArgAt<string>(1);
                    return previous;
                });
        }
    }

    private static int SeedMovie(TestDbContextFactory factory)
    {
        using var db = factory.CreateDbContext();
        var movie = new Movie { Code = "ABC-123" };
        db.Movies.Add(movie);
        db.SaveChanges();
        return movie.Id;
    }

    [Fact]
    public async Task SaveAsync_WritesContentAndStoresReplacedVersion()
    {
        using var factory = new TestDbContextFactory();
        var movieId = SeedMovie(factory);
        var nfo = new FakeNfo();
        var service = new NfoHistoryService(factory, nfo.Client);

        Assert.True(await service.SaveAsync(movieId, "<movie>v1</movie>"));

        Assert.Equal("<movie>v1</movie>", nfo.Content);
        var generation = Assert.Single(await service.GetGenerationsAsync(movieId));
        Assert.Equal("<movie>v0</movie>", generation.Content);
        Assert.Equal(NfoWriteTrigger.ManualEdit, generation.Trigger);
    }

    [Fact]
    public async Task SaveAsync_StoresNothing_WhenContentIsUnchanged()
    {
        using var factory = new TestDbContextFactory();
        var movieId = SeedMovie(factory);
        var service = new NfoHistoryService(factory, new FakeNfo().Client);

        Assert.True(await service.SaveAsync(movieId, "<movie>v0</movie>"));

        Assert.Empty(await service.GetGenerationsAsync(movieId));
    }

    [Fact]
    public async Task SaveAsync_ReturnsFalseAndStoresNothing_WhenTheNfoCannotBeWritten()
    {
        using var factory = new TestDbContextFactory();
        var movieId = SeedMovie(factory);
        var nfo = new FakeNfo { Content = null };
        var service = new NfoHistoryService(factory, nfo.Client);

        Assert.False(await service.SaveAsync(movieId, "<movie>v1</movie>"));

        Assert.Empty(await service.GetGenerationsAsync(movieId));
    }

    [Fact]
    public async Task SaveAsync_KeepsOnlyTheNewestGenerations()
    {
        using var factory = new TestDbContextFactory();
        var movieId = SeedMovie(factory);
        var service = new NfoHistoryService(factory, new FakeNfo().Client);

        for (var i = 1; i <= NfoHistoryService.GenerationsKept + 2; i++)
        {
            Assert.True(await service.SaveAsync(movieId, $"<movie>v{i}</movie>"));
        }

        // v7 is on disk; v2–v6 are the five kept, newest first.
        Assert.Equal(
            ["<movie>v6</movie>", "<movie>v5</movie>", "<movie>v4</movie>", "<movie>v3</movie>", "<movie>v2</movie>"],
            (await service.GetGenerationsAsync(movieId)).Select(g => g.Content));
    }

    [Fact]
    public async Task RestoreAsync_WritesTheOldContentBack_AndKeepsTheReplacedVersion()
    {
        using var factory = new TestDbContextFactory();
        var movieId = SeedMovie(factory);
        var nfo = new FakeNfo();
        var service = new NfoHistoryService(factory, nfo.Client);
        await service.SaveAsync(movieId, "<movie>v1</movie>");
        var v0 = Assert.Single(await service.GetGenerationsAsync(movieId));

        Assert.Equal("<movie>v0</movie>", await service.RestoreAsync(movieId, v0.Id));

        Assert.Equal("<movie>v0</movie>", nfo.Content);
        var newest = (await service.GetGenerationsAsync(movieId))[0];
        Assert.Equal(NfoWriteTrigger.Restore, newest.Trigger);
        Assert.Equal("<movie>v1</movie>", newest.Content);
    }

    [Fact]
    public async Task RestoreAsync_ReturnsNull_ForAnotherMoviesGeneration()
    {
        using var factory = new TestDbContextFactory();
        var movieId = SeedMovie(factory);
        var nfo = new FakeNfo();
        var service = new NfoHistoryService(factory, nfo.Client);
        await service.SaveAsync(movieId, "<movie>v1</movie>");
        var generation = Assert.Single(await service.GetGenerationsAsync(movieId));

        Assert.Null(await service.RestoreAsync(movieId + 1, generation.Id));
        Assert.Equal("<movie>v1</movie>", nfo.Content);
    }

    [Fact]
    public async Task DeletingTheMovie_DeletesItsHistory()
    {
        using var factory = new TestDbContextFactory();
        var movieId = SeedMovie(factory);
        var service = new NfoHistoryService(factory, new FakeNfo().Client);
        await service.SaveAsync(movieId, "<movie>v1</movie>");

        using (var db = factory.CreateDbContext())
        {
            db.Movies.Remove(db.Movies.Single(m => m.Id == movieId));
            db.SaveChanges();
        }

        Assert.Empty(await service.GetGenerationsAsync(movieId));
    }
}
