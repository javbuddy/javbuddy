using Bunit;
using Javbuddy.Components.Pages.MoviesSections;
using Javbuddy.Components.Shared;
using Javbuddy.Models;

namespace Javbuddy.Tests.Components.Shared;

public class MoviePosterGridTests : BunitContext
{
    private static Movie NewMovie(int id, string code, MovieStatus status = MovieStatus.Got) =>
        new() { Id = id, Code = code, Status = status, MetaSourceName = "dmm" };

    [Fact]
    public void AppliesPosterOptionsToTheGridWrapper()
    {
        var options = new MoviePosterOptions(PosterSize: "large", ShowTitle: false, ShowStatus: false, ShowSize: false, ShowQuality: false);

        var cut = Render<MoviePosterGrid>(p => p
            .Add(x => x.Movies, [NewMovie(1, "AAA-001")])
            .Add(x => x.Options, options));

        var classes = cut.Find(".movie-poster-grid").ClassList;
        Assert.Contains("movie-poster-size-large", classes);
        Assert.Contains("movie-poster-hide-title", classes);
        Assert.Contains("movie-poster-hide-status", classes);
        Assert.Contains("movie-poster-hide-info", classes);
    }

    [Fact]
    public void MissingMovieWithATorrentShowsAsDownloading()
    {
        var cut = Render<MoviePosterGrid>(p => p
            .Add(x => x.Movies, [NewMovie(1, "AAA-001", MovieStatus.Missing), NewMovie(2, "BBB-002", MovieStatus.Missing)])
            .Add(x => x.DownloadingCodes, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aaa-001" }));

        var images = cut.FindAll(".poster-image");
        Assert.Contains("status-downloading", images[0].ClassList);
        Assert.Contains("status-wanted", images[1].ClassList);
    }

    [Fact]
    public void AppendsHrefQueryToEachLink()
    {
        var cut = Render<MoviePosterGrid>(p => p
            .Add(x => x.Movies, [NewMovie(1, "AAA 001")])
            .Add(x => x.HrefQuery, "?actor=Yua"));

        Assert.Equal("/movies/AAA%20001?actor=Yua", cut.Find("a.poster-card").GetAttribute("href"));
    }

    [Fact]
    public void LoadsTheFirstEagerImageCountImagesEagerly_ThenUsesLoading()
    {
        var cut = Render<MoviePosterGrid>(p => p
            .Add(x => x.Movies, [NewMovie(1, "AAA-001"), NewMovie(2, "BBB-002"), NewMovie(3, "CCC-003")])
            .Add(x => x.Loading, PosterImageLoading.JsLazy)
            .Add(x => x.EagerImageCount, 2));

        var images = cut.FindAll(".poster-image img");
        Assert.NotNull(images[0].GetAttribute("src"));
        Assert.NotNull(images[1].GetAttribute("src"));
        Assert.Null(images[2].GetAttribute("src"));
        Assert.Contains("js-lazy-poster-img", images[2].ClassList);
    }

    [Fact]
    public void VrBadgeFollowsTheShowVrBadgeOption()
    {
        var movie = NewMovie(1, "AAA-001");
        movie.VrType = "VR180 SBS";

        var shown = Render<MoviePosterGrid>(p => p.Add(x => x.Movies, [movie]));
        Assert.Equal("VR", shown.Find(".poster-corner-badge").TextContent);

        var hidden = Render<MoviePosterGrid>(p => p
            .Add(x => x.Movies, [movie])
            .Add(x => x.Options, MoviePosterOptions.Default with { ShowVrBadge = false }));
        Assert.Empty(hidden.FindAll(".poster-corner-badge"));
    }

    [Fact]
    public void FavoriteToggleReportsTheMovie()
    {
        var movie = NewMovie(1, "AAA-001");
        Movie? toggled = null;

        var cut = Render<MoviePosterGrid>(p => p
            .Add(x => x.Movies, [movie])
            .Add(x => x.OnToggleFavorite, (Movie m) => toggled = m));
        cut.Find(".poster-favorite").Click();

        Assert.Same(movie, toggled);
    }
}
