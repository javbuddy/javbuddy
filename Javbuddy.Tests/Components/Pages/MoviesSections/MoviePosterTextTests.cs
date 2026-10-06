using Javbuddy.Components.Pages.MoviesSections;
using Javbuddy.Models;

namespace Javbuddy.Tests.Components.Pages.MoviesSections;

public class MoviePosterTextTests
{
    [Fact]
    public void ActorWarningTooltip_CountsUnmatchedActors()
    {
        Assert.Equal("1 unmatched actor: A", MoviePosterText.ActorWarningTooltip(new Movie { HasUnmatchedActors = true, UnmatchedActorNames = "A" }));
        Assert.Equal("2 unmatched actors: A, B", MoviePosterText.ActorWarningTooltip(new Movie { HasUnmatchedActors = true, UnmatchedActorNames = "A, B" }));
    }

    [Fact]
    public void ActorWarningTooltip_FlagsMissingCastOnlyOnceMetadataWasFetched()
    {
        Assert.Equal("No actors listed", MoviePosterText.ActorWarningTooltip(new Movie { MetaFetchedAt = DateTime.UtcNow }));
        Assert.Null(MoviePosterText.ActorWarningTooltip(new Movie()));
        Assert.Null(MoviePosterText.ActorWarningTooltip(new Movie { MetaFetchedAt = DateTime.UtcNow, MetaActresses = "A" }));
    }

    [Fact]
    public void StatusCssClass_ShowsDownloadingOnlyForAMissingMovie()
    {
        Assert.Equal("status-downloading", MoviePosterText.StatusCssClass(new Movie { Status = MovieStatus.Missing }, isDownloading: true));
        Assert.Equal("status-wanted", MoviePosterText.StatusCssClass(new Movie { Status = MovieStatus.Missing }, isDownloading: false));
        Assert.Equal("status-got", MoviePosterText.StatusCssClass(new Movie { Status = MovieStatus.Got }, isDownloading: true));
    }

    [Fact]
    public void QualityLabel_JoinsVrFormat()
    {
        Assert.Equal("4K · VR180 SBS", MoviePosterText.QualityLabel(new Movie { MediaWidth = 3840, MediaHeight = 2160, VrType = "VR180 SBS" }));
        Assert.Equal("VR180 SBS", MoviePosterText.QualityLabel(new Movie { VrType = "VR180 SBS" }));
        Assert.Null(MoviePosterText.QualityLabel(new Movie()));
    }

    [Fact]
    public void MetaSubLines_FollowsSizeAndQualityToggles()
    {
        var movie = new Movie { LocalFileSizeBytes = 1024L * 1024 * 1024, FileCount = 2 };

        Assert.Equal(new string?[] { "1 GB · 2 versions", null }, MoviePosterOptions.Default.MetaSubLines(movie));
        Assert.Equal(new string?[] { "2 versions" }, (MoviePosterOptions.Default with { ShowSize = false, ShowQuality = false }).MetaSubLines(movie));
    }

    [Theory]
    [InlineData("small", "small")]
    [InlineData("large", "large")]
    [InlineData("huge", "medium")]
    [InlineData(null, "medium")]
    public void NormalizeSize_FallsBackToMedium(string? size, string expected) =>
        Assert.Equal(expected, MoviePosterOptions.NormalizeSize(size));

    [Fact]
    public void PosterOptionsJson_KeepsTheCookieShape()
    {
        Assert.Equal("""{"PosterSize":"large","ShowTitle":false,"ShowStatus":true,"ShowSize":true,"ShowQuality":true,"ShowVrBadge":true}""",
            (MoviePosterOptions.Default with { PosterSize = "large", ShowTitle = false }).ToJson());
    }

    [Fact]
    public void PosterOptionsJson_DefaultsShowVrBadgeOnForACookieSavedBeforeIt()
    {
        var saved = """{"PosterSize":"large","ShowTitle":false,"ShowStatus":true,"ShowSize":true,"ShowQuality":true}""";

        Assert.True(System.Text.Json.JsonSerializer.Deserialize<MoviePosterOptions>(saved)!.ShowVrBadge);
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<MoviePosterOptions>((MoviePosterOptions.Default with { ShowVrBadge = false }).ToJson())!.ShowVrBadge);
    }

    [Fact]
    public void Parse_ReadsTheBrowsersCookieValue_FallingBackToDefaults()
    {
        Assert.Equal(MoviePosterOptions.Default with { PosterSize = "small", ShowTitle = false }, MoviePosterOptions.Parse("""{"PosterSize":"small","ShowTitle":false}"""));
        Assert.Equal("medium", MoviePosterOptions.Parse("""{"PosterSize":"huge"}""").PosterSize);
        Assert.Equal(MoviePosterOptions.Default, MoviePosterOptions.Parse(null));
        Assert.Equal(MoviePosterOptions.Default, MoviePosterOptions.Parse("not json"));
    }
}
