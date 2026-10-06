using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Torrents;
using Javbuddy.Services.VrMerge;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

/// <summary>Covers TorrentSort.razor's Destination step: a row of buttons — one per
/// Javbuddy-configured named destination alias, showing the underlying path as a hover title,
/// the selected one visually marked — replacing the old freetext input, plus the blocked state
/// shown when no aliases are configured.</summary>
public class TorrentSortDestinationStepTests : BunitContext
{
    private (TestDbContextFactory Factory, int TorrentDownloadId) SetUpServices(IConfiguration? config = null)
    {
        var factory = new TestDbContextFactory();
        int torrentDownloadId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "ABC-123" };
            db.Movies.Add(movie);
            db.SaveChanges();

            var torrent = new TorrentDownload { MovieId = movie.Id, MovieCode = "ABC-123", SavePath = "/downloads/ABC-123" };
            db.TorrentDownloads.Add(torrent);
            db.SaveChanges();
            torrentDownloadId = torrent.Id;
        }

        var torrentSortService = Substitute.For<ITorrentSortService>();
        torrentSortService.GetJavinizerBaseUrlAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var pathMappingService = Substitute.For<IPathMappingService>();
        pathMappingService.TranslateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<string>());

        var localLibraryClient = Substitute.For<ILocalLibraryClient>();
        var vrMergeService = Substitute.For<IVrMergeService>();
        var vrMergeJobTracker = Substitute.For<IVrMergeJobTracker>();
        var ffmpegResolver = Substitute.For<IFfmpegBinaryResolver>();
        ffmpegResolver.IsAvailable.Returns(false);

        var state = new TorrentSortWizardState(
            torrentSortService, pathMappingService, localLibraryClient, factory,
            config ?? new ConfigurationBuilder().Build(), vrMergeService, vrMergeJobTracker, ffmpegResolver);
        Services.AddSingleton(state);

        return (factory, torrentDownloadId);
    }

    private static IConfiguration TwoAliasesConfig() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Javinizer:DestinationAliases:0:Name"] = "jav",
        ["Javinizer:DestinationAliases:0:Path"] = "/media/jav",
        ["Javinizer:DestinationAliases:1:Name"] = "vr",
        ["Javinizer:DestinationAliases:1:Path"] = "/media/vr"
    }).Build();

    private async Task<IRenderedComponent<TorrentSort>> RenderAtDestinationStepAsync(int torrentDownloadId)
    {
        var state = Services.GetRequiredService<TorrentSortWizardState>();
        await state.InitializeAsync(torrentDownloadId);
        state.Step = WizardStep.Destination;

        return Render<TorrentSort>(p => p.Add(x => x.TorrentDownloadId, torrentDownloadId));
    }

    [Fact]
    public async Task DestinationStep_AliasesConfigured_RendersButtonsWithFirstAliasActiveAndPathAsTitle()
    {
        var (factory, torrentDownloadId) = SetUpServices(TwoAliasesConfig());
        using var f = factory;

        var cut = await RenderAtDestinationStepAsync(torrentDownloadId);

        var buttons = cut.FindAll("button").Where(b => b.TextContent.Trim() is "jav" or "vr").ToList();
        Assert.Equal(2, buttons.Count);

        var javButton = buttons.Single(b => b.TextContent.Trim() == "jav");
        var vrButton = buttons.Single(b => b.TextContent.Trim() == "vr");

        Assert.Equal("/media/jav", javButton.GetAttribute("title"));
        Assert.Equal("/media/vr", vrButton.GetAttribute("title"));
        Assert.Contains("btn-outline-primary", javButton.ClassList);
        Assert.Contains("btn-outline-secondary", vrButton.ClassList);
        Assert.DoesNotContain("No destination aliases are configured", cut.Markup);
    }

    [Fact]
    public async Task DestinationStep_ClickingAnotherAliasButton_UpdatesStateDestinationAndActiveStyling()
    {
        var (factory, torrentDownloadId) = SetUpServices(TwoAliasesConfig());
        using var f = factory;

        var cut = await RenderAtDestinationStepAsync(torrentDownloadId);
        var state = Services.GetRequiredService<TorrentSortWizardState>();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "vr").Click();

        Assert.Equal("vr", state.Scrape.SelectedDestinationAliasName);
        Assert.Equal("/media/vr", state.Destination);

        var vrButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "vr");
        var javButton = cut.FindAll("button").Single(b => b.TextContent.Trim() == "jav");
        Assert.Contains("btn-outline-primary", vrButton.ClassList);
        Assert.Contains("btn-outline-secondary", javButton.ClassList);
    }

    [Fact]
    public async Task DestinationStep_NoAliasesConfigured_ShowsBlockedMessage_NoAliasButtons()
    {
        var (factory, torrentDownloadId) = SetUpServices();
        using var f = factory;

        var cut = await RenderAtDestinationStepAsync(torrentDownloadId);

        Assert.Contains("No destination aliases are configured", cut.Markup);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() is "jav" or "vr");
    }
}
