using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Tags;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.MovieDetailSections;

public class ActorTagPillsTests : BunitContext
{
    private const int Mei = 1, Blonde = 10, Long = 11, Short = 12, Tattoo = 13, Hair = 20;

    private readonly IActorTagService service = Substitute.For<IActorTagService>();
    private readonly List<ActorTagResult> changes = [];

    private static readonly IReadOnlyList<ActorTagListItem> Library =
    [
        new(Blonde, "Blonde", 0), new(Long, "Long", 0, Hair, "Hair"), new(Short, "Short", 0, Hair, "Hair"), new(Tattoo, "Tattoo", 0),
    ];

    public ActorTagPillsTests()
    {
        service.GetActorTagsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Library);
        service.SetAsync(Arg.Any<ActorTagLevel>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(ActorTagResult.Ok(true));
        Services.AddSingleton(service);
        Services.AddSingleton(Substitute.For<ITagService>());
    }

    private IRenderedComponent<ActorTagPills> RenderPills(bool editing, params EffectiveActorTag[] tags) =>
        Render<ActorTagPills>(p => p
            .Add(x => x.MovieId, 5)
            .Add(x => x.ActorId, Mei)
            .Add(x => x.ActorName, "Mei")
            .Add(x => x.Library, Library)
            .Add(x => x.Tags, tags)
            .Add(x => x.Editing, editing)
            .Add(x => x.OnChanged, EventCallback.Factory.Create<ActorTagResult>(this, changes.Add)));

    private static EffectiveActorTag Own(int tagId) => new(Mei, tagId, false, null);

    private static EffectiveActorTag FromClips(int tagId) => new(Mei, tagId, true, "its scenes");

    private IReadOnlyCollection<int> SavedIds() =>
        (IReadOnlyCollection<int>)service.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IActorTagService.SetAsync)).GetArguments()[3]!;

    [Fact]
    public void WithoutTagsAndNotEditing_RendersNothing()
    {
        Assert.Empty(RenderPills(false).Markup.Trim());
    }

    [Fact]
    public void ShowsTheFirstTwoOwnTags_AndCountsTheRestAndTheClipOnesInAMorePill()
    {
        var cut = RenderPills(false, Own(Blonde), Own(Long), Own(Tattoo), FromClips(Short));

        Assert.Equal(["Blonde", "Long"], cut.FindAll(".actor-pills-row .actor-pill:not(.actor-pill-more)").Select(p => p.TextContent.Trim()));
        Assert.Equal("+2", cut.Find(".actor-pill-more").TextContent.Trim());
    }

    [Fact]
    public void ThePopoverHoldsEveryTag_WithClipOnesGrayedAndSubtagsUnderTheirParent()
    {
        var cut = RenderPills(false, Own(Long), Own(Tattoo), Own(Blonde), FromClips(Short));

        var popover = cut.Find(".actor-pills-popover");
        Assert.Equal(["Hair › Long", "Tattoo", "Blonde"], popover.QuerySelectorAll(".actor-pill:not(.actor-pill-clip)").Select(p => p.TextContent.Trim()));
        Assert.Equal(["Hair › Short"], popover.QuerySelectorAll(".actor-pill-clip").Select(p => p.TextContent.Trim()));
        Assert.Empty(popover.QuerySelectorAll("button"));
    }

    [Fact]
    public void Editing_OffersAddAndRemove_AndSavesTheNewSet()
    {
        var cut = RenderPills(true, Own(Blonde));

        cut.Find(".actor-pill-add").Click();
        cut.Find("input.tag-search-input").Input("tat");
        cut.FindAll(".tag-search-candidate").Single(c => c.TextContent.Trim() == "Tattoo").Click();

        Assert.Equal([Blonde, Tattoo], SavedIds().Order());
        Assert.True(Assert.Single(changes).MovieTagsChanged);
    }

    [Fact]
    public void Editing_RemoveSavesTheRest()
    {
        var cut = RenderPills(true, Own(Blonde), Own(Tattoo));

        cut.Find("button[aria-label='Remove Blonde from Mei']").Click();

        Assert.Equal([Tattoo], SavedIds());
    }
}
