using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Actors;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class CastSearchAddTests : BunitContext
{
    private IActorService SetUpActorService(params ActorSearchItem[] results)
    {
        var actorService = Substitute.For<IActorService>();
        actorService.SearchActorsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(results);
        Services.AddSingleton(actorService);
        return actorService;
    }

    private IRenderedComponent<CastSearchAdd> RenderComponent(
        IReadOnlyCollection<int>? excluded = null,
        Action<int>? onAdd = null) =>
        Render<CastSearchAdd>(p => p
            .Add(x => x.ExcludedActorIds, excluded ?? [])
            .Add(x => x.OnAdd, EventCallback.Factory.Create<int>(this, id => onAdd?.Invoke(id))));

    [Fact]
    public void Initially_ShowsSearchInputAndNoResults()
    {
        SetUpActorService();

        var cut = RenderComponent();

        Assert.Equal("Search actors to add…", cut.Find("input.cast-search-input").GetAttribute("placeholder"));
        Assert.Empty(cut.FindAll(".cast-search-candidate"));
    }

    [Fact]
    public void Typing_ShowsMatchingActors_WithImageOrInitialsAvatar()
    {
        SetUpActorService(
            new ActorSearchItem(1, "Hatano Yui", false),
            new ActorSearchItem(2, "Mikami Yua", true));
        var cut = RenderComponent();

        cut.Find("input.cast-search-input").Input("yu");

        var candidates = cut.FindAll(".cast-search-candidate");
        Assert.Equal(2, candidates.Count);
        Assert.Equal("HY", candidates[0].QuerySelector("span.cast-search-avatar")!.TextContent.Trim());
        Assert.Equal("/actor-image/2/thumb", candidates[1].QuerySelector("img.cast-search-avatar")!.GetAttribute("src"));
    }

    [Fact]
    public void Typing_ExcludesActorsAlreadyOnTheMovie()
    {
        SetUpActorService(
            new ActorSearchItem(1, "Hatano Yui", false),
            new ActorSearchItem(2, "Mikami Yua", false));
        var cut = RenderComponent(excluded: [1]);

        cut.Find("input.cast-search-input").Input("yu");

        var candidate = Assert.Single(cut.FindAll(".cast-search-candidate"));
        Assert.Contains("Mikami Yua", candidate.TextContent);
    }

    [Fact]
    public void ClearingTheInput_HidesResultsWithoutSearching()
    {
        var actorService = SetUpActorService(new ActorSearchItem(1, "Hatano Yui", false));
        var cut = RenderComponent();
        cut.Find("input.cast-search-input").Input("yu");
        Assert.Single(cut.FindAll(".cast-search-candidate"));
        actorService.ClearReceivedCalls();

        cut.Find("input.cast-search-input").Input("  ");

        Assert.Empty(cut.FindAll(".cast-search-candidate"));
        actorService.DidNotReceiveWithAnyArgs().SearchActorsAsync(default!, default);
    }

    [Fact]
    public void ClickingAResult_InvokesOnAddWithActorId_AndResetsSearch()
    {
        SetUpActorService(new ActorSearchItem(7, "Hatano Yui", false));
        int? added = null;
        var cut = RenderComponent(onAdd: id => added = id);
        cut.Find("input.cast-search-input").Input("hat");

        cut.Find(".cast-search-candidate").Click();

        Assert.Equal(7, added);
        Assert.Empty(cut.FindAll(".cast-search-candidate"));
        Assert.Equal("", cut.Find("input.cast-search-input").GetAttribute("value") ?? "");
    }
}
