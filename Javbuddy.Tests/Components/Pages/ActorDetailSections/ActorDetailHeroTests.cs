using Bunit;
using Javbuddy.Components.Pages.ActorDetailSections;
using Javbuddy.Models;

namespace Javbuddy.Tests.Components.Pages.ActorDetailSections;

public class ActorDetailHeroTests : BunitContext
{
    private static Actor MikamiYua() => new() { Id = 7, FirstName = "Yua", LastName = "Mikami" };

    private IRenderedComponent<ActorDetailHero> RenderHero(Actor actor, Action<ComponentParameterCollectionBuilder<ActorDetailHero>>? configure = null) =>
        Render<ActorDetailHero>(p =>
        {
            p.Add(c => c.Actor, actor);
            configure?.Invoke(p);
        });

    [Fact]
    public void ShowsTheNameAndTheCountsWithTheirPlurals()
    {
        var cut = RenderHero(MikamiYua(), p => p.Add(c => c.MovieCount, 1).Add(c => c.ImageCount, 3));

        Assert.StartsWith("Mikami Yua", cut.Find("h1.actor-hero-title").TextContent.Trim());
        Assert.Equal("1 movie · 3 images", cut.Find("p.actor-hero-meta").TextContent);

        cut.Render(p => p.Add(c => c.Actor, MikamiYua()).Add(c => c.MovieCount, 2).Add(c => c.ImageCount, 1));

        Assert.Equal("2 movies · 1 image", cut.Find("p.actor-hero-meta").TextContent);
    }

    [Fact]
    public void WithAPhoto_ShowsItWithTheImageVersion_AndClickingItAsksForTheLightbox()
    {
        var asked = 0;
        var cut = RenderHero(MikamiYua(), p => p
            .Add(c => c.HasImage, true)
            .Add(c => c.ImageVersion, 42)
            .Add(c => c.OnLightboxRequested, () => asked++));

        Assert.Equal("/actor-image/7/full?v=42", cut.Find(".actor-hero-avatar-photo img").GetAttribute("src"));

        cut.Find(".actor-hero-avatar-photo").Click();

        Assert.Equal(1, asked);
    }

    [Fact]
    public void WithoutAPhoto_ShowsTheInitials_AndClickingThemAsksForTheUpload()
    {
        var asked = 0;
        var cut = RenderHero(MikamiYua(), p => p.Add(c => c.OnPhotoUploadRequested, () => asked++));

        var avatar = cut.Find(".actor-hero-avatar");
        Assert.Empty(cut.FindAll(".actor-hero-avatar-photo"));
        Assert.Equal("Click to upload photo", avatar.GetAttribute("title"));

        avatar.Click();

        Assert.Equal(1, asked);
    }

    [Fact]
    public void ShowsTheFavoriteRetiredAndTransgenderMarks_AndTheJapaneseName()
    {
        var actor = MikamiYua();
        actor.IsFavorite = true;
        actor.IsRetired = true;
        actor.IsTransgender = true;
        actor.JapaneseNameKanji = "三上悠亜";
        actor.JapaneseNameKana = "みかみゆあ";

        var cut = RenderHero(actor);

        Assert.NotNull(cut.Find(".actor-hero-favorite"));
        Assert.Equal("Retired", cut.Find(".actor-badge-retired").TextContent);
        Assert.Equal("Transgender", cut.Find(".actor-badge-transgender").TextContent);
        Assert.Contains("三上悠亜", cut.Find(".actor-hero-japanese").TextContent);
        Assert.Contains("みかみゆあ", cut.Find(".actor-hero-japanese").TextContent);
        Assert.NotNull(cut.Find(".actor-hero-japanese-sep"));
    }

    [Fact]
    public void ShowsTheCupSizeChainFromTheActorsPeriods_ThePlainCupWithoutAny()
    {
        var plain = MikamiYua();
        plain.CupSize = "D";

        var plainCut = RenderHero(plain);

        Assert.Single(plainCut.FindAll(".actor-cup-step"));
        Assert.Empty(plainCut.FindAll(".actor-cup-arrow"));
        Assert.Equal("D", plainCut.Find(".actor-cup-step").TextContent);

        var changing = MikamiYua();
        changing.CupSize = "C";
        changing.CupSizePeriods.Add(new ActorCupSizePeriod { EffectiveFrom = new DateTime(2024, 1, 1), CupSize = "E" });

        var chainCut = RenderHero(changing);

        Assert.Equal(2, chainCut.FindAll(".actor-cup-step").Count);
        Assert.Single(chainCut.FindAll(".actor-cup-arrow"));
        Assert.Equal("E", chainCut.Find(".actor-cup-step-current").TextContent);
    }

    [Fact]
    public void ShowsTheJellyfinLinkWhenTheUrlIsKnown_AndAPlainBadgeWhenOnlyTheIdIs()
    {
        var actor = MikamiYua();
        actor.JellyfinPersonId = "jf-1";

        var linked = RenderHero(actor, p => p.Add(c => c.JellyfinWebUrl, "https://jellyfin.local/x"));
        Assert.Equal("https://jellyfin.local/x", linked.Find("a.actor-badge-jellyfin").GetAttribute("href"));

        var unlinked = RenderHero(actor);
        Assert.Empty(unlinked.FindAll("a.actor-badge-jellyfin"));
        Assert.Equal("Jellyfin", unlinked.Find(".actor-hero-badges .actor-badge").TextContent);
    }

    [Fact]
    public void ShowsTheR18DevNameAndTheAliases()
    {
        var actor = MikamiYua();
        actor.R18DevName = "Yua Mikami r18";
        actor.Aliases.Add(new ActorAlias { Name = "Ichika" });

        var cut = RenderHero(actor);

        Assert.Equal("r18: Yua Mikami r18", cut.Find(".actor-hero-badges .actor-badge").TextContent);
        Assert.Equal("Ichika", cut.Find(".actor-alias-tag").TextContent);
    }
}
