using Bunit;
using Javbuddy.Components.Pages.ActorDetailSections;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.ActorDetailSections;

public class ActorClipsSectionTests : BunitContext
{
    private readonly ISceneWallQueryService wall = Substitute.For<ISceneWallQueryService>();
    private readonly ISceneMediaService media = Substitute.For<ISceneMediaService>();
    private readonly IHighlightMediaService highlightMedia = Substitute.For<IHighlightMediaService>();
    private readonly IApexMediaService apexMedia = Substitute.For<IApexMediaService>();
    private readonly BunitJSModuleInterop clipCardModule;

    private static SceneWallCard Scene(int id, long? thumb = 111) =>
        new(id, id * 10, $"ABC-{id:000}", "Title", $"Scene {id}", 0, 60, ["Yua"], [], false, thumb, thumb) { HasLocalVideo = true };

    public ActorClipsSectionTests()
    {
        Services.AddSingleton(wall);
        Services.AddSingleton(media);
        Services.AddSingleton(highlightMedia);
        Services.AddSingleton(apexMedia);
        Services.AddSingleton(Substitute.For<IJellyfinClient>());
        Services.AddSingleton(Substitute.For<IMovieHighlightService>());
        Services.AddSingleton(Substitute.For<IMovieApexService>());
        Services.AddSingleton(StreamServiceStubs.AllPlayable());
        Services.AddSingleton(Substitute.For<ITrickplayService>());
        Services.AddSingleton(new TrickplayGenerationTracker());
        Services.AddSingleton(Substitute.For<IHighlightTrickplayService>());
        Services.AddSingleton(new SceneMediaQueue(Substitute.For<IServiceScopeFactory>(), NullLogger<SceneMediaQueue>.Instance));
        clipCardModule = JSInterop.SetupModule("./Components/Shared/ClipCard.razor.js");
        clipCardModule.Mode = JSRuntimeMode.Loose;
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<ActorClipsSection> RenderSection(PlayerClipKind kind, int actorId = 7) =>
        Render<ActorClipsSection>(p => p.Add(c => c.ActorId, actorId).Add(c => c.Kind, kind));

    [Fact]
    public void WithoutClips_RendersNothing()
    {
        wall.GetHighlightPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new HighlightWallPage([], 0));

        var cut = RenderSection(PlayerClipKind.Highlight);

        Assert.Empty(cut.FindAll(".actor-clips"));
        Assert.Empty(cut.FindComponents<ClipPlayerModal>());
    }

    [Fact]
    public void LoadsTheActorsFirstPage_NewestReleaseFirst_AndShowsTheCount()
    {
        wall.GetPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new SceneWallPage([Scene(1), Scene(2)], 2));

        var cut = RenderSection(PlayerClipKind.Scene);

        Assert.Equal("Scenes (2)", cut.Find(".actor-clips h2").TextContent);
        Assert.Equal(2, cut.FindAll(".scene-wall-card").Count);
        Assert.Empty(cut.FindAll(".actor-clips-more"));
        wall.Received().GetPageAsync(Arg.Is<SceneWallFilter>(f => f.ActorIds!.SequenceEqual(new[] { 7 }) && !f.IncludeHidden),
            Arg.Is(SceneWallSort.ReleaseDate), Arg.Any<int>(), Arg.Is(0), Arg.Is(ActorClipsSection.PageSize), Arg.Any<CancellationToken>());
        Assert.Single(clipCardModule.Invocations, i => i.Identifier == "preventPlainCardClicks");
        Assert.Single(clipCardModule.Invocations, i => i.Identifier == "measureTagOverflow");
    }

    [Fact]
    public void ShowMore_AppendsTheNextPage()
    {
        var first = Enumerable.Range(1, ActorClipsSection.PageSize).Select(i => Scene(i)).ToList();
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(0), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage(first, ActorClipsSection.PageSize + 1));
        wall.GetPageAsync(Arg.Any<SceneWallFilter>(), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Is(ActorClipsSection.PageSize), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([Scene(99)], ActorClipsSection.PageSize + 1));

        var cut = RenderSection(PlayerClipKind.Scene);
        Assert.Contains("1 more", cut.Find(".actor-clips-more button").TextContent);

        cut.Find(".actor-clips-more button").Click();

        Assert.Equal(ActorClipsSection.PageSize + 1, cut.FindAll(".scene-wall-card").Count);
        Assert.Empty(cut.FindAll(".actor-clips-more"));
    }

    [Fact]
    public void ClickingACard_PlaysItsClip()
    {
        var apex = new ApexWallCard(3, 30, "ABC-003", "Title", "Yua — Squirt", 300, ["Yua"], [], false, 5) { HasLocalVideo = true, DurationSeconds = 3600 };
        wall.GetApexPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new ApexWallPage([apex], 1));

        var cut = RenderSection(PlayerClipKind.Apex);
        Assert.Equal("Apexes (1)", cut.Find(".actor-clips h2").TextContent);

        cut.Find(".scene-wall-card").Click();

        Assert.Equal(PlayerClip.FromApex(apex), cut.FindComponent<ClipPlayerModal>().Instance.Clip);
    }

    [Fact]
    public void CardsMissingMedia_QueueTheirMovie()
    {
        var highlight = new HighlightWallCard(4, 40, "ABC-004", "Title", "Highlight 1", 10, 20, [], [], false, null, null);
        wall.GetHighlightPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new HighlightWallPage([highlight], 1));

        RenderSection(PlayerClipKind.Highlight);

        highlightMedia.Received().EnsureQueuedAsync(40, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AnotherActor_ReloadsTheSection()
    {
        wall.GetPageAsync(Arg.Is<SceneWallFilter>(f => f.ActorIds!.Contains(7)), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([Scene(1)], 1));
        wall.GetPageAsync(Arg.Is<SceneWallFilter>(f => f.ActorIds!.Contains(8)), Arg.Any<SceneWallSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SceneWallPage([], 0));

        var cut = RenderSection(PlayerClipKind.Scene);
        Assert.Single(cut.FindAll(".scene-wall-card"));

        cut.Render(p => p.Add(c => c.ActorId, 8));

        Assert.Empty(cut.FindAll(".actor-clips"));
    }
}
