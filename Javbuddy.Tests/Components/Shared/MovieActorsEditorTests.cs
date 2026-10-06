using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class MovieActorsEditorTests : BunitContext
{
    private readonly TestDbContextFactory factory = new();
    private readonly IActorService actorService = Substitute.For<IActorService>();
    private readonly int movieId;
    private readonly int yuaId;
    private readonly int remuId;

    public MovieActorsEditorTests()
    {
        Services.AddSingleton<IMovieService>(new MovieService(factory));
        Services.AddSingleton<IMovieDetailQueryService>(new MovieDetailQueryService(factory));
        Services.AddSingleton(actorService);

        using var db = factory.CreateDbContext();
        var yua = new Actor { FirstName = "Yua", LastName = "Mikami" };
        var remu = new Actor { FirstName = "Remu", LastName = "Suzumori" };
        var movie = new Movie { Code = "ABC-123", MetaActresses = "Mikami Yua, Unknown Person" };
        db.Movies.Add(movie);
        db.Actors.AddRange(yua, remu);
        db.SaveChanges();
        db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = yua.Id });
        db.SaveChanges();
        movieId = movie.Id;
        yuaId = yua.Id;
        remuId = remu.Id;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            factory.Dispose();
        }
    }

    private int changedCount;

    private IRenderedComponent<MovieActorsEditor> RenderEditor() =>
        Render<MovieActorsEditor>(p => p
            .Add(x => x.MovieId, movieId)
            .Add(x => x.OnChanged, () => changedCount++));

    private static string ItemName(AngleSharp.Dom.IElement item) => item.QuerySelector(".movie-actors-name")!.TextContent.Trim();

    private async Task<string?> MetaActressesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Movies.Where(m => m.Id == movieId).Select(m => m.MetaActresses).SingleAsync();
    }

    [Fact]
    public async Task IsCollapsedByDefault_AndExpandingShowsTheCast()
    {
        var cut = RenderEditor();
        Assert.Empty(cut.FindAll(".movie-actors-item"));
        Assert.Empty(cut.FindAll(".cast-search-input"));

        await cut.Find(".movie-actors-toggle").ClickAsync();

        var items = cut.FindAll(".movie-actors-item");
        Assert.Equal(["Mikami Yua", "Unknown Person"], items.Select(ItemName));
        Assert.DoesNotContain("movie-actors-item-unmatched", items[0].ClassList);
        Assert.Contains("movie-actors-item-unmatched", items[1].ClassList);
        Assert.NotNull(items[1].QuerySelector(".movie-actors-warning-badge"));
        Assert.NotEmpty(cut.FindAll(".cast-search-input"));
        Assert.Contains("(2)", cut.Find(".movie-actors-toggle").TextContent);
    }

    [Fact]
    public async Task Avatars_ShowThePortraitWhenThereIsOne_AndInitialsOtherwise()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ActorImages.Add(new ActorImage { ActorId = yuaId, Variant = "thumb", SourceMovieCode = "X", StorageId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        var cut = RenderEditor();
        await cut.Find(".movie-actors-toggle").ClickAsync();

        var items = cut.FindAll(".movie-actors-item");
        var img = items[0].QuerySelector(".movie-actors-avatar img");
        Assert.NotNull(img);
        Assert.StartsWith($"/actor-image/{yuaId}/thumb?v=", img.GetAttribute("src"));
        Assert.Null(items[1].QuerySelector(".movie-actors-avatar img"));
        Assert.Equal("UP", items[1].QuerySelector(".movie-actors-avatar")!.TextContent.Trim());
    }

    [Fact]
    public async Task Search_AddsTheChosenActorToTheMovie_AndHidesTheExistingCast()
    {
        actorService.SearchActorsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([new ActorSearchItem(yuaId, "Mikami Yua", false), new ActorSearchItem(remuId, "Suzumori Remu", false)]);
        var cut = RenderEditor();
        await cut.Find(".movie-actors-toggle").ClickAsync();

        await cut.Find(".cast-search-input").InputAsync("u");
        var candidate = Assert.Single(cut.FindAll(".cast-search-candidate"));
        await candidate.ClickAsync();

        Assert.Contains("Suzumori Remu", cut.FindAll(".movie-actors-item").Select(ItemName));
        Assert.Equal(1, changedCount);
        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.MovieActors.AnyAsync(ma => ma.MovieId == movieId && ma.ActorId == remuId));
    }

    [Fact]
    public async Task RemoveButton_OnATrackedActor_RemovesThemFromTheMovie()
    {
        var cut = RenderEditor();
        await cut.Find(".movie-actors-toggle").ClickAsync();

        await cut.Find("[aria-label='Remove Mikami Yua']").ClickAsync();

        Assert.Equal(["Unknown Person"], cut.FindAll(".movie-actors-item").Select(ItemName));
        Assert.Equal(1, changedCount);
        Assert.Equal("Unknown Person", await MetaActressesAsync());
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.MovieActors.AnyAsync(ma => ma.MovieId == movieId));
    }

    [Fact]
    public async Task RemoveButton_OnAnUnmatchedName_RemovesItFromTheCastText()
    {
        var cut = RenderEditor();
        await cut.Find(".movie-actors-toggle").ClickAsync();

        await cut.Find("[aria-label='Remove Unknown Person']").ClickAsync();

        Assert.Equal(["Mikami Yua"], cut.FindAll(".movie-actors-item").Select(ItemName));
        Assert.Equal(1, changedCount);
        Assert.Equal("Mikami Yua", await MetaActressesAsync());
    }

    [Fact]
    public async Task FailedEdit_ShowsTheError_AndDoesNotRaiseOnChanged()
    {
        var cut = RenderEditor();
        await cut.Find(".movie-actors-toggle").ClickAsync();
        // The cast changed elsewhere after the chips were loaded.
        await new MovieService(factory).RemoveActorFromMovieAsync(movieId, "Unknown Person");

        await cut.Find("[aria-label='Remove Unknown Person']").ClickAsync();

        Assert.Equal("Actor is not on this movie.", cut.Find(".movie-actors-error").TextContent);
        Assert.Equal(0, changedCount);
        Assert.Equal(["Mikami Yua"], cut.FindAll(".movie-actors-item").Select(ItemName));
    }
}
