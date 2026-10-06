using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Tags;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class MovieTagsEditorTests : BunitContext
{
    private readonly TestDbContextFactory factory = new();
    private readonly TagService tagService;
    private readonly int movieId;

    public MovieTagsEditorTests()
    {
        var nfoSync = Substitute.For<INfoSyncService>();
        nfoSync.CheckMovieNfoConflictAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ActorNfoConflictCheckResult(ci.ArgAt<int>(0), "", false, null));
        tagService = new TagService(factory, nfoSync);
        Services.AddSingleton<ITagService>(tagService);
        Services.AddSingleton<IMovieDetailQueryService>(new MovieDetailQueryService(factory));

        using var db = factory.CreateDbContext();
        var movie = new Movie { Code = "ABC-123" };
        db.Movies.Add(movie);
        db.SaveChanges();
        movieId = movie.Id;
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

    private IRenderedComponent<MovieTagsEditor> RenderEditor() =>
        Render<MovieTagsEditor>(p => p
            .Add(x => x.MovieId, movieId)
            .Add(x => x.OnChanged, () => changedCount++));

    private async Task<int> CreateTagAsync(string name)
    {
        var result = await tagService.CreateTagAsync(name);
        return result.Tag!.Id;
    }

    [Fact]
    public async Task IsCollapsedByDefault_AndExpandingShowsTheMovieTags()
    {
        var tagId = await CreateTagAsync("Drama");
        await tagService.AddTagToMovieAsync(movieId, tagId);

        var cut = RenderEditor();
        Assert.Empty(cut.FindAll(".movie-tags-chip"));
        Assert.Empty(cut.FindAll(".tag-search-input"));

        await cut.Find(".movie-tags-toggle").ClickAsync();

        Assert.Equal("Drama", cut.Find(".movie-tags-chip").TextContent.Replace("×", "").Trim());
        Assert.NotEmpty(cut.FindAll(".tag-search-input"));
    }

    [Fact]
    public async Task Search_AddsTheChosenTagToTheMovie()
    {
        await CreateTagAsync("Outdoor");
        var cut = RenderEditor();
        await cut.Find(".movie-tags-toggle").ClickAsync();

        await cut.Find(".tag-search-input").InputAsync("Outd");
        await cut.Find(".tag-search-candidate").ClickAsync();

        Assert.Contains("Outdoor", cut.Find(".movie-tags-chip").TextContent);
        Assert.Equal(1, changedCount);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Single(await db.MovieTags.Where(mt => mt.MovieId == movieId).ToListAsync());
    }

    [Fact]
    public async Task RemoveButton_RemovesTheTagFromTheMovie()
    {
        var tagId = await CreateTagAsync("Drama");
        await tagService.AddTagToMovieAsync(movieId, tagId);
        var cut = RenderEditor();
        await cut.Find(".movie-tags-toggle").ClickAsync();

        await cut.Find(".movie-tags-chip-remove").ClickAsync();

        Assert.Empty(cut.FindAll(".movie-tags-chip"));
        Assert.Equal(1, changedCount);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.MovieTags.Where(mt => mt.MovieId == movieId).ToListAsync());
    }

    [Fact]
    public async Task Refresh_ShowsTagsAddedElsewhere_OnceLoaded()
    {
        var cut = RenderEditor();
        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());
        await cut.Find(".movie-tags-toggle").ClickAsync();
        Assert.Empty(cut.FindAll(".movie-tags-chip"));

        await tagService.AddTagToMovieAsync(movieId, await CreateTagAsync("Creampie"));
        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        Assert.Equal("Creampie", cut.Find(".movie-tags-chip").TextContent.Replace("×", "").Trim());
        Assert.Equal("Movie tags (1)", cut.Find(".movie-tags-toggle").TextContent.Replace("▾", "").Trim());
    }

    private async Task<int> SeedClipOnlyTagAsync(string name, bool alsoExplicit)
    {
        var tagId = await CreateTagAsync(name);
        await using var db = await factory.CreateDbContextAsync();
        db.MovieTags.Add(new MovieTag { MovieId = movieId, TagId = tagId, IsExplicit = alsoExplicit, FromClips = true });
        await db.SaveChangesAsync();
        return tagId;
    }

    [Fact]
    public async Task ATagOnlyTheClipsCarry_ShowsAsAnImpliedChip_WithoutARemoveButton()
    {
        await SeedClipOnlyTagAsync("Squirt", alsoExplicit: false);
        var cut = RenderEditor();
        await cut.Find(".movie-tags-toggle").ClickAsync();

        Assert.Empty(cut.FindAll(".movie-tags-chip"));
        var chip = cut.Find(".implicit-chip");
        Assert.Equal("Squirt", chip.TextContent);
        Assert.Equal("From scenes, highlights or apexes", chip.GetAttribute("title"));
        Assert.Empty(cut.FindAll(".movie-tags-chip-remove"));
        Assert.Equal("Movie tags (1)", cut.Find(".movie-tags-toggle").TextContent.Replace("▾", "").Trim());
    }

    [Fact]
    public async Task RemovingAnExplicitTagTheClipsAlsoCarry_LeavesItImplied()
    {
        await SeedClipOnlyTagAsync("Squirt", alsoExplicit: true);
        var cut = RenderEditor();
        await cut.Find(".movie-tags-toggle").ClickAsync();
        Assert.Empty(cut.FindAll(".implicit-chip"));

        await cut.Find(".movie-tags-chip-remove").ClickAsync();

        Assert.Empty(cut.FindAll(".movie-tags-chip"));
        Assert.Equal("Squirt", cut.Find(".implicit-chip").TextContent);
    }

    [Fact]
    public async Task AnExplicitTagTheClipsAlsoCarry_IsMarkedRedundant_APlainOneIsNot()
    {
        await SeedClipOnlyTagAsync("Squirt", alsoExplicit: true);
        await tagService.AddTagToMovieAsync(movieId, await CreateTagAsync("Drama"));
        var cut = RenderEditor();
        await cut.Find(".movie-tags-toggle").ClickAsync();

        var chips = cut.FindAll(".movie-tags-chip");
        var drama = chips.Single(c => c.TextContent.Contains("Drama"));
        var squirt = chips.Single(c => c.TextContent.Contains("Squirt"));
        Assert.Null(drama.QuerySelector(".redundant-mark"));
        var mark = squirt.QuerySelector(".redundant-mark")!;
        Assert.Equal("redundant", mark.TextContent);
        Assert.StartsWith("Also from its scenes, highlights or apexes.", mark.GetAttribute("title"));
    }
}
