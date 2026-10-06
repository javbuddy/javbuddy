using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Tags;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class TagCreateModalTests : BunitContext
{
    [Fact]
    public void Modal_WhenShowFalse_RendersNothing()
    {
        var tagService = Substitute.For<ITagService>();
        Services.AddSingleton(tagService);

        var cut = Render<TagCreateModal>(p => p
            .Add(x => x.Show, false));

        Assert.Empty(cut.FindAll(".tag-create-modal-backdrop"));
    }

    [Fact]
    public void Modal_WhenShowTrue_RendersHeaderAndDisabledCreateButton()
    {
        var tagService = Substitute.For<ITagService>();
        Services.AddSingleton(tagService);

        var cut = Render<TagCreateModal>(p => p
            .Add(x => x.Show, true));

        Assert.Contains("Create Tag", cut.Find(".tag-create-modal-title").TextContent);
        var input = cut.Find("#new-tag-name");
        Assert.Equal("", input.GetAttribute("value") ?? "");

        var createBtn = cut.Find(".btn-primary");
        Assert.True(createBtn.HasAttribute("disabled"));
    }

    [Fact]
    public void TypingTagName_EnablesCreateButton()
    {
        var tagService = Substitute.For<ITagService>();
        Services.AddSingleton(tagService);

        var cut = Render<TagCreateModal>(p => p
            .Add(x => x.Show, true));

        var input = cut.Find("#new-tag-name");
        input.Input("Cosplay");

        var createBtn = cut.Find(".btn-primary");
        Assert.False(createBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task SubmittingValidName_CallsCreateTagAsync_AndInvokesOnCreated()
    {
        var tagService = Substitute.For<ITagService>();
        var createdTag = new Tag { Id = 10, Name = "Cosplay" };
        tagService.CreateTagAsync("Cosplay", Arg.Any<CancellationToken>())
            .Returns(TagOperationResult.Ok(createdTag));
        Services.AddSingleton(tagService);

        Tag? receivedTag = null;
        var cut = Render<TagCreateModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.OnCreated, (Tag tag) => receivedTag = tag));

        cut.Find("#new-tag-name").Input("Cosplay");
        await cut.Find(".btn-primary").ClickAsync(new MouseEventArgs());

        await tagService.Received(1).CreateTagAsync("Cosplay", Arg.Any<CancellationToken>());
        Assert.NotNull(receivedTag);
        Assert.Equal("Cosplay", receivedTag.Name);
    }

    [Fact]
    public async Task SubmittingDuplicateName_DisplaysErrorMessage()
    {
        var tagService = Substitute.For<ITagService>();
        tagService.CreateTagAsync("Cosplay", Arg.Any<CancellationToken>())
            .Returns(TagOperationResult.Fail("A tag named \"Cosplay\" already exists."));
        Services.AddSingleton(tagService);

        var cut = Render<TagCreateModal>(p => p
            .Add(x => x.Show, true));

        cut.Find("#new-tag-name").Input("Cosplay");
        await cut.Find(".btn-primary").ClickAsync(new MouseEventArgs());

        var alert = cut.Find(".alert-danger");
        Assert.Contains("already exists", alert.TextContent);
    }

    [Fact]
    public async Task PressingEnterOnInput_SubmitsCreation()
    {
        var tagService = Substitute.For<ITagService>();
        var createdTag = new Tag { Id = 10, Name = "Cosplay" };
        tagService.CreateTagAsync("Cosplay", Arg.Any<CancellationToken>())
            .Returns(TagOperationResult.Ok(createdTag));
        Services.AddSingleton(tagService);

        Tag? receivedTag = null;
        var cut = Render<TagCreateModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.OnCreated, (Tag tag) => receivedTag = tag));

        var input = cut.Find("#new-tag-name");
        input.Input("Cosplay");
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        await tagService.Received(1).CreateTagAsync("Cosplay", Arg.Any<CancellationToken>());
        Assert.NotNull(receivedTag);
    }

    [Fact]
    public async Task ClickingCancel_InvokesOnClose()
    {
        var tagService = Substitute.For<ITagService>();
        Services.AddSingleton(tagService);

        var closed = false;
        var cut = Render<TagCreateModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.OnClose, () => closed = true));

        await cut.Find(".btn-secondary").ClickAsync(new MouseEventArgs());

        Assert.True(closed);
    }

    [Fact]
    public async Task PressingEscape_InvokesOnClose()
    {
        var tagService = Substitute.For<ITagService>();
        Services.AddSingleton(tagService);

        var closed = false;
        var cut = Render<TagCreateModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.OnClose, () => closed = true));

        await cut.Find(".tag-create-modal-backdrop").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(closed);
    }

    [Fact]
    public async Task SubmittingWithParent_CallsCreateTagAsyncWithParent_AndInvokesOnCreated()
    {
        var tagService = Substitute.For<ITagService>();
        var rootTag = new TagListItem(5, "Cosplay", 1, false, DateTime.UtcNow, null, null, 0);
        tagService.GetTagsAsync().Returns([rootTag]);

        var createdTag = new Tag { Id = 11, Name = "ram", ParentTagId = 5 };
        tagService.CreateTagAsync("ram", 5, Arg.Any<CancellationToken>())
            .Returns(TagOperationResult.Ok(createdTag));
        Services.AddSingleton(tagService);

        Tag? receivedTag = null;
        var cut = Render<TagCreateModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.DefaultParentTagId, 5)
            .Add(x => x.OnCreated, (Tag tag) => receivedTag = tag));

        cut.Find("#new-tag-name").Input("ram");
        await cut.Find(".btn-primary").ClickAsync(new MouseEventArgs());

        await tagService.Received(1).CreateTagAsync("ram", 5, Arg.Any<CancellationToken>());
        Assert.NotNull(receivedTag);
        Assert.Equal("ram", receivedTag.Name);
        Assert.Equal(5, receivedTag.ParentTagId);
    }

    [Fact]
    public async Task TypingInParentSearchbox_DisplaysSuggestions_AndClickingSelectsParent()
    {
        var tagService = Substitute.For<ITagService>();
        var rootTag = new TagListItem(5, "Cosplay", 3, false, DateTime.UtcNow, null, null, 0);
        tagService.GetTagsAsync().Returns([rootTag]);

        var createdTag = new Tag { Id = 12, Name = "ram", ParentTagId = 5 };
        tagService.CreateTagAsync("ram", 5, Arg.Any<CancellationToken>())
            .Returns(TagOperationResult.Ok(createdTag));
        Services.AddSingleton(tagService);

        Tag? receivedTag = null;
        var cut = Render<TagCreateModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.OnCreated, (Tag tag) => receivedTag = tag));

        cut.Find("#new-tag-name").Input("ram");

        var parentInput = cut.Find("#new-tag-parent");
        parentInput.Input("Cos");

        cut.WaitForAssertion(() => Assert.Contains("Cosplay", cut.Find(".tag-parent-suggestion-item").TextContent));
        cut.Find(".tag-parent-suggestion-item").Click();

        cut.WaitForAssertion(() => Assert.Equal("Selected parent: Cosplay", cut.Find(".form-text.text-success").TextContent.Trim()));

        await cut.Find(".btn-primary").ClickAsync(new MouseEventArgs());

        await tagService.Received(1).CreateTagAsync("ram", 5, Arg.Any<CancellationToken>());
        Assert.NotNull(receivedTag);
        Assert.Equal(5, receivedTag.ParentTagId);
    }

    [Fact]
    public async Task ClickingClearParent_ClearsSelection_AndSubmitsWithoutParent()
    {
        var tagService = Substitute.For<ITagService>();
        var rootTag = new TagListItem(5, "Cosplay", 3, false, DateTime.UtcNow, null, null, 0);
        tagService.GetTagsAsync().Returns([rootTag]);

        var createdTag = new Tag { Id = 13, Name = "Solo" };
        tagService.CreateTagAsync("Solo", Arg.Any<CancellationToken>())
            .Returns(TagOperationResult.Ok(createdTag));
        Services.AddSingleton(tagService);

        Tag? receivedTag = null;
        var cut = Render<TagCreateModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.DefaultParentTagId, 5)
            .Add(x => x.OnCreated, (Tag tag) => receivedTag = tag));

        cut.WaitForAssertion(() => Assert.Equal("Selected parent: Cosplay", cut.Find(".form-text.text-success").TextContent.Trim()));

        // Click the clear button
        cut.Find(".tag-parent-field .btn-outline-secondary").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".form-text.text-success")));

        cut.Find("#new-tag-name").Input("Solo");
        await cut.Find(".btn-primary").ClickAsync(new MouseEventArgs());

        await tagService.Received(1).CreateTagAsync("Solo", Arg.Any<CancellationToken>());
        Assert.NotNull(receivedTag);
        Assert.Null(receivedTag.ParentTagId);
    }

    [Fact]
    public void ParentSearchbox_DoesNotSuggestUnapprovedTags()
    {
        var tagService = Substitute.For<ITagService>();
        var approvedRoot = new TagListItem(5, "Cosplay", 3, false, DateTime.UtcNow, null, null, 0);
        var unapprovedRoot = new TagListItem(6, "Costume", 1, true, DateTime.UtcNow, null, null, 0);
        tagService.GetTagsAsync().Returns([approvedRoot, unapprovedRoot]);
        Services.AddSingleton(tagService);

        var cut = Render<TagCreateModal>(p => p
            .Add(x => x.Show, true));

        var parentInput = cut.Find("#new-tag-parent");
        parentInput.Input("Cos");

        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll(".tag-parent-suggestion-item");
            Assert.Single(items);
            Assert.Contains("Cosplay", items[0].TextContent);
            Assert.DoesNotContain("Costume", cut.Markup);
        });
    }
}
