using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Tags;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class TagSearchAddTests : BunitContext
{
    private static TagListItem Tag(int id, string name, bool needsReview = false, string? parent = null) =>
        new(id, name, 0, needsReview, DateTime.UtcNow, parent is null ? null : 999, parent);

    private ITagService SetUpTagService(params TagListItem[] results)
    {
        var tagService = Substitute.For<ITagService>();
        tagService.GetTagsAsync(Arg.Any<string?>(), Arg.Any<TagSortOrder>(), Arg.Any<CancellationToken>())
            .Returns(results);
        Services.AddSingleton(tagService);
        return tagService;
    }

    private IRenderedComponent<TagSearchAdd> RenderComponent(
        IReadOnlyCollection<int>? excluded = null,
        Action<int>? onAdd = null) =>
        Render<TagSearchAdd>(p => p
            .Add(x => x.ExcludedTagIds, excluded ?? [])
            .Add(x => x.OnAdd, EventCallback.Factory.Create<int>(this, id => onAdd?.Invoke(id))));

    [Fact]
    public void Initially_ShowsSearchInputAndNoResults()
    {
        SetUpTagService();

        var cut = RenderComponent();

        Assert.Equal("Search tags to add…", cut.Find("input.tag-search-input").GetAttribute("placeholder"));
        Assert.Empty(cut.FindAll(".tag-search-candidate"));
    }

    [Fact]
    public void Typing_ShowsMatchingTags_WithParentBreadcrumbForSubtags()
    {
        SetUpTagService(Tag(1, "Solo"), Tag(2, "Subtag", parent: "Category"));
        var cut = RenderComponent();

        cut.Find("input.tag-search-input").Input("s");

        var candidates = cut.FindAll(".tag-search-candidate");
        Assert.Equal("Solo", candidates[0].TextContent.Trim());
        Assert.Equal("Category › Subtag", candidates[1].TextContent.Trim());
    }

    [Fact]
    public void Typing_ExcludesTagsAlreadyOnTheMovie_AndTagsNotYetApproved()
    {
        SetUpTagService(Tag(1, "On Movie"), Tag(2, "Pending", needsReview: true), Tag(3, "Solo"));
        var cut = RenderComponent(excluded: [1]);

        cut.Find("input.tag-search-input").Input("o");

        var candidate = Assert.Single(cut.FindAll(".tag-search-candidate"));
        Assert.Equal("Solo", candidate.TextContent.Trim());
    }

    [Fact]
    public void Typing_ShowsAtMostTenResults()
    {
        SetUpTagService([.. Enumerable.Range(1, 15).Select(i => Tag(i, $"Tag {i}"))]);
        var cut = RenderComponent();

        cut.Find("input.tag-search-input").Input("tag");

        Assert.Equal(10, cut.FindAll(".tag-search-candidate").Count);
    }

    [Fact]
    public void ClearingTheInput_HidesResultsWithoutSearching()
    {
        var tagService = SetUpTagService(Tag(1, "Solo"));
        var cut = RenderComponent();
        cut.Find("input.tag-search-input").Input("so");
        Assert.Single(cut.FindAll(".tag-search-candidate"));
        tagService.ClearReceivedCalls();

        cut.Find("input.tag-search-input").Input("  ");

        Assert.Empty(cut.FindAll(".tag-search-candidate"));
        tagService.DidNotReceiveWithAnyArgs().GetTagsAsync(default, default, default);
    }

    [Fact]
    public void ClickingAResult_InvokesOnAddWithTagId_AndResetsSearch()
    {
        SetUpTagService(Tag(7, "Solo"));
        int? added = null;
        var cut = RenderComponent(onAdd: id => added = id);
        cut.Find("input.tag-search-input").Input("so");

        cut.Find(".tag-search-candidate").Click();

        Assert.Equal(7, added);
        Assert.Empty(cut.FindAll(".tag-search-candidate"));
        Assert.Equal("", cut.Find("input.tag-search-input").GetAttribute("value") ?? "");
    }

    [Fact]
    public void ClickingCandidate_RaisesOnAddItemWithTheTag()
    {
        SetUpTagService(Tag(4, "Cowgirl", parent: "Position"));
        TagListItem? item = null;
        var cut = Render<TagSearchAdd>(p => p.Add(x => x.OnAddItem, (TagListItem t) => item = t));

        cut.Find("input").Input("cow");
        cut.Find(".tag-search-candidate").Click();

        Assert.Equal(("Cowgirl", "Position"), (item!.Name, item.ParentTagName));
    }

    [Fact]
    public void Enter_WithFreeTextHandler_AddsTrimmedTextAndResetsBox()
    {
        SetUpTagService();
        string? text = null;
        var cut = Render<TagSearchAdd>(p => p.Add(x => x.OnAddFreeText, (string s) => text = s));

        cut.Find("input").Input("  Brand New  ");
        cut.Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Brand New", text);
        Assert.Equal("", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void Enter_WithoutFreeTextHandler_DoesNothing()
    {
        SetUpTagService();
        var added = 0;
        var cut = RenderComponent(onAdd: _ => added++);

        cut.Find("input").Input("Brand New");
        cut.Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(0, added);
        Assert.Equal("Brand New", cut.Find("input").GetAttribute("value"));
    }
}
