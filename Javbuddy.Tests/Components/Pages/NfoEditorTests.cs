using BlazorMonaco.Editor;
using Bunit;
using Javbuddy.Components.Pages.MovieDetailSections;
using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Nfo;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class NfoEditorTests : BunitContext
{
    private readonly ILocalLibraryClient localLibrary = Substitute.For<ILocalLibraryClient>();
    private readonly INfoSyncService nfoSync = Substitute.For<INfoSyncService>();
    private readonly INfoHistoryService history = Substitute.For<INfoHistoryService>();

    public NfoEditorTests()
    {
        Services.AddSingleton(localLibrary);
        Services.AddSingleton(nfoSync);
        Services.AddSingleton(history);
        JSInterop.Mode = JSRuntimeMode.Loose;
        localLibrary.GetRawNfoAsync("ABC-123", Arg.Any<CancellationToken>()).Returns("<movie><title>On disk</title></movie>");
    }

    [Fact]
    public async Task ConflictReview_AfterAnEarlierOpen_MountsTheDiffOnlyOnceBothSidesHaveLoaded()
    {
        var proposed = new TaskCompletionSource<string?>();
        nfoSync.GenerateProposedNfoAsync(7, Arg.Any<CancellationToken>()).Returns(proposed.Task);
        var cut = Render<NfoEditor>(p => p.Add(x => x.Code, "ABC-123").Add(x => x.MovieId, 7));

        // An earlier edit session leaves the on-disk .nfo loaded.
        await cut.InvokeAsync(cut.Instance.OpenAsync);
        cut.FindAll("button").Single(b => b.TextContent == "Close").Click();

        var review = cut.InvokeAsync(cut.Instance.OpenConflictReviewAsync);

        // Mounted now, the diff editor's init would find no proposed .nfo to show, and nothing
        // would seed it once that arrives.
        Assert.Empty(cut.FindComponents<StandaloneDiffEditor>());
        Assert.Contains("Loading…", cut.Markup);

        proposed.SetResult("<movie><title>Proposed</title></movie>");
        await review;

        Assert.Single(cut.FindComponents<StandaloneDiffEditor>());
    }

    [Fact]
    public async Task History_ListsGenerations_AndRestoreWritesTheSelectedOneBack()
    {
        var older = new NfoGenerationItem(1, new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc), NfoWriteTrigger.ActorSync, "<movie>older</movie>");
        var newer = new NfoGenerationItem(2, new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc), NfoWriteTrigger.ManualEdit, "<movie>newer</movie>");
        history.GetGenerationsAsync(7, Arg.Any<CancellationToken>()).Returns(new List<NfoGenerationItem> { newer, older });
        history.RestoreAsync(7, 1, Arg.Any<CancellationToken>()).Returns("<movie>older</movie>");
        var cut = Render<NfoEditor>(p => p.Add(x => x.Code, "ABC-123").Add(x => x.MovieId, 7));
        await cut.InvokeAsync(cut.Instance.OpenAsync);

        cut.FindAll("button").Single(b => b.TextContent == "History").Click();

        var options = cut.FindAll("select.nfo-history-select option");
        Assert.Equal(["2", "1"], options.Select(o => o.GetAttribute("value")));
        Assert.Contains("replaced by manual edit", options[0].TextContent);
        Assert.Contains("replaced by actor name sync", options[1].TextContent);
        Assert.Single(cut.FindComponents<StandaloneDiffEditor>());

        cut.Find("select.nfo-history-select").Change("1");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Restore this version").Click();

        await history.Received(1).RestoreAsync(7, 1, Arg.Any<CancellationToken>());
        cut.WaitForAssertion(() => Assert.Contains("Saved.", cut.Markup));
        Assert.Empty(cut.FindAll("select.nfo-history-select"));
        Assert.Contains("Edit .nfo", cut.Find(".nfo-modal-title").TextContent);
    }

    [Fact]
    public async Task History_WithNoGenerations_SaysSoAndOffersNoRestore()
    {
        history.GetGenerationsAsync(7, Arg.Any<CancellationToken>()).Returns(new List<NfoGenerationItem>());
        var cut = Render<NfoEditor>(p => p.Add(x => x.Code, "ABC-123").Add(x => x.MovieId, 7));
        await cut.InvokeAsync(cut.Instance.OpenAsync);

        cut.FindAll("button").Single(b => b.TextContent == "History").Click();

        Assert.Contains("No earlier versions stored yet", cut.Markup);
        Assert.True(cut.FindAll("button").Single(b => b.TextContent.Trim() == "Restore this version").HasAttribute("disabled"));
    }
}
