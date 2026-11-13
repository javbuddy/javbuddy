using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Common;
using Javbuddy.Services.Movies;
using Javbuddy.Services.R18Dev;
using Javbuddy.Services.Scenes;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.MovieDetailSections;

public class MovieDetailBadgesTests : BunitContext
{
    private readonly IMovieService movieService = Substitute.For<IMovieService>();
    private readonly IMovieCleanupService cleanupService = Substitute.For<IMovieCleanupService>();
    private readonly MovieFilterNavigationState filterState = new();
    private readonly List<MovieDetailDialog> opened = [];
    private readonly List<MovieParts> reloaded = [];
    private int played;
    private readonly List<(PlayerClipKind Kind, bool Shuffle)> clipPlays = [];

    public MovieDetailBadgesTests()
    {
        Services.AddSingleton(movieService);
        Services.AddSingleton(cleanupService);
        Services.AddSingleton(filterState);
    }

    private static Movie GotMovie() => new() { Id = 7, Code = "ABC-123", Status = MovieStatus.Got, MetaStudio = "Studio Shot" };

    private IRenderedComponent<MovieDetailBadges> RenderBadges(Movie? movie = null, Action<ComponentParameterCollectionBuilder<MovieDetailBadges>>? configure = null, bool interactive = true) =>
        Render<MovieDetailBadges>(p =>
        {
            p.Add(x => x.Movie, movie ?? GotMovie())
                .Add(x => x.Interactive, interactive)
                .Add(x => x.OnOpenDialog, d => opened.Add(d))
                .Add(x => x.OnReload, parts => reloaded.Add(parts))
                .Add(x => x.OnPlay, () => played++)
                .Add(x => x.OnPlayClips, request => clipPlays.Add(request));
            configure?.Invoke(p);
        });

    private static Movie MovieWithTwoVersions()
    {
        var movie = GotMovie();
        movie.MovieFiles.Add(new MovieFile { Id = 70, FileName = "ABC-123.mp4", VersionTag = "Original", IsPrimary = true });
        movie.MovieFiles.Add(new MovieFile { Id = 71, FileName = "ABC-123-RIFE.mkv", VersionTag = "RIFE" });
        return movie;
    }

    private static HighlightItem Highlight(int id, double start, double end) => new(id, id, "H", null, start, end, false, 0);

    [Fact]
    public void ShowsTheStatusCodeStudioAndRuntime()
    {
        var movie = GotMovie();
        movie.MetaRuntimeMinutes = 120;
        movie.MetaReleaseDate = new DateTime(2024, 3, 1);

        var cut = RenderBadges(movie);

        var texts = cut.FindAll(".movie-detail-badge").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("Got", texts);
        Assert.Contains("120 Minutes", texts);
        Assert.Contains("2024", texts);
        Assert.Contains("ABC-123", texts);
        Assert.Contains("Studio Shot", texts);
    }

    [Fact]
    public void ClickingTheStudio_GoesToTheMoviesPageFilteredByIt()
    {
        var cut = RenderBadges();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/movies/ABC-123");

        cut.FindAll("button.movie-detail-badge").Single(b => b.TextContent.Trim() == "Studio Shot").Click();

        Assert.EndsWith("/", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Equal("Studio Shot", filterState.PeekPendingFilter()!.Studio);
    }

    [Fact]
    public void ClickingTheCodePrefix_GoesToTheMoviesPageFilteredByIt()
    {
        var cut = RenderBadges();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/movies/ABC-123");

        cut.Find("button.movie-detail-code-prefix").Click();

        Assert.EndsWith("/", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Equal("ABC", filterState.PeekPendingFilter()!.CodePrefix);
    }

    [Fact]
    public void ClickingTheContentType_GoesToTheMoviesPageFilteredByIt()
    {
        var movie = GotMovie();
        movie.ContentType = MovieContentType.Uncensored;
        var cut = RenderBadges(movie);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/movies/ABC-123");

        cut.Find("button.movie-detail-badge-content-type").Click();

        Assert.EndsWith("/", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Equal(MovieContentType.Uncensored, filterState.PeekPendingFilter()!.ContentType);
    }

    [Fact]
    public void TheSeriesAndLabelBadges_BrowseTheCatalogOnlyWhenTheCatalogKnowsThem()
    {
        var movie = GotMovie();
        movie.MetaLabel = "Label Shot";
        movie.MetaSeries = "Series Shot";

        var without = RenderBadges(movie);
        Assert.DoesNotContain(without.FindAll("button.movie-detail-badge"), b => b.TextContent.Contains("Label Shot"));
        Assert.Contains(without.FindAll("span.movie-detail-badge"), b => b.TextContent.Contains("Label Shot"));

        var series = new R18DevCatalogRef(20, "Series A");
        var label = new R18DevCatalogRef(10, "Label A");
        var cut = RenderBadges(movie, p => p.Add(x => x.Related, new R18DevRelatedReleases(series, label, [], 0)));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/movies/ABC-123");

        cut.FindAll("button.movie-detail-badge").Single(b => b.TextContent.Trim() == "Series Shot").Click();

        Assert.Equal(series, filterState.PeekPendingFilter()!.CatalogSeries);
    }

    [Fact]
    public void ADriftedNfo_OffersTheConflictReview()
    {
        var movie = GotMovie();
        movie.NfoDriftKind = NfoDriftKind.ExternalEdit;
        var cut = RenderBadges(movie);

        cut.Find("button.movie-detail-badge-warning").Click();

        Assert.Equal([MovieDetailDialog.NfoConflictReview], opened);
    }

    [Fact]
    public void BrokenBFrames_OffersTheRepair_ButNotForAMissingMovie()
    {
        var movie = GotMovie();
        movie.HasBrokenBFrames = true;
        var cut = RenderBadges(movie);

        cut.Find("button.movie-detail-badge-danger").Click();

        Assert.Equal([MovieDetailDialog.VideoRepair], opened);

        movie.Status = MovieStatus.Missing;
        Assert.Empty(RenderBadges(movie).FindAll("button.movie-detail-badge-danger"));
    }

    [Fact]
    public async Task TheCleanupBlacklistBadge_RemovesTheMovieFromItAndReloadsTheCore()
    {
        var movie = GotMovie();
        movie.CleanupBlacklisted = true;
        var cut = RenderBadges(movie);

        await cut.InvokeAsync(() => cut.FindAll("button.movie-detail-badge-warning").Single(b => b.TextContent.Contains("Blacklisted from Cleanup")).Click());

        await cleanupService.Received(1).UnblacklistAsync(7, Arg.Any<CancellationToken>());
        Assert.Equal([MovieParts.Core], reloaded);
    }

    [Fact]
    public void TheBadgesThatNeedTheCircuit_AreDisabledBeforeItIsInteractive()
    {
        var movie = GotMovie();
        movie.NfoDriftKind = NfoDriftKind.ExternalEdit;

        var cut = RenderBadges(movie, interactive: false);

        Assert.True(cut.Find("button.movie-detail-badge-content-type").HasAttribute("disabled"));
        Assert.True(cut.Find("button.movie-detail-badge-warning").HasAttribute("disabled"));
    }

    [Fact]
    public void AMovieWithOneFile_HasNoVersionsBadge()
    {
        var movie = GotMovie();
        movie.MovieFiles.Add(new MovieFile { Id = 70, FileName = "ABC-123.mp4", IsPrimary = true });

        Assert.Empty(RenderBadges(movie).FindAll(".movie-detail-badge-versions"));
    }

    [Fact]
    public void TheVersionsBadge_OpensAMenuWithThePrimaryFirst_AndEscapeClosesIt()
    {
        var cut = RenderBadges(MovieWithTwoVersions());

        Assert.Contains("2 Versions", cut.Find("button.movie-detail-badge-versions").TextContent);
        Assert.Empty(cut.FindAll(".movie-detail-version-menu"));

        cut.Find("button.movie-detail-badge-versions").Click();

        var choices = cut.FindAll(".movie-detail-version-choice");
        Assert.Equal(2, choices.Count);
        Assert.Equal("true", choices[0].GetAttribute("aria-checked"));
        Assert.Equal("true", cut.Find("button.movie-detail-badge-versions").GetAttribute("aria-expanded"));

        cut.Find(".movie-detail-version-anchor").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".movie-detail-version-menu"));
    }

    [Fact]
    public async Task ChoosingAnotherVersion_MakesItPrimary_ClosesTheMenuAndReloadsEverything()
    {
        movieService.SetPrimaryFileAsync(7, 71, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        var cut = RenderBadges(MovieWithTwoVersions());
        cut.Find("button.movie-detail-badge-versions").Click();

        await cut.InvokeAsync(() => cut.FindAll(".movie-detail-version-choice")[1].Click());

        await movieService.Received(1).SetPrimaryFileAsync(7, 71, Arg.Any<CancellationToken>());
        Assert.Equal([MovieParts.All], reloaded);
        Assert.Empty(cut.FindAll(".movie-detail-version-menu"));
    }

    [Fact]
    public async Task ChoosingTheCurrentPrimary_OnlyClosesTheMenu()
    {
        var cut = RenderBadges(MovieWithTwoVersions());
        cut.Find("button.movie-detail-badge-versions").Click();

        await cut.InvokeAsync(() => cut.FindAll(".movie-detail-version-choice")[0].Click());

        await movieService.DidNotReceiveWithAnyArgs().SetPrimaryFileAsync(default, default, default);
        Assert.Empty(cut.FindAll(".movie-detail-version-menu"));
    }

    [Fact]
    public async Task AFailedVersionChange_KeepsTheMenuOpenWithTheReason()
    {
        movieService.SetPrimaryFileAsync(7, 71, Arg.Any<CancellationToken>()).Returns(OperationResult.Fail("That file is gone."));
        var cut = RenderBadges(MovieWithTwoVersions());
        cut.Find("button.movie-detail-badge-versions").Click();

        await cut.InvokeAsync(() => cut.FindAll(".movie-detail-version-choice")[1].Click());

        Assert.Equal("That file is gone.", cut.Find(".movie-detail-version-error").TextContent);
        Assert.Empty(reloaded);
    }

    [Fact]
    public void Play_IsOnlyOfferedForAMovieThePlayerCanStream()
    {
        Assert.Empty(RenderBadges().FindAll(".movie-detail-badge-play"));

        var cut = RenderBadges(configure: p => p.Add(x => x.CanPlay, true));

        cut.Find("button[title='Play movie']").Click();

        Assert.Equal(1, played);
        Assert.Empty(cut.FindAll(".movie-detail-play-caret"));
    }

    [Fact]
    public void WithClips_PlayGetsAMenuToPlayThemInOrderOrShuffled()
    {
        var cut = RenderBadges(configure: p => p
            .Add(x => x.CanPlay, true)
            .Add(x => x.Highlights, [Highlight(1, 0, 10), Highlight(2, 20, 25)])
            .Add(x => x.Apexes, [new ApexItem(3, 1, 30, [])]));

        cut.Find("button.movie-detail-play-caret").Click();

        Assert.Contains("2 clips · ", cut.Markup);
        Assert.Contains("1 clip · ", cut.Markup);

        cut.Find("button[aria-label='Shuffle highlights']").Click();
        Assert.Equal([(PlayerClipKind.Highlight, true)], clipPlays);
        Assert.Empty(cut.FindAll(".movie-detail-play-menu"));

        cut.Find("button.movie-detail-play-caret").Click();
        cut.FindAll(".movie-detail-play-menu button.movie-detail-version-choice").Single(b => b.TextContent.Contains("All apexes")).Click();
        Assert.Equal((PlayerClipKind.Apex, false), clipPlays.Last());

        cut.Find("button.movie-detail-play-caret").Click();
        cut.FindAll(".movie-detail-play-menu button.movie-detail-version-choice").Single(b => b.TextContent.Contains("Play movie")).Click();
        Assert.Equal(1, played);
    }

    [Fact]
    public void TheJellyfinBadge_LinksToTheResolvedUrl()
    {
        var movie = GotMovie();
        movie.JellyfinItemId = "item-1";

        var cut = RenderBadges(movie, p => p.Add(x => x.JellyfinWebUrl, "https://jellyfin.local/web/#!/details?id=item-1"));

        Assert.Equal("https://jellyfin.local/web/#!/details?id=item-1", cut.Find("a.movie-detail-badge-jellyfin").GetAttribute("href"));
    }
}
