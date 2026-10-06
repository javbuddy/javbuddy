using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Tags;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class MovieMetadataRefreshModalTests : BunitContext
{
    private static MovieViewDto CreateScrapedMovie() => new()
    {
        Title = "New Title",
        OriginalTitle = "New Original Title",
        Description = "New overview.",
        ReleaseDate = new DateTime(2024, 6, 1),
        Director = "New Director",
        Maker = "New Studio",
        Label = "New Label",
        Series = "New Series",
        Runtime = 130,
        CoverUrl = "https://example.com/new-cover.jpg",
        Actresses = [new ActressViewDto { FirstName = "Yua", LastName = "Mikami" }],
        Genres = [new GenreViewDto { Name = "Drama" }]
    };

    [Fact]
    public void Modal_WhenShowFalse_RendersNothing()
    {
        var movieService = Substitute.For<IMovieService>();
        Services.AddSingleton(movieService);

        var movie = new Movie { Id = 1, Code = "ABC-100" };
        var cut = Render<MovieMetadataRefreshModal>(p => p
            .Add(x => x.Show, false)
            .Add(x => x.Movie, movie));

        Assert.Empty(cut.FindAll(".metadata-refresh-backdrop"));
    }

    [Fact]
    public async Task Modal_WhenShowTrue_FetchesAndRendersFieldDiff()
    {
        var movieService = Substitute.For<IMovieService>();
        var movie = new Movie { Id = 1, Code = "ABC-100", MetaTitle = "Old Title", MetaStudio = "Old Studio" };
        var scraped = CreateScrapedMovie();
        movieService.FetchJavinizerMetadataAsync(1, Arg.Any<CancellationToken>())
            .Returns(new MovieMetadataFetchResult(true, null, scraped));

        Services.AddSingleton(movieService);

        var cut = Render<MovieMetadataRefreshModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Movie, movie));

        await movieService.Received(1).FetchJavinizerMetadataAsync(1, Arg.Any<CancellationToken>());

        var title = cut.Find(".metadata-refresh-title");
        Assert.Contains("ABC-100", title.TextContent);

        var titleField = cut.Find("#field-title").Closest(".import-field-item");
        Assert.NotNull(titleField);
        Assert.Contains("ABC-100 New Title", titleField!.TextContent);
        Assert.Contains("Old Title", titleField.TextContent);
    }

    [Fact]
    public async Task Modal_FetchFails_ShowsErrorAndNoFieldPicker()
    {
        var movieService = Substitute.For<IMovieService>();
        var movie = new Movie { Id = 1, Code = "ABC-100" };
        movieService.FetchJavinizerMetadataAsync(1, Arg.Any<CancellationToken>())
            .Returns(new MovieMetadataFetchResult(false, "javinizer-go is not configured.", null));

        Services.AddSingleton(movieService);

        var cut = Render<MovieMetadataRefreshModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Movie, movie));

        var alert = cut.Find(".alert-danger");
        Assert.Contains("javinizer-go is not configured.", alert.TextContent);
        Assert.Empty(cut.FindAll(".import-fields-table"));
    }

    [Fact]
    public async Task Modal_DiffersFieldsArePreselected_UnchangedFieldsAreNot()
    {
        var movieService = Substitute.For<IMovieService>();
        // Title differs from the scrape; Studio matches it exactly already.
        var movie = new Movie { Id = 1, Code = "ABC-100", MetaTitle = "Old Title", MetaStudio = "New Studio" };
        var scraped = CreateScrapedMovie();
        movieService.FetchJavinizerMetadataAsync(1, Arg.Any<CancellationToken>())
            .Returns(new MovieMetadataFetchResult(true, null, scraped));

        Services.AddSingleton(movieService);

        var cut = Render<MovieMetadataRefreshModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Movie, movie));

        var titleCheckbox = cut.Find("#field-title");
        var studioCheckbox = cut.Find("#field-studio");
        Assert.True(titleCheckbox.HasAttribute("checked"));
        Assert.False(studioCheckbox.HasAttribute("checked"));
    }

    [Fact]
    public async Task Modal_KeepAllCurrent_UnchecksEveryField()
    {
        var movieService = Substitute.For<IMovieService>();
        var movie = new Movie { Id = 1, Code = "ABC-100" };
        var scraped = CreateScrapedMovie();
        movieService.FetchJavinizerMetadataAsync(1, Arg.Any<CancellationToken>())
            .Returns(new MovieMetadataFetchResult(true, null, scraped));

        Services.AddSingleton(movieService);

        var cut = Render<MovieMetadataRefreshModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Movie, movie));

        Assert.NotEmpty(cut.FindAll("input.form-check-input:checked"));

        var keepCurrentBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Keep All Current"));
        await cut.InvokeAsync(() => keepCurrentBtn.Click());

        Assert.Empty(cut.FindAll("input.form-check-input:checked"));

        var applyBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Apply Selected"));
        Assert.True(applyBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Modal_ApplySelected_CallsServiceWithChosenScrapeAndOptionsThenInvokesOnApplied()
    {
        var movieService = Substitute.For<IMovieService>();
        var movie = new Movie { Id = 1, Code = "ABC-100" };
        var scraped = CreateScrapedMovie();
        movieService.FetchJavinizerMetadataAsync(1, Arg.Any<CancellationToken>())
            .Returns(new MovieMetadataFetchResult(true, null, scraped));
        movieService.ApplyJavinizerMetadataAsync(1, scraped, Arg.Any<MovieMetadataImportOptions>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok());

        Services.AddSingleton(movieService);

        var applied = false;
        var cut = Render<MovieMetadataRefreshModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Movie, movie)
            .Add(x => x.OnApplied, () => applied = true));

        var applyBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Apply Selected"));
        await cut.InvokeAsync(() => applyBtn.Click());

        await movieService.Received(1).ApplyJavinizerMetadataAsync(1, scraped, Arg.Any<MovieMetadataImportOptions>(), Arg.Any<CancellationToken>());
        Assert.True(applied);
    }

    [Fact]
    public async Task Modal_ApplyFails_ShowsErrorAndKeepsModalOpen()
    {
        var movieService = Substitute.For<IMovieService>();
        var movie = new Movie { Id = 1, Code = "ABC-100" };
        var scraped = CreateScrapedMovie();
        movieService.FetchJavinizerMetadataAsync(1, Arg.Any<CancellationToken>())
            .Returns(new MovieMetadataFetchResult(true, null, scraped));
        movieService.ApplyJavinizerMetadataAsync(1, scraped, Arg.Any<MovieMetadataImportOptions>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Fail("Movie not found."));

        Services.AddSingleton(movieService);

        var closed = false;
        var cut = Render<MovieMetadataRefreshModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Movie, movie)
            .Add(x => x.OnClose, () => closed = true));

        var applyBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Apply Selected"));
        await cut.InvokeAsync(() => applyBtn.Click());

        var alert = cut.Find(".alert-danger");
        Assert.Contains("Movie not found.", alert.TextContent);
        Assert.False(closed);
    }

    [Fact]
    public void Modal_WhenScrapedTitleAlreadyStartsWithDvdId_DoesNotDuplicateInDiff()
    {
        var movieService = Substitute.For<IMovieService>();
        var movie = new Movie { Id = 1, Code = "ABC-100", MetaTitle = "Old Title" };
        var scraped = CreateScrapedMovie();
        scraped.Title = "ABC-100 Already Has Code";
        movieService.FetchJavinizerMetadataAsync(1, Arg.Any<CancellationToken>())
            .Returns(new MovieMetadataFetchResult(true, null, scraped));

        Services.AddSingleton(movieService);

        var cut = Render<MovieMetadataRefreshModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Movie, movie));

        var titleField = cut.Find("#field-title").Closest(".import-field-item");
        Assert.NotNull(titleField);
        Assert.Contains("ABC-100 Already Has Code", titleField!.TextContent);
        Assert.DoesNotContain("ABC-100 ABC-100", titleField.TextContent);
    }
}
