using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Torrents;

namespace Javbuddy.Tests.Services.Torrents;

public class TorrentSortEditingHelperTests
{
    [Theory]
    [InlineData("default.jpg", "default.jpg", null, false)]
    [InlineData("custom.jpg", "default.jpg", null, true)]
    [InlineData("cover.jpg", null, "cover.jpg", false)]
    [InlineData("custom.jpg", null, "cover.jpg", true)]
    [InlineData("default.jpg", null, null, false)]
    [InlineData(null, "default.jpg", null, false)]
    public void HasCustomPoster_ComparesWithOriginalImage(string? poster, string? original, string? cover, bool expected)
    {
        var movie = new MovieViewDto { PosterUrl = poster, OriginalPosterUrl = original, OriginalCoverUrl = cover };

        Assert.Equal(expected, TorrentSortEditingHelper.HasCustomPoster(movie));
    }

    [Fact]
    public void Clone_CopiesScalarFieldsAndListsIndependently()
    {
        var original = new MovieViewDto
        {
            Id = "id1",
            Code = "code1",
            DisplayTitle = "Title",
            Actresses = [new ActressViewDto { FirstName = "Noa", LastName = "Araki" }],
            Genres = [new GenreViewDto { Name = "Drama" }],
            ScreenshotUrls = ["http://example.test/1.jpg"]
        };

        var clone = TorrentSortEditingHelper.Clone(original);

        Assert.Equal(original.Id, clone.Id);
        Assert.Equal(original.Code, clone.Code);
        Assert.Equal(original.DisplayTitle, clone.DisplayTitle);
        Assert.NotSame(original.Actresses, clone.Actresses);
        Assert.NotSame(original.Genres, clone.Genres);
        Assert.NotSame(original.ScreenshotUrls, clone.ScreenshotUrls);

        // Mutating the clone's lists must not affect the original.
        clone.Actresses!.Clear();
        Assert.Single(original.Actresses);
    }

    [Fact]
    public void Clone_NullLists_BecomeEmptyLists()
    {
        var original = new MovieViewDto { Actresses = null, Genres = null, ScreenshotUrls = null };

        var clone = TorrentSortEditingHelper.Clone(original);

        Assert.Empty(clone.Actresses!);
        Assert.Empty(clone.Genres!);
        Assert.Empty(clone.ScreenshotUrls!);
    }

    [Theory]
    [InlineData("Noa", "Araki", "Noa Araki", "Noa Araki")]
    [InlineData("Noa", "Araki", "新木 希空", "Noa Araki (新木 希空)")]
    [InlineData("", "", "新木 希空", "新木 希空")]
    [InlineData("", "", "", "")]
    public void FormatActressName_PrefersLatinName_FallsBackToJapanese(string first, string last, string japanese, string expected)
    {
        var actress = new ActressViewDto { FirstName = first, LastName = last, JapaneseName = japanese };

        Assert.Equal(expected, TorrentSortEditingHelper.FormatActressName(actress));
    }

    [Theory]
    [InlineData(5, null, "X", 5, null, "Y", true)]
    [InlineData(null, "新木 希空", "", null, "新木希空", "Other", true)]
    [InlineData(null, null, "Yui Hatano", null, null, "yui hatano", true)]
    [InlineData(1, null, "Same", 2, null, "Same", false)]
    public void IsSameActress_ComparesIdThenJapaneseThenName(int? idA, string? jpA, string nameA, int? idB, string? jpB, string nameB, bool expected)
    {
        static ActressViewDto Make(int? id, string? jp, string name) => new() { Id = id, JapaneseName = jp, FirstName = name };
        Assert.Equal(expected, TorrentSortEditingHelper.IsSameActress(Make(idA, jpA, nameA), Make(idB, jpB, nameB)));
    }

    [Theory]
    [InlineData("Noa", "Araki", "新木希空", "Noa Araki", "新木希空")]
    [InlineData(null, null, "榊原萌", "榊原萌", null)]
    [InlineData("Cher", null, null, "Cher", null)]
    public void SortEditorFormat_PrimaryRomajiThenJapanese(string? first, string? last, string? jp, string primary, string? secondary)
    {
        var a = new ActressViewDto { FirstName = first, LastName = last, JapaneseName = jp };
        Assert.Equal(primary, SortEditorFormat.ActressPrimaryName(a));
        Assert.Equal(secondary, SortEditorFormat.ActressSecondaryName(a));
    }
}
