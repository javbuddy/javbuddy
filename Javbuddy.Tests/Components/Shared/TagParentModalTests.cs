using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Tags;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class TagParentModalTests : BunitContext
{
    [Fact]
    public void Modal_WhenShowFalse_RendersNothing()
    {
        var tagService = Substitute.For<ITagService>();
        Services.AddSingleton(tagService);

        var cut = Render<TagParentModal>(p => p
            .Add(x => x.Show, false)
            .Add(x => x.Tag, new TagListItem(1, "Cosplay", 0, false, DateTime.UtcNow, null, null, 0)));

        Assert.Empty(cut.FindAll(".tag-parent-modal-backdrop"));
    }

    [Fact]
    public void Modal_WhenTagHasSubtags_DisplaysNoticeAndNoSaveButton()
    {
        var tagService = Substitute.For<ITagService>();
        Services.AddSingleton(tagService);

        var cut = Render<TagParentModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Tag, new TagListItem(1, "Cosplay", 5, false, DateTime.UtcNow, null, null, 2)));

        Assert.Contains("has 2 subtags", cut.Find(".alert-info").TextContent);
        Assert.Empty(cut.FindAll(".btn-primary"));
    }

    [Fact]
    public async Task Modal_SelectingParent_CallsSetParentAsync_AndInvokesOnUpdated()
    {
        var tagService = Substitute.For<ITagService>();
        var rootTag = new TagListItem(2, "Cosplay", 0, false, DateTime.UtcNow, null, null, 0);
        tagService.GetTagsAsync().Returns([rootTag]);
        tagService.SetParentAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(TagOperationResult.Ok(new Tag { Id = 1, Name = "ram", ParentTagId = 2 }));
        Services.AddSingleton(tagService);

        var updated = false;
        var cut = Render<TagParentModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Tag, new TagListItem(1, "ram", 1, false, DateTime.UtcNow, null, null, 0))
            .Add(x => x.OnUpdated, () => updated = true));

        var select = cut.Find("#parent-select");
        select.Change("2");
        await cut.Find(".btn-primary").ClickAsync(new MouseEventArgs());

        await tagService.Received(1).SetParentAsync(1, 2, Arg.Any<CancellationToken>());
        Assert.True(updated);
    }

    [Fact]
    public async Task Modal_PromotingToTopLevel_CallsSetParentAsyncWithNull()
    {
        var tagService = Substitute.For<ITagService>();
        var rootTag = new TagListItem(2, "Cosplay", 0, false, DateTime.UtcNow, null, null, 0);
        tagService.GetTagsAsync().Returns([rootTag]);
        tagService.SetParentAsync(1, null, Arg.Any<CancellationToken>())
            .Returns(TagOperationResult.Ok(new Tag { Id = 1, Name = "ram", ParentTagId = null }));
        Services.AddSingleton(tagService);

        var updated = false;
        var cut = Render<TagParentModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Tag, new TagListItem(1, "ram", 1, false, DateTime.UtcNow, 2, "Cosplay", 0))
            .Add(x => x.OnUpdated, () => updated = true));

        var select = cut.Find("#parent-select");
        select.Change("");
        await cut.Find(".btn-primary").ClickAsync(new MouseEventArgs());

        await tagService.Received(1).SetParentAsync(1, null, Arg.Any<CancellationToken>());
        Assert.True(updated);
    }

    [Fact]
    public async Task ClickingCancel_InvokesOnClose()
    {
        var tagService = Substitute.For<ITagService>();
        Services.AddSingleton(tagService);

        var closed = false;
        var cut = Render<TagParentModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Tag, new TagListItem(1, "ram", 1, false, DateTime.UtcNow, null, null, 0))
            .Add(x => x.OnClose, () => closed = true));

        await cut.Find(".btn-secondary").ClickAsync(new MouseEventArgs());

        Assert.True(closed);
    }

    [Fact]
    public void Modal_EligibleParents_ExcludesUnapprovedTags()
    {
        var tagService = Substitute.For<ITagService>();
        var approvedParent = new TagListItem(2, "Cosplay", 0, false, DateTime.UtcNow, null, null, 0);
        var unapprovedParent = new TagListItem(3, "DiscoveredCategory", 0, true, DateTime.UtcNow, null, null, 0);
        tagService.GetTagsAsync().Returns([approvedParent, unapprovedParent]);
        Services.AddSingleton(tagService);

        var cut = Render<TagParentModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Tag, new TagListItem(1, "ram", 1, false, DateTime.UtcNow, null, null, 0)));

        var options = cut.FindAll("#parent-select option");
        Assert.Equal(2, options.Count);
        Assert.Contains("Cosplay", options[1].TextContent);
        Assert.DoesNotContain("DiscoveredCategory", cut.Markup);
    }
}
