using Javbuddy.Models;
using Javbuddy.Services.Api;
using Javbuddy.Services.Api.Contracts;
using Javbuddy.Services.Common;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.Settings;
using Javbuddy.Services.Torrents;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Javbuddy.Tests.Services.Api;

public class ReleaseApiServiceTests : IDisposable
{
    private readonly MemoryCache memoryCache = new(new MemoryCacheOptions());
    private readonly FakeTimeProvider time = new();
    private readonly IProwlarrClient prowlarr = Substitute.For<IProwlarrClient>();
    private readonly ITorrentGrabService grabService = Substitute.For<ITorrentGrabService>();
    private readonly ILibraryApiService library = Substitute.For<ILibraryApiService>();
    private readonly IConnectionStatusService status = Substitute.For<IConnectionStatusService>();
    private readonly ReleaseSearchCache cache;
    private readonly ReleaseApiService service;

    public ReleaseApiServiceTests()
    {
        cache = new ReleaseSearchCache(memoryCache, time);
        service = new ReleaseApiService(prowlarr, grabService, library, status, cache);
        Configure(prowlarr: true, qbittorrent: true);
        prowlarr.GetDisabledIndexersAsync(Arg.Any<CancellationToken>()).Returns([]);
    }

    public void Dispose() => memoryCache.Dispose();

    private void Configure(bool prowlarr, bool qbittorrent) =>
        status.GetStatusesAsync(Arg.Any<CancellationToken>()).Returns([
            new ConnectionStatus("prowlarr", prowlarr, false, ""),
            new ConnectionStatus("qbittorrent", qbittorrent, false, ""),
        ]);

    private static ReleaseResourceDto Release(string guid, int? seeders = 1, string url = "magnet:?xt=1") =>
        new() { Guid = guid, Title = $"Title {guid}", Indexer = "Idx", Size = 100, Seeders = seeders, Leechers = 2, Age = 3, PublishDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), MagnetUrl = url, DownloadUrl = "http://secret/dl" };

    private void ProwlarrReturns(params ReleaseResourceDto[] releases) =>
        prowlarr.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ProwlarrSearchResult(true, [.. releases], null));

    private static Movie LibraryMovie(string code = "ABP-123") => new() { Id = 7, Code = code };

    [Fact]
    public async Task Search_DefaultsQueryToCode_OrdersBySeeders_AndNeverExposesUrls()
    {
        ProwlarrReturns(Release("a", 1), Release("b", 9), Release("", 50));

        var result = await service.SearchAsync("ABP-123", null);

        Assert.True(result.IsOk);
        await prowlarr.Received().SearchAsync("ABP-123", Arg.Any<CancellationToken>());
        Assert.Equal(["b", "a"], result.Value!.Releases.Select(r => r.Guid));
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(result.Value));
        Assert.DoesNotContain("magnet", System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public async Task Search_UsesExplicitQuery_AndReportsDisabledIndexers()
    {
        ProwlarrReturns();
        prowlarr.GetDisabledIndexersAsync(Arg.Any<CancellationToken>()).Returns([new ProwlarrDisabledIndexer(1, "Sukebei")]);

        var result = await service.SearchAsync("ABP-123", " abp 123 ");

        await prowlarr.Received().SearchAsync("abp 123", Arg.Any<CancellationToken>());
        Assert.Equal(["Indexer Sukebei is disabled in Prowlarr and was not searched."], result.Value!.Warnings);
    }

    [Fact]
    public async Task Search_ProwlarrNotConfigured_IsNotConfigured_WithoutSearching()
    {
        Configure(prowlarr: false, qbittorrent: true);

        var result = await service.SearchAsync("ABP-123", null);

        Assert.Equal(ApiStatus.NotConfigured, result.Status);
        await prowlarr.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_ProwlarrFailure_IsUpstream()
    {
        prowlarr.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ProwlarrSearchResult(false, null, "Prowlarr returned 500"));

        var result = await service.SearchAsync("ABP-123", null);

        Assert.Equal(ApiStatus.Upstream, result.Status);
        Assert.Equal("Prowlarr returned 500", result.Message);
    }

    [Fact]
    public async Task Grab_UnknownGuid_IsGone()
    {
        var result = await service.GrabAsync("ABP-123", "nope");

        Assert.Equal(ApiStatus.Gone, result.Status);
        await grabService.DidNotReceive().GrabAsync(Arg.Any<Movie>(), Arg.Any<ReleaseResourceDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Grab_GuidFromAnotherCode_IsGone()
    {
        ProwlarrReturns(Release("a"));
        await service.SearchAsync("ABP-123", null);

        Assert.Equal(ApiStatus.Gone, (await service.GrabAsync("XYZ-999", "a")).Status);
    }

    [Fact]
    public async Task Grab_AfterSearch_HitsTheCache_EvenForADifferentCodeSpelling()
    {
        ProwlarrReturns(Release("a"));
        await service.SearchAsync("abp00123", null);
        library.ResolveAsync("ABP-123", Arg.Any<CancellationToken>()).Returns(LibraryMovie());
        grabService.GrabAsync(Arg.Any<Movie>(), Arg.Any<ReleaseResourceDto>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());

        var result = await service.GrabAsync("ABP-123", "a");

        Assert.True(result.IsOk);
        Assert.False(result.Value!.Added);
        Assert.Equal("ABP-123", result.Value.Movie.Code);
        await grabService.Received().GrabAsync(Arg.Is<Movie>(m => m.Id == 7), Arg.Is<ReleaseResourceDto>(r => r.MagnetUrl == "magnet:?xt=1"), Arg.Any<CancellationToken>());
        await library.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Grab_AfterExpiry_IsGone()
    {
        ProwlarrReturns(Release("a"));
        await service.SearchAsync("ABP-123", null);

        time.Advance(ReleaseSearchCache.Lifetime - TimeSpan.FromSeconds(1));
        Assert.NotNull(cache.TryGet("ABP-123", "a"));

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(ApiStatus.Gone, (await service.GrabAsync("ABP-123", "a")).Status);
    }

    [Fact]
    public async Task Grab_MissingMovie_AddsItFirst()
    {
        ProwlarrReturns(Release("a"));
        await service.SearchAsync("NEW-001", null);
        var movie = LibraryMovie("NEW-001");
        library.ResolveAsync("NEW-001", Arg.Any<CancellationToken>()).Returns(null, movie);
        library.AddAsync("NEW-001", Arg.Any<CancellationToken>()).Returns(ApiResult<MovieAddResponse>.Ok(
            new MovieAddResponse(MovieApiMapper.ToSummary(movie), false, false, null)));
        grabService.GrabAsync(Arg.Any<Movie>(), Arg.Any<ReleaseResourceDto>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());

        var result = await service.GrabAsync("NEW-001", "a");

        Assert.True(result.IsOk);
        Assert.True(result.Value!.Added);
        Received.InOrder(() =>
        {
            library.AddAsync("NEW-001", Arg.Any<CancellationToken>());
            grabService.GrabAsync(Arg.Any<Movie>(), Arg.Any<ReleaseResourceDto>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Grab_InvalidCodeToAdd_IsInvalid_AndDoesNotGrab()
    {
        ProwlarrReturns(Release("a"));
        await service.SearchAsync("bad code!", null);
        library.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Movie?)null);
        library.AddAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<MovieAddResponse>.Invalid("bad code"));

        var result = await service.GrabAsync("bad code!", "a");

        Assert.Equal(ApiStatus.Invalid, result.Status);
        await grabService.DidNotReceive().GrabAsync(Arg.Any<Movie>(), Arg.Any<ReleaseResourceDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Grab_QBittorrentNotConfigured_IsNotConfigured_AndDoesNotAddTheMovie()
    {
        ProwlarrReturns(Release("a"));
        await service.SearchAsync("NEW-001", null);
        Configure(prowlarr: true, qbittorrent: false);

        var result = await service.GrabAsync("NEW-001", "a");

        Assert.Equal(ApiStatus.NotConfigured, result.Status);
        await library.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Grab_QBittorrentFailure_IsUpstream()
    {
        ProwlarrReturns(Release("a"));
        await service.SearchAsync("ABP-123", null);
        library.ResolveAsync("ABP-123", Arg.Any<CancellationToken>()).Returns(LibraryMovie());
        grabService.GrabAsync(Arg.Any<Movie>(), Arg.Any<ReleaseResourceDto>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Fail("qBittorrent rejected the torrent."));

        var result = await service.GrabAsync("ABP-123", "a");

        Assert.Equal(ApiStatus.Upstream, result.Status);
        Assert.Equal("qBittorrent rejected the torrent.", result.Message);
    }
}
