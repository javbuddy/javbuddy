using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.MinnanoAv;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class MinnanoAvMetadataSearchModalTests : BunitContext
{
    [Fact]
    public void Modal_WhenShowFalse_RendersNothing()
    {
        var client = Substitute.For<IMinnanoAvClient>();
        var actorService = Substitute.For<IActorService>();
        Services.AddSingleton(client);
        Services.AddSingleton(actorService);

        var actor = new Actor { Id = 1, FirstName = "Miho", LastName = "Tohno" };
        var cut = Render<MinnanoAvMetadataSearchModal>(p => p
            .Add(x => x.Show, false)
            .Add(x => x.Actor, actor));

        Assert.Empty(cut.FindAll(".search-modal-backdrop"));
    }

    [Fact]
    public async Task Modal_WhenInitialSelectedPathProvided_SkipsSearchAndLoadsThatCandidateDirectly()
    {
        var client = Substitute.For<IMinnanoAvClient>();
        var actorService = Substitute.For<IActorService>();

        var performerDetail = new MinnanoAvPerformerDetail
        {
            Name = "通野未帆",
            Kana = "とおのみほ",
            Romaji = "Tohno Miho",
            HeightCm = 160,
            CupSize = "E",
            Aliases = new List<string>()
        };
        client.GetPerformerDetailAsync("actress148426.html", Arg.Any<CancellationToken>())
            .Returns(performerDetail);

        Services.AddSingleton(client);
        Services.AddSingleton(actorService);

        var actor = new Actor { Id = 1, FirstName = "Miho", LastName = "Tohno" };
        var cut = Render<MinnanoAvMetadataSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor)
            .Add(x => x.InitialSelectedPath, "actress148426.html"));

        // Went straight to the detail — no name-based search was ever issued, and no candidate
        // list is rendered.
        await client.DidNotReceive().SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await client.Received(1).GetPerformerDetailAsync("actress148426.html", Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll(".search-result-item"));

        var detailCard = cut.Find(".performer-detail-card");
        Assert.Contains("Height: 160 cm", detailCard.TextContent);
    }

    [Fact]
    public async Task Modal_WhenShowTrue_SearchesAndAutoSelectsExactMatch()
    {
        var client = Substitute.For<IMinnanoAvClient>();
        var actorService = Substitute.For<IActorService>();

        var searchResult = new MinnanoAvSearchResult(
            Name: "通野未帆",
            Kana: "とおのみほ",
            Romaji: "Tohno Miho",
            PathOrUrl: "actress148426.html",
            ImageUrl: "https://example.com/miho.jpg",
            DebutInfo: null,
            IsExactMatch: true,
            KnownAliases: new[] { "澤口美帆" });

        client.SearchPerformersAsync("通野未帆", Arg.Any<CancellationToken>())
            .Returns(new List<MinnanoAvSearchResult> { searchResult });

        var performerDetail = new MinnanoAvPerformerDetail
        {
            Name = "通野未帆",
            Kana = "とおのみほ",
            Romaji = "Tohno Miho",
            HeightCm = 160,
            CupSize = "E",
            Bust = 84,
            Waist = 59,
            Hips = 85,
            BirthDate = new DateTime(1991, 1, 21),
            IsRetired = true,
            Aliases = new List<string> { "澤口美帆" }
        };

        client.GetPerformerDetailAsync(searchResult.PathOrUrl, Arg.Any<CancellationToken>())
            .Returns(performerDetail);

        Services.AddSingleton(client);
        Services.AddSingleton(actorService);

        var actor = new Actor { Id = 1, FirstName = "Miho", LastName = "Tohno", JapaneseNameKanji = "通野未帆" };
        var cut = Render<MinnanoAvMetadataSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor));

        // Initial search executes on kanji name
        await client.Received(1).SearchPerformersAsync("通野未帆", Arg.Any<CancellationToken>());

        // Modal title includes actor display name
        var title = cut.Find(".search-modal-title");
        Assert.Contains("Tohno Miho", title.TextContent);

        // Result is rendered
        var resultItem = cut.Find(".search-result-item");
        Assert.Contains("通野未帆", resultItem.TextContent);
        Assert.Contains("Exact match", resultItem.TextContent);

        // Performer detail is loaded and rendered
        var detailCard = cut.Find(".performer-detail-card");
        Assert.Contains("Height: 160 cm", detailCard.TextContent);
        Assert.Contains("Cup: E", detailCard.TextContent);
        Assert.Contains("B/W/H: 84-59-85", detailCard.TextContent);
        Assert.Contains("Retired", detailCard.TextContent);

        // Enrich button is enabled
        var enrichBtn = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Enrich"));
        Assert.NotNull(enrichBtn);
        Assert.False(enrichBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Modal_WhenEnrichClicked_CallsActorServiceAndInvokesCallback()
    {
        var client = Substitute.For<IMinnanoAvClient>();
        var actorService = Substitute.For<IActorService>();

        var searchResult = new MinnanoAvSearchResult(
            Name: "通野未帆",
            Kana: "とおのみほ",
            Romaji: "Tohno Miho",
            PathOrUrl: "actress148426.html",
            ImageUrl: null,
            DebutInfo: null,
            IsExactMatch: true,
            KnownAliases: Array.Empty<string>());

        client.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<MinnanoAvSearchResult> { searchResult });

        var performerDetail = new MinnanoAvPerformerDetail
        {
            Name = "通野未帆",
            Kana = "とおのみほ",
            Romaji = "Tohno Miho",
            HeightCm = 160,
            CupSize = "E",
            Bust = 84,
            Waist = 59,
            Hips = 85,
            BirthDate = null,
            IsRetired = false,
            Aliases = new List<string>()
        };

        client.GetPerformerDetailAsync(searchResult.PathOrUrl, Arg.Any<CancellationToken>())
            .Returns(performerDetail);

        var actor = new Actor { Id = 42, FirstName = "Miho", LastName = "Tohno" };
        actorService.ImportOrEnrichFromMinnanoAvAsync(performerDetail, 42, Arg.Any<MinnanoAvImportOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ActorOperationResult.Ok(actor));

        Services.AddSingleton(client);
        Services.AddSingleton(actorService);

        var enrichedInvoked = false;
        var closedInvoked = false;
        var cut = Render<MinnanoAvMetadataSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor)
            .Add(x => x.OnEnriched, () => enrichedInvoked = true)
            .Add(x => x.OnClose, () => closedInvoked = true));

        var enrichBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Enrich"));
        await cut.InvokeAsync(() => enrichBtn.Click());

        await actorService.Received(1).ImportOrEnrichFromMinnanoAvAsync(performerDetail, 42, Arg.Any<MinnanoAvImportOptions?>(), Arg.Any<CancellationToken>());
        Assert.True(enrichedInvoked);
        Assert.True(closedInvoked);
    }

    [Fact]
    public async Task Modal_UserDecidesFieldsToImport_OnlySelectedFieldsPassedToActorService()
    {
        var client = Substitute.For<IMinnanoAvClient>();
        var actorService = Substitute.For<IActorService>();

        var searchResult = new MinnanoAvSearchResult(
            Name: "通野未帆",
            Kana: "とおのみほ",
            Romaji: "Tohno Miho",
            PathOrUrl: "actress148426.html",
            ImageUrl: null,
            DebutInfo: null,
            IsExactMatch: true,
            KnownAliases: Array.Empty<string>());

        client.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<MinnanoAvSearchResult> { searchResult });

        var performerDetail = new MinnanoAvPerformerDetail
        {
            Name = "通野未帆",
            Kana = "とおのみほ",
            Romaji = "Tohno Miho",
            HeightCm = 160,
            CupSize = "E",
            Bust = 84,
            Waist = 59,
            Hips = 85,
            BirthDate = new DateTime(1991, 1, 21),
            IsRetired = true,
            Aliases = new List<string> { "澤口美帆" }
        };

        client.GetPerformerDetailAsync(searchResult.PathOrUrl, Arg.Any<CancellationToken>())
            .Returns(performerDetail);

        MinnanoAvImportOptions? capturedOptions = null;
        var actor = new Actor { Id = 10, FirstName = "Miho", LastName = "Tohno", JapaneseNameKanji = "旧通野" };
        actorService.ImportOrEnrichFromMinnanoAvAsync(
                performerDetail,
                10,
                Arg.Do<MinnanoAvImportOptions?>(opt => capturedOptions = opt),
                Arg.Any<CancellationToken>())
            .Returns(ActorOperationResult.Ok(actor));

        Services.AddSingleton(client);
        Services.AddSingleton(actorService);

        var cut = Render<MinnanoAvMetadataSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor));

        // Verify comparison is shown
        var comparison = cut.Find(".import-fields-card");
        Assert.NotNull(comparison);
        Assert.Contains("Current: 旧通野", comparison.TextContent);
        Assert.Contains("通野未帆", comparison.TextContent);

        // Click Deselect All
        var deselectBtn = cut.FindAll("button.field-toggle-btn").First(b => b.TextContent.Contains("Deselect all"));
        await cut.InvokeAsync(() => deselectBtn.Click());

        // Check only Height
        var heightCheckbox = cut.Find("#field-height");
        await cut.InvokeAsync(() => heightCheckbox.Change(true));

        // Enrich button shows (1)
        var enrichBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Enrich"));
        Assert.Contains("(1)", enrichBtn.TextContent);

        // Click Enrich
        await cut.InvokeAsync(() => enrichBtn.Click());

        // Assert that only Height was selected in the captured options
        Assert.NotNull(capturedOptions);
        Assert.True(capturedOptions.ImportHeight);
        Assert.False(capturedOptions.ImportJapaneseName);
        Assert.False(capturedOptions.ImportCupSize);
        Assert.False(capturedOptions.ImportMeasurements);
        Assert.False(capturedOptions.ImportBirthDate);
        Assert.False(capturedOptions.ImportRetiredStatus);
        Assert.False(capturedOptions.ImportAliases);
    }

    [Fact]
    public async Task Modal_WhenExactMatchHasAllUnknownAttributes_DoesNotPreselectForImport()
    {
        var client = Substitute.For<IMinnanoAvClient>();
        var actorService = Substitute.For<IActorService>();

        var searchResult = new MinnanoAvSearchResult(
            Name: "伊藤はる",
            Kana: "いとうはる",
            Romaji: "Ito Haru",
            PathOrUrl: "actress99999.html",
            ImageUrl: null,
            DebutInfo: null,
            IsExactMatch: true,
            KnownAliases: Array.Empty<string>());

        client.SearchPerformersAsync("伊藤はる", Arg.Any<CancellationToken>())
            .Returns(new List<MinnanoAvSearchResult> { searchResult });

        var unknownDetail = new MinnanoAvPerformerDetail
        {
            Name = "伊藤はる",
            Kana = "いとうはる",
            Romaji = "Ito Haru",
            PathOrUrl = searchResult.PathOrUrl,
            HeightCm = null,
            CupSize = null,
            Bust = null,
            Waist = null,
            Hips = null,
            BirthDate = null,
            IsRetired = null,
            Aliases = new List<string>()
        };

        client.GetPerformerDetailAsync(searchResult.PathOrUrl, Arg.Any<CancellationToken>())
            .Returns(unknownDetail);

        Services.AddSingleton(client);
        Services.AddSingleton(actorService);

        var actor = new Actor { Id = 15, FirstName = "Haru", LastName = "Ito", JapaneseNameKanji = "伊藤はる" };
        var cut = Render<MinnanoAvMetadataSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor));

        // The exact match was retrieved
        await client.Received(1).SearchPerformersAsync("伊藤はる", Arg.Any<CancellationToken>());

        // Since all stats are unknown, performer detail is NOT auto-selected/preselected
        Assert.Empty(cut.FindAll(".performer-detail-card"));

        // Enrich button is not visible
        var enrichBtns = cut.FindAll("button").Where(b => b.TextContent.Contains("Enrich"));
        Assert.Empty(enrichBtns);

        // When user manually clicks the performer to view it
        var resultItem = cut.Find(".search-result-item");
        await cut.InvokeAsync(() => resultItem.Click());

        // Performer detail card is now shown
        Assert.NotEmpty(cut.FindAll(".performer-detail-card"));

        // Warning alert is displayed
        var alert = cut.Find(".alert-warning");
        Assert.Contains("All biographical and physical attributes are unknown", alert.TextContent);

        // No fields are preselected for import
        Assert.Empty(cut.FindAll("input.form-check-input:checked"));

        // Enrich button is disabled (count is 0)
        var enrichBtnAfterSelect = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Enrich"));
        Assert.NotNull(enrichBtnAfterSelect);
        Assert.True(enrichBtnAfterSelect.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Modal_WhenCloseClicked_InvokesOnClose()
    {
        var client = Substitute.For<IMinnanoAvClient>();
        var actorService = Substitute.For<IActorService>();

        client.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<MinnanoAvSearchResult>());

        Services.AddSingleton(client);
        Services.AddSingleton(actorService);

        var closed = false;
        var actor = new Actor { Id = 1, FirstName = "Miho", LastName = "Tohno" };
        var cut = Render<MinnanoAvMetadataSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor)
            .Add(x => x.OnClose, () => closed = true));

        var closeBtn = cut.Find(".search-modal-close-btn");
        await cut.InvokeAsync(() => closeBtn.Click());

        Assert.True(closed);
    }
}
