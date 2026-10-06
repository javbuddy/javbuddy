using Javbuddy.Models;
using Javbuddy.Services.ActorEnrichment;
using Javbuddy.Services.ActorEnrichment.Sources;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.ActorEnrichment;

public class LocalActorMetadataSourceTests
{
    [Fact]
    public async Task LookupAsync_DiscoversKanjiKanaAliasesAndThumbnail_FromMovieNfo()
    {
        using var factory = new TestDbContextFactory();
        var localLibraryClient = Substitute.For<ILocalLibraryClient>();

        localLibraryClient.GetMovieActorsAsync("ABP-123", Arg.Any<CancellationToken>())
            .Returns([
                new LocalActorMetadata
                {
                    Name = "Mikami Yua",
                    AltName = "三上悠亜",
                    Aliases = ["みかみ ゆあ", "Yuachan"],
                    Thumb = "https://example.com/thumb.jpg"
                }
            ]);

        localLibraryClient.ResolveActorImagePathAsync("ABP-123", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var source = new LocalActorMetadataSource(localLibraryClient, factory);

        var context = new ActorEnrichmentContext(
            ActorId: 1,
            DisplayName: "Mikami Yua",
            FirstName: "Yua",
            LastName: "Mikami",
            JapaneseNameKanji: null,
            JapaneseNameKana: null,
            ExistingAliases: [],
            LinkedMovieCodes: ["ABP-123"]);

        var result = await source.LookupAsync(context);

        Assert.NotNull(result);
        Assert.Equal("Local", result.SourceName);
        Assert.Equal("三上悠亜", result.JapaneseNameKanji);
        Assert.Equal("みかみ ゆあ", result.JapaneseNameKana);
        Assert.Contains("Yuachan", result.Aliases);
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenActorNotFoundInMovieNfo()
    {
        using var factory = new TestDbContextFactory();
        var localLibraryClient = Substitute.For<ILocalLibraryClient>();

        localLibraryClient.GetMovieActorsAsync("ABP-123", Arg.Any<CancellationToken>())
            .Returns([
                new LocalActorMetadata
                {
                    Name = "Some Other Actress",
                    AltName = "別の人"
                }
            ]);

        localLibraryClient.ResolveActorImagePathAsync("ABP-123", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var source = new LocalActorMetadataSource(localLibraryClient, factory);

        var context = new ActorEnrichmentContext(
            ActorId: 1,
            DisplayName: "Mikami Yua",
            FirstName: "Yua",
            LastName: "Mikami",
            JapaneseNameKanji: null,
            JapaneseNameKana: null,
            ExistingAliases: [],
            LinkedMovieCodes: ["ABP-123"]);

        var result = await source.LookupAsync(context);

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupAsync_FindsCandidateMoviesInDb_WhenLinkedMovieCodesIsEmpty()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "SSNI-001", MetaActresses = "Mikami Yua, Other Actress" });
            await db.SaveChangesAsync();
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.GetMovieActorsAsync("SSNI-001", Arg.Any<CancellationToken>())
            .Returns([
                new LocalActorMetadata
                {
                    Name = "Mikami Yua",
                    AltName = "三上悠亜"
                }
            ]);

        localLibraryClient.ResolveActorImagePathAsync("SSNI-001", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var source = new LocalActorMetadataSource(localLibraryClient, factory);

        var context = new ActorEnrichmentContext(
            ActorId: 1,
            DisplayName: "Mikami Yua",
            FirstName: "Yua",
            LastName: "Mikami",
            JapaneseNameKanji: null,
            JapaneseNameKana: null,
            ExistingAliases: [],
            LinkedMovieCodes: []); // Empty linked codes

        var result = await source.LookupAsync(context);

        Assert.NotNull(result);
        Assert.Equal("三上悠亜", result.JapaneseNameKanji);
    }

    [Fact]
    public async Task LookupAsync_WithRunCache_ReadsLibraryCastOnceAndStillMatchesEachActor()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "SSNI-001", MetaActresses = "Mikami Yua" },
                new Movie { Code = "IPX-002", MetaActresses = "Momo Sakura" });
            await db.SaveChangesAsync();
        }

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        localLibraryClient.GetMovieActorsAsync("SSNI-001", Arg.Any<CancellationToken>())
            .Returns([new LocalActorMetadata { Name = "Mikami Yua", AltName = "三上悠亜" }]);
        localLibraryClient.GetMovieActorsAsync("IPX-002", Arg.Any<CancellationToken>())
            .Returns([new LocalActorMetadata { Name = "Momo Sakura", AltName = "桜空もも" }]);
        localLibraryClient.ResolveActorImagePathAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var source = new LocalActorMetadataSource(localLibraryClient, factory);
        var runCache = new ActorEnrichmentRunCache();

        var first = await source.LookupAsync(UnlinkedContext(1, "Mikami Yua", "Yua", "Mikami") with { RunCache = runCache });
        var libraryCast = runCache.LibraryCast;
        var second = await source.LookupAsync(UnlinkedContext(2, "Momo Sakura", "Sakura", "Momo") with { RunCache = runCache });

        Assert.NotNull(libraryCast);
        Assert.Same(libraryCast, runCache.LibraryCast);
        Assert.Equal("三上悠亜", first?.JapaneseNameKanji);
        Assert.Equal("桜空もも", second?.JapaneseNameKanji);
    }

    private static ActorEnrichmentContext UnlinkedContext(int id, string displayName, string firstName, string lastName) =>
        new(
            ActorId: id,
            DisplayName: displayName,
            FirstName: firstName,
            LastName: lastName,
            JapaneseNameKanji: null,
            JapaneseNameKana: null,
            ExistingAliases: [],
            LinkedMovieCodes: []);
}
