using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class ActorEditTests : BunitContext
{
    [Fact]
    public void ActorEdit_ShowsNotFound_WhenActorDoesNotExist()
    {
        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Unknown", Arg.Any<CancellationToken>())
            .Returns((Actor?)null);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());

        var cut = Render<ActorEdit>(parameters => parameters.Add(p => p.RouteName, "Unknown"));

        Assert.Contains("Actor not found.", cut.Markup);
    }

    [Fact]
    public void ActorEdit_PopulatesFieldsFromExistingActor()
    {
        var actor = new Actor
        {
            Id = 42,
            FirstName = "Yua",
            LastName = "Mikami",
            JapaneseNameKanji = "三上悠亜",
            JapaneseNameKana = "みかみ ゆあ",
            R18DevName = "Yua Mikami",
            JellyfinPersonId = "jelly-guid-5678",
            R18DevId = 9876
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());

        var cut = Render<ActorEdit>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        Assert.Contains("Edit Actor", cut.Markup);
        var firstNameInput = cut.Find("#actor-first-name");
        Assert.Equal("Yua", firstNameInput.GetAttribute("value"));

        var lastNameInput = cut.Find("#actor-last-name");
        Assert.Equal("Mikami", lastNameInput.GetAttribute("value"));

        var kanjiInput = cut.Find("#actor-japanese-kanji");
        Assert.Equal("三上悠亜", kanjiInput.GetAttribute("value"));

        var kanaInput = cut.Find("#actor-japanese-kana");
        Assert.Equal("みかみ ゆあ", kanaInput.GetAttribute("value"));

        var r18NameInput = cut.Find("#actor-r18dev-name");
        Assert.Equal("Yua Mikami", r18NameInput.GetAttribute("value"));

        // Upload button present
        var uploadBtn = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Upload / Change Photo"));
        Assert.NotNull(uploadBtn);
    }

    [Fact]
    public void ActorEdit_ShowsCustomPhotoBadge_WhenCustomPhotoExists()
    {
        var actor = new Actor { Id = 15, FirstName = "Sora", LastName = "Aoi" };
        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Aoi Sora", Arg.Any<CancellationToken>())
            .Returns(actor);
        Services.AddSingleton(actorService);

        var cacheService = Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>();
        cacheService.HasCustomImageAsync(15, Arg.Any<CancellationToken>()).Returns(true);
        Services.AddSingleton(cacheService);

        var cut = Render<ActorEdit>(parameters => parameters.Add(p => p.RouteName, "Aoi Sora"));

        Assert.Contains("Active Custom Photo", cut.Markup);
    }

    [Fact]
    public async Task ActorEdit_DisplaysErrorMessage_WhenSaveFails()
    {
        var actor = new Actor { Id = 10, FirstName = "Yua", LastName = "Mikami" };
        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        actorService.UpdateAsync(Arg.Any<ActorUpdateModel>(), Arg.Any<CancellationToken>())
            .Returns(ActorOperationResult.Fail("An actor named \"Mikami Yua\" already exists."));
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());

        var cut = Render<ActorEdit>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var form = cut.Find("form");
        await cut.InvokeAsync(() => form.Submit());

        var alert = cut.Find("div.alert-warning");
        Assert.Contains("An actor named \"Mikami Yua\" already exists.", alert.TextContent);
    }

    [Fact]
    public async Task ActorEdit_NavigatesToUpdatedRoute_WhenSaveSucceeds()
    {
        var actor = new Actor { Id = 10, FirstName = "Yua", LastName = "Mikami" };
        var updatedActor = new Actor { Id = 10, FirstName = "Yua", LastName = "Updated" };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(actor);
        actorService.UpdateAsync(Arg.Any<ActorUpdateModel>(), Arg.Any<CancellationToken>())
            .Returns(ActorOperationResult.Ok(updatedActor));
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());

        var cut = Render<ActorEdit>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var form = cut.Find("form");
        await cut.InvokeAsync(() => form.Submit());

        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/actors/Updated%20Yua", nav.Uri);
    }

    [Fact]
    public void ActorEdit_PreviewsLocalActorImageEndpoint_AndDoesNotRenderExternalThumbnailUrlDirectly()
    {
        var actor = new Actor
        {
            Id = 42,
            FirstName = "Yua",
            LastName = "Mikami"
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(actor);
        actorService.HasImageAsync(42, Arg.Any<CancellationToken>()).Returns(true);
        Services.AddSingleton(actorService);

        var cacheService = Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>();
        cacheService.HasCustomImageAsync(42, Arg.Any<CancellationToken>()).Returns(false);
        Services.AddSingleton(cacheService);

        var cut = Render<ActorEdit>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var previewImg = cut.Find("img.actor-preview-img");
        Assert.StartsWith("/actor-image/42/thumb", previewImg.GetAttribute("src"));

        // Guarantee no external images are rendered directly
        var allImgs = cut.FindAll("img");
        Assert.DoesNotContain(allImgs, img => (img.GetAttribute("src") ?? "").Contains("external.com"));
    }

    [Fact]
    public void ActorEdit_DoesNotRenderMetadataSourceAndAuditSection()
    {
        var actor = new Actor
        {
            Id = 42,
            FirstName = "Yua",
            LastName = "Mikami"
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());

        var cut = Render<ActorEdit>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        Assert.DoesNotContain("Metadata Source & Audit", cut.Markup);
    }

    [Fact]
    public void ActorEdit_PopulatesPhysicalAttributesFromExistingActor()
    {
        var actor = new Actor
        {
            Id = 42,
            FirstName = "Yua",
            LastName = "Mikami",
            HeightCm = 157,
            CupSize = "C",
            Bust = 89,
            Waist = 65,
            Hips = 94,
            BirthDate = new DateTime(1993, 8, 16),
            IsRetired = true
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());

        var cut = Render<ActorEdit>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        Assert.Contains("Physical Attributes & Measurements", cut.Markup);

        var birthdateInput = cut.Find("#actor-birthdate");
        Assert.Equal("1993-08-16", birthdateInput.GetAttribute("value"));

        var retiredCheckbox = cut.Find("#actor-retired");
        Assert.True(retiredCheckbox.HasAttribute("checked"));

        var heightInput = cut.Find("#actor-height");
        Assert.Equal("157", heightInput.GetAttribute("value"));

        var cupInput = cut.Find("#actor-cup-size");
        Assert.Equal("C", cupInput.GetAttribute("value"));

        var bustInput = cut.Find("#actor-bust");
        Assert.Equal("89", bustInput.GetAttribute("value"));

        var waistInput = cut.Find("#actor-waist");
        Assert.Equal("65", waistInput.GetAttribute("value"));

        var hipsInput = cut.Find("#actor-hips");
        Assert.Equal("94", hipsInput.GetAttribute("value"));

        // Helper explanation text
        Assert.Contains(ActorPhysicalAttributesHelper.JapaneseCupSizeExplanation, cut.Markup);
    }

    [Fact]
    public void ActorEdit_SavesPhysicalAttributes_WhenSubmitted()
    {
        var actor = new Actor
        {
            Id = 42,
            FirstName = "Yua",
            LastName = "Mikami"
        };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(actor);
        actorService.UpdateAsync(Arg.Any<ActorUpdateModel>(), Arg.Any<CancellationToken>())
            .Returns(ActorOperationResult.Ok(actor));
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());

        var cut = Render<ActorEdit>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        cut.Find("#actor-birthdate").Change("1993-08-16");
        cut.Find("#actor-retired").Change(true);
        cut.Find("#actor-height").Change("158");
        cut.Find("#actor-cup-size").Change("D");
        cut.Find("#actor-bust").Change("88");
        cut.Find("#actor-waist").Change("60");
        cut.Find("#actor-hips").Change("90");

        cut.Find("form").Submit();

        actorService.Received(1).UpdateAsync(
            Arg.Is<ActorUpdateModel>(m =>
                m.Id == 42
                && m.BirthDate == new DateTime(1993, 8, 16)
                && m.IsRetired == true
                && m.HeightCm == 158
                && m.CupSize == "D"
                && m.Bust == 88
                && m.Waist == 60
                && m.Hips == 90),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ActorEdit_CupSize_IsDropdownOfStandardSizes()
    {
        var actor = new Actor { Id = 42, FirstName = "Yua", LastName = "Mikami", CupSize = "C" };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(actor);
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());

        var cut = Render<ActorEdit>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var select = cut.Find("select#actor-cup-size");
        var values = select.QuerySelectorAll("option").Select(o => o.GetAttribute("value")).ToList();
        Assert.Equal(["", .. ActorPhysicalAttributesHelper.StandardCupSizes], values);
        Assert.Equal("— None —", select.QuerySelector("option")!.TextContent);
    }

    [Fact]
    public void ActorEdit_CupSize_KeepsStoredNonStandardValueAsOption()
    {
        var actor = new Actor { Id = 42, FirstName = "Yua", LastName = "Mikami", CupSize = "DD" };

        var actorService = Substitute.For<IActorService>();
        actorService.GetByRouteNameAsync("Mikami Yua", Arg.Any<CancellationToken>()).Returns(actor);
        actorService.UpdateAsync(Arg.Any<ActorUpdateModel>(), Arg.Any<CancellationToken>())
            .Returns(ActorOperationResult.Ok(actor));
        Services.AddSingleton(actorService);
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Images.IActorImageCacheService>());

        var cut = Render<ActorEdit>(parameters => parameters.Add(p => p.RouteName, "Mikami Yua"));

        var select = cut.Find("select#actor-cup-size");
        Assert.Equal("DD", select.GetAttribute("value"));
        Assert.Equal("DD", select.QuerySelectorAll("option").Last().GetAttribute("value"));

        cut.Find("#actor-cup-size").Change("");
        cut.Find("form").Submit();

        actorService.Received(1).UpdateAsync(
            Arg.Is<ActorUpdateModel>(m => string.IsNullOrEmpty(m.CupSize)),
            Arg.Any<CancellationToken>());
    }
}
