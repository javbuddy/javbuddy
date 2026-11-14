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

public class ActorClipsSectionTests : InteractiveBunitContext
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
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Movies.IMovieSubtitleService>());
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
        Assert.Empty(cut.FindAll(".actor-clips-skeleton-card"));
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
        Assert.Single(clipCardModule.Invocations, i => i.Identifier == "bindHoverPreviews");
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

    [Fact]
    public void ApexWithAPreviewButNoScreenshot_IsQueuedForItsScreenshot()
    {
        apexMedia.ServesVariant(Arg.Any<string>()).Returns(true);
        apexMedia.ServesPreview.Returns(true);
        var apex = new ApexWallCard(3, 30, "ABC-003", "Title", "Yua — Squirt", 300, ["Yua"], [], false, 5) { HasLocalVideo = true, DurationSeconds = 3600 };
        wall.GetApexPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new ApexWallPage([apex], 1));

        RenderSection(PlayerClipKind.Apex);

        apexMedia.Received(1).EnsureQueuedAsync(30, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ApexWithBothImages_IsNotQueued()
    {
        apexMedia.ServesVariant(Arg.Any<string>()).Returns(true);
        apexMedia.ServesPreview.Returns(true);
        var apex = new ApexWallCard(3, 30, "ABC-003", "Title", "Yua — Squirt", 300, ["Yua"], [], false, 5) { HasLocalVideo = true, DurationSeconds = 3600, ThumbVersion = 4 };
        wall.GetApexPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new ApexWallPage([apex], 1));

        RenderSection(PlayerClipKind.Apex);

        apexMedia.DidNotReceive().EnsureQueuedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeavingThePage_UnbindsTheHoverPreviews()
    {
        wall.GetPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(new SceneWallPage([Scene(1)], 1));
        var binding = clipCardModule.SetupModule("bindHoverPreviews", _ => true);
        binding.Mode = JSRuntimeMode.Loose;
        var cut = RenderSection(PlayerClipKind.Scene);

        await cut.Instance.DisposeAsync();

        Assert.Single(binding.Invocations, i => i.Identifier == "unbind");
    }

    private void ReturnCount(PlayerClipKind kind, int count)
    {
        switch (kind)
        {
            case PlayerClipKind.Highlight: wall.CountHighlightsAsync(default!, default).ReturnsForAnyArgs(count); break;
            case PlayerClipKind.Apex: wall.CountApexesAsync(default!, default).ReturnsForAnyArgs(count); break;
            default: wall.CountScenesAsync(default!, default).ReturnsForAnyArgs(count); break;
        }
    }

    private sealed record PersistedCount(int ActorId, int Total);

    [Theory]
    [InlineData(PlayerClipKind.Scene)]
    [InlineData(PlayerClipKind.Highlight)]
    [InlineData(PlayerClipKind.Apex)]
    public void WhenNotInteractive_WithClips_OnlyCountsThem_AndShowsASkeleton(PlayerClipKind kind)
    {
        Interactive = false;
        ReturnCount(kind, 2);

        var cut = RenderSection(kind);

        Assert.Equal(2, cut.FindAll(".actor-clips-skeleton-card").Count);
        Assert.Empty(cut.FindAll(".actor-clips-grid"));
        Assert.All(wall.ReceivedCalls(), call => Assert.StartsWith("Count", call.GetMethodInfo().Name));
        Assert.Single(wall.ReceivedCalls());
        media.ReceivedCalls().ToList().ForEach(call => Assert.Fail($"Unexpected call {call.GetMethodInfo().Name}"));
        highlightMedia.ReceivedCalls().ToList().ForEach(call => Assert.Fail($"Unexpected call {call.GetMethodInfo().Name}"));
        apexMedia.ReceivedCalls().ToList().ForEach(call => Assert.Fail($"Unexpected call {call.GetMethodInfo().Name}"));
    }

    [Fact]
    public void WhenNotInteractive_WithoutClips_RendersNothing()
    {
        Interactive = false;
        ReturnCount(PlayerClipKind.Highlight, 0);

        var cut = RenderSection(PlayerClipKind.Highlight);

        Assert.Empty(cut.FindAll(".actor-clips"));
    }

    [Fact]
    public void WhenNotInteractive_PersistsTheCount_ForTheCircuit()
    {
        Interactive = false;
        var state = AddBunitPersistentComponentState();
        ReturnCount(PlayerClipKind.Scene, 5);
        RenderSection(PlayerClipKind.Scene);

        state.TriggerOnPersisting();

        Assert.True(state.TryTake<PersistedCount>("actor-clips-Scene", out var persisted));
        Assert.Equal(new PersistedCount(7, 5), persisted);
    }

    [Fact]
    public void Interactive_WithAPersistedCount_ShowsTheSkeletonUntilTheFirstPageLands()
    {
        var state = AddBunitPersistentComponentState();
        state.Persist("actor-clips-Scene", new PersistedCount(7, 3));
        var page = new TaskCompletionSource<SceneWallPage>();
        wall.GetPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(page.Task);

        var cut = RenderSection(PlayerClipKind.Scene);
        Assert.Equal(3, cut.FindAll(".actor-clips-skeleton-card").Count);

        cut.InvokeAsync(() => page.SetResult(new SceneWallPage([Scene(1)], 1)));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".actor-clips-grid")));
        Assert.Empty(cut.FindAll(".actor-clips-skeleton-card"));
    }

    [Fact]
    public void Interactive_WithoutAPersistedCount_ShowsNoSkeletonWhileLoading()
    {
        var page = new TaskCompletionSource<SceneWallPage>();
        wall.GetPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(page.Task);

        var cut = RenderSection(PlayerClipKind.Scene);

        Assert.Empty(cut.FindAll(".actor-clips"));
    }

    [Fact]
    public void Interactive_APersistedCountForAnotherActor_IsIgnored()
    {
        var state = AddBunitPersistentComponentState();
        state.Persist("actor-clips-Scene", new PersistedCount(99, 3));
        var page = new TaskCompletionSource<SceneWallPage>();
        wall.GetPageAsync(default!, default, default, default, default, default).ReturnsForAnyArgs(page.Task);

        var cut = RenderSection(PlayerClipKind.Scene);

        Assert.Empty(cut.FindAll(".actor-clips-skeleton-card"));
    }
}
