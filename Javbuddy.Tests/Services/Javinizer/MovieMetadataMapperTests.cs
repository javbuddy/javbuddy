using Javbuddy.Models;
using Javbuddy.Services.Javinizer;

namespace Javbuddy.Tests.Services.Javinizer;

public class MovieMetadataMapperTests
{
    [Fact]
    public void Apply_MapsScalarFields()
    {
        var movie = new Movie { Code = "ABC-123" };
        var meta = new MovieViewDto
        {
            Title = "The Title",
            OriginalTitle = "Original Title",
            Description = "A description.",
            ReleaseDate = new DateTime(2026, 5, 1),
            Director = "Director Name",
            Maker = "Studio Name",
            Label = "Label Name",
            Series = "Series Name",
            RatingScore = 4.5,
            RatingVotes = 10,
            SourceName = "javinizer-go",
            SourceUrl = "http://example.test/source",
        };

        MovieMetadataMapper.Apply(movie, meta);

        Assert.Equal("The Title", movie.MetaTitle);
        Assert.Equal("Original Title", movie.MetaOriginalTitle);
        Assert.Equal("A description.", movie.MetaDescription);
        Assert.Equal(new DateTime(2026, 5, 1), movie.MetaReleaseDate);
        Assert.Equal("Director Name", movie.MetaDirector);
        Assert.Equal("Studio Name", movie.MetaStudio);
        Assert.Equal("Label Name", movie.MetaLabel);
        Assert.Equal("Series Name", movie.MetaSeries);
        Assert.Equal(4.5, movie.MetaRatingScore);
        Assert.Equal(10, movie.MetaRatingVotes);
        Assert.Equal("javinizer-go", movie.MetaSourceName);
        Assert.Equal("http://example.test/source", movie.MetaSourceUrl);
        Assert.NotNull(movie.MetaFetchedAt);
    }

    [Fact]
    public void Apply_TitleBlank_FallsBackToDisplayTitle()
    {
        var movie = new Movie { Code = "ABC-123" };
        var meta = new MovieViewDto { Title = "", DisplayTitle = "Display Title" };

        MovieMetadataMapper.Apply(movie, meta);

        Assert.Equal("Display Title", movie.MetaTitle);
    }

    [Fact]
    public void Apply_CoverUrlBlank_FallsBackToPosterUrl_AndBackdropMirrorsCover()
    {
        var movie = new Movie { Code = "ABC-123" };
        var meta = new MovieViewDto { CoverUrl = "", PosterUrl = "http://example.test/poster.jpg" };

        MovieMetadataMapper.Apply(movie, meta);

        Assert.Equal("http://example.test/poster.jpg", movie.MetaCoverUrl);
        Assert.Equal(movie.MetaCoverUrl, movie.MetaBackdropUrl);
    }

    [Fact]
    public void Apply_ActressesJoined_LastNameFirstNameOrder()
    {
        var movie = new Movie { Code = "ABC-123" };
        var meta = new MovieViewDto
        {
            Actresses =
            [
                new ActressViewDto { FirstName = "Yui", LastName = "Hatano" },
                new ActressViewDto { JapaneseName = "新木 希空" },
            ]
        };

        MovieMetadataMapper.Apply(movie, meta);

        Assert.Equal("Hatano Yui, 新木 希空", movie.MetaActresses);
    }

    [Fact]
    public void Apply_NoActresses_MetaActressesIsNull()
    {
        var movie = new Movie { Code = "ABC-123" };
        var meta = new MovieViewDto { Actresses = [] };

        MovieMetadataMapper.Apply(movie, meta);

        Assert.Null(movie.MetaActresses);
    }

    [Fact]
    public void Apply_GenresJoinedByName()
    {
        var movie = new Movie { Code = "ABC-123" };
        var meta = new MovieViewDto { Genres = [new GenreViewDto { Name = "Drama" }, new GenreViewDto { Name = "Solowork" }] };

        MovieMetadataMapper.Apply(movie, meta);

        Assert.Equal("Drama, Solowork", movie.MetaGenres);
    }

    [Fact]
    public void ResolveTitle_WhenCleanTitleWithoutCode_PrependsDvdId()
    {
        var meta = new MovieViewDto { Title = "Special Training with Beautiful Maid" };
        var title = MovieMetadataMapper.ResolveTitle(meta, "IPX-535");

        Assert.Equal("IPX-535 Special Training with Beautiful Maid", title);
    }

    [Fact]
    public void ResolveTitle_WhenCodeNull_FallsBackToScrapedId()
    {
        var meta = new MovieViewDto { Id = "IPX-535", Title = "Special Training with Beautiful Maid" };
        var title = MovieMetadataMapper.ResolveTitle(meta, null);

        Assert.Equal("IPX-535 Special Training with Beautiful Maid", title);
    }

    [Fact]
    public void ResolveTitle_WhenTitleAlreadyStartsWithCode_DoesNotDuplicate()
    {
        var meta = new MovieViewDto { Title = "IPX-535 Special Training with Beautiful Maid" };
        var title = MovieMetadataMapper.ResolveTitle(meta, "IPX-535");

        Assert.Equal("IPX-535 Special Training with Beautiful Maid", title);
    }

    [Fact]
    public void ResolveTitle_WhenTitleStartsWithCodeCaseInsensitive_DoesNotDuplicate()
    {
        var meta = new MovieViewDto { Title = "ipx-535 Special Training with Beautiful Maid" };
        var title = MovieMetadataMapper.ResolveTitle(meta, "IPX-535");

        Assert.Equal("ipx-535 Special Training with Beautiful Maid", title);
    }

    [Fact]
    public void ResolveTitle_WhenTitleStartsWithScrapedId_DoesNotDuplicate()
    {
        var meta = new MovieViewDto { Id = "IPX-00535", Title = "IPX-00535 Special Training" };
        var title = MovieMetadataMapper.ResolveTitle(meta, "IPX-535");

        Assert.Equal("IPX-00535 Special Training", title);
    }

    [Fact]
    public void ResolveTitle_WhenTitleCodePrefixIsPartOfLongerCode_DoesNotTreatAsMatch()
    {
        var meta = new MovieViewDto { Title = "ABC-12345 The Sequel" };
        var title = MovieMetadataMapper.ResolveTitle(meta, "ABC-12");

        Assert.Equal("ABC-12 ABC-12345 The Sequel", title);
    }

    [Fact]
    public void ResolveTitle_WhenTitleBlank_FallsBackToDisplayTitle()
    {
        var meta = new MovieViewDto { Title = "", DisplayTitle = "Special Training" };
        var title = MovieMetadataMapper.ResolveTitle(meta, "IPX-535");

        Assert.Equal("IPX-535 Special Training", title);
    }

    [Fact]
    public void ResolveTitle_WhenTitleAndDisplayTitleBlank_ReturnsNull()
    {
        var meta = new MovieViewDto { Title = null, DisplayTitle = "   " };
        var title = MovieMetadataMapper.ResolveTitle(meta, "IPX-535");

        Assert.Null(title);
    }

    [Fact]
    public void ResolveTitle_WhenNoCodeOrScrapedIdAvailable_ReturnsTrimmedTitle()
    {
        var meta = new MovieViewDto { Title = "  Some Clean Title  " };
        var title = MovieMetadataMapper.ResolveTitle(meta, null);

        Assert.Equal("Some Clean Title", title);
    }

    // Javinizer-go aggregates cast from several scrapers, which can list the same
    // actress in both name orderings, as a single combined field, or twice.
    [Fact]
    public void FormatActresses_CollapsesOrderPermutationsAndExactDuplicates()
    {
        List<ActressViewDto> actresses =
        [
            new() { LastName = "Yura", FirstName = "Kana" },
            new() { LastName = "Kana", FirstName = "Yura" },
            new() { LastName = "Kana", FirstName = "Yura" },
        ];

        Assert.Equal("Yura Kana", MovieMetadataMapper.FormatActresses(actresses));
    }

    [Fact]
    public void FormatActresses_CollapsesCombinedSingleFieldName()
    {
        List<ActressViewDto> actresses =
        [
            new() { LastName = "Yura", FirstName = "Kana" },
            new() { FirstName = "KANA  YURA" },
        ];

        Assert.Equal("Yura Kana", MovieMetadataMapper.FormatActresses(actresses));
    }

    [Fact]
    public void FormatActresses_CollapsesSameDmmIdOrJapaneseName()
    {
        List<ActressViewDto> actresses =
        [
            new() { DmmId = 1044099, LastName = "Yura", FirstName = "Kana" },
            new() { DmmId = 1044099, JapaneseName = "由良かな" },
            new() { LastName = "Mikami", FirstName = "Yua", JapaneseName = "三上悠亜" },
            new() { JapaneseName = "三上悠亜" },
        ];

        Assert.Equal("Yura Kana, Mikami Yua", MovieMetadataMapper.FormatActresses(actresses));
    }

    [Fact]
    public void FormatActresses_KeepsDistinctActressesInOrder()
    {
        List<ActressViewDto> actresses =
        [
            new() { LastName = "Yura", FirstName = "Kana" },
            new() { LastName = "Yura", FirstName = "Ai" },
            new() { JapaneseName = "三上悠亜" },
        ];

        Assert.Equal("Yura Kana, Yura Ai, 三上悠亜", MovieMetadataMapper.FormatActresses(actresses));
    }

    [Fact]
    public void FormatActresses_SkipsNamelessEntries()
    {
        List<ActressViewDto> actresses = [new() { DmmId = 5 }, new() { LastName = "Yura", FirstName = "Kana" }];

        Assert.Equal("Yura Kana", MovieMetadataMapper.FormatActresses(actresses));
        Assert.Null(MovieMetadataMapper.FormatActresses([new ActressViewDto { DmmId = 5 }]));
    }
}
