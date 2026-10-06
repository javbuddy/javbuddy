using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Tags;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class TagMergeModalTests : BunitContext
{
    [Fact]
    public void Modal_WhenShowFalse_RendersNothing()
    {
        var tagService = Substitute.For<ITagService>();
        Services.AddSingleton(tagService);

        var source = new Tag { Id = 1, Name = "Solowork" };
        var cut = Render<TagMergeModal>(p => p
            .Add(x => x.Show, false)
            .Add(x => x.SourceTag, source));

        Assert.Empty(cut.FindAll(".tag-merge-modal-backdrop"));
    }

    [Fact]
    public void Modal_WhenShowTrue_RendersSourceAndCandidates()
    {
        var tagService = Substitute.For<ITagService>();
        var candidates = new List<TagMergeCandidate>
        {
            new(2, "Solo", 5, true),
            new(3, "VR", 12, false)
        };
        tagService.GetMergeCandidatesAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(candidates);
        Services.AddSingleton(tagService);

        var source = new Tag { Id = 1, Name = "Solowork" };
        var cut = Render<TagMergeModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceTag, source));

        Assert.Contains("Solowork", cut.Find(".tag-merge-modal-title").TextContent);

        // First candidate is suggested, so it auto-selects as the target
        var candidateItems = cut.FindAll(".tag-candidate-item");
        Assert.Equal(2, candidateItems.Count);
        Assert.Contains("Suggested", candidateItems[0].TextContent);
        Assert.Contains("selected", candidateItems[0].ClassList);

        var mergeBtn = cut.Find(".btn-danger");
        Assert.False(mergeBtn.HasAttribute("disabled"));
        Assert.Contains("Merge into Solo", mergeBtn.TextContent);
    }

    [Fact]
    public async Task SelectingCandidate_UpdatesSelectedTarget()
    {
        var tagService = Substitute.For<ITagService>();
        var candidates = new List<TagMergeCandidate>
        {
            new(2, "Target One", 1, false),
            new(3, "Target Two", 8, false)
        };
        tagService.GetMergeCandidatesAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(candidates);
        Services.AddSingleton(tagService);

        var source = new Tag { Id = 1, Name = "Source" };
        var cut = Render<TagMergeModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceTag, source));

        var items = cut.FindAll(".tag-candidate-item");
        await cut.InvokeAsync(() => items[1].Click());

        var mergeBtn = cut.Find(".btn-danger");
        Assert.Contains("Merge into Target Two", mergeBtn.TextContent);
    }

    [Fact]
    public async Task ConfirmMerge_CallsTagService_WithReplacementRuleFlag_AndInvokesCallback()
    {
        var tagService = Substitute.For<ITagService>();
        var targetTag = new Tag { Id = 2, Name = "Solo" };
        var candidates = new List<TagMergeCandidate> { new(2, "Solo", 3, true) };
        tagService.GetMergeCandidatesAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(candidates);
        tagService.MergeAsync(1, 2, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(TagOperationResult.Ok(targetTag));
        Services.AddSingleton(tagService);

        Tag? mergedResult = null;
        var source = new Tag { Id = 1, Name = "Solowork" };
        var cut = Render<TagMergeModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceTag, source)
            .Add(x => x.OnMerged, (Tag t) => mergedResult = t));

        var checkbox = cut.Find("#createRuleCheckbox");
        await cut.InvokeAsync(() => checkbox.Change(true));

        var mergeBtn = cut.Find(".btn-danger");
        await cut.InvokeAsync(() => mergeBtn.Click());

        await tagService.Received(1).MergeAsync(1, 2, true, Arg.Any<CancellationToken>());
        Assert.NotNull(mergedResult);
        Assert.Equal(2, mergedResult.Id);
    }

    [Fact]
    public void Modal_WithSourceTags_ShowsPluralTitleAndAllNames()
    {
        var tagService = Substitute.For<ITagService>();
        var candidates = new List<TagMergeCandidate> { new(3, "Solo", 5, true) };
        tagService.GetMergeCandidatesAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(candidates);
        Services.AddSingleton(tagService);

        var sources = new List<Tag> { new() { Id = 1, Name = "Solowork" }, new() { Id = 2, Name = "Solo Work" } };
        var cut = Render<TagMergeModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceTags, sources));

        Assert.Contains("Merge 2 Tags", cut.Find(".tag-merge-modal-title").TextContent);
        Assert.Contains("Solowork, Solo Work", cut.Find(".tag-merge-notice").TextContent);
    }

    [Fact]
    public void Modal_WithSourceTags_ExcludesAllSourcesFromCandidateList()
    {
        var tagService = Substitute.For<ITagService>();
        var candidates = new List<TagMergeCandidate>
        {
            new(2, "Solo Work", 1, false),
            new(3, "Solo", 5, true)
        };
        tagService.GetMergeCandidatesAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(candidates);
        Services.AddSingleton(tagService);

        var sources = new List<Tag> { new() { Id = 1, Name = "Solowork" }, new() { Id = 2, Name = "Solo Work" } };
        var cut = Render<TagMergeModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceTags, sources));

        var candidateItems = cut.FindAll(".tag-candidate-item");
        Assert.Single(candidateItems);
        Assert.Contains("Solo", candidateItems[0].TextContent);
    }

    [Fact]
    public async Task ConfirmMerge_WithSourceTags_CallsMergeManyAsync_AndInvokesCallback()
    {
        var tagService = Substitute.For<ITagService>();
        var targetTag = new Tag { Id = 3, Name = "Solo" };
        var candidates = new List<TagMergeCandidate> { new(3, "Solo", 5, true) };
        tagService.GetMergeCandidatesAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(candidates);
        tagService.MergeManyAsync(Arg.Any<IReadOnlyList<int>>(), 3, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(TagOperationResult.Ok(targetTag));
        Services.AddSingleton(tagService);

        Tag? mergedResult = null;
        var sources = new List<Tag> { new() { Id = 1, Name = "Solowork" }, new() { Id = 2, Name = "Solo Work" } };
        var cut = Render<TagMergeModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceTags, sources)
            .Add(x => x.OnMerged, (Tag t) => mergedResult = t));

        var mergeBtn = cut.Find(".btn-danger");
        await cut.InvokeAsync(() => mergeBtn.Click());

        await tagService.Received(1).MergeManyAsync(
            Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 1, 2 })), 3, false, Arg.Any<CancellationToken>());
        await tagService.DidNotReceive().MergeAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        Assert.NotNull(mergedResult);
        Assert.Equal(3, mergedResult.Id);
    }

    [Fact]
    public async Task ConfirmMerge_WhenServiceFails_ShowsErrorAndDoesNotInvokeCallback()
    {
        var tagService = Substitute.For<ITagService>();
        var candidates = new List<TagMergeCandidate> { new(2, "Solo", 3, true) };
        tagService.GetMergeCandidatesAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(candidates);
        tagService.MergeAsync(1, 2, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(TagOperationResult.Fail("Cannot merge a tag into itself."));
        Services.AddSingleton(tagService);

        var invoked = false;
        var source = new Tag { Id = 1, Name = "Solowork" };
        var cut = Render<TagMergeModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceTag, source)
            .Add(x => x.OnMerged, (Tag _) => invoked = true));

        var mergeBtn = cut.Find(".btn-danger");
        await cut.InvokeAsync(() => mergeBtn.Click());

        Assert.Contains("Cannot merge a tag into itself.", cut.Find(".alert-danger").TextContent);
        Assert.False(invoked);
    }
}
