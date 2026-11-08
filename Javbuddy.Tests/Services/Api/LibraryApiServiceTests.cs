using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Api;
using Javbuddy.Services.Api.Contracts;
using Javbuddy.Services.Common;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Settings;
using Javbuddy.Services.Tags;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.Api;

public class LibraryApiServiceTests
{
    private sealed class Fixture : IDisposable
    {
        public TestDbContextFactory Factory { get; } = new();
        public IMovieAddService AddService { get; } = Substitute.For<IMovieAddService>();
        public ITagService Tags { get; } = Substitute.For<ITagService>();
        public IActorService Actors { get; } = Substitute.For<IActorService>();
        public INfoSyncService NfoSync { get; } = Substitute.For<INfoSyncService>();
        public IMovieService MovieService { get; }
        public LibraryApiService Service { get; }

        public Fixture(Func<TestDbContextFactory, IMovieService>? createMovieService = null)
        {
            MovieService = createMovieService?.Invoke(Factory) ?? Substitute.For<IMovieService>();
            Service = new LibraryApiService(
                Factory,
                new MovieGridQueryService(Factory),
                new MovieDetailQueryService(Factory),
                AddService,
                MovieService,
                Tags,
                Actors,
                NfoSync,
                Substitute.For<IConnectionStatusService>());
        }

        public async Task SeedAsync(params string[] codes)
        {
            await using var db = await Factory.CreateDbContextAsync();
            foreach (var code in codes) db.Movies.Add(new Movie { Code = code });
            await db.SaveChangesAsync();
        }

        public void Dispose() => Factory.Dispose();
    }

    // Verified behavior of the resolution rules against CodeNormalization.GetZeroPaddingToleranceKey.
    [Theory]
    [InlineData("ABP-123", "ABP-123", "ABP-123")]      // exact
    [InlineData("abp-123", "ABP-123", "ABP-123")]      // exact, case-insensitive
    [InlineData("ABP123", "ABP-123", "ABP-123")]       // no hyphen
    [InlineData("abp00123", "ABP-123", "ABP-123")]     // zero-padded, no hyphen
    [InlineData("ABP-00123", "ABP-123", "ABP-123")]    // zero-padded
    [InlineData("ABP-123", "ABP-00123", "ABP-00123")]  // the stored code is the padded one
    [InlineData("  abp123 ", "ABP-123", "ABP-123")]    // surrounding whitespace
    public async Task ResolveAsync_FindsTheMovie_ByExactThenZeroPaddingTolerantMatch(string query, string stored, string expected)
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync(stored, "ABP-124", "XYZ-123");

        var movie = await fixture.Service.ResolveAsync(query);

        Assert.Equal(expected, movie?.Code);
    }

    [Theory]
    [InlineData("ABP-125")]
    [InlineData("ABP-1230")]
    [InlineData("ABD-123")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    public async Task ResolveAsync_UnknownOrUnusableCode_ReturnsNull(string query)
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync("ABP-123");

        Assert.Null(await fixture.Service.ResolveAsync(query));
    }

    [Fact]
    public async Task ResolveAsync_ExactMatchBeatsATolerantOne()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync("ABP-00123", "ABP-123");

        Assert.Equal("ABP-00123", (await fixture.Service.ResolveAsync("ABP-00123"))!.Code);
        Assert.Equal("ABP-123", (await fixture.Service.ResolveAsync("abp123"))!.Code);
    }

    [Fact]
    public async Task ResolveAsync_SeveralTolerantMatches_PicksTheShortestCode()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync("ABP-0123", "ABP-00123");

        Assert.Equal("ABP-0123", (await fixture.Service.ResolveAsync("abp123"))!.Code);
    }

    [Fact]
    public async Task LookupAsync_ResolvesABatch_KeepingTheQueriesAsGiven()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync("ABP-123", "SSIS-001");

        var result = await fixture.Service.LookupAsync(["abp00123", "SSIS-001", "NOPE-9", " ", "abp123"]);

        Assert.True(result.IsOk);
        var rows = result.Value!;
        Assert.Equal(5, rows.Count);
        Assert.Equal(["abp00123", "SSIS-001", "NOPE-9", " ", "abp123"], rows.Select(r => r.Query));
        Assert.Equal([true, true, false, false, true], rows.Select(r => r.InLibrary));
        Assert.Equal("ABP-123", rows[0].Code);
        Assert.Equal("/movies/ABP-123", rows[0].Movie!.WebPath);
        Assert.Null(rows[2].Movie);
    }

    [Fact]
    public async Task LookupAsync_MoreThan100Codes_OrNone_IsInvalid()
    {
        using var fixture = new Fixture();

        Assert.Equal(ApiStatus.Invalid, (await fixture.Service.LookupAsync([])).Status);
        Assert.Equal(ApiStatus.Invalid, (await fixture.Service.LookupAsync(Enumerable.Range(1, 101).Select(i => $"A-{i}").ToList())).Status);
        Assert.True((await fixture.Service.LookupAsync(Enumerable.Range(1, 100).Select(i => $"A-{i}").ToList())).IsOk);
    }

    [Fact]
    public async Task ListAsync_FiltersByStatusAndText_AndPages()
    {
        using var fixture = new Fixture();
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Got });
            db.Movies.Add(new Movie { Code = "AAA-002", Status = MovieStatus.Missing });
            db.Movies.Add(new Movie { Code = "BBB-001", Status = MovieStatus.Missing });
            await db.SaveChangesAsync();
        }

        var missing = await fixture.Service.ListAsync(new MovieListQuery(Status: "missing"));
        var text = await fixture.Service.ListAsync(new MovieListQuery(Text: "aaa", Take: 1));

        Assert.Equal(2, missing.Value!.Total);
        Assert.All(missing.Value.Items, m => Assert.Equal("missing", m.Status));
        Assert.Equal(2, text.Value!.Total);
        Assert.Single(text.Value.Items);
    }

    [Fact]
    public async Task ListAsync_BadStatusOrDrift_IsInvalid()
    {
        using var fixture = new Fixture();

        var result = await fixture.Service.ListAsync(new MovieListQuery(Status: "owned", NfoDrift: "weird"));

        Assert.Equal(ApiStatus.Invalid, result.Status);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public async Task GetDetailAsync_ResolvesAndMapsTagsAndFiles()
    {
        using var fixture = new Fixture();
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABP-123", MetaTitle = "T" };
            movie.MovieFiles.Add(new MovieFile { FileName = "abp-123.mp4", VersionTag = "Original", IsPrimary = true });
            movie.MovieTags.Add(new MovieTag { Tag = new Tag { Name = "Drama" }, IsExplicit = true });
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }

        var result = await fixture.Service.GetDetailAsync("abp123");

        Assert.True(result.IsOk);
        Assert.Equal("ABP-123", result.Value!.Code);
        Assert.Equal("Drama", Assert.Single(result.Value.Tags).Name);
        Assert.Single(result.Value.Files);
    }

    [Fact]
    public async Task GetDetailAsync_UnknownCode_IsNotFound()
    {
        using var fixture = new Fixture();

        Assert.Equal(ApiStatus.NotFound, (await fixture.Service.GetDetailAsync("NOPE-1")).Status);
    }

    [Fact]
    public async Task AddAsync_ExistingCode_IsConflict_CarryingTheExistingMovie_AndDoesNotAdd()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync("ABP-123");

        var result = await fixture.Service.AddAsync("abp00123");

        Assert.Equal(ApiStatus.Conflict, result.Status);
        Assert.Equal("ABP-123", result.Value!.Movie.Code);
        await fixture.AddService.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABP 123")]
    [InlineData("../etc")]
    public async Task AddAsync_BlankOrInvalidCode_IsInvalid(string code)
    {
        using var fixture = new Fixture();

        Assert.Equal(ApiStatus.Invalid, (await fixture.Service.AddAsync(code)).Status);
        await fixture.AddService.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task AddAsync_NewCode_AddsTheTrimmedCode_AndReturnsTheOutcome()
    {
        using var fixture = new Fixture();
        fixture.AddService.AddAsync("NEW-001", Arg.Any<CancellationToken>())
            .Returns(new MovieAddResult(new Movie { Id = 9, Code = "NEW-001" }, OwnedViaJellyfin: true, MetadataFound: false, MetadataError: "nope"));

        var result = await fixture.Service.AddAsync(" NEW-001 ");

        Assert.True(result.IsOk);
        Assert.Equal("NEW-001", result.Value!.Movie.Code);
        Assert.True(result.Value.OwnedViaJellyfin);
        Assert.False(result.Value.MetadataFound);
        Assert.Equal("nope", result.Value.MetadataError);
    }

    [Fact]
    public async Task PatchMetadataAsync_RoundTripsThroughARealMovieService()
    {
        // The facade and a real MovieService share one in-memory database.
        using var fixture = new Fixture(factory => new MovieService(factory, nfoSyncService: Substitute.For<INfoSyncService>()));
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "ABP-123", MetaTitle = "Keep", MetaStudio = "Old Studio", MetaSeries = "Gone", MetaDescription = "Old" });
            await db.SaveChangesAsync();
        }
        var service = fixture.Service;

        var result = await service.PatchMetadataAsync("abp123", new MetadataPatchRequest(
            new MetadataFields(Description: "New", ReleaseDate: new DateOnly(2023, 2, 1), ContentType: "uncensored"), ["series"]));

        Assert.True(result.IsOk);
        Assert.Null(result.Value!.Warning);
        var movie = result.Value.Movie;
        Assert.Equal("New", movie.Description);
        Assert.Equal(new DateOnly(2023, 2, 1), movie.ReleaseDate);
        Assert.Equal("uncensored", movie.ContentType);
        Assert.Null(movie.Series);
        Assert.Equal("Keep", movie.Title);
        Assert.Equal("Old Studio", movie.Studio);
    }

    [Fact]
    public async Task PatchMetadataAsync_UnknownMovie_IsNotFound_AndInvalidPatch_IsInvalid_WithoutSaving()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync("ABP-123");

        Assert.Equal(ApiStatus.NotFound, (await fixture.Service.PatchMetadataAsync("NOPE-1", new MetadataPatchRequest(new MetadataFields(Studio: "x")))).Status);
        Assert.Equal(ApiStatus.Invalid, (await fixture.Service.PatchMetadataAsync("ABP-123", new MetadataPatchRequest())).Status);
        await fixture.MovieService.DidNotReceiveWithAnyArgs().UpdateMetadataAsync(default, default!, default);
    }

    [Fact]
    public async Task PatchMetadataAsync_SaveWithWarning_ReturnsTheWarning()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync("ABP-123");
        fixture.MovieService.UpdateMetadataAsync(Arg.Any<int>(), Arg.Any<MovieMetadataUpdate>(), Arg.Any<CancellationToken>())
            .Returns(new OperationResult(true, "Couldn't create the .nfo."));

        var result = await fixture.Service.PatchMetadataAsync("ABP-123", new MetadataPatchRequest(new MetadataFields(Studio: "x")));

        Assert.True(result.IsOk);
        Assert.Equal("Couldn't create the .nfo.", result.Value!.Warning);
    }

    [Fact]
    public async Task RemoveTagAsync_UsesTheExplicitOnlyRemoval_NeverTheClipStrippingOne()
    {
        using var fixture = new Fixture();
        int tagId;
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABP-123" };
            var tag = new Tag { Name = "Drama" };
            movie.MovieTags.Add(new MovieTag { Tag = tag, IsExplicit = true });
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            tagId = tag.Id;
        }
        fixture.Tags.RemoveTagFromMovieAsync(Arg.Any<int>(), tagId, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());

        var result = await fixture.Service.RemoveTagAsync("ABP-123", tagId);

        Assert.True(result.IsOk);
        await fixture.Tags.Received(1).RemoveTagFromMovieAsync(Arg.Any<int>(), tagId, Arg.Any<CancellationToken>());
        await fixture.Tags.DidNotReceiveWithAnyArgs().RemoveTagFromMovieAndClipsAsync(default, default, default);
    }

    [Fact]
    public async Task RemoveTagAsync_TagTheMovieDoesNotExplicitlyHave_IsANoOp()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync("ABP-123");

        var result = await fixture.Service.RemoveTagAsync("ABP-123", 999);

        Assert.True(result.IsOk);
        await fixture.Tags.DidNotReceiveWithAnyArgs().RemoveTagFromMovieAsync(default, default, default);
    }

    [Fact]
    public async Task AddTagAsync_UnknownTag_IsNotFound()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync("ABP-123");

        Assert.Equal(ApiStatus.NotFound, (await fixture.Service.AddTagAsync("ABP-123", 5)).Status);
        Assert.Equal(ApiStatus.NotFound, (await fixture.Service.AddTagAsync("NOPE-1", 5)).Status);
    }

    [Fact]
    public async Task AddCastAsync_UnknownActor_IsNotFound_AndKnownActorIsLinked()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync("ABP-123");

        Assert.Equal(ApiStatus.NotFound, (await fixture.Service.AddCastAsync("ABP-123", 5)).Status);

        fixture.Actors.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(new Actor { Id = 5, FirstName = "Yua" });
        fixture.MovieService.AddActorToMovieAsync(Arg.Any<int>(), 5, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());

        Assert.True((await fixture.Service.AddCastAsync("abp123", 5)).IsOk);
    }

    [Fact]
    public async Task RemoveCastByNameAsync_BlankName_IsInvalid_AndNameIsTrimmedOtherwise()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync("ABP-123");
        fixture.MovieService.RemoveActorFromMovieAsync(Arg.Any<int>(), "Some Name", Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());

        Assert.Equal(ApiStatus.Invalid, (await fixture.Service.RemoveCastByNameAsync("ABP-123", " ")).Status);
        Assert.True((await fixture.Service.RemoveCastByNameAsync("ABP-123", " Some Name ")).IsOk);
    }

    [Fact]
    public async Task GetNfoAsync_And_PushNfoAsync_UseTheResolvedMovieId()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync("ABP-123");
        var id = (await fixture.Service.ResolveAsync("ABP-123"))!.Id;
        fixture.NfoSync.GenerateProposedNfoAsync(id, Arg.Any<CancellationToken>()).Returns("<movie/>");
        fixture.NfoSync.PushNfoDriftAsync(Arg.Any<IReadOnlyList<int>>(), true, Arg.Any<IProgress<TaskProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new NfoDriftPushResult(1, 0, 0, 0));

        var preview = await fixture.Service.GetNfoAsync("abp123");
        var push = await fixture.Service.PushNfoAsync("abp123", includeExternal: true);

        Assert.Equal("<movie/>", preview.Value!.ProposedNfo);
        Assert.Equal("none", preview.Value.DriftKind);
        Assert.Equal(1, push.Value!.Written);
        await fixture.NfoSync.Received(1).PushNfoDriftAsync(Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { id })), true, Arg.Any<IProgress<TaskProgress>?>(), Arg.Any<CancellationToken>());
        Assert.Equal(ApiStatus.NotFound, (await fixture.Service.GetNfoAsync("NOPE-1")).Status);
        Assert.Equal(ApiStatus.NotFound, (await fixture.Service.PushNfoAsync("NOPE-1", false)).Status);
    }

    [Fact]
    public async Task FetchJavinizerAsync_DistinguishesNotConfiguredFromUpstreamFailure()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync("ABP-123");
        fixture.MovieService.FetchJavinizerMetadataAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                new MovieMetadataFetchResult(false, "javinizer-go is not configured."),
                new MovieMetadataFetchResult(false, "boom"),
                new MovieMetadataFetchResult(true, null, new MovieViewDto
                {
                    Id = "ABP-123",
                    Title = "T",
                    Maker = "M",
                    ReleaseDate = new DateTime(2024, 1, 2),
                    Actresses = [new ActressViewDto { FirstName = "Yua", LastName = "Mikami" }],
                    Genres = [new GenreViewDto { Name = "Drama" }],
                }));

        Assert.Equal(ApiStatus.NotConfigured, (await fixture.Service.FetchJavinizerAsync("ABP-123")).Status);
        Assert.Equal(ApiStatus.Upstream, (await fixture.Service.FetchJavinizerAsync("ABP-123")).Status);
        var ok = await fixture.Service.FetchJavinizerAsync("ABP-123");
        Assert.True(ok.IsOk);
        Assert.Equal("M", ok.Value!.Studio);
        Assert.Equal(new DateOnly(2024, 1, 2), ok.Value.ReleaseDate);
        Assert.Equal(["Yua Mikami"], ok.Value.Cast);
        Assert.Equal(["Drama"], ok.Value.Genres);
        Assert.Equal(ApiStatus.NotFound, (await fixture.Service.FetchJavinizerAsync("NOPE-1")).Status);
    }

    [Fact]
    public async Task GetSystemInfoAsync_ReportsConfiguredIntegrations()
    {
        using var fixture = new Fixture();
        var status = Substitute.For<IConnectionStatusService>();
        status.GetStatusesAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ConnectionStatus("javinizer", true, false, ""),
            new ConnectionStatus("prowlarr", false, false, ""),
            new ConnectionStatus("qbittorrent", true, false, ""),
        ]);
        var service = new LibraryApiService(fixture.Factory, new MovieGridQueryService(fixture.Factory), new MovieDetailQueryService(fixture.Factory),
            fixture.AddService, fixture.MovieService, fixture.Tags, fixture.Actors, fixture.NfoSync, status);

        var info = await service.GetSystemInfoAsync();

        Assert.Equal(new IntegrationStatus(true, false, true, false), info.Integrations);
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
    }
}
