using Javbuddy.Models;
using Javbuddy.Services.Api;
using Javbuddy.Services.Movies;

namespace Javbuddy.Tests.Services.Api;

public class MovieApiMapperTests
{
    [Fact]
    public void WebPathFor_EscapesTheCode()
    {
        Assert.Equal("/movies/ABC-123", MovieApiMapper.WebPathFor("ABC-123"));
        Assert.Equal("/movies/A%2FB%20C", MovieApiMapper.WebPathFor("A/B C"));
    }

    [Fact]
    public void ToSummary_MapsFieldsAndNamesEnumsInCamelCase()
    {
        var movie = new Movie
        {
            Id = 7,
            Code = "ABC-123",
            MetaTitle = "Title",
            Status = MovieStatus.Got,
            IsFavorite = true,
            ContentType = MovieContentType.Decensored,
            MetaReleaseDate = new DateTime(2024, 3, 4, 10, 0, 0),
            MetaStudio = "Studio",
            FileCount = 2,
            NfoDriftKind = NfoDriftKind.JavbuddyChanged,
        };

        var summary = MovieApiMapper.ToSummary(movie);

        Assert.Equal(7, summary.Id);
        Assert.Equal("ABC-123", summary.Code);
        Assert.Equal("Title", summary.Title);
        Assert.Equal("got", summary.Status);
        Assert.True(summary.Favorite);
        Assert.Equal("decensored", summary.ContentType);
        Assert.Equal(new DateOnly(2024, 3, 4), summary.ReleaseDate);
        Assert.Equal("Studio", summary.Studio);
        Assert.Equal(2, summary.FileCount);
        Assert.Equal("javbuddyChanged", summary.NfoDriftKind);
        Assert.Equal("/movies/ABC-123", summary.WebPath);
    }

    [Fact]
    public void ToDetail_MapsTagsCastFilesAndNfo()
    {
        var movie = new Movie
        {
            Id = 1,
            Code = "ABC-123",
            MetaTitle = "Meta",
            Title = "Override",
            MetaRuntimeMinutes = 120,
            MetaRatingScore = 4.5,
            NfoDriftKind = NfoDriftKind.ExternalEdit,
            NfoConflictDetails = "[External edit] Studio",
        };
        var parent = new Tag { Id = 1, Name = "Genre" };
        var tags = new[]
        {
            new MovieTag { TagId = 3, Tag = new Tag { Id = 3, Name = "zebra", ParentTag = parent }, IsExplicit = false, FromClips = true },
            new MovieTag { TagId = 2, Tag = new Tag { Id = 2, Name = "Apple" }, IsExplicit = true },
        };
        var cast = new[] { new MovieCastEntry("Yua Mikami", 5, true), new MovieCastEntry("Unknown", null, false) };
        var files = new[]
        {
            new MovieFile { Id = 2, FileName = "b.mp4", VersionTag = "4K", FileSizeBytes = 10 },
            new MovieFile { Id = 1, FileName = "a.mp4", VersionTag = "Original", FileSizeBytes = 5, IsPrimary = true },
        };

        var detail = MovieApiMapper.ToDetail(movie, tags, cast, files);

        Assert.Equal("Meta", detail.Title);
        Assert.Equal("Override", detail.TitleOverride);
        Assert.Equal(120, detail.RuntimeMinutes);
        Assert.Equal(["Apple", "zebra"], detail.Tags.Select(t => t.Name));
        Assert.Equal(new[] { true, false }, detail.Tags.Select(t => t.Explicit));
        Assert.Equal("Genre", detail.Tags[1].Parent);
        Assert.True(detail.Tags[1].FromClips);
        Assert.Equal([true, false], detail.Cast.Select(c => c.Matched));
        Assert.Equal(5, detail.Cast[0].ActorId);
        Assert.Equal(["a.mp4", "b.mp4"], detail.Files.Select(f => f.FileName));
        Assert.True(detail.Files[0].Primary);
        Assert.Equal("externalEdit", detail.Nfo.DriftKind);
        Assert.Equal("[External edit] Studio", detail.Nfo.Details);
    }
}
