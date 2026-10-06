using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.MinnanoAv;
using Javbuddy.Services.Warashi;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class CombinedActorSearchModalTests : BunitContext
{
    [Fact]
    public void Modal_WhenShowFalse_RendersNothing()
    {
        Services.AddSingleton(Substitute.For<IWarashiClient>());
        Services.AddSingleton(Substitute.For<IMinnanoAvClient>());

        var actor = new Actor { Id = 1, FirstName = "Miho", LastName = "Tohno" };
        var cut = Render<CombinedActorSearchModal>(p => p
            .Add(x => x.Show, false)
            .Add(x => x.Actor, actor));

        Assert.Empty(cut.FindAll(".search-modal-backdrop"));
    }

    [Fact]
    public async Task Modal_WhenShowTrue_SearchesBothProvidersConcurrentlyAndRendersBothSections()
    {
        var warashiClient = Substitute.For<IWarashiClient>();
        var minnanoAvClient = Substitute.For<IMinnanoAvClient>();

        warashiClient.SearchPerformersAsync("通野未帆", Arg.Any<CancellationToken>())
            .Returns(new List<WarashiSearchResult>
            {
                new(Name: "Miho TOHNO", JapaneseName: "通野未帆", PathOrUrl: "/en/actress-1", ImageUrl: null, CareerActivity: null, IsExactMatch: true, KnownAliases: Array.Empty<string>())
            });

        minnanoAvClient.SearchPerformersAsync("通野未帆", Arg.Any<CancellationToken>())
            .Returns(new List<MinnanoAvSearchResult>
            {
                new(Name: "通野未帆", Kana: "とおのみほ", Romaji: "Tohno Miho", PathOrUrl: "actress148426.html", ImageUrl: null, DebutInfo: null, IsExactMatch: true, KnownAliases: Array.Empty<string>())
            });

        Services.AddSingleton(warashiClient);
        Services.AddSingleton(minnanoAvClient);

        var actor = new Actor { Id = 1, FirstName = "Miho", LastName = "Tohno", JapaneseNameKanji = "通野未帆" };
        var cut = Render<CombinedActorSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor));

        await warashiClient.Received(1).SearchPerformersAsync("通野未帆", Arg.Any<CancellationToken>());
        await minnanoAvClient.Received(1).SearchPerformersAsync("通野未帆", Arg.Any<CancellationToken>());

        var sections = cut.FindAll(".combined-search-source-title");
        Assert.Equal(2, sections.Count);
        Assert.Equal("WAPdB", sections[0].TextContent);
        Assert.Equal("minnano-av.com", sections[1].TextContent);

        var items = cut.FindAll(".search-result-item");
        Assert.Equal(2, items.Count);
        Assert.Contains("Miho TOHNO", items[0].TextContent);
        Assert.Contains("通野未帆", items[1].TextContent);
    }

    [Fact]
    public async Task Modal_WhenInspectingWarashiResult_InvokesOnPickWarashiWithPath()
    {
        var warashiClient = Substitute.For<IWarashiClient>();
        var minnanoAvClient = Substitute.For<IMinnanoAvClient>();

        warashiClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<WarashiSearchResult>
            {
                new(Name: "Miho TOHNO", JapaneseName: "通野未帆", PathOrUrl: "/en/actress-1", ImageUrl: null, CareerActivity: null, IsExactMatch: true, KnownAliases: Array.Empty<string>())
            });
        minnanoAvClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<MinnanoAvSearchResult>());

        Services.AddSingleton(warashiClient);
        Services.AddSingleton(minnanoAvClient);

        string? pickedPath = null;
        var actor = new Actor { Id = 1, FirstName = "Miho", LastName = "Tohno" };
        var cut = Render<CombinedActorSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor)
            .Add(x => x.OnPickWarashi, (string path) => pickedPath = path));

        var inspectBtn = cut.Find(".search-result-item button");
        await cut.InvokeAsync(() => inspectBtn.Click());

        Assert.Equal("/en/actress-1", pickedPath);
    }

    [Fact]
    public async Task Modal_WhenInspectingMinnanoAvResult_InvokesOnPickMinnanoAvWithPath()
    {
        var warashiClient = Substitute.For<IWarashiClient>();
        var minnanoAvClient = Substitute.For<IMinnanoAvClient>();

        warashiClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<WarashiSearchResult>());
        minnanoAvClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<MinnanoAvSearchResult>
            {
                new(Name: "通野未帆", Kana: "とおのみほ", Romaji: "Tohno Miho", PathOrUrl: "actress148426.html", ImageUrl: null, DebutInfo: null, IsExactMatch: true, KnownAliases: Array.Empty<string>())
            });

        Services.AddSingleton(warashiClient);
        Services.AddSingleton(minnanoAvClient);

        string? pickedPath = null;
        var actor = new Actor { Id = 1, FirstName = "Miho", LastName = "Tohno" };
        var cut = Render<CombinedActorSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor)
            .Add(x => x.OnPickMinnanoAv, (string path) => pickedPath = path));

        var inspectBtn = cut.Find(".search-result-item button");
        await cut.InvokeAsync(() => inspectBtn.Click());

        Assert.Equal("actress148426.html", pickedPath);
    }

    [Fact]
    public async Task Modal_WhenOneProviderFails_StillShowsTheOthersResults()
    {
        var warashiClient = Substitute.For<IWarashiClient>();
        var minnanoAvClient = Substitute.For<IMinnanoAvClient>();

        warashiClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<WarashiSearchResult>>(_ => throw new HttpRequestException("boom"));
        minnanoAvClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<MinnanoAvSearchResult>
            {
                new(Name: "通野未帆", Kana: "とおのみほ", Romaji: "Tohno Miho", PathOrUrl: "actress148426.html", ImageUrl: null, DebutInfo: null, IsExactMatch: true, KnownAliases: Array.Empty<string>())
            });

        Services.AddSingleton(warashiClient);
        Services.AddSingleton(minnanoAvClient);

        var actor = new Actor { Id = 1, FirstName = "Miho", LastName = "Tohno" };
        var cut = Render<CombinedActorSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor));

        Assert.Contains("WAPdB search failed", cut.Markup);
        var items = cut.FindAll(".search-result-item");
        Assert.Single(items);
        Assert.Contains("通野未帆", items[0].TextContent);
    }

    [Fact]
    public async Task Modal_WhenCloseClicked_InvokesOnClose()
    {
        var warashiClient = Substitute.For<IWarashiClient>();
        var minnanoAvClient = Substitute.For<IMinnanoAvClient>();
        warashiClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new List<WarashiSearchResult>());
        minnanoAvClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new List<MinnanoAvSearchResult>());
        Services.AddSingleton(warashiClient);
        Services.AddSingleton(minnanoAvClient);

        var closed = false;
        var actor = new Actor { Id = 1, FirstName = "Miho", LastName = "Tohno" };
        var cut = Render<CombinedActorSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor)
            .Add(x => x.OnClose, () => closed = true));

        var closeBtn = cut.Find(".search-modal-close-btn");
        await cut.InvokeAsync(() => closeBtn.Click());

        Assert.True(closed);
    }
}
