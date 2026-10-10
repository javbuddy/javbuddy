using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Tags;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class ActorTagsEditorTests : BunitContext
{
    private const int Mei = 1, Blonde = 10, Brunette = 11, Tattoo = 12;

    private readonly IActorTagService service = Substitute.For<IActorTagService>();
    private readonly List<ActorTagResult> changes = [];

    public ActorTagsEditorTests()
    {
        service.GetActorTagsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([new ActorTagListItem(Blonde, "Blonde", 0), new ActorTagListItem(Brunette, "Brunette", 0), new ActorTagListItem(Tattoo, "Tattoo", 0)]);
        service.SetAsync(Arg.Any<ActorTagLevel>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(ActorTagResult.Ok(true));
        Services.AddSingleton(service);
        Services.AddSingleton(Substitute.For<ITagService>());
    }

    private IRenderedComponent<ActorTagsEditor> RenderEditor(params EffectiveActorTag[] tags) =>
        Render<ActorTagsEditor>(p => p
            .Add(x => x.Level, ActorTagLevel.Scene)
            .Add(x => x.OwnerId, 7)
            .Add(x => x.Actors, [new SceneActorItem(Mei, "Mei")])
            .Add(x => x.Tags, tags)
            .Add(x => x.OnChanged, EventCallback.Factory.Create<ActorTagResult>(this, changes.Add)));

    private IReadOnlyCollection<int> SavedIds() =>
        (IReadOnlyCollection<int>)service.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IActorTagService.SetAsync)).GetArguments()[3]!;

    [Fact]
    public void ShowsOwnInheritedAndRolledUpTagsDifferently()
    {
        var cut = RenderEditor(
            new EffectiveActorTag(Mei, Blonde, false, null),
            new EffectiveActorTag(Mei, Tattoo, false, "the movie"),
            new EffectiveActorTag(Mei, Brunette, true, "apex 1"));

        Assert.Single(cut.FindAll(".actor-tag-chip-own"));
        Assert.Equal("Inherited from the movie", Assert.Single(cut.FindAll(".actor-tag-chip-inherited")).GetAttribute("title"));
        var rolledUp = Assert.Single(cut.FindAll(".actor-tag-chip-rolled-up"));
        Assert.Equal("From apex 1", rolledUp.GetAttribute("title"));
        Assert.Empty(rolledUp.QuerySelectorAll("button"));
    }

    [Fact]
    public async Task PickingATagFromTheSearch_AddsItToTheOwnOnes()
    {
        var cut = RenderEditor(new EffectiveActorTag(Mei, Blonde, false, null));

        await cut.InvokeAsync(() => cut.Find("input.tag-search-input").Input("tat"));
        await cut.InvokeAsync(() => cut.FindAll(".tag-search-candidate").Single(c => c.TextContent.Trim() == "Tattoo").Click());

        Assert.Equal([Blonde, Tattoo], SavedIds().Order());
        Assert.True(Assert.Single(changes).MovieTagsChanged);
    }

    [Fact]
    public async Task TheSearch_ShowsASubtagUnderItsParent_AndHidesTheActorsOwnTags()
    {
        service.GetActorTagsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([new ActorTagListItem(Blonde, "Blonde", 0), new ActorTagListItem(30, "Long", 0, 29, "Hair")]);
        var cut = RenderEditor(new EffectiveActorTag(Mei, Blonde, false, null));

        await cut.InvokeAsync(() => cut.Find("input.tag-search-input").Input("l"));

        Assert.Equal(["Hair › Long"], cut.FindAll(".tag-search-candidate").Select(c => c.TextContent.Trim()));
    }

    [Fact]
    public async Task RemovingAnInheritedTag_MakesTheRestTheActorsOwn()
    {
        var cut = RenderEditor(
            new EffectiveActorTag(Mei, Blonde, false, "the movie"),
            new EffectiveActorTag(Mei, Tattoo, false, "the movie"));

        await cut.Find("button[aria-label='Remove Blonde from Mei']").ClickAsync();

        Assert.Equal([Tattoo], SavedIds());
    }

    [Fact]
    public async Task RemovingTheLastOwnTag_SavesAnEmptySet_SoTheActorInheritsAgain()
    {
        var cut = RenderEditor(new EffectiveActorTag(Mei, Blonde, false, null));

        await cut.Find("button[aria-label='Remove Blonde from Mei']").ClickAsync();

        Assert.Empty(SavedIds());
    }

    [Fact]
    public async Task ASaveError_IsShown()
    {
        service.SetAsync(Arg.Any<ActorTagLevel>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(ActorTagResult.Fail("Actor is not in the movie's cast."));
        var cut = RenderEditor(new EffectiveActorTag(Mei, Blonde, false, null));

        await cut.Find("button[aria-label='Remove Blonde from Mei']").ClickAsync();

        Assert.Equal("Actor is not in the movie's cast.", cut.Find(".actor-tags-error").TextContent);
    }
}
