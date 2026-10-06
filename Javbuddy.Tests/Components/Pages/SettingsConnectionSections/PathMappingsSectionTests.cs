using Bunit;
using Javbuddy.Components.Pages.SettingsConnectionSections;
using Javbuddy.Models;
using Javbuddy.Services.Torrents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Javbuddy.Tests.Components.Pages.SettingsConnectionSections;

public class PathMappingsSectionTests : BunitContext
{
    private readonly IPathMappingService pathMappingService = Substitute.For<IPathMappingService>();

    private void Register(List<PathMapping> mappings, Dictionary<string, string?>? env = null)
    {
        pathMappingService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(mappings);
        Services.AddSingleton(pathMappingService);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(env ?? []).Build());
    }

    [Fact]
    public void RendersOneCardPerMappingWithThreeLabelledPrefixFields()
    {
        Register([new PathMapping { Id = 7, QBittorrentPrefix = "/downloads", JavinizerPrefix = "/scratch", AppPrefix = @"D:\scratch" }]);

        var cut = Render<PathMappingsSection>();

        var cards = cut.FindAll(".path-mapping-card");
        Assert.Equal(2, cards.Count); // the mapping plus the "New mapping" card
        Assert.Equal("/downloads", cut.Find("#pm-7-qbittorrent").GetAttribute("value"));
        Assert.Equal("/scratch", cut.Find("#pm-7-javinizer").GetAttribute("value"));
        Assert.Equal(@"D:\scratch", cut.Find("#pm-7-app").GetAttribute("value"));
        Assert.Contains("qBittorrent prefix", cards[0].TextContent);
        Assert.Contains("javinizer-go prefix", cards[0].TextContent);
        Assert.Contains("App prefix (optional)", cards[0].TextContent);
    }

    [Fact]
    public void EditingAMapping_EnablesSaveAndCallsUpdateAsync()
    {
        Register([new PathMapping { Id = 7, QBittorrentPrefix = "/downloads", JavinizerPrefix = "/scratch" }]);
        var cut = Render<PathMappingsSection>();
        var save = cut.FindAll(".path-mapping-card")[0].QuerySelector("button.btn-primary")!;
        Assert.True(save.HasAttribute("disabled"));

        cut.Find("#pm-7-app").Input(@"D:\scratch");
        cut.FindAll(".path-mapping-card")[0].QuerySelector("button.btn-primary")!.Click();

        pathMappingService.Received(1).UpdateAsync(7, "/downloads", "/scratch", @"D:\scratch", Arg.Any<CancellationToken>());
        cut.WaitForAssertion(() => Assert.Contains("Saved.", cut.Markup));
    }

    [Fact]
    public void SavingADuplicateQBittorrentPrefix_ShowsErrorOnThatCard()
    {
        Register([new PathMapping { Id = 7, QBittorrentPrefix = "/downloads", JavinizerPrefix = "/scratch" }]);
        pathMappingService.UpdateAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateException());
        var cut = Render<PathMappingsSection>();

        cut.Find("#pm-7-qbittorrent").Input("/other");
        cut.FindAll(".path-mapping-card")[0].QuerySelector("button.btn-primary")!.Click();

        cut.WaitForAssertion(() => Assert.Contains("A mapping for \"/other\" already exists.", cut.FindAll(".path-mapping-card")[0].TextContent));
    }

    [Fact]
    public void AddingAMapping_CallsAddAsyncWithAllThreePrefixes()
    {
        Register([]);
        var cut = Render<PathMappingsSection>();

        cut.Find("#pm-new-qbittorrent").Input("/downloads");
        cut.Find("#pm-new-javinizer").Input("/scratch");
        cut.Find("#pm-new-app").Input(@"D:\scratch");
        cut.Find(".path-mapping-card-new button.btn-primary").Click();

        pathMappingService.Received(1).AddAsync("/downloads", "/scratch", @"D:\scratch", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EnvConfigured_ShowsMappingReadOnlyWithoutEditOrAddControls()
    {
        Register(
            [new PathMapping { QBittorrentPrefix = "/downloads", JavinizerPrefix = "/scratch" }],
            new Dictionary<string, string?>
            {
                ["PathMapping:QBittorrentPrefix"] = "/downloads",
                ["PathMapping:JavinizerPrefix"] = "/scratch"
            });

        var cut = Render<PathMappingsSection>();

        Assert.Single(cut.FindAll(".path-mapping-card"));
        Assert.Empty(cut.FindAll(".path-mapping-card-new"));
        Assert.Empty(cut.FindAll(".path-mapping-card button"));
        Assert.True(cut.Find("#pm-0-qbittorrent").HasAttribute("disabled"));
    }
}
