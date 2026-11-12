using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Tags;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class ClipTagChipsTests : BunitContext
{
    private static readonly SceneTagItem Alpha = new(1, "Alpha", null);
    private static readonly SceneTagItem Beta = new(2, "Beta", "Play");

    private readonly ITagService tagService = Substitute.For<ITagService>();

    public ClipTagChipsTests()
    {
        tagService.GetTagsAsync(Arg.Any<string?>(), Arg.Any<TagSortOrder>(), Arg.Any<CancellationToken>())
            .Returns([new TagListItem(3, "Gamma", 0, false, DateTime.UtcNow)]);
        tagService.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(new Tag { Id = 3, Name = "Gamma" });
        Services.AddSingleton(tagService);
    }

    private IRenderedComponent<ClipTagChips> RenderChips(Action<ComponentParameterCollectionBuilder<ClipTagChips>> configure) =>
        Render<ClipTagChips>(p =>
        {
            p.Add(x => x.ClassPrefix, "apex-editor");
            configure(p);
        });

    private static void AddByTyping(IRenderedComponent<ClipTagChips> cut)
    {
        cut.Find("input.tag-search-input").Input("Gam");
        cut.WaitForElement(".tag-search-candidate").Click();
    }

    [Fact]
    public void ShowsEachTagAsAChip_WithTheHostsClassNames_AndNothingWhenEmpty()
    {
        var cut = RenderChips(p => p.Add(x => x.Tags, [Alpha, Beta]).Add(x => x.ChipClass, "extra-chip"));

        var chips = cut.FindAll(".apex-editor-chips .apex-editor-chip.extra-chip");
        Assert.Equal(["Alpha×", "Play › Beta×"], chips.Select(c => c.TextContent.Trim()));
        Assert.Equal("Remove Alpha", cut.Find(".apex-editor-chip-remove").GetAttribute("aria-label"));

        Assert.Empty(RenderChips(p => { }).FindAll(".apex-editor-chips"));
    }

    [Fact]
    public void OwnTagThatRollsUpIsMarkedRedundant_AndRolledUpTagsShowGrayed()
    {
        var cut = RenderChips(p => p
            .Add(x => x.Tags, [Alpha])
            .Add(x => x.RedundantTags, [new ImplicitTag(Alpha, ["apex 1"])])
            .Add(x => x.RolledUp, ImplicitChip.ForTags([new ImplicitTag(Beta, ["apex 2"])])));

        Assert.Single(cut.FindAll(".redundant-mark"));
        Assert.Single(cut.FindAll(".implicit-chip"));
    }

    [Fact]
    public void RolledUpTagsAlone_StillShowTheChipList()
    {
        var cut = RenderChips(p => p.Add(x => x.RolledUp, ImplicitChip.ForTags([new ImplicitTag(Beta, ["apex 2"])])));

        Assert.Single(cut.FindAll(".apex-editor-chips .implicit-chip"));
    }

    [Fact]
    public void CallbackMode_RaisesOnRemoveAndOnAdd_WithoutTouchingTheList()
    {
        var removed = new List<SceneTagItem>();
        var added = new List<int>();
        var cut = RenderChips(p => p
            .Add(x => x.Tags, [Alpha])
            .Add(x => x.OnRemove, EventCallback.Factory.Create<SceneTagItem>(this, removed.Add))
            .Add(x => x.OnAdd, EventCallback.Factory.Create<int>(this, added.Add)));

        cut.Find(".apex-editor-chip-remove").Click();
        AddByTyping(cut);

        // The debounced search's handler finishes on the thread pool, so the click can be dispatched after it returns.
        cut.WaitForAssertion(() => Assert.Equal([3], added));
        Assert.Equal([Alpha], removed);
        Assert.Single(cut.FindAll(".apex-editor-chip"));
        _ = tagService.DidNotReceive().GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void FormMode_AddsAndRemovesInPlace_AndRaisesOnChanged()
    {
        List<SceneTagItem> tags = [Alpha];
        var changes = 0;
        var cut = RenderChips(p => p
            .Add(x => x.FormTags, tags)
            .Add(x => x.OnChanged, EventCallback.Factory.Create(this, () => changes++)));

        AddByTyping(cut);

        cut.WaitForAssertion(() => Assert.Equal([1, 3], tags.Select(t => t.TagId)));
        Assert.Equal(1, changes);
        Assert.Equal(2, cut.FindAll(".apex-editor-chip").Count);

        cut.Find(".apex-editor-chip-remove").Click();

        Assert.Equal([3], tags.Select(t => t.TagId));
        Assert.Equal(2, changes);
        Assert.Single(cut.FindAll(".apex-editor-chip"));
    }

    [Fact]
    public void FormMode_IgnoresATagAlreadyHeld()
    {
        List<SceneTagItem> tags = [new(3, "Gamma", null)];
        var cut = RenderChips(p => p.Add(x => x.FormTags, tags));

        // The search hides held tags, so raise the add the way the box would.
        cut.InvokeAsync(() => cut.FindComponent<TagSearchAdd>().Instance.OnAdd.InvokeAsync(3));

        Assert.Single(tags);
    }

    [Fact]
    public void ChildContent_SitsBetweenTheChipsAndTheAddBox()
    {
        var cut = RenderChips(p => p
            .Add(x => x.Tags, [Alpha])
            .Add(x => x.ChildContent, b => b.AddMarkupContent(0, "<p class=\"slot\">extra</p>")));

        var html = cut.Markup;
        Assert.True(html.IndexOf("apex-editor-chips", StringComparison.Ordinal) < html.IndexOf("class=\"slot\"", StringComparison.Ordinal));
        Assert.True(html.IndexOf("class=\"slot\"", StringComparison.Ordinal) < html.IndexOf("tag-search-input", StringComparison.Ordinal));
    }
}
