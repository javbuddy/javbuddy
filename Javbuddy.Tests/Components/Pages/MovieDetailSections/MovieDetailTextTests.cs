using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Models;
using Javbuddy.Services.Movies;

namespace Javbuddy.Tests.Components.Pages.MovieDetailSections;

public class MovieDetailTextTests
{
    [Theory]
    [InlineData("ABC-123", "ABC")]
    [InlineData("T28-0123-2", "T28-0123")]
    [InlineData("ABC123", null)]
    [InlineData("-123", null)]
    [InlineData(null, null)]
    public void CodePrefix_SplitsOnTheLastHyphen(string? code, string? expected) =>
        Assert.Equal(expected, MovieDetailText.CodePrefix(code));

    [Fact]
    public void DeleteLabel_PairsCodeWithTitle()
    {
        Assert.Equal("ABC-123 - Some Title", MovieDetailText.DeleteLabel(new Movie { Code = "ABC-123", MetaTitle = "Some Title" }));
        Assert.Equal("ABC-123 Some Title", MovieDetailText.DeleteLabel(new Movie { Code = "ABC-123", MetaTitle = "ABC-123 Some Title" }));
        Assert.Equal("ABC-123", MovieDetailText.DeleteLabel(new Movie { Code = "ABC-123" }));
        Assert.Equal("Some Title", MovieDetailText.DeleteLabel(new Movie { Title = "Some Title" }));
    }

    [Fact]
    public void Truncate_CapsWithAnEllipsis()
    {
        Assert.Equal("short", MovieDetailText.Truncate("short", 10));
        Assert.Equal("abc…", MovieDetailText.Truncate("abc defghij", 4));
    }

    [Fact]
    public void UnderageCastTooltip_ListsOnlyUnderageCast()
    {
        Assert.Null(MovieDetailText.UnderageCastTooltip([new MovieCastEntry("A", 1, false, Age: 25)]));

        var tooltip = MovieDetailText.UnderageCastTooltip([new MovieCastEntry("A", 1, false, Age: 25), new MovieCastEntry("B", 2, false, Age: 16)]);

        Assert.Contains("B (16)", tooltip);
        Assert.DoesNotContain("A (25)", tooltip);
    }

    [Fact]
    public void ActorThumbUrl_VersionsWhenKnown()
    {
        Assert.Equal("/actor-image/5/thumb", MovieDetailText.ActorThumbUrl(5, null));
        Assert.Equal("/actor-image/5/thumb?v=9", MovieDetailText.ActorThumbUrl(5, 9));
    }

    [Fact]
    public void JellyfinLinkedTooltip_AddsLibraryName()
    {
        Assert.Equal("Linked to a library item in JAV", MovieDetailText.JellyfinLinkedTooltip(new Movie { JellyfinLibraryName = "JAV" }));
    }
}
