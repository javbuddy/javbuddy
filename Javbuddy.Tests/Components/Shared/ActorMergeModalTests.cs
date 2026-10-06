using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class ActorMergeModalTests : BunitContext
{
    [Fact]
    public void Modal_WhenShowFalse_RendersNothing()
    {
        var actorService = Substitute.For<IActorService>();
        Services.AddSingleton(actorService);

        var source = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var cut = Render<ActorMergeModal>(p => p
            .Add(x => x.Show, false)
            .Add(x => x.SourceActor, source));

        Assert.Empty(cut.FindAll(".merge-modal-backdrop"));
    }

    [Fact]
    public void Modal_WhenShowTrue_RendersSourceAndCandidates()
    {
        var actorService = Substitute.For<IActorService>();
        var candidates = new List<ActorMergeCandidate>
        {
            new(2, "Mikami Yua", "三上悠亜", "みかみ ゆあ", 5, false, true),
            new(3, "Yui Hatano", "波多野結衣", null, 12, true, false)
        };
        actorService.GetMergeCandidatesAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(candidates);
        Services.AddSingleton(actorService);

        var source = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var cut = Render<ActorMergeModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceActor, source));

        // Source card shows actor display name
        var sourceCard = cut.Find(".source-card");
        Assert.Contains("Mikami Yua", sourceCard.TextContent);

        // First candidate is suggested, so it auto-selects as the target
        var targetCard = cut.Find(".target-card");
        Assert.Contains("Mikami Yua", targetCard.TextContent);

        // Candidate items are rendered
        var candidateItems = cut.FindAll(".candidate-item");
        Assert.Equal(2, candidateItems.Count);
        Assert.Contains("Suggested", candidateItems[0].TextContent);

        // Merge button is enabled with target name
        var mergeBtn = cut.Find(".btn-danger");
        Assert.False(mergeBtn.HasAttribute("disabled"));
        Assert.Contains("Merge into Mikami Yua", mergeBtn.TextContent);
    }

    [Fact]
    public async Task SelectingCandidate_UpdatesTargetAndPreview()
    {
        var actorService = Substitute.For<IActorService>();
        var candidates = new List<ActorMergeCandidate>
        {
            new(2, "Target One", null, null, 1, false, false),
            new(3, "Target Two", null, null, 8, false, false)
        };
        actorService.GetMergeCandidatesAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(candidates);
        Services.AddSingleton(actorService);

        var source = new Actor { Id = 1, FirstName = "Source", LastName = "Actor" };
        var cut = Render<ActorMergeModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceActor, source));

        // Click second candidate
        var items = cut.FindAll(".candidate-item");
        await cut.InvokeAsync(() => items[1].Click());

        // Target card now shows Target Two
        var targetCard = cut.Find(".target-card");
        Assert.Contains("Target Two", targetCard.TextContent);

        var preview = cut.Find(".merge-preview-summary");
        Assert.Contains("Target Two", preview.TextContent);
    }

    [Fact]
    public async Task ConfirmMerge_CallsActorService_AndInvokesCallback()
    {
        var actorService = Substitute.For<IActorService>();
        var targetActor = new Actor { Id = 2, FirstName = "Canonical", LastName = "Target" };
        var candidates = new List<ActorMergeCandidate>
        {
            new(2, "Target Canonical", null, null, 3, false, true)
        };
        actorService.GetMergeCandidatesAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(candidates);
        actorService.MergeAsync(1, 2, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ActorOperationResult.Ok(targetActor));
        Services.AddSingleton(actorService);

        Actor? mergedResult = null;
        var source = new Actor { Id = 1, FirstName = "Duplicate", LastName = "Actor" };
        var cut = Render<ActorMergeModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceActor, source)
            .Add(x => x.OnMerged, (Actor a) => mergedResult = a));

        var mergeBtn = cut.Find(".btn-danger");
        await cut.InvokeAsync(() => mergeBtn.Click());

        await actorService.Received(1).MergeAsync(1, 2, true, Arg.Any<CancellationToken>());
        Assert.NotNull(mergedResult);
        Assert.Equal(2, mergedResult.Id);
    }

    [Fact]
    public void Modal_WhenActorsHaveImages_RendersImgTags()
    {
        var actorService = Substitute.For<IActorService>();
        var candidates = new List<ActorMergeCandidate>
        {
            new(2, "Target Imaged", null, null, 5, true, true),
            new(3, "Target NoImage", null, null, 2, false, false)
        };
        actorService.GetMergeCandidatesAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(candidates);
        Services.AddSingleton(actorService);

        var source = new Actor { Id = 1, FirstName = "Source", LastName = "Imaged" };
        var cut = Render<ActorMergeModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceActor, source)
            .Add(x => x.SourceHasImage, true));

        // Source card renders <img> with thumb URL
        var sourceCard = cut.Find(".source-card");
        var sourceImg = sourceCard.QuerySelector("img");
        Assert.NotNull(sourceImg);
        Assert.Equal("/actor-image/1/thumb", sourceImg.GetAttribute("src"));

        // Target card auto-selected suggested candidate with image
        var targetCard = cut.Find(".target-card");
        var targetImg = targetCard.QuerySelector("img");
        Assert.NotNull(targetImg);
        Assert.Equal("/actor-image/2/thumb", targetImg.GetAttribute("src"));

        // First candidate has img, second candidate has initials
        var items = cut.FindAll(".candidate-item");
        Assert.NotNull(items[0].QuerySelector("img"));
        Assert.Equal("/actor-image/2/thumb", items[0].QuerySelector("img")!.GetAttribute("src"));
        Assert.Null(items[1].QuerySelector("img"));
        Assert.Contains("TN", items[1].QuerySelector(".candidate-avatar")!.TextContent);
    }

    [Fact]
    public async Task MergeModal_WhenUpdateNfoUnticked_PassesFalseToMergeAsync()
    {
        var actorService = Substitute.For<IActorService>();
        var targetActor = new Actor { Id = 2, FirstName = "Canonical", LastName = "Target" };
        var candidates = new List<ActorMergeCandidate>
        {
            new(2, "Target Canonical", null, null, 3, false, true)
        };
        actorService.GetMergeCandidatesAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(candidates);
        actorService.MergeAsync(1, 2, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ActorOperationResult.Ok(targetActor));
        Services.AddSingleton(actorService);

        var source = new Actor { Id = 1, FirstName = "Duplicate", LastName = "Actor" };
        var cut = Render<ActorMergeModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceActor, source));

        var checkbox = cut.Find("#updateNfoCheckbox");
        await cut.InvokeAsync(() => checkbox.Change(false));

        var mergeBtn = cut.Find(".btn-danger");
        await cut.InvokeAsync(() => mergeBtn.Click());

        await actorService.Received(1).MergeAsync(1, 2, false, Arg.Any<CancellationToken>());
    }
}

