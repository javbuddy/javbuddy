using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Models;
using Javbuddy.Services.Common;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.MovieDetailSections;

public class MovieDetailToolbarTests : BunitContext
{
    private readonly IMovieService movieService = Substitute.For<IMovieService>();
    private readonly IMovieRescanService rescanService = Substitute.For<IMovieRescanService>();
    private readonly IMoviePosterGenerationService posterGeneration = Substitute.For<IMoviePosterGenerationService>();
    private readonly List<MovieDetailDialog> opened = [];
    private readonly List<MovieParts> reloaded = [];
    private readonly List<MovieNotice?> notices = [];

    public MovieDetailToolbarTests()
    {
        Services.AddSingleton(movieService);
        Services.AddSingleton(rescanService);
        Services.AddSingleton(posterGeneration);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static Movie GotMovie() => new() { Id = 7, Code = "ABC-123", Status = MovieStatus.Got, MediaDurationSeconds = 600 };

    private IRenderedComponent<MovieDetailToolbar> RenderToolbar(Movie? movie = null, bool interactive = true, bool loadingRawNfo = false) =>
        Render<MovieDetailToolbar>(p => p
            .Add(x => x.Movie, movie ?? GotMovie())
            .Add(x => x.BackHref, "/actors/Yua")
            .Add(x => x.BackLabel, "Yua")
            .Add(x => x.Interactive, interactive)
            .Add(x => x.LoadingRawNfo, loadingRawNfo)
            .Add(x => x.OnOpenDialog, d => opened.Add(d))
            .Add(x => x.OnReload, parts => reloaded.Add(parts))
            .Add(x => x.OnNotice, n => notices.Add(n)));

    private static void OpenMenu(IRenderedComponent<MovieDetailToolbar> cut, string menu) =>
        cut.FindAll("button.toolbar-menu-btn").Single(b => b.TextContent.Trim() == menu).Click();

    private static AngleSharp.Dom.IElement MenuItem(IRenderedComponent<MovieDetailToolbar> cut, string item) =>
        cut.FindAll(".toolbar-menu-item").Single(b => b.TextContent.Trim() == item);

    private static AngleSharp.Dom.IElement ToolbarButton(IRenderedComponent<MovieDetailToolbar> cut, string text) =>
        cut.FindAll("button.page-toolbar-btn").Single(b => b.TextContent.Trim().Contains(text));

    [Fact]
    public void BackButton_LinksToTheGivenPlaceWithItsLabel()
    {
        var cut = RenderToolbar();

        var back = cut.Find("a.page-toolbar-btn");
        Assert.Equal("/actors/Yua", back.GetAttribute("href"));
        Assert.Contains("Yua", back.TextContent);
    }

    [Theory]
    [InlineData("Search Prowlarr", MovieDetailDialog.Search)]
    [InlineData("Edit metadata", MovieDetailDialog.MetadataEditor)]
    [InlineData("Media Info", MovieDetailDialog.MediaInfo)]
    [InlineData("Delete", MovieDetailDialog.DeleteConfirm)]
    public void ToolbarButtons_AskThePageToOpenTheirDialog(string label, MovieDetailDialog expected)
    {
        var cut = RenderToolbar();

        var button = cut.FindAll("button.page-toolbar-btn")
            .First(b => b.GetAttribute("aria-label") == label || b.TextContent.Trim() == label);
        button.Click();

        Assert.Equal([expected], opened);
    }

    [Theory]
    [InlineData("Refresh", "Refresh with Javinizer", MovieDetailDialog.MetadataRefresh)]
    [InlineData("Tools", "Crop Cover", MovieDetailDialog.CoverCrop)]
    [InlineData("Tools", "Edit .nfo", MovieDetailDialog.NfoEditor)]
    [InlineData("Tools", "Generate stills", MovieDetailDialog.Stills)]
    [InlineData("Tools", "Repair Video", MovieDetailDialog.VideoRepair)]
    public void MenuItems_AskThePageToOpenTheirDialog(string menu, string item, MovieDetailDialog expected)
    {
        var cut = RenderToolbar();
        OpenMenu(cut, menu);

        MenuItem(cut, item).Click();

        Assert.Equal([expected], opened);
    }

    [Fact]
    public void BeforeTheCircuitIsInteractive_TheDialogButtonsAreDisabled()
    {
        var cut = RenderToolbar(interactive: false);

        Assert.True(ToolbarButton(cut, "Edit").HasAttribute("disabled"));
        Assert.True(ToolbarButton(cut, "Media Info").HasAttribute("disabled"));
        Assert.True(ToolbarButton(cut, "Favorite").HasAttribute("disabled"));
        OpenMenu(cut, "Tools");
        Assert.True(MenuItem(cut, "Edit .nfo").HasAttribute("disabled"));
    }

    [Fact]
    public void AMovieWithoutACode_CannotBeSearchedOrRescanned()
    {
        var movie = GotMovie();
        movie.Code = "";
        var cut = RenderToolbar(movie);

        Assert.True(cut.Find("button[aria-label='Search Prowlarr']").HasAttribute("disabled"));
        OpenMenu(cut, "Refresh");
        Assert.True(MenuItem(cut, "Rescan").HasAttribute("disabled"));
    }

    [Fact]
    public void AMissingMovie_HasNoMediaInfoNfoOrRepair()
    {
        var movie = GotMovie();
        movie.Status = MovieStatus.Missing;
        var cut = RenderToolbar(movie);

        Assert.DoesNotContain(cut.FindAll("button.page-toolbar-btn"), b => b.TextContent.Contains("Media Info"));
        OpenMenu(cut, "Tools");
        var items = cut.FindAll(".toolbar-menu-item").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(["Crop Cover"], items);
        Assert.Contains("Mark as Got", cut.Markup);
    }

    [Fact]
    public void GenerateStills_NeedsADuration()
    {
        var movie = GotMovie();
        movie.MediaDurationSeconds = null;
        var cut = RenderToolbar(movie);
        OpenMenu(cut, "Tools");

        Assert.True(MenuItem(cut, "Generate stills").HasAttribute("disabled"));
    }

    [Fact]
    public void WhileTheNfoLoads_TheToolsMenuSaysSo_AndEditNfoIsDisabled()
    {
        var cut = RenderToolbar(loadingRawNfo: true);

        Assert.Contains(cut.FindAll("button.toolbar-menu-btn"), b => b.TextContent.Trim() == "Loading…");
        OpenMenu(cut, "Loading…");
        Assert.True(MenuItem(cut, "Loading…").HasAttribute("disabled"));
    }

    [Fact]
    public void CreateNfo_IsOnlyOfferedForAMovieWhoseMetadataWasGenerated()
    {
        var movie = GotMovie();
        var plain = RenderToolbar(movie);
        OpenMenu(plain, "Tools");
        Assert.DoesNotContain(plain.FindAll(".toolbar-menu-item"), b => b.TextContent.Contains("Create .nfo"));

        movie.MetaSourceName = LocalLibraryMetadataMapper.GeneratedSourceName;
        var generated = RenderToolbar(movie);
        OpenMenu(generated, "Tools");
        Assert.Contains(generated.FindAll(".toolbar-menu-item"), b => b.TextContent.Contains("Create .nfo"));
    }

    [Fact]
    public async Task Rescan_RescansTheMovie_ThenAsksForEveryPartAgain()
    {
        var cut = RenderToolbar();
        OpenMenu(cut, "Refresh");

        await cut.InvokeAsync(() => MenuItem(cut, "Rescan").Click());

        await rescanService.Received(1).RescanAsync(7, Arg.Any<CancellationToken>());
        Assert.Equal([MovieParts.All], reloaded);
    }

    [Fact]
    public async Task GeneratePosterAndFanart_ClearsTheNotice_ReportsTheOutcome_AndReloadsThePosterOnSuccess()
    {
        posterGeneration.GenerateAsync(7, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        var cut = RenderToolbar();
        OpenMenu(cut, "Tools");

        await cut.InvokeAsync(() => MenuItem(cut, "Generate poster & fanart").Click());

        Assert.Equal([null, new MovieNotice("Poster and fanart generated from the video.", false)], notices);
        Assert.Equal([MovieParts.Poster], reloaded);
    }

    [Fact]
    public async Task GeneratePosterAndFanart_Failure_ReportsAWarning_WithoutReloading()
    {
        posterGeneration.GenerateAsync(7, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Fail("No video."));
        var cut = RenderToolbar();
        OpenMenu(cut, "Tools");

        await cut.InvokeAsync(() => MenuItem(cut, "Generate poster & fanart").Click());

        Assert.Equal(new MovieNotice("No video.", true), notices.Last());
        Assert.Empty(reloaded);
    }

    [Fact]
    public async Task CreateNfo_ReportsTheOutcome_AndReloadsThePosterOnSuccess()
    {
        var movie = GotMovie();
        movie.MetaSourceName = LocalLibraryMetadataMapper.GeneratedSourceName;
        movieService.CreateNfoAsync(7, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        var cut = RenderToolbar(movie);
        OpenMenu(cut, "Tools");

        await cut.InvokeAsync(() => MenuItem(cut, "Create .nfo").Click());

        Assert.Equal([new MovieNotice("Created the .nfo from the stored metadata.", false)], notices);
        Assert.Equal([MovieParts.Poster], reloaded);
    }

    [Fact]
    public async Task Favorite_StoresTheReturnedState_ShowsItOnTheButton_AndDoesNotReload()
    {
        movieService.ToggleFavoriteAsync(7, Arg.Any<CancellationToken>()).Returns(true);
        var movie = GotMovie();
        var cut = RenderToolbar(movie);

        await cut.InvokeAsync(() => ToolbarButton(cut, "Favorite").Click());

        Assert.True(movie.IsFavorite);
        Assert.NotNull(movie.FavoritedAt);
        Assert.Contains("Favorited", ToolbarButton(cut, "Favorited").TextContent);
        Assert.Empty(reloaded);
    }

    [Fact]
    public async Task MarkAsMissing_TogglesTheStatus_ThenReloadsOnlyTheCore()
    {
        var cut = RenderToolbar();

        await cut.InvokeAsync(() => ToolbarButton(cut, "Mark as Missing").Click());

        await movieService.Received(1).ToggleStatusAsync(7, Arg.Any<CancellationToken>());
        Assert.Equal([MovieParts.Core], reloaded);
    }
}
