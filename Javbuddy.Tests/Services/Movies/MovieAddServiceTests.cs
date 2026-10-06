using Javbuddy.Models;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.MediaServer;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Movies;

public class MovieAddServiceTests
{
    private static MovieAddService CreateService(
        TestDbContextFactory factory,
        IJellyfinClient? jellyfinClient = null,
        ILocalLibraryClient? localLibraryClient = null,
        IJavinizerClient? javinizerClient = null)
    {
        if (jellyfinClient is null)
        {
            jellyfinClient = Substitute.For<IJellyfinClient>();
            jellyfinClient.LookupInSelectedLibrariesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new MediaServerLookupResult(true, [], null));
        }

        if (localLibraryClient is null)
        {
            localLibraryClient = Substitute.For<ILocalLibraryClient>();
            localLibraryClient.TryGetMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new LocalLookupResult(false, null, null, null));
        }

        if (javinizerClient is null)
        {
            javinizerClient = Substitute.For<IJavinizerClient>();
            javinizerClient.ScrapeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new JavinizerScrapeResult(false, null, "not configured"));
        }

        return new MovieAddService(factory, jellyfinClient, localLibraryClient, javinizerClient, new MovieChangeNotifier());
    }

    [Fact]
    public async Task AddAsync_CreatesAMissingMovie_WithTheGivenCode()
    {
        using var factory = new TestDbContextFactory();

        var result = await CreateService(factory).AddAsync("ABC-123");

        Assert.Equal("ABC-123", result.Movie.Code);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.Movies.AnyAsync(m => m.Code == "ABC-123"));
    }

    [Fact]
    public async Task AddAsync_ClearsAllMatchingCanonicalHistoryOnly()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            foreach (var code in new[] { "ABC-00123", "ABC-123", "XYZ-999" })
                await DeletedMovieHistory.RecordAsync(db, new Movie { Code = code });
            await db.SaveChangesAsync();
        }

        await CreateService(factory).AddAsync("abc123");

        await using var readDb = await factory.CreateDbContextAsync();
        Assert.Equal("XYZ-999", (await readDb.DeletedMovies.SingleAsync()).Code);
        Assert.Equal("abc123", (await readDb.Movies.SingleAsync()).Code);
    }

    [Fact]
    public async Task AddAsync_FailedInsertKeepsMatchingHistory()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "ABC-123" });
            await DeletedMovieHistory.RecordAsync(db, new Movie { Code = "ABC-123" });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => CreateService(factory).AddAsync("ABC-123"));

        await using var readDb = await factory.CreateDbContextAsync();
        Assert.Equal("ABC-123", (await readDb.DeletedMovies.SingleAsync()).Code);
        Assert.Single(await readDb.Movies.ToListAsync());
    }

    [Fact]
    public async Task AddAsync_JellyfinMatchFound_MarksMovieGotAndAppliesTheMatch()
    {
        using var factory = new TestDbContextFactory();
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.LookupInSelectedLibrariesAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new MediaServerLookupResult(true, [new JellyfinItemDto { Id = "item-1", ServerId = "server-1" }], null));

        var result = await CreateService(factory, jellyfinClient: jellyfinClient).AddAsync("ABC-123");

        Assert.True(result.OwnedViaJellyfin);

        await using var db = await factory.CreateDbContextAsync();
        var movie = await db.Movies.SingleAsync();
        Assert.Equal(MovieStatus.Got, movie.Status);
        Assert.Equal("item-1", movie.JellyfinItemId);
    }

    [Fact]
    public async Task AddAsync_NoJellyfinMatch_LocalMetadataFound_AppliesLocalMetadata()
    {
        using var factory = new TestDbContextFactory();
        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.TryGetMetadataAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new LocalLookupResult(true, "/media/ABC-123", new LocalMovieMetadata { Title = "Local Title" }, null));

        var result = await CreateService(factory, localLibraryClient: localLibraryClient).AddAsync("ABC-123");

        Assert.True(result.MetadataFound);
        Assert.False(result.OwnedViaJellyfin);

        await using var db = await factory.CreateDbContextAsync();
        var movie = await db.Movies.SingleAsync();
        Assert.Equal("Local Title", movie.MetaTitle);
        Assert.Equal("Local", movie.MetaSourceName);
    }

    [Fact]
    public async Task AddAsync_NoLocalMetadata_FallsBackToJavinizerScrape()
    {
        using var factory = new TestDbContextFactory();
        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.TryGetMetadataAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new LocalLookupResult(false, null, null, null));

        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.ScrapeAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new JavinizerScrapeResult(true, new MovieViewDto { Title = "Scraped Title", SourceName = "javinizer-go" }, null));

        var result = await CreateService(factory, localLibraryClient: localLibraryClient, javinizerClient: javinizerClient)
            .AddAsync("ABC-123");

        Assert.True(result.MetadataFound);

        await using var db = await factory.CreateDbContextAsync();
        var movie = await db.Movies.SingleAsync();
        Assert.Equal("Scraped Title", movie.MetaTitle);
        Assert.Equal("javinizer-go", movie.MetaSourceName);
    }

    [Fact]
    public async Task AddAsync_NoLocalMetadataAndScrapeFails_ReturnsMetadataNotFoundWithError()
    {
        using var factory = new TestDbContextFactory();
        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.TryGetMetadataAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new LocalLookupResult(false, null, null, null));

        var javinizerClient = Substitute.For<IJavinizerClient>();
        javinizerClient.ScrapeAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new JavinizerScrapeResult(false, null, "javinizer-go is not configured."));

        var result = await CreateService(factory, localLibraryClient: localLibraryClient, javinizerClient: javinizerClient)
            .AddAsync("ABC-123");

        Assert.False(result.MetadataFound);
        Assert.Equal("javinizer-go is not configured.", result.MetadataError);
    }
}
