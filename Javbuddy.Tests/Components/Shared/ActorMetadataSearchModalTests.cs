using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Warashi;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class ActorMetadataSearchModalTests : BunitContext
{
    [Fact]
    public void Modal_WhenShowFalse_RendersNothing()
    {
        var warashiClient = Substitute.For<IWarashiClient>();
        var actorService = Substitute.For<IActorService>();
        Services.AddSingleton(warashiClient);
        Services.AddSingleton(actorService);

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var cut = Render<ActorMetadataSearchModal>(p => p
            .Add(x => x.Show, false)
            .Add(x => x.Actor, actor));

        Assert.Empty(cut.FindAll(".search-modal-backdrop"));
    }

    [Fact]
    public async Task Modal_WhenInitialSelectedPathProvided_SkipsSearchAndLoadsThatCandidateDirectly()
    {
        var warashiClient = Substitute.For<IWarashiClient>();
        var actorService = Substitute.For<IActorService>();

        var performerDetail = new WarashiPerformerDetail
        {
            Name = "Yua MIKAMI",
            JapaneseName = "三上悠亜",
            HeightCm = 159,
            CupSize = "F",
            Aliases = new List<string>()
        };
        warashiClient.GetPerformerDetailAsync("/en/actress-1", Arg.Any<CancellationToken>())
            .Returns(performerDetail);

        Services.AddSingleton(warashiClient);
        Services.AddSingleton(actorService);

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var cut = Render<ActorMetadataSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor)
            .Add(x => x.InitialSelectedPath, "/en/actress-1"));

        // Went straight to the detail — no name-based search was ever issued, and no candidate
        // list is rendered.
        await warashiClient.DidNotReceive().SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await warashiClient.Received(1).GetPerformerDetailAsync("/en/actress-1", Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll(".search-result-item"));

        var detailCard = cut.Find(".performer-detail-card");
        Assert.Contains("Height: 159 cm", detailCard.TextContent);
    }

    [Fact]
    public async Task Modal_WhenShowTrue_SearchesAndAutoSelectsExactMatch()
    {
        var warashiClient = Substitute.For<IWarashiClient>();
        var actorService = Substitute.For<IActorService>();

        var searchResult = new WarashiSearchResult(
            Name: "Yua Mikami",
            JapaneseName: "三上悠亜",
            PathOrUrl: "/en/actress-1/yua-mikami",
            ImageUrl: "https://example.com/yua.jpg",
            CareerActivity: "2015-2023",
            IsExactMatch: true,
            KnownAliases: new[] { "Kito Momona" });

        warashiClient.SearchPerformersAsync("三上悠亜", Arg.Any<CancellationToken>())
            .Returns(new List<WarashiSearchResult> { searchResult });

        var performerDetail = new WarashiPerformerDetail
        {
            Name = "Yua Mikami",
            JapaneseName = "三上悠亜",
            GivenName = "Yua",
            FamilyName = "Mikami",
            HeightCm = 159,
            CupSize = "F",
            Bust = 83,
            Waist = 59,
            Hips = 88,
            BirthDate = new DateTime(1993, 8, 16),
            IsRetired = true,
            MainPhotoUrl = "https://example.com/yua_detail.jpg",
            Aliases = new List<string> { "Kito Momona" }
        };

        warashiClient.GetPerformerDetailAsync(searchResult.PathOrUrl, Arg.Any<CancellationToken>())
            .Returns(performerDetail);

        Services.AddSingleton(warashiClient);
        Services.AddSingleton(actorService);

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami", JapaneseNameKanji = "三上悠亜" };
        var cut = Render<ActorMetadataSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor));

        // Initial search executes on kanji name
        await warashiClient.Received(1).SearchPerformersAsync("三上悠亜", Arg.Any<CancellationToken>());

        // Modal title includes actor display name
        var title = cut.Find(".search-modal-title");
        Assert.Contains("Mikami Yua", title.TextContent);

        // Result is rendered
        var resultItem = cut.Find(".search-result-item");
        Assert.Contains("Yua Mikami", resultItem.TextContent);
        Assert.Contains("Exact match", resultItem.TextContent);

        // Performer detail is loaded and rendered
        var detailCard = cut.Find(".performer-detail-card");
        Assert.Contains("Height: 159 cm", detailCard.TextContent);
        Assert.Contains("Cup: F", detailCard.TextContent);
        Assert.Contains("B/W/H: 83-59-88", detailCard.TextContent);
        Assert.Contains("Retired", detailCard.TextContent);

        // Enrich button is enabled
        var enrichBtn = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Enrich"));
        Assert.NotNull(enrichBtn);
        Assert.False(enrichBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Modal_WhenEnrichClicked_CallsActorServiceAndInvokesCallback()
    {
        var warashiClient = Substitute.For<IWarashiClient>();
        var actorService = Substitute.For<IActorService>();

        var searchResult = new WarashiSearchResult(
            Name: "Yua Mikami",
            JapaneseName: "三上悠亜",
            PathOrUrl: "/en/actress-1/yua-mikami",
            ImageUrl: null,
            CareerActivity: null,
            IsExactMatch: true,
            KnownAliases: Array.Empty<string>());

        warashiClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<WarashiSearchResult> { searchResult });

        var performerDetail = new WarashiPerformerDetail
        {
            Name = "Yua Mikami",
            JapaneseName = "三上悠亜",
            GivenName = null,
            FamilyName = null,
            HeightCm = 159,
            CupSize = "F",
            Bust = 83,
            Waist = 59,
            Hips = 88,
            BirthDate = null,
            IsRetired = false,
            MainPhotoUrl = null,
            Aliases = new List<string>()
        };

        warashiClient.GetPerformerDetailAsync(searchResult.PathOrUrl, Arg.Any<CancellationToken>())
            .Returns(performerDetail);

        var actor = new Actor { Id = 42, FirstName = "Yua", LastName = "Mikami" };
        actorService.ImportOrEnrichFromWarashiAsync(performerDetail, 42, Arg.Any<WarashiImportOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ActorOperationResult.Ok(actor));

        Services.AddSingleton(warashiClient);
        Services.AddSingleton(actorService);

        var enrichedInvoked = false;
        var closedInvoked = false;
        var cut = Render<ActorMetadataSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor)
            .Add(x => x.OnEnriched, () => enrichedInvoked = true)
            .Add(x => x.OnClose, () => closedInvoked = true));

        var enrichBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Enrich"));
        await cut.InvokeAsync(() => enrichBtn.Click());

        await actorService.Received(1).ImportOrEnrichFromWarashiAsync(performerDetail, 42, Arg.Any<WarashiImportOptions?>(), Arg.Any<CancellationToken>());
        Assert.True(enrichedInvoked);
        Assert.True(closedInvoked);
    }

    [Fact]
    public async Task Modal_WhenParentRerendersWithSameActorAndEmptyQuery_DoesNotResetOrResearch()
    {
        var warashiClient = Substitute.For<IWarashiClient>();
        var actorService = Substitute.For<IActorService>();

        warashiClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<WarashiSearchResult>());

        Services.AddSingleton(warashiClient);
        Services.AddSingleton(actorService);

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var cut = Render<ActorMetadataSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor));

        // Initial auto-search executes once on open
        await warashiClient.Received(1).SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // User clears the box, about to type a custom query
        var input = cut.Find("input.search-input");
        await cut.InvokeAsync(() => input.Input(string.Empty));

        // Parent re-renders the modal (e.g. an unrelated sibling changed) with the same actor still shown
        await cut.InvokeAsync(() => cut.Render(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor)));

        // The reset-and-search must not re-fire just because the query happens to be empty
        await warashiClient.Received(1).SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Modal_WhenCloseClicked_InvokesOnClose()
    {
        var warashiClient = Substitute.For<IWarashiClient>();
        var actorService = Substitute.For<IActorService>();

        warashiClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<WarashiSearchResult>());

        Services.AddSingleton(warashiClient);
        Services.AddSingleton(actorService);

        var closed = false;
        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var cut = Render<ActorMetadataSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor)
            .Add(x => x.OnClose, () => closed = true));

        var closeBtn = cut.Find(".search-modal-close-btn");
        await cut.InvokeAsync(() => closeBtn.Click());

        Assert.True(closed);
    }

    [Fact]
    public async Task Modal_UserDecidesFieldsToImport_OnlySelectedFieldsPassedToActorService()
    {
        var warashiClient = Substitute.For<IWarashiClient>();
        var actorService = Substitute.For<IActorService>();

        var searchResult = new WarashiSearchResult(
            Name: "Yua Mikami",
            JapaneseName: "三上悠亜",
            PathOrUrl: "/en/actress-1/yua-mikami",
            ImageUrl: null,
            CareerActivity: null,
            IsExactMatch: true,
            KnownAliases: Array.Empty<string>());

        warashiClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<WarashiSearchResult> { searchResult });

        var performerDetail = new WarashiPerformerDetail
        {
            Name = "Yua Mikami",
            JapaneseName = "三上悠亜",
            HeightCm = 159,
            CupSize = "F",
            Bust = 83,
            Waist = 59,
            Hips = 88,
            BirthDate = new DateTime(1993, 8, 16),
            IsRetired = true,
            MainPhotoUrl = "https://example.com/photo.jpg",
            Aliases = new List<string> { "Kito Momona" }
        };

        warashiClient.GetPerformerDetailAsync(searchResult.PathOrUrl, Arg.Any<CancellationToken>())
            .Returns(performerDetail);

        WarashiImportOptions? capturedOptions = null;
        var actor = new Actor { Id = 10, FirstName = "Yua", LastName = "Mikami", JapaneseNameKanji = "旧三上" };
        actorService.ImportOrEnrichFromWarashiAsync(
                performerDetail,
                10,
                Arg.Do<WarashiImportOptions?>(opt => capturedOptions = opt),
                Arg.Any<CancellationToken>())
            .Returns(ActorOperationResult.Ok(actor));

        Services.AddSingleton(warashiClient);
        Services.AddSingleton(actorService);

        var cut = Render<ActorMetadataSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor));

        // Verify comparison is shown
        var comparison = cut.Find(".import-fields-card");
        Assert.NotNull(comparison);
        Assert.Contains("Current: 旧三上", comparison.TextContent);
        Assert.Contains("三上悠亜", comparison.TextContent);

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
        Assert.False(capturedOptions.ImportPhotos);
    }

    [Fact]
    public async Task Modal_WhenPhotosSelected_PassesImportPhotosTrueToActorService()
    {
        var warashiClient = Substitute.For<IWarashiClient>();
        var actorService = Substitute.For<IActorService>();

        var searchResult = new WarashiSearchResult(
            Name: "Yua Mikami",
            JapaneseName: "三上悠亜",
            PathOrUrl: "/en/actress-1/yua-mikami",
            ImageUrl: null,
            CareerActivity: null,
            IsExactMatch: true,
            KnownAliases: Array.Empty<string>());

        warashiClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<WarashiSearchResult> { searchResult });

        var performerDetail = new WarashiPerformerDetail
        {
            Name = "Yua Mikami",
            JapaneseName = "三上悠亜",
            MainPhotoUrl = "https://example.com/photo1.jpg",
            AdditionalPhotoUrls = new List<string> { "https://example.com/photo2.jpg" },
            Aliases = new List<string>()
        };

        warashiClient.GetPerformerDetailAsync(searchResult.PathOrUrl, Arg.Any<CancellationToken>())
            .Returns(performerDetail);

        WarashiImportOptions? capturedOptions = null;
        var actor = new Actor { Id = 10, FirstName = "Yua", LastName = "Mikami" };
        actorService.ImportOrEnrichFromWarashiAsync(
                performerDetail,
                10,
                Arg.Do<WarashiImportOptions?>(opt => capturedOptions = opt),
                Arg.Any<CancellationToken>())
            .Returns(ActorOperationResult.Ok(actor));

        Services.AddSingleton(warashiClient);
        Services.AddSingleton(actorService);

        var cut = Render<ActorMetadataSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor));

        // Deselect all
        var deselectBtn = cut.FindAll("button.field-toggle-btn").First(b => b.TextContent.Contains("Deselect all"));
        await cut.InvokeAsync(() => deselectBtn.Click());

        // Check only Photos
        var photoCheckbox = cut.Find("#field-photo");
        await cut.InvokeAsync(() => photoCheckbox.Change(true));

        var enrichBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Enrich"));
        Assert.Contains("(1)", enrichBtn.TextContent);

        await cut.InvokeAsync(() => enrichBtn.Click());

        Assert.NotNull(capturedOptions);
        Assert.True(capturedOptions.ImportPhotos);
        Assert.False(capturedOptions.ImportJapaneseName);
        Assert.False(capturedOptions.ImportHeight);
    }

    [Fact]
    public async Task Modal_ByDefault_PhotoImportIsDisabledEvenWhenPhotosFound()
    {
        var warashiClient = Substitute.For<IWarashiClient>();
        var actorService = Substitute.For<IActorService>();

        var searchResult = new WarashiSearchResult(
            Name: "Yua Mikami",
            JapaneseName: "三上悠亜",
            PathOrUrl: "/en/actress-1/yua-mikami",
            ImageUrl: null,
            CareerActivity: null,
            IsExactMatch: true,
            KnownAliases: Array.Empty<string>());

        warashiClient.SearchPerformersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<WarashiSearchResult> { searchResult });

        var performerDetail = new WarashiPerformerDetail
        {
            Name = "Yua Mikami",
            JapaneseName = "三上悠亜",
            HeightCm = 159,
            MainPhotoUrl = "https://example.com/photo1.jpg",
            AdditionalPhotoUrls = new List<string> { "https://example.com/photo2.jpg" },
            Aliases = new List<string>()
        };

        warashiClient.GetPerformerDetailAsync(searchResult.PathOrUrl, Arg.Any<CancellationToken>())
            .Returns(performerDetail);

        WarashiImportOptions? capturedOptions = null;
        var actor = new Actor { Id = 10, FirstName = "Yua", LastName = "Mikami" };
        actorService.ImportOrEnrichFromWarashiAsync(
                performerDetail,
                10,
                Arg.Do<WarashiImportOptions?>(opt => capturedOptions = opt),
                Arg.Any<CancellationToken>())
            .Returns(ActorOperationResult.Ok(actor));

        Services.AddSingleton(warashiClient);
        Services.AddSingleton(actorService);

        var cut = Render<ActorMetadataSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor));

        // Photos checkbox is rendered but unchecked by default
        var photoCheckbox = cut.Find("#field-photo");
        Assert.False(photoCheckbox.HasAttribute("checked"));

        // Enrich directly
        var enrichBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Enrich"));
        await cut.InvokeAsync(() => enrichBtn.Click());

        Assert.NotNull(capturedOptions);
        Assert.False(capturedOptions.ImportPhotos);
        Assert.True(capturedOptions.ImportJapaneseName);
        Assert.True(capturedOptions.ImportHeight);
    }

    [Fact]
    public async Task Modal_WhenExactMatchHasAllUnknownAttributes_DoesNotPreselectForImport()
    {
        var warashiClient = Substitute.For<IWarashiClient>();
        var actorService = Substitute.For<IActorService>();

        var searchResult = new WarashiSearchResult(
            Name: "Haru ITÔ",
            JapaneseName: "伊藤はる",
            PathOrUrl: "/en/s-4-1/haru-ito/female-pornstar/56136",
            ImageUrl: null,
            CareerActivity: null,
            IsExactMatch: true,
            KnownAliases: Array.Empty<string>());

        warashiClient.SearchPerformersAsync("伊藤はる", Arg.Any<CancellationToken>())
            .Returns(new List<WarashiSearchResult> { searchResult });

        var unknownDetail = new WarashiPerformerDetail
        {
            Name = "Haru ITÔ",
            JapaneseName = "伊藤はる",
            PathOrUrl = searchResult.PathOrUrl,
            HeightCm = null,
            CupSize = null,
            Bust = null,
            Waist = null,
            Hips = null,
            BirthDate = null,
            IsRetired = null,
            MainPhotoUrl = null,
            Aliases = new List<string>()
        };

        warashiClient.GetPerformerDetailAsync(searchResult.PathOrUrl, Arg.Any<CancellationToken>())
            .Returns(unknownDetail);

        Services.AddSingleton(warashiClient);
        Services.AddSingleton(actorService);

        var actor = new Actor { Id = 15, FirstName = "Haru", LastName = "Ito", JapaneseNameKanji = "伊藤はる" };
        var cut = Render<ActorMetadataSearchModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor));

        // The exact match was retrieved
        await warashiClient.Received(1).SearchPerformersAsync("伊藤はる", Arg.Any<CancellationToken>());

        // Since all stats are unknown, performer detail is NOT auto-selected/preselected
        Assert.Empty(cut.FindAll(".performer-detail-card"));
        Assert.Empty(cut.FindAll(".performer-badges"));

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
}
