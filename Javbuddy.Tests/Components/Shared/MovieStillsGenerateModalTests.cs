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
            .Add(m => m.OnSaved, () => savedCount++));

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

        await service.Received(1).SaveAsync(7, Arg.Any<ExtraFanartStillsPreview>(),
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.Count == 1 && ids.Contains(1)), 92, ExtraFanartStillsMode.Append, Arg.Any<CancellationToken>());
        Assert.Equal(1, savedCount);
        Assert.Contains("Added 1 image", cut.Markup);
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
