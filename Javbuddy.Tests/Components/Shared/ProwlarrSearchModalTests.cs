using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Common;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class ProwlarrSearchModalTests : BunitContext
{
    public ProwlarrSearchModalTests() => JSInterop.AllowModalShell();

    [Fact]
    public async Task UntrackedCode_ShowsAddAndDownloadButton()
    {
        var release = new ReleaseResourceDto { Title = "ABC-123 release", Guid = "release-1", MagnetUrl = "magnet:?xt=urn:btih:abc" };
        var prowlarr = Substitute.For<IProwlarrClient>();
        prowlarr.SearchAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new ProwlarrSearchResult(true, [release], null));
        Services.AddSingleton(prowlarr);
        Services.AddSingleton(Substitute.For<ITorrentGrabService>());
        Services.AddSingleton(Substitute.For<IMovieAddService>());

        var cut = Render<ProwlarrSearchModal>(parameters => parameters
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.Show, true));

        cut.WaitForAssertion(() => Assert.Contains("ABC-123 release", cut.Markup));
        await prowlarr.Received(1).SearchAsync("ABC-123", Arg.Any<CancellationToken>());
        var button = Assert.Single(cut.FindAll("button.btn-primary"));
        Assert.Contains("Add & Download", button.TextContent);
    }

    [Fact]
    public void DisabledIndexers_ShowNamedWarnings_AndNoneWhenAllEnabled()
    {
        var prowlarr = Substitute.For<IProwlarrClient>();
        prowlarr.SearchAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new ProwlarrSearchResult(true, [new ReleaseResourceDto { Title = "ABC-123 release", Guid = "r1" }], null));
        prowlarr.GetDisabledIndexersAsync(Arg.Any<CancellationToken>()).Returns([
            new ProwlarrDisabledIndexer(1, "Beta"),
            new ProwlarrDisabledIndexer(2, "Gamma", DateTime.UtcNow.AddHours(1))]);
        Services.AddSingleton(prowlarr);
        Services.AddSingleton(Substitute.For<ITorrentGrabService>());
        Services.AddSingleton(Substitute.For<IMovieAddService>());

        var cut = Render<ProwlarrSearchModal>(parameters => parameters
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.Show, true));

        cut.WaitForAssertion(() => Assert.Contains("ABC-123 release", cut.Markup));
        var warnings = cut.FindAll(".disabled-indexer-warning");
        Assert.Equal(2, warnings.Count);
        Assert.Contains("Indexer Beta is disabled in Prowlarr and was not searched", warnings[0].TextContent);
        Assert.Contains("Indexer Gamma is temporarily disabled", warnings[1].TextContent);
    }

    [Fact]
    public void NoDisabledIndexers_ShowsNoWarning()
    {
        var prowlarr = Substitute.For<IProwlarrClient>();
        prowlarr.SearchAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new ProwlarrSearchResult(true, [new ReleaseResourceDto { Title = "ABC-123 release", Guid = "r1" }], null));
        prowlarr.GetDisabledIndexersAsync(Arg.Any<CancellationToken>()).Returns([]);
        Services.AddSingleton(prowlarr);
        Services.AddSingleton(Substitute.For<ITorrentGrabService>());
        Services.AddSingleton(Substitute.For<IMovieAddService>());

        var cut = Render<ProwlarrSearchModal>(parameters => parameters
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.Show, true));

        cut.WaitForAssertion(() => Assert.Contains("ABC-123 release", cut.Markup));
        Assert.Empty(cut.FindAll(".disabled-indexer-warning"));
    }

    [Fact]
    public async Task UntrackedCode_ClickAddAndDownload_AddsMovieGrabsReleaseAndNotifiesParent()
    {
        var release = new ReleaseResourceDto { Title = "ABC-123 release", Guid = "release-1", MagnetUrl = "magnet:?xt=urn:btih:abc" };
        var prowlarr = Substitute.For<IProwlarrClient>();
        prowlarr.SearchAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new ProwlarrSearchResult(true, [release], null));
        Services.AddSingleton(prowlarr);

        var addedMovie = new Movie { Id = 42, Code = "ABC-123" };
        var movieAddService = Substitute.For<IMovieAddService>();
        movieAddService.AddAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new MovieAddResult(addedMovie, true, null));
        Services.AddSingleton(movieAddService);

        var torrentGrabService = Substitute.For<ITorrentGrabService>();
        torrentGrabService.GrabAsync(addedMovie, release, Arg.Any<CancellationToken>())
            .Returns(new OperationResult(true, null));
        Services.AddSingleton(torrentGrabService);

        Movie? notifiedMovie = null;
        var cut = Render<ProwlarrSearchModal>(parameters => parameters
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.Show, true)
            .Add(x => x.OnMovieAdded, (Movie m) => notifiedMovie = m));

        cut.WaitForAssertion(() => Assert.Contains("ABC-123 release", cut.Markup));

        cut.Find("button.btn-primary").Click();

        cut.WaitForAssertion(() => Assert.Contains("Added ABC-123 to library and grabbed", cut.Markup));
        await movieAddService.Received(1).AddAsync("ABC-123", Arg.Any<CancellationToken>());
        await torrentGrabService.Received(1).GrabAsync(addedMovie, release, Arg.Any<CancellationToken>());
        Assert.Same(addedMovie, notifiedMovie);
    }

    [Fact]
    public async Task AfterAddAndDownload_ReusedForDifferentCode_SearchesNewCodeInsteadOfStaleMovie()
    {
        var releaseA = new ReleaseResourceDto { Title = "ABC-123 release", Guid = "release-a", MagnetUrl = "magnet:?xt=urn:btih:a" };
        var releaseB = new ReleaseResourceDto { Title = "XYZ-999 release", Guid = "release-b", MagnetUrl = "magnet:?xt=urn:btih:b" };
        var prowlarr = Substitute.For<IProwlarrClient>();
        prowlarr.SearchAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new ProwlarrSearchResult(true, [releaseA], null));
        prowlarr.SearchAsync("XYZ-999", Arg.Any<CancellationToken>())
            .Returns(new ProwlarrSearchResult(true, [releaseB], null));
        Services.AddSingleton(prowlarr);

        var addedMovie = new Movie { Id = 42, Code = "ABC-123" };
        var movieAddService = Substitute.For<IMovieAddService>();
        movieAddService.AddAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new MovieAddResult(addedMovie, true, null));
        Services.AddSingleton(movieAddService);

        var torrentGrabService = Substitute.For<ITorrentGrabService>();
        torrentGrabService.GrabAsync(addedMovie, releaseA, Arg.Any<CancellationToken>())
            .Returns(new OperationResult(true, null));
        Services.AddSingleton(torrentGrabService);

        // ActorMissing.razor keeps one ProwlarrSearchModal instance alive across searches,
        // only ever binding Code (never Movie) — reproduces that reuse pattern directly.
        var cut = Render<ProwlarrSearchModal>(parameters => parameters
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.Show, true));

        cut.WaitForAssertion(() => Assert.Contains("ABC-123 release", cut.Markup));
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => Assert.Contains("Added ABC-123 to library and grabbed", cut.Markup));

        cut.Render(parameters => parameters.Add(x => x.Code, "XYZ-999"));

        cut.WaitForAssertion(() => Assert.Contains("XYZ-999 release", cut.Markup));
        await prowlarr.Received(1).SearchAsync("XYZ-999", Arg.Any<CancellationToken>());
        var button = Assert.Single(cut.FindAll("button.btn-primary"));
        Assert.Contains("Add & Download", button.TextContent);
    }

    [Fact]
    public void Search_ThatThrows_ShowsTheError_InsteadOfSpinningForever()
    {
        var prowlarr = Substitute.For<IProwlarrClient>();
        prowlarr.SearchAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ProwlarrSearchResult>(new InvalidOperationException("prowlarr down")));
        Services.AddSingleton(prowlarr);
        Services.AddSingleton(Substitute.For<ITorrentGrabService>());
        Services.AddSingleton(Substitute.For<IMovieAddService>());

        var cut = Render<ProwlarrSearchModal>(parameters => parameters
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.Show, true));

        cut.WaitForAssertion(() => Assert.Contains("prowlarr down", cut.Find(".alert-danger").TextContent));
    }

    [Fact]
    public void Grab_ThatThrows_ShowsTheError_AndReEnablesTheButton()
    {
        var release = new ReleaseResourceDto { Title = "ABC-123 release", Guid = "release-1", MagnetUrl = "magnet:?xt=urn:btih:abc" };
        var prowlarr = Substitute.For<IProwlarrClient>();
        prowlarr.SearchAsync("ABC-123", Arg.Any<CancellationToken>())
            .Returns(new ProwlarrSearchResult(true, [release], null));
        Services.AddSingleton(prowlarr);
        var movie = new Movie { Id = 1, Code = "ABC-123" };
        var grab = Substitute.For<ITorrentGrabService>();
        grab.GrabAsync(movie, release, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OperationResult>(new InvalidOperationException("qbittorrent down")));
        Services.AddSingleton(grab);
        Services.AddSingleton(Substitute.For<IMovieAddService>());
        var cut = Render<ProwlarrSearchModal>(parameters => parameters
            .Add(x => x.Movie, movie)
            .Add(x => x.Show, true));
        cut.WaitForAssertion(() => Assert.Contains("ABC-123 release", cut.Markup));

        cut.Find("button.btn-primary").Click();

        cut.WaitForAssertion(() => Assert.Contains("qbittorrent down", cut.Find(".alert-danger").TextContent));
        var button = cut.Find("button.btn-primary");
        Assert.False(button.HasAttribute("disabled"));
        Assert.Contains("Grab", button.TextContent);
    }
}
