using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Movies;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class MovieStillsGenerateModalTests : BunitContext
{
    private readonly IMovieExtraFanartStillsService service = Substitute.For<IMovieExtraFanartStillsService>();
    private int savedCount;
    private int closedCount;

    public MovieStillsGenerateModalTests()
    {
        Services.AddSingleton(service);
    }

    private IRenderedComponent<MovieStillsGenerateModal> RenderModal(bool show = true, bool isVr = false) =>
        Render<MovieStillsGenerateModal>(p => p
            .Add(m => m.Show, show)
            .Add(m => m.MovieId, 7)
            .Add(m => m.Code, "IPX-535")
            .Add(m => m.IsVr, isVr)
            .Add(m => m.MinSavingMilliseconds, 0)
            .Add(m => m.OnSaved, () => savedCount++)
            .Add(m => m.OnClose, () => closedCount++));

    private static ExtraFanartStillsPreview Preview()
    {
        var dir = Path.Combine(Path.GetTempPath(), "javbuddy-stills-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new ExtraFanartStillsPreview(dir, [new(1, 90, [1, 2, 3]), new(2, 400, [1, 2, 3])]);
    }

    [Fact]
    public void RendersNothing_WhenShowIsFalse() => Assert.Empty(RenderModal(show: false).FindAll(".modal-shell-backdrop"));

    [Fact]
    public async Task Generate_ShowsAllStillsTicked_AndSaveSendsTheSelection()
    {
        service.PreviewAsync(7, 10, ExtraFanartStillsMode.Append, false, Arg.Any<Action<ExtraFanartStill>?>(), Arg.Any<CancellationToken>()).Returns(new ExtraFanartStillsPreviewResult(Preview()));
        service.SaveAsync(7, Arg.Any<ExtraFanartStillsPreview>(), Arg.Any<IReadOnlyCollection<int>>(), 92, ExtraFanartStillsMode.Append, Arg.Any<CancellationToken>())
            .Returns(ExtraFanartResult.Ok(1));
        var cut = RenderModal();

        cut.FindAll("button").First(b => b.TextContent.Contains("Generate previews")).Click();
        Assert.Equal(2, cut.FindAll(".stills-tile").Count);
        cut.FindAll(".stills-tile input")[1].Change(false);
        cut.FindAll("button").First(b => b.TextContent.Contains("Save 1 selected")).Click();

        // The save runs off the circuit thread, so its outcome arrives a moment after the click.
        await cut.WaitForAssertionAsync(() =>
        {
            service.Received(1).SaveAsync(7, Arg.Any<ExtraFanartStillsPreview>(),
                Arg.Is<IReadOnlyCollection<int>>(ids => ids.Count == 1 && ids.Contains(1)), 92, ExtraFanartStillsMode.Append, Arg.Any<CancellationToken>());
            Assert.Equal(1, savedCount);
            Assert.Equal(1, closedCount);
        });
    }

    [Fact]
    public async Task Save_ThatBlocksBeforeItsFirstAwait_DoesNotHoldBackTheSavingState()
    {
        // The real service re-encodes every frame synchronously before its first await; run on the circuit, that kept
        // the saving state from rendering until the encoding was done.
        service.PreviewAsync(7, 10, ExtraFanartStillsMode.Append, false, Arg.Any<Action<ExtraFanartStill>?>(), Arg.Any<CancellationToken>()).Returns(new ExtraFanartStillsPreviewResult(Preview()));
        using var gate = new ManualResetEventSlim();
        service.SaveAsync(7, Arg.Any<ExtraFanartStillsPreview>(), Arg.Any<IReadOnlyCollection<int>>(), 92, ExtraFanartStillsMode.Append, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                gate.Wait(TimeSpan.FromSeconds(10));
                return Task.FromResult(ExtraFanartResult.Ok(2));
            });
        var cut = RenderModal();
        cut.FindAll("button").First(b => b.TextContent.Contains("Generate previews")).Click();

        cut.FindAll("button").First(b => b.TextContent.Contains("Save 2 selected")).Click();

        try
        {
            Assert.NotEmpty(cut.FindAll(".stills-saving"));
        }
        finally
        {
            gate.Set();
        }
        await cut.WaitForAssertionAsync(() => Assert.Equal(1, closedCount));
    }

    [Fact]
    public async Task ANegativeMinimum_DoesNotWaitForever()
    {
        service.PreviewAsync(7, 10, ExtraFanartStillsMode.Append, false, Arg.Any<Action<ExtraFanartStill>?>(), Arg.Any<CancellationToken>()).Returns(new ExtraFanartStillsPreviewResult(Preview()));
        service.SaveAsync(7, Arg.Any<ExtraFanartStillsPreview>(), Arg.Any<IReadOnlyCollection<int>>(), 92, ExtraFanartStillsMode.Append, Arg.Any<CancellationToken>())
            .Returns(ExtraFanartResult.Ok(2));
        var cut = Render<MovieStillsGenerateModal>(p => p
            .Add(m => m.Show, true).Add(m => m.MovieId, 7).Add(m => m.Code, "IPX-535").Add(m => m.MinSavingMilliseconds, -1).Add(m => m.OnClose, () => closedCount++));
        cut.FindAll("button").First(b => b.TextContent.Contains("Generate previews")).Click();

        cut.FindAll("button").First(b => b.TextContent.Contains("Save 2 selected")).Click();

        await cut.WaitForAssertionAsync(() => Assert.Equal(1, closedCount), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Save_ShowsASpinnerAndTheCount_WhileSaving_ThenTheResult()
    {
        service.PreviewAsync(7, 10, ExtraFanartStillsMode.Append, false, Arg.Any<Action<ExtraFanartStill>?>(), Arg.Any<CancellationToken>()).Returns(new ExtraFanartStillsPreviewResult(Preview()));
        var saving = new TaskCompletionSource<ExtraFanartResult>();
        service.SaveAsync(7, Arg.Any<ExtraFanartStillsPreview>(), Arg.Any<IReadOnlyCollection<int>>(), 92, ExtraFanartStillsMode.Append, Arg.Any<CancellationToken>())
            .Returns(saving.Task);
        var cut = RenderModal();
        Assert.Empty(cut.FindAll(".stills-saving"));
        cut.FindAll("button").First(b => b.TextContent.Contains("Generate previews")).Click();

        cut.FindAll("button").First(b => b.TextContent.Contains("Save 2 selected")).Click();

        var status = cut.Find(".stills-saving");
        Assert.Equal("status", status.GetAttribute("role"));
        Assert.Contains("Saving 2 images", status.TextContent);
        Assert.NotNull(status.QuerySelector(".spinner-border"));
        var saveButton = cut.FindAll("button").First(b => b.TextContent.Contains("Saving…"));
        Assert.NotNull(saveButton.QuerySelector(".spinner-border"));
        Assert.True(saveButton.HasAttribute("disabled"));

        saving.SetResult(ExtraFanartResult.Ok(2));
        await cut.WaitForAssertionAsync(() =>
        {
            Assert.Empty(cut.FindAll(".stills-saving"));
            Assert.Equal(1, closedCount);
        });
    }

    [Fact]
    public async Task Save_KeepsTheSavingStateVisible_ForAtLeastTheMinimumTime()
    {
        service.PreviewAsync(7, 10, ExtraFanartStillsMode.Append, false, Arg.Any<Action<ExtraFanartStill>?>(), Arg.Any<CancellationToken>()).Returns(new ExtraFanartStillsPreviewResult(Preview()));
        service.SaveAsync(7, Arg.Any<ExtraFanartStillsPreview>(), Arg.Any<IReadOnlyCollection<int>>(), 92, ExtraFanartStillsMode.Append, Arg.Any<CancellationToken>())
            .Returns(ExtraFanartResult.Ok(2));
        var cut = Render<MovieStillsGenerateModal>(p => p
            .Add(m => m.Show, true).Add(m => m.MovieId, 7).Add(m => m.Code, "IPX-535").Add(m => m.MinSavingMilliseconds, 1000).Add(m => m.OnClose, () => closedCount++));
        cut.FindAll("button").First(b => b.TextContent.Contains("Generate previews")).Click();

        cut.FindAll("button").First(b => b.TextContent.Contains("Save 2 selected")).Click();

        // The save itself is instant, yet the state is still showing a moment later...
        await Task.Delay(100);
        Assert.NotEmpty(cut.FindAll(".stills-saving"));
        // ...and gives way to the result once the minimum has passed.
        await cut.WaitForAssertionAsync(() => Assert.Equal(1, closedCount), TimeSpan.FromSeconds(5));
        Assert.Empty(cut.FindAll(".stills-saving"));
    }

    [Fact]
    public async Task Save_Failure_KeepsTheDialogOpen_WithTheError()
    {
        service.PreviewAsync(7, 10, ExtraFanartStillsMode.Append, false, Arg.Any<Action<ExtraFanartStill>?>(), Arg.Any<CancellationToken>()).Returns(new ExtraFanartStillsPreviewResult(Preview()));
        service.SaveAsync(7, Arg.Any<ExtraFanartStillsPreview>(), Arg.Any<IReadOnlyCollection<int>>(), 92, ExtraFanartStillsMode.Append, Arg.Any<CancellationToken>())
            .Returns(ExtraFanartResult.Fail("Disk full."));
        var cut = RenderModal();
        cut.FindAll("button").First(b => b.TextContent.Contains("Generate previews")).Click();

        cut.FindAll("button").First(b => b.TextContent.Contains("Save 2 selected")).Click();

        await cut.WaitForAssertionAsync(() => Assert.Contains("Disk full.", cut.Markup));
        Assert.Equal(0, closedCount);
        Assert.Equal(0, savedCount);
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Save 2 selected"));
    }

    [Fact]
    public async Task Stop_CancelsTheRunningGeneration_AndReturnsTheDialogToIdle()
    {
        CancellationToken seen = default;
        service.PreviewAsync(7, 10, ExtraFanartStillsMode.Append, false, Arg.Any<Action<ExtraFanartStill>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                seen = call.Arg<CancellationToken>();
                call.Arg<Action<ExtraFanartStill>?>()?.Invoke(new ExtraFanartStill(1, 90, [1, 2, 3]));
                await Task.Delay(Timeout.Infinite, seen);
                return new ExtraFanartStillsPreviewResult(null);
            });
        var cut = RenderModal();
        cut.FindAll("button").First(b => b.TextContent.Contains("Generate previews")).Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".stills-tile img")));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Cancel");

        cut.FindAll("button").First(b => b.TextContent.Trim() is "Stop").Click();

        await cut.WaitForAssertionAsync(() =>
        {
            Assert.True(seen.IsCancellationRequested);
            Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Generate previews"));
            Assert.Empty(cut.FindAll(".stills-tile"));
            Assert.Empty(cut.FindAll(".alert-danger"));
        });
        Assert.Equal(0, closedCount);
    }

    [Fact]
    public void Generate_Failure_ShowsTheError()
    {
        service.PreviewAsync(7, 10, ExtraFanartStillsMode.Append, false, Arg.Any<Action<ExtraFanartStill>?>(), Arg.Any<CancellationToken>()).Returns(new ExtraFanartStillsPreviewResult(null, "No local video."));
        var cut = RenderModal();

        cut.FindAll("button").First(b => b.TextContent.Contains("Generate previews")).Click();

        Assert.Contains("No local video.", cut.Find(".alert-danger").TextContent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LeftEyeCrop_StartsTickedOnlyForVrMovies(bool isVr)
    {
        var cut = RenderModal(isVr: isVr);

        Assert.Equal(isVr, cut.Find("#stills-left-eye").HasAttribute("checked"));
    }

    [Fact]
    public void DefaultsToTenStills() => Assert.Equal("10", RenderModal().Find("input[type=number]").GetAttribute("value"));

    [Fact]
    public async Task Stills_appear_as_they_arrive_with_shimmer_placeholders_for_the_rest()
    {
        var release = new TaskCompletionSource<ExtraFanartStillsPreviewResult>();
        service.PreviewAsync(7, 10, ExtraFanartStillsMode.Append, false, Arg.Any<Action<ExtraFanartStill>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.ArgAt<Action<ExtraFanartStill>?>(4)!(new ExtraFanartStill(1, 90, [1, 2, 3]));
                return release.Task;
            });
        var cut = RenderModal();

        cut.FindAll("button").First(b => b.TextContent.Contains("Generate previews")).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("img"));
            Assert.Equal(9, cut.FindAll(".stills-shimmer").Count);
        });
        release.SetResult(new ExtraFanartStillsPreviewResult(Preview()));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".stills-shimmer")));
    }
}
